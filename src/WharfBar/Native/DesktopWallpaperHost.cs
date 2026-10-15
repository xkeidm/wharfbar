using System.Text;

namespace WharfBar.Native;

/// <summary>
/// Locates the shell WorkerW that sits behind desktop icons so a transition
/// overlay can paint under icons (they stay visible).
/// </summary>
internal static class DesktopWallpaperHost
{
    /// <summary>
    /// Returns the wallpaper WorkerW HWND, or <see cref="IntPtr.Zero"/> if unavailable.
    /// </summary>
    internal static IntPtr TryGetWorkerW()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        // Spawns / refreshes the WorkerW pair used for wallpaper behind DefView.
        _ = NativeMethods.SendMessageTimeout(
            progman,
            NativeMethods.WM_SPAWN_WORKERW,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.SMTO_NORMAL,
            1000,
            out _);

        IntPtr worker = IntPtr.Zero;
        NativeMethods.EnumWindows((top, _) =>
        {
            IntPtr defView = NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                return true;
            }

            // WorkerW immediately after the desktop DefView host is the wallpaper layer.
            worker = NativeMethods.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return true;
        }, IntPtr.Zero);

        if (worker != IntPtr.Zero)
        {
            return worker;
        }

        // Fallback: last top-level WorkerW that does not host DefView.
        NativeMethods.EnumWindows((top, _) =>
        {
            if (!IsClass(top, "WorkerW"))
            {
                return true;
            }

            if (NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                return true;
            }

            worker = top;
            return true;
        }, IntPtr.Zero);

        return worker;
    }

    private static bool IsClass(IntPtr hwnd, string className)
    {
        var sb = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0
            && string.Equals(sb.ToString(), className, StringComparison.Ordinal);
    }
}
