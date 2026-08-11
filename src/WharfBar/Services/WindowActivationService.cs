using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed class WindowActivationService
{
    /// <summary>
    /// Foreground window captured on dock icon mouse-down, before the dock may steal focus.
    /// </summary>
    private IntPtr _foregroundAtClick;

    /// <summary>
    /// Last hwnd we activated from the dock — used when WharfBar already held focus at click.
    /// </summary>
    private IntPtr _lastActivatedHwnd;

    private static readonly uint CurrentProcessId = (uint)Environment.ProcessId;

    internal void CaptureForegroundHint()
    {
        _foregroundAtClick = NativeMethods.GetForegroundWindow();
    }

    internal bool TryActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (NativeMethods.IsIconic(hwnd))
        {
            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        return NativeMethods.SetForegroundWindow(hwnd);
    }

    internal bool TryMinimize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        return NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
    }

    /// <summary>
    /// Launch if not running; restore/activate if minimized or in background;
    /// minimize if the app was already the foreground app (taskbar-style toggle).
    /// </summary>
    internal void LaunchOrActivate(DockAppItem item, ProcessLauncherService launcher)
    {
        if (item.IsRunning && item.MainWindowHandle != IntPtr.Zero)
        {
            IntPtr hwnd = item.MainWindowHandle;

            if (NativeMethods.IsIconic(hwnd))
            {
                if (TryActivate(hwnd))
                {
                    _lastActivatedHwnd = hwnd;
                }

                return;
            }

            if (ShouldMinimize(hwnd))
            {
                if (TryMinimize(hwnd))
                {
                    _lastActivatedHwnd = IntPtr.Zero;
                }

                return;
            }

            if (TryActivate(hwnd))
            {
                _lastActivatedHwnd = hwnd;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            launcher.Launch(item.Path, item.Args);
        }
    }

    private bool ShouldMinimize(IntPtr appHwnd)
    {
        IntPtr fg = _foregroundAtClick != IntPtr.Zero
            ? _foregroundAtClick
            : NativeMethods.GetForegroundWindow();

        if (BelongsToSameProcess(appHwnd, fg))
        {
            return true;
        }

        // Dock already held focus: mouse-down hint is WharfBar, not the app.
        // Toggle minimize if this is the window we last brought forward.
        return IsCurrentProcessWindow(fg) && _lastActivatedHwnd == appHwnd;
    }

    private static bool BelongsToSameProcess(IntPtr appHwnd, IntPtr other)
    {
        if (appHwnd == IntPtr.Zero || other == IntPtr.Zero)
        {
            return false;
        }

        if (appHwnd == other)
        {
            return true;
        }

        NativeMethods.GetWindowThreadProcessId(appHwnd, out uint appPid);
        NativeMethods.GetWindowThreadProcessId(other, out uint otherPid);
        return appPid != 0 && appPid == otherPid;
    }

    private static bool IsCurrentProcessWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == CurrentProcessId;
    }
}
