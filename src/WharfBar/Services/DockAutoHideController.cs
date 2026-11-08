using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

internal enum DockAutoHideVisualState
{
    Visible,
    Showing,
    Hiding,
    Hidden,
}

internal readonly record struct EdgeProbeContext(
    int MonitorLeft,
    int MonitorTop,
    int MonitorRight,
    int MonitorBottom,
    int DockLeft,
    int DockTop,
    int DockRight,
    int DockBottom,
    uint Dpi,
    bool LeftEdge);

/// <summary>
/// Proximity auto-hide: polls cursor against bottom edge band + dock rect.
/// </summary>
internal sealed class DockAutoHideController : IDisposable
{
    internal const double EdgeBandDip = 8d;
    internal const int HideDelayMs = 500;
    internal const int PollMs = 50;

    /// <summary>Cursor poll while the dock is already hidden. The edge is not under the pointer.</summary>
    internal const int IdlePollMs = 200;

    internal static int PollIntervalMs(DockAutoHideVisualState state)
        => state == DockAutoHideVisualState.Hidden ? IdlePollMs : PollMs;

    private bool _enabled;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _hideTimer;
    private readonly Func<bool>? _isTrayPeekActive;
    private Func<EdgeProbeContext>? _probe;
    private bool _fullscreen;
    private bool _disposed;

    internal DockAutoHideController(bool enabled)
        : this(enabled, Dispatcher.CurrentDispatcher)
    {
    }

    internal DockAutoHideController(bool enabled, Dispatcher dispatcher, Func<bool>? isTrayPeekActive = null)
    {
        _enabled = enabled;
        _isTrayPeekActive = isTrayPeekActive;
        State = DockAutoHideVisualState.Visible;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(PollMs),
        };
        _pollTimer.Tick += OnPollTick;
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(HideDelayMs),
        };
        _hideTimer.Tick += OnHideTimerTick;
    }

    internal bool Enabled => _enabled;

    internal void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        if (!_enabled)
        {
            Stop();
            SetState(DockAutoHideVisualState.Visible);
        }
        else if (!_fullscreen && _probe is not null)
        {
            _pollTimer.Start();
        }
    }
    internal DockAutoHideVisualState State { get; private set; }
    internal event EventHandler? Changed;

    internal void Start(Func<EdgeProbeContext> probe)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        if (!_enabled || _fullscreen)
        {
            return;
        }

        _pollTimer.Start();
    }

    internal void Stop()
    {
        _pollTimer.Stop();
        _hideTimer.Stop();
    }

    internal void NotifyFullscreen(bool isFullscreen)
    {
        _fullscreen = isFullscreen;
        if (isFullscreen)
        {
            Stop();
            return;
        }

        // Caller shows dock; reset to Visible then Start again.
        SetState(DockAutoHideVisualState.Visible);
        if (_enabled && _probe is not null)
        {
            _pollTimer.Start();
        }
    }

    internal void NotifyAnimationCompleted()
    {
        if (State == DockAutoHideVisualState.Showing)
        {
            SetState(DockAutoHideVisualState.Visible);
        }
        else if (State == DockAutoHideVisualState.Hiding)
        {
            SetState(DockAutoHideVisualState.Hidden);
        }
    }

    /// <summary>
    /// Initial placement at hidden Y without raising <see cref="Changed"/> (already Hidden is a no-op).
    /// </summary>
    internal void AssumeHidden()
    {
        if (State == DockAutoHideVisualState.Visible)
        {
            State = DockAutoHideVisualState.Hidden;
        }
    }

    /// <summary>Compat shim until Task 3 wires <see cref="Start"/>.</summary>
    internal void Show() { }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _pollTimer.Tick -= OnPollTick;
        _hideTimer.Tick -= OnHideTimerTick;
        _probe = null;
        Changed = null;
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!_enabled || _fullscreen || _probe is null)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out POINT pos))
        {
            return;
        }

        EdgeProbeContext ctx = _probe();
        int bandHeightPx = Display.DipToPixels(EdgeBandDip, ctx.Dpi);
        bool trayPeekActive = _isTrayPeekActive?.Invoke() == true;

        if (ShouldShowForCursor(trayPeekActive, IsInEdgeBand(pos.X, pos.Y, ctx, bandHeightPx), IsInDockRect(pos.X, pos.Y, ctx)))
        {
            _hideTimer.Stop();
            ApplyPollInterval(PollMs);
            RequestShow();
            return;
        }

        if (trayPeekActive)
        {
            // A tray peek/reveal owns the pointer: the quiet mirror refresh parks the real cursor
            // in the bottom show band for its hover gesture. Keep the dock exactly where it is
            // instead of treating that synthetic position as a user hover.
            ApplyPollInterval(PollIntervalMs(State));
            return;
        }

        if (State is DockAutoHideVisualState.Hidden or DockAutoHideVisualState.Hiding)
        {
            ApplyPollInterval(PollIntervalMs(State));
            return;
        }

        ApplyPollInterval(PollMs);
        RestartHideDebounce();
    }

    private void ApplyPollInterval(int milliseconds)
    {
        var next = TimeSpan.FromMilliseconds(milliseconds);
        if (_pollTimer.Interval != next)
        {
            _pollTimer.Interval = next;
        }
    }

    private void OnHideTimerTick(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        if (!_enabled || _fullscreen)
        {
            return;
        }

        RequestHide();
    }

    /// <summary>
    /// Pure: does the current cursor position request the dock? A tray peek/reveal is excluded:
    /// its hover-reveal parks the real pointer inside the bottom edge band, so a plain position
    /// test would pop the dock every 20–60 s during the quiet mirror refresh. Normal auto-hide is
    /// untouched outside a peek.
    /// </summary>
    internal static bool ShouldShowForCursor(bool trayPeekActive, bool inEdgeBand, bool inDockRect) =>
        !trayPeekActive && (inEdgeBand || inDockRect);

    private static bool IsInEdgeBand(int x, int y, EdgeProbeContext ctx, int bandHeightPx)
    {
        if (ctx.LeftEdge)
        {
            return x < ctx.MonitorLeft + bandHeightPx
                && y >= ctx.MonitorTop
                && y < ctx.MonitorBottom;
        }

        return y >= ctx.MonitorBottom - bandHeightPx
            && x >= ctx.MonitorLeft
            && x < ctx.MonitorRight;
    }

    private static bool IsInDockRect(int x, int y, EdgeProbeContext ctx)
    {
        return x >= ctx.DockLeft
            && x < ctx.DockRight
            && y >= ctx.DockTop
            && y < ctx.DockBottom;
    }

    /// <summary>Start hide debounce once; do not reset every poll or hide never fires.</summary>
    private void RestartHideDebounce()
    {
        if (_hideTimer.IsEnabled)
        {
            return;
        }

        _hideTimer.Start();
    }

    private void RequestShow()
    {
        if (!_enabled || _fullscreen)
        {
            return;
        }

        switch (State)
        {
            case DockAutoHideVisualState.Visible:
            case DockAutoHideVisualState.Showing:
                return;
            default:
                SetState(DockAutoHideVisualState.Showing);
                break;
        }
    }

    private void RequestHide()
    {
        if (!_enabled || _fullscreen)
        {
            return;
        }

        switch (State)
        {
            case DockAutoHideVisualState.Hidden:
            case DockAutoHideVisualState.Hiding:
                return;
            default:
                SetState(DockAutoHideVisualState.Hiding);
                break;
        }
    }

    private void SetState(DockAutoHideVisualState next)
    {
        if (State == next)
        {
            return;
        }

        State = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
