using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Model representing a connected monitor and its brightness capabilities.
/// </summary>
internal sealed class MonitorItem
{
    internal required string Id { get; init; }
    internal required string Name { get; init; }
    internal required string DeviceName { get; init; }
    internal int CurrentBrightness { get; set; }
    internal int MinBrightness { get; init; } = 0;
    internal int MaxBrightness { get; init; } = 100;
    internal bool IsSupported { get; init; }
    internal bool IsWmi { get; init; }
    internal IntPtr PhysicalHandle { get; init; }
    internal string? WmiInstanceName { get; init; }
}

/// <summary>
/// Service managing connected display monitors and their brightness settings (DDC/CI + WMI).
/// Provides non-blocking, throttled brightness updates for smooth WPF slider interaction.
/// </summary>
internal sealed class MonitorBrightnessService : IDisposable
{
    private readonly object _lock = new();
    private readonly List<MonitorItem> _monitors = [];
    private readonly DispatcherTimer _throttleTimer;
    private readonly Dictionary<string, int> _pendingBrightnessUpdates = new(StringComparer.Ordinal);
    private ManagementEventWatcher? _brightnessWatcher;
    private int? _announcedBrightness;
    private bool _isDisposed;

    internal event EventHandler? MonitorsChanged;

    /// <summary>Hardware or in-app brightness moved. HUD consumers seed the first value.</summary>
    internal event EventHandler? BrightnessChanged;

    internal int? AnnouncedBrightness
    {
        get
        {
            lock (_lock)
            {
                return _announcedBrightness;
            }
        }
    }

    internal IReadOnlyList<MonitorItem> Monitors
    {
        get
        {
            lock (_lock)
            {
                return _monitors.ToList();
            }
        }
    }

    internal MonitorBrightnessService()
    {
        _throttleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _throttleTimer.Tick += OnThrottleTimerTick;

        RefreshMonitors();
        TryStartBrightnessWatcher();
    }

    /// <summary>
    /// Re-enumerates connected displays and updates their brightness states.
    /// Cleans up previous DDC/CI physical monitor handles before recreating.
    /// </summary>
    internal void RefreshMonitors()
    {
        Task.Run(() =>
        {
            List<MonitorItem> newMonitors = [];

            try
            {
                // 1. Enumerate monitors via EnumDisplayMonitors (DDC/CI)
                MonitorNative.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
                {
                    var info = new MONITORINFOEX
                    {
                        cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>()
                    };

                    if (!MonitorNative.GetMonitorInfo(hMonitor, ref info))
                    {
                        return true;
                    }

                    string deviceName = info.szDevice;
                    string friendlyName = GetFriendlyDeviceName(deviceName);

                    // Try DDC/CI via dxva2.dll
                    if (MonitorNative.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                    {
                        var physicalMonitors = new PHYSICAL_MONITOR[count];
                        if (MonitorNative.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physicalMonitors))
                        {
                            for (int i = 0; i < count; i++)
                            {
                                IntPtr pHandle = physicalMonitors[i].hPhysicalMonitor;
                                string description = physicalMonitors[i].szPhysicalMonitorDescription;

                                string monitorName = !string.IsNullOrWhiteSpace(description)
                                    ? description.Trim()
                                    : (!string.IsNullOrWhiteSpace(friendlyName) ? friendlyName : $"Monitör ({deviceName})");

                                if (count > 1)
                                {
                                    monitorName += $" #{i + 1}";
                                }

                                bool isSupported = MonitorNative.GetMonitorBrightness(pHandle, out uint minB, out uint curB, out uint maxB);

                                newMonitors.Add(new MonitorItem
                                {
                                    Id = $"{deviceName}_DDC_{i}",
                                    Name = monitorName,
                                    DeviceName = deviceName,
                                    CurrentBrightness = isSupported ? (int)curB : 50,
                                    MinBrightness = isSupported ? (int)minB : 0,
                                    MaxBrightness = isSupported ? (int)maxB : 100,
                                    IsSupported = isSupported,
                                    IsWmi = false,
                                    PhysicalHandle = pHandle
                                });
                            }
                        }
                    }

                    return true;
                }, IntPtr.Zero);

                // 2. Fallback WMI query for internal laptop panels if not found via DDC/CI
                try
                {
                    using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightness");
                    using var results = searcher.Get();

                    foreach (ManagementObject obj in results)
                    {
                        string instanceName = obj["InstanceName"]?.ToString() ?? string.Empty;
                        byte curB = (byte)(obj["CurrentBrightness"] ?? (byte)50);

                        // Check if this display isn't already covered by DDC/CI
                        bool exists = newMonitors.Any(m => m.IsSupported);
                        if (!exists || newMonitors.Count == 0)
                        {
                            newMonitors.Add(new MonitorItem
                            {
                                Id = $"WMI_{instanceName.GetHashCode():X}",
                                Name = "Dahili Ekran",
                                DeviceName = "DISPLAY_INTERNAL",
                                CurrentBrightness = curB,
                                MinBrightness = 0,
                                MaxBrightness = 100,
                                IsSupported = true,
                                IsWmi = true,
                                PhysicalHandle = IntPtr.Zero,
                                WmiInstanceName = instanceName
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MonitorBrightnessService] WMI query failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MonitorBrightnessService] Refresh error: {ex.Message}");
            }

            lock (_lock)
            {
                CleanupPhysicalMonitors_NoLock();
                _monitors.Clear();
                _monitors.AddRange(newMonitors);
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                MonitorsChanged?.Invoke(this, EventArgs.Empty);
                int? sample = FirstSupportedBrightness();
                if (sample is int value)
                {
                    PublishBrightness(value);
                }
            });
        });
    }

    private int? FirstSupportedBrightness()
    {
        lock (_lock)
        {
            MonitorItem? supported = _monitors.FirstOrDefault(static m => m.IsSupported);
            return supported?.CurrentBrightness;
        }
    }

    private void PublishBrightness(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (_lock)
        {
            if (_announcedBrightness == percent)
            {
                return;
            }

            _announcedBrightness = percent;
        }

        void Raise() => BrightnessChanged?.Invoke(this, EventArgs.Empty);
        System.Windows.Threading.Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Raise();
            return;
        }

        _ = dispatcher.BeginInvoke(Raise);
    }

    /// <summary>
    /// Laptop brightness keys raise <c>WmiMonitorBrightnessEvent</c>. Desktops without the
    /// class skip the watch; the island then only sees refreshes and in-app sets.
    /// </summary>
    private void TryStartBrightnessWatcher()
    {
        _ = Task.Run(() =>
        {
            ManagementEventWatcher? watcher = null;
            try
            {
                if (_isDisposed)
                {
                    return;
                }

                watcher = new ManagementEventWatcher(
                    new ManagementScope(@"\\.\root\wmi"),
                    new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                watcher.EventArrived += OnWmiBrightnessArrived;
                watcher.Start();

                lock (_lock)
                {
                    if (_isDisposed)
                    {
                        watcher.Stop();
                        watcher.Dispose();
                        return;
                    }

                    _brightnessWatcher = watcher;
                    watcher = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MonitorBrightnessService] brightness watch skipped: {ex.Message}");
                watcher?.Dispose();
            }
        });
    }

    private void OnWmiBrightnessArrived(object sender, EventArrivedEventArgs e)
    {
        try
        {
            object? raw = e.NewEvent.Properties["Brightness"]?.Value;
            if (raw is null)
            {
                return;
            }

            PublishBrightness(Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonitorBrightnessService] brightness event dropped: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets brightness for a specific monitor. Immediately updates internal state and
    /// schedules background DDC/CI or WMI execution via throttling.
    /// </summary>
    internal void SetBrightness(MonitorItem item, int brightness)
    {
        ArgumentNullException.ThrowIfNull(item);
        brightness = Math.Clamp(brightness, item.MinBrightness, item.MaxBrightness);

        lock (_lock)
        {
            item.CurrentBrightness = brightness;
            _pendingBrightnessUpdates[item.Id] = brightness;
        }

        PublishBrightness(brightness);

        if (!_throttleTimer.IsEnabled)
        {
            _throttleTimer.Start();
        }
    }

    /// <summary>
    /// Sets brightness across all connected supported monitors simultaneously.
    /// </summary>
    internal void SetAllBrightness(int brightness)
    {
        lock (_lock)
        {
            foreach (var monitor in _monitors.Where(m => m.IsSupported))
            {
                SetBrightness(monitor, brightness);
            }
        }
    }

    private void OnThrottleTimerTick(object? sender, EventArgs e)
    {
        _throttleTimer.Stop();

        Dictionary<string, int> targets;
        List<MonitorItem> snapshot;

        lock (_lock)
        {
            if (_pendingBrightnessUpdates.Count == 0)
            {
                return;
            }

            targets = new Dictionary<string, int>(_pendingBrightnessUpdates, StringComparer.Ordinal);
            _pendingBrightnessUpdates.Clear();
            snapshot = _monitors.ToList();
        }

        Task.Run(() =>
        {
            foreach (var kvp in targets)
            {
                var item = snapshot.FirstOrDefault(m => string.Equals(m.Id, kvp.Key, StringComparison.Ordinal));
                if (item is null || !item.IsSupported)
                {
                    continue;
                }

                int val = kvp.Value;

                if (item.IsWmi && !string.IsNullOrEmpty(item.WmiInstanceName))
                {
                    SetWmiBrightness(item.WmiInstanceName, val);
                }
                else if (item.PhysicalHandle != IntPtr.Zero)
                {
                    MonitorNative.SetMonitorBrightness(item.PhysicalHandle, (uint)val);
                }
            }
        });
    }

    private static void SetWmiBrightness(string instanceName, int brightness)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
            using var results = searcher.Get();

            foreach (ManagementObject obj in results)
            {
                string objInstanceName = obj["InstanceName"]?.ToString() ?? string.Empty;
                if (string.Equals(objInstanceName, instanceName, StringComparison.OrdinalIgnoreCase) || results.Count == 1)
                {
                    obj.InvokeMethod("WmiSetBrightness", new object[] { 1U, (byte)brightness });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MonitorBrightnessService] SetWmiBrightness error: {ex.Message}");
        }
    }

    private static string GetFriendlyDeviceName(string deviceName)
    {
        try
        {
            var dev = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (MonitorNative.EnumDisplayDevices(deviceName, 0, ref dev, 0))
            {
                if (!string.IsNullOrWhiteSpace(dev.DeviceString))
                {
                    return dev.DeviceString.Trim();
                }
            }
        }
        catch
        {
            // Fallback to deviceName
        }

        return deviceName;
    }

    private void CleanupPhysicalMonitors_NoLock()
    {
        foreach (var m in _monitors)
        {
            if (m.PhysicalHandle != IntPtr.Zero)
            {
                try
                {
                    MonitorNative.DestroyPhysicalMonitor(m.PhysicalHandle);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _throttleTimer.Stop();

        ManagementEventWatcher? watcher;
        lock (_lock)
        {
            watcher = _brightnessWatcher;
            _brightnessWatcher = null;
            CleanupPhysicalMonitors_NoLock();
            _monitors.Clear();
        }

        if (watcher is not null)
        {
            try
            {
                watcher.EventArrived -= OnWmiBrightnessArrived;
                watcher.Stop();
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MonitorBrightnessService] brightness watch stop failed: {ex.Message}");
            }
        }
    }
}
