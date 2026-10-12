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

    private bool _enabled;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _hideTimer;
    private Func<EdgeProbeContext>? _probe;
    private bool _fullscreen;
    private bool _disposed;

    internal DockAutoHideController(bool enabled)
        : this(enabled, Dispatcher.CurrentDispatcher)
    {
    }

    internal DockAutoHideController(bool enabled, Dispatcher dispatcher)
    {
        _enabled = enabled;
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

        if (IsInEdgeBand(pos.X, pos.Y, ctx, bandHeightPx) || IsInDockRect(pos.X, pos.Y, ctx))
        {
            _hideTimer.Stop();
            RequestShow();
            return;
        }

        RestartHideDebounce();
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
