using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

internal enum MonitorPart
{
    TopBar,
    Dock,
}

/// <summary>
/// Owns monitor enumeration and the mapping between live monitors and config visibility.
/// Window-set reconciliation and per-monitor top bars/docks arrive in later tasks; this task
/// establishes enumeration, change events, and enable/disable queries on the UI thread.
/// </summary>
internal sealed class MonitorManager : IDisposable
{
    private readonly ConfigService _config;
    private readonly MonitorMessageWindow _messageWindow;
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();

    internal event EventHandler? MonitorsChanged;

    internal MonitorManager(ConfigService config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _messageWindow = new MonitorMessageWindow();
        _messageWindow.DisplaySettingsChanged += OnDisplayChanged;
    }

    /// <summary>Live monitors, primary first. Only valid on the UI thread.</summary>
    internal IReadOnlyList<MonitorInfo> CurrentMonitors => _monitors;

    /// <summary>Call once on the UI thread after construction.</summary>
    internal void Initialize()
    {
        Refresh();
    }

    internal void Refresh()
    {
        IReadOnlyList<MonitorInfo> next = MonitorNative.Enumerate();
        bool changed = !MonitorSetsEqual(_monitors, next);
        _monitors = next;
        if (changed)
        {
            MonitorsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal bool IsEnabled(string deviceId, MonitorPart part)
    {
        MonitorConfig? entry = FindEntry(deviceId);
        if (entry is null)
        {
            MonitorInfo? live = _monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
            if (live is null)
            {
                return false;
            }

            entry = MonitorIdentityMatcher.Resolve(_config.Current.Monitors, live);
        }

        return part == MonitorPart.TopBar ? entry.TopBar : entry.Dock;
    }

    internal MonitorConfig? FindEntry(string deviceId)
    {
        return _config.Current.Monitors.FirstOrDefault(c =>
            string.Equals(c.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds or updates a config entry for a live monitor and persists. Returns the entry.</summary>
    internal MonitorConfig UpsertEntry(MonitorInfo monitor)
    {
        MonitorConfig? existing = FindEntry(monitor.DeviceId);
        if (existing is not null)
        {
            existing.DeviceName = monitor.DeviceName;
            existing.Name = monitor.FriendlyName;
            return existing;
        }

        var entry = new MonitorConfig
        {
            DeviceId = monitor.DeviceId,
            DeviceName = monitor.DeviceName,
            Name = monitor.FriendlyName,
            TopBar = monitor.IsPrimary,
            Dock = monitor.IsPrimary,
        };
        _config.Current.Monitors.Add(entry);
        return entry;
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private static bool MonitorSetsEqual(IReadOnlyList<MonitorInfo> a, IReadOnlyList<MonitorInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            MonitorInfo x = a[i];
            MonitorInfo y = b[i];
            if (!string.Equals(x.DeviceId, y.DeviceId, StringComparison.OrdinalIgnoreCase)
                || x.IsPrimary != y.IsPrimary
                || x.Bounds.Left != y.Bounds.Left || x.Bounds.Top != y.Bounds.Top
                || x.Bounds.Right != y.Bounds.Right || x.Bounds.Bottom != y.Bounds.Bottom
                || x.Dpi != y.Dpi)
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        _messageWindow.DisplaySettingsChanged -= OnDisplayChanged;
        _messageWindow.Dispose();
    }
}