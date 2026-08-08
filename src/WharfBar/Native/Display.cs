namespace WharfBar.Native;

/// <summary>
/// Monitor geometry and DPI helpers. Everything returned here is in physical pixels
/// (virtual-screen coordinates), never WPF device-independent units.
/// </summary>
internal static class Display
{
    internal const uint DefaultDpi = 96;

    /// <summary>
    /// DPI of the monitor hosting <paramref name="hwnd"/>. Falls back to 96 before the
    /// handle is realized, which keeps early layout math finite instead of dividing by zero.
    /// </summary>
    internal static uint GetDpi(IntPtr hwnd)
    {
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi == 0 ? DefaultDpi : dpi;
    }

    internal static int DipToPixels(double dip, uint dpi) => (int)Math.Round(dip * dpi / DefaultDpi);

    /// <summary>
    /// Full bounds of the primary monitor. v1 reserves work area on the primary monitor only
    /// (see docs/02-win32-appbar-dwm.md), so the AppBar rect is always derived from this.
    /// </summary>
    internal static RECT GetPrimaryMonitorBounds(IntPtr hwnd)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };

        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return info.rcMonitor;
        }

        return new RECT { Left = 0, Top = 0, Right = 0, Bottom = 0 };
    }

    /// <summary>
    /// Work area of the primary monitor (excludes AppBars / taskbar). Physical pixels.
    /// </summary>
    internal static RECT GetPrimaryMonitorWorkArea(IntPtr hwnd)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };

        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return info.rcWork;
        }

        return new RECT { Left = 0, Top = 0, Right = 0, Bottom = 0 };
    }
}
