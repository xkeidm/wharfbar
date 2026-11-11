using System.Diagnostics;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Keeps the top bar's AppBar strip clear of windows.
///
/// <para>Windows has no API that forbids a window from being placed in a reserved strip: the AppBar
/// contract shapes the <b>work area</b>, so maximize / snap / new-window placement respect the bar,
/// but a manual drag can always put a window there (this is also true of the taskbar — the shell
/// simply paints over it). The strip is therefore enforced actively instead of painted over:
/// an out-of-context WinEvent hook (documented, nothing injected) catches the end of every
/// interactive move/resize and every newly shown/restored window, and a normal framed window whose
/// top edge landed under the bar is pushed back below it with one <c>SetWindowPos</c>.</para>
///
/// <para>The push is never mid-drag (only <c>EVENT_SYSTEM_MOVESIZEEND</c>, i.e. after the mouse is
/// released), the move is queued to the owner's thread (<c>SWP_ASYNCWINDOWPOS</c>) so a busy app
/// cannot stall the AppBar UI thread, and fullscreen / tool / no-activate / own-process windows are
/// ignored (<see cref="StripIntrusionPolicy"/>). Because nothing is painted, the bar stays fully
/// translucent and the desktop wallpaper — including animated walls and Windows slideshows — is
/// always exact; there is no wallpaper snapshot to keep in sync.</para>
/// </summary>
internal sealed class TopBarStripGuard : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly WindowEventHook.WinEventProc _proc; // Native side holds this delegate.
    private readonly IntPtr _moveSizeEndHook;
    private readonly IntPtr _showHook;
    private bool _disposed;
    private bool _sweepQueued;

    /// <summary>The app-wide guard while it is installed (the bars are created before it).</summary>
    internal static TopBarStripGuard? Current { get; private set; }

    private TopBarStripGuard(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _proc = OnWindowEvent;
        _moveSizeEndHook = WindowEventHook.TryHookMoveSizeEnd(_proc);
        _showHook = WindowEventHook.TryHookShow(_proc);
    }

    /// <summary>
    /// Installs the app-wide guard. A refused hook is not fatal: the bar and the dock keep working,
    /// only windows can be dragged back under the bar (the hook failure is in the debug log).
    /// </summary>
    internal static TopBarStripGuard Install(Dispatcher dispatcher)
    {
        Current ??= new TopBarStripGuard(dispatcher);
        return Current;
    }

    /// <summary>Runs on the installing (UI) thread; the callback must stay cheap, so it only posts.</summary>
    private void OnWindowEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint thread,
        uint eventTime)
    {
        if (hwnd == IntPtr.Zero || idObject != WindowEventHook.OBJID_WINDOW)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, () => Clamp(hwnd));
    }

    /// <summary>
    /// One deferred pass over the desktop (startup, display/DPI change, top-bar rebuild) that pushes
    /// windows already sitting under a bar out of the strip. Coalesced: a burst queues one pass.
    /// </summary>
    internal void QueueSweep()
    {
        if (_disposed || _sweepQueued)
        {
            return;
        }

        _sweepQueued = true;
        _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _sweepQueued = false;
            if (!_disposed)
            {
                Sweep();
            }
        });
    }

    private void Sweep()
    {
        uint ownPid = (uint)Environment.ProcessId;
        _ = NativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != ownPid)
                {
                    Clamp(hwnd);
                }

                return true;
            },
            IntPtr.Zero);
    }

    /// <summary>Pushes one window out of the strip of its monitor, when it intrudes on it.</summary>
    private void Clamp(IntPtr hwnd)
    {
        if (_disposed || !IsEligible(hwnd))
        {
            return;
        }

        // A posted clamp can land a few milliseconds after a new drag started; never move a window
        // that currently owns the mouse capture (the system move/resize loop is tracking it).
        if (NativeMethods.GetCapture() == hwnd)
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out RECT window))
        {
            return;
        }

        RECT monitor = Display.GetMonitorBounds(hwnd);
        RECT workArea = Display.GetMonitorWorkArea(hwnd);
        if (StripIntrusionPolicy.ClampTop(window, monitor, workArea) is not { } top)
        {
            return;
        }

        _ = NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            window.Left,
            top,
            0,
            0,
            NativeMethods.SWP_NOSIZE
            | NativeMethods.SWP_NOZORDER
            | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_NOOWNERZORDER
            | NativeMethods.SWP_ASYNCWINDOWPOS);
    }

    /// <summary>
    /// Normal framed application windows only: menus, overlays, splash screens, tool palettes,
    /// no-activate OSDs and child windows are left exactly where their owner put them.
    /// </summary>
    private static bool IsEligible(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)
            || !NativeMethods.IsWindowVisible(hwnd)
            || NativeMethods.IsIconic(hwnd)
            || NativeMethods.IsZoomed(hwnd))
        {
            return false;
        }

        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        if ((style & NativeMethods.WS_CHILD) != 0)
        {
            return false;
        }

        // A caption is what makes a window a "normal" application window the user can drag around;
        // menus, popups, OSDs and splash screens do not have one (they are not the target here).
        if ((style & NativeMethods.WS_CAPTION) != NativeMethods.WS_CAPTION)
        {
            return false;
        }

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        return (exStyle & NativeMethods.WS_EX_TOOLWINDOW) == 0
               && (exStyle & NativeMethods.WS_EX_NOACTIVATE) == 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        WindowEventHook.Unhook(_moveSizeEndHook);
        WindowEventHook.Unhook(_showHook);
    }
}
