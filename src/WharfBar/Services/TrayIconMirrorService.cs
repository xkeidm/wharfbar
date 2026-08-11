using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed class TrayIconMirrorSnapshot
{
    public required IReadOnlyList<TrayIconItem> Items { get; init; }
}

/// <summary>
/// Polls Win11 notification-area icons via UIA while briefly peeking the hidden taskbar
/// (same peek discipline as <see cref="TrayOverflowInvoker"/>). Background polls use
/// stealth (off-screen) peek so the real taskbar does not flash every interval.
/// </summary>
internal sealed class TrayIconMirrorService : IDisposable
{
    private readonly TaskbarVisibilityService _taskbar;
    private readonly Dispatcher _uiDispatcher;
    private readonly DispatcherTimer _pollTimer;
    private readonly object _lock = new();
    private List<TrayIconItem> _currentItems = [];
    private bool _isDisposed;
    private bool _isScanning;
    private bool _clickInFlight;
    private bool _enabled;
    private DispatcherTimer? _missingIconTimer;
    private DateTime _lastVisibleCaptureUtc = DateTime.MinValue;

    public event EventHandler? Changed;

    public TrayIconMirrorSnapshot Snapshot
    {
        get
        {
            lock (_lock)
            {
                return new TrayIconMirrorSnapshot { Items = _currentItems.ToList() };
            }
        }
    }

    public TrayIconMirrorService(TaskbarVisibilityService taskbar)
    {
        _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
        _uiDispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, _uiDispatcher)
        {
            Interval = TimeSpan.FromSeconds(10.0),
        };
        _pollTimer.Tick += OnPollTick;
    }

    /// <summary>
    /// Starts/stops background stealth polls. When disabled, clears mirrored items.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (!_uiDispatcher.CheckAccess())
        {
            _ = _uiDispatcher.BeginInvoke(() => SetEnabled(enabled));
            return;
        }

        if (_isDisposed)
        {
            return;
        }

        if (_enabled == enabled)
        {
            if (enabled && !_pollTimer.IsEnabled)
            {
                _pollTimer.Start();
            }

            return;
        }

        _enabled = enabled;
        if (!enabled)
        {
            _pollTimer.Stop();
            _missingIconTimer?.Stop();
            bool cleared;
            lock (_lock)
            {
                cleared = _currentItems.Count > 0;
                _currentItems = [];
            }

            if (cleared)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        _pollTimer.Start();
        // Stealth polls skip BitBlt (off-screen). Seed glyphs once with a visible peek
        // so the strip is not empty-with-tooltips until the user happens to open overflow.
        _ = _uiDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => PollSystemTray(stealth: false));
    }

    private void OnPollTick(object? sender, EventArgs e) => PollSystemTray(stealth: true);

    /// <summary>
    /// Scans while a visible peek is already held (overflow / click). Captures icons via BitBlt.
    /// </summary>
    public void PollWhilePeeked()
    {
        if (_isDisposed || !_enabled)
        {
            return;
        }

        if (!_uiDispatcher.CheckAccess())
        {
            _ = _uiDispatcher.BeginInvoke(DispatcherPriority.Background, PollWhilePeeked);
            return;
        }

        if (!_taskbar.IsTrayPeekActive || _taskbar.IsStealthPeekActive)
        {
            return;
        }

        ApplyScan(TrayIconNative.GetSystemTrayIcons(captureIcons: true), mergeIcons: false);
    }

    public void PollSystemTray(bool stealth = true)
    {
        if (_isDisposed || !_enabled || _isScanning || _clickInFlight)
        {
            return;
        }

        if (!_uiDispatcher.CheckAccess())
        {
            _ = _uiDispatcher.BeginInvoke(DispatcherPriority.Background, () => PollSystemTray(stealth));
            return;
        }

        // Nested inside an existing visible peek — capture icons without another Begin/End.
        if (_taskbar.IsTrayPeekActive && !_taskbar.IsStealthPeekActive)
        {
            PollWhilePeeked();
            return;
        }

        _isScanning = true;
        bool peeked = false;
        try
        {
            peeked = _taskbar.BeginTrayPeek(stealth: stealth);
            bool captureIcons = !stealth;
            if (captureIcons)
            {
                _lastVisibleCaptureUtc = DateTime.UtcNow;
            }

            var scanned = TrayIconNative.GetSystemTrayIcons(captureIcons: captureIcons);
            ApplyScan(scanned, mergeIcons: stealth);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: tray mirror poll failed — {ex.Message}");
        }
        finally
        {
            if (peeked)
            {
                _taskbar.EndTrayPeek();
            }

            _isScanning = false;
        }
    }

    private void ApplyScan(List<TrayIconItem> scanned, bool mergeIcons)
    {
        bool changed;
        bool needsIconCapture;
        lock (_lock)
        {
            if (mergeIcons)
            {
                scanned = MergePreservedIcons(_currentItems, scanned);
            }

            changed = HasListChanged(_currentItems, scanned);
            if (changed)
            {
                _currentItems = scanned;
            }

            needsIconCapture = _currentItems.Exists(i => i.IconSource is null);
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (needsIconCapture && mergeIcons)
        {
            ScheduleMissingIconCapture();
        }
    }

    /// <summary>
    /// Stealth list updates can introduce new ids without glyphs — capture soon, throttled.
    /// </summary>
    private void ScheduleMissingIconCapture()
    {
        if (_isDisposed || !_enabled)
        {
            return;
        }

        // Avoid flashing the taskbar more than once per minute for icon seeds.
        if ((DateTime.UtcNow - _lastVisibleCaptureUtc).TotalSeconds < 60)
        {
            return;
        }

        if (_missingIconTimer is null)
        {
            _missingIconTimer = new DispatcherTimer(DispatcherPriority.Background, _uiDispatcher)
            {
                Interval = TimeSpan.FromSeconds(1.5),
            };
            _missingIconTimer.Tick += (_, _) =>
            {
                _missingIconTimer.Stop();
                if (!_isDisposed && _enabled)
                {
                    PollSystemTray(stealth: false);
                }
            };
        }

        if (!_missingIconTimer.IsEnabled)
        {
            _missingIconTimer.Start();
        }
    }

    private static List<TrayIconItem> MergePreservedIcons(
        List<TrayIconItem> previous,
        List<TrayIconItem> scanned)
    {
        if (previous.Count == 0)
        {
            return scanned;
        }

        var byId = previous.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var merged = new List<TrayIconItem>(scanned.Count);
        foreach (TrayIconItem item in scanned)
        {
            if (item.IconSource is null && byId.TryGetValue(item.Id, out TrayIconItem? old)
                && old.IconSource is not null)
            {
                merged.Add(new TrayIconItem
                {
                    Id = item.Id,
                    Title = item.Title,
                    IconSource = old.IconSource,
                });
            }
            else
            {
                merged.Add(item);
            }
        }

        return merged;
    }

    private static bool HasListChanged(List<TrayIconItem> oldList, List<TrayIconItem> newList)
    {
        if (oldList.Count != newList.Count)
        {
            return true;
        }

        for (int i = 0; i < oldList.Count; i++)
        {
            if (!string.Equals(oldList[i].Id, newList[i].Id, StringComparison.Ordinal)
                || !string.Equals(oldList[i].Title, newList[i].Title, StringComparison.Ordinal)
                || !IconEquals(oldList[i].IconSource, newList[i].IconSource))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IconEquals(ImageSource? a, ImageSource? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        return a is null == b is null;
    }

    public void ForwardClick(TrayIconItem item, bool isRightClick)
    {
        if (item is null || _isDisposed)
        {
            return;
        }

        if (!_uiDispatcher.CheckAccess())
        {
            _ = _uiDispatcher.BeginInvoke(() => ForwardClick(item, isRightClick));
            return;
        }

        if (_clickInFlight)
        {
            return;
        }

        _clickInFlight = true;
        bool peeked = false;
        try
        {
            peeked = _taskbar.BeginTrayPeek(stealth: false);
            if (peeked)
            {
                ApplyScan(TrayIconNative.GetSystemTrayIcons(captureIcons: true), mergeIcons: false);
            }

            TrayIconNative.ForwardClick(item, isRightClick);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: tray mirror click failed — {ex.Message}");
        }
        finally
        {
            // Keep peek briefly so flyouts / context menus can appear.
            int delayMs = isRightClick ? 2500 : 400;
            var end = new DispatcherTimer(DispatcherPriority.Background, _uiDispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(delayMs),
            };
            bool endPeek = peeked;
            end.Tick += (_, _) =>
            {
                end.Stop();
                if (endPeek)
                {
                    _taskbar.EndTrayPeek();
                }

                _clickInFlight = false;
            };
            end.Start();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _enabled = false;
        _pollTimer.Stop();
        _missingIconTimer?.Stop();
        _missingIconTimer = null;
    }
}
