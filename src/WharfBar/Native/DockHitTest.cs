namespace WharfBar.Native;

/// <summary>
/// Edge-band auto-hide hit-testing: clear or set WS_EX_TRANSPARENT for click-through.
/// </summary>
internal static class DockHitTest
{
    internal static void ApplyInteractive(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        int cleared = ex & ~NativeMethods.WS_EX_TRANSPARENT;
        if (cleared != ex)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, cleared);
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE
                | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER
                | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_FRAMECHANGED);
        }
    }

    internal static void ApplyPassthrough(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        int with = ex | NativeMethods.WS_EX_TRANSPARENT;
        if (with != ex)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, with);
            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE
                | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER
                | NativeMethods.SWP_NOACTIVATE
                | NativeMethods.SWP_FRAMECHANGED);
        }
    }
}
