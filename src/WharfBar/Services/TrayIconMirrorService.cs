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
/// Mirrors Win11 notification-area icons via UIA. Does not peek the hidden taskbar on
/// a timer — DWM cloak of <c>Shell_TrayWnd</c> from another process fails, and a UIA
/// scan holds peek for seconds, leaving the real taskbar visible. Icons are captured
/// only during a user-facing overflow / click peek (<see cref="PollWhilePeeked"/>).
/// </summary>
internal sealed class TrayIconMirrorService : IDisposable
{
    private readonly TaskbarVisibilityService _taskbar;
    private readonly Dispatcher _uiDispatcher;
    private readonly object _lock = new();
    private List<TrayIconItem> _currentItems = [];
    private bool _isDisposed;
    private bool _clickInFlight;
    private bool _enabled;

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
    }

    /// <summary>
    /// Enables capturing during overflow peeks. When disabled, clears mirrored items.
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
            return;
        }

        _enabled = enabled;
        if (!enabled)
        {
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
        }
    }

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

        ApplyScan(TrayIconNative.GetSystemTrayIcons(captureIcons: true));
    }

    private void ApplyScan(List<TrayIconItem> scanned)
    {
        bool changed;
        lock (_lock)
        {
            changed = HasListChanged(_currentItems, scanned);
            if (changed)
            {
                _currentItems = scanned;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
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
                ApplyScan(TrayIconNative.GetSystemTrayIcons(captureIcons: true));
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
    }
}
