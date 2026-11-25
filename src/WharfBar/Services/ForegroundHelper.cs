using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Raises a WharfBar window to the foreground when the shell (a non-activating AppBar or a closed
/// top-bar menu) still owns the foreground lock. Windows refuses a plain
/// <c>SetForegroundWindow</c> from a background process, so the input queue is attached to the
/// foreground thread for the call — the standard, documented-API way around the lock.
/// </summary>
internal static class ForegroundHelper
{
    /// <summary>Best-effort activation; returns true when the window ended up foreground.</summary>
    internal static bool TryActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return true;
        }

        uint currentThread = NativeMethods.GetCurrentThreadId();
        uint foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);

        bool attached = foregroundThread != 0
            && foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            _ = NativeMethods.BringWindowToTop(hwnd);
            _ = NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                _ = NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        return NativeMethods.GetForegroundWindow() == hwnd;
    }
}
