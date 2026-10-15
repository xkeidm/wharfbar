using System.Diagnostics;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Opens the native Windows tray overflow (ADR-005 hybrid). While WharfBar keeps
/// <c>Shell_TrayWnd</c> hidden, bare Win+B is a no-op — we peek-show the tray, open overflow,
/// then re-hide after the flyout closes (or a timeout).
/// </summary>
internal sealed class TrayOverflowInvoker
{
    private static readonly TimeSpan PeekSettle = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MinVisibleBeforeHide = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MaxPeekDuration = TimeSpan.FromSeconds(45);

    private readonly TaskbarVisibilityService _taskbar;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _rehideTimer;
    private DateTime _peekStartedUtc;
    private int _busy; // 0 = idle

    internal TrayOverflowInvoker(TaskbarVisibilityService taskbar)
    {
        _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
        _dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;
    }

    internal bool TryOpen()
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            return false;
        }

        // Native sleep / SendInput must not block the WPF dispatcher.
        _ = Task.Run(OpenCore);
        return true;
    }

    private void OpenCore()
    {
        bool peeked = false;
        bool refreshHeld = false;
        if (TaskbarNative.GetState() == NativeMethods.ABS_AUTOHIDE)
        {
            TrayPeekDiagnostics.LogStateChange("overflow");
        }

        _taskbar.BeginIconRefresh();
        try
        {
            // Shell ignores Win+B / chevron activation while the tray is parked by ABS_AUTOHIDE.
            peeked = _taskbar.BeginTrayPeek(stealth: false);
            if (peeked)
            {
                Thread.Sleep(PeekSettle);
            }

            bool opened = TrayOverflowNative.TryOpen();
            if (!opened)
            {
                Debug.WriteLine("WharfBar: tray overflow open strategies failed");
                if (peeked)
                {
                    _taskbar.EndTrayPeek();
                }

                return;
            }

            if (peeked)
            {
                refreshHeld = true;
                _dispatcher.BeginInvoke(ScheduleRehideAfterOverflow);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: tray overflow invoke failed — {ex.Message}");
            if (peeked)
            {
                _taskbar.EndTrayPeek();
            }
        }
        finally
        {
            if (!refreshHeld)
            {
                _taskbar.EndIconRefresh();
            }

            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void ScheduleRehideAfterOverflow()
    {
        _peekStartedUtc = DateTime.UtcNow;
        _rehideTimer?.Stop();

        _rehideTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = PollInterval,
        };
        _rehideTimer.Tick += OnRehideTick;
        _rehideTimer.Start();
    }

    private void OnRehideTick(object? sender, EventArgs e)
    {
        TimeSpan elapsed = DateTime.UtcNow - _peekStartedUtc;
        bool overflowVisible = TrayOverflowNative.IsOverflowVisible();

        // Keep the real taskbar up while the overflow flyout is open so icons stay usable.
        if (overflowVisible && elapsed < MaxPeekDuration)
        {
            return;
        }

        // Give Win+B focus / first paint a moment before hiding again when no flyout HWND appears.
        if (!overflowVisible && elapsed < MinVisibleBeforeHide)
        {
            return;
        }

        _rehideTimer?.Stop();
        _rehideTimer = null;
        _taskbar.EndTrayPeek();
        _taskbar.EndIconRefresh();
    }
}
