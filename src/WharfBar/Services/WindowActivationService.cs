using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed class WindowActivationService
{
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

    internal void LaunchOrActivate(DockAppItem item, ProcessLauncherService launcher)
    {
        if (item.IsRunning && item.MainWindowHandle != IntPtr.Zero)
        {
            if (TryActivate(item.MainWindowHandle))
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(item.Path))
        {
            launcher.Launch(item.Path, item.Args);
        }
    }
}
