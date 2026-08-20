using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Views;

namespace WharfBar.Services;

internal enum MonitorPart
{
    TopBar,
    Dock,
}

/// <summary>
/// Owns monitor enumeration, the mapping between live monitors and config visibility,
/// and the per-monitor top-bar window set, which is reconciled against config × monitors.
/// </summary>
internal sealed class MonitorManager : IDisposable
{
    private readonly ConfigService _config;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly VeilService _veil;
    private readonly MonitorMessageWindow _messageWindow;
    private readonly List<TopBarWindow> _topBars = new();
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private TopBarEngine? _engine;
    private bool _sourceConfigured;

    internal event EventHandler? MonitorsChanged;

    internal MonitorManager(ConfigService config, TaskbarVisibilityService taskbar, VeilService veil)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(taskbar);
        ArgumentNullException.ThrowIfNull(veil);
        _config = config;
        _taskbar = taskbar;
        _veil = veil;
        _messageWindow = new MonitorMessageWindow();
        _messageWindow.DisplaySettingsChanged += OnDisplayChanged;
    }

    internal IReadOnlyList<TopBarWindow> TopBars => _topBars;

    internal TopBarWindow? PrimaryTopBar =>
        _topBars.FirstOrDefault(b => b.Monitor.IsPrimary) ?? _topBars.FirstOrDefault();

    internal TopBarEngine Engine => _engine ?? throw new InvalidOperationException("Engine not created.");

    /// <summary>Live monitors, primary first. Only valid on the UI thread.</summary>
    internal IReadOnlyList<MonitorInfo> CurrentMonitors => _monitors;

    /// <summary>Call once on the UI thread after construction.</summary>
    internal void Initialize()
    {
        Refresh();
        _sourceConfigured = true;
        Reconcile();
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

    internal void Reconcile()
    {
        if (_engine is null)
        {
            _engine = new TopBarEngine(_taskbar, _veil);
            _engine.Initialize();
        }

        bool persisted = false;
        foreach (MonitorInfo monitor in _monitors)
        {
            MonitorConfig entry = MonitorIdentityMatcher.Resolve(_config.Current.Monitors, monitor);
            if (FindEntry(monitor.DeviceId) is null)
            {
                _config.Current.Monitors.Add(entry);
                persisted = true;
            }
        }

        if (persisted)
        {
            _config.Save(_config.Current);
        }

        List<MonitorInfo> desired = _monitors
            .Where(m => IsEnabled(m.DeviceId, MonitorPart.TopBar))
            .ToList();

        // Close bars whose monitor is no longer desired.
        for (int i = _topBars.Count - 1; i >= 0; i--)
        {
            TopBarWindow bar = _topBars[i];
            if (!desired.Any(m => m.DeviceId == bar.Monitor.DeviceId))
            {
                bar.PrepareForRecreate();
                bar.Close();
                _topBars.RemoveAt(i);
            }
        }

        // Open bars for monitors that need one.
        foreach (MonitorInfo monitor in desired)
        {
            if (_topBars.Any(b => b.Monitor.DeviceId == monitor.DeviceId))
            {
                continue;
            }

            var bar = new TopBarWindow(_engine, _config, _taskbar, _veil, monitor);
            _topBars.Add(bar);
            bar.Show();
        }

        RefreshPrimaryOwnership();
    }

    internal void RefreshAllPlacements()
    {
        foreach (TopBarWindow bar in _topBars)
        {
            bar.QueueApplyPlacement();
        }
    }

    internal void ApplyTopBarChanges()
    {
        foreach (TopBarWindow bar in _topBars)
        {
            bar.ApplyConfigChanges();
        }
    }

    private void RefreshPrimaryOwnership()
    {
        // Kept as a no-op hook for Task 11 (MainWindow assignment happens in App).
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
        if (_sourceConfigured)
        {
            Reconcile();
        }
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

        foreach (TopBarWindow bar in _topBars)
        {
            bar.PrepareForRecreate();
            bar.Close();
        }

        _topBars.Clear();
        _engine?.Dispose();
        _engine = null;
    }
}