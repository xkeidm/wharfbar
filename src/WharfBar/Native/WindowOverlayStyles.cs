namespace WharfBar.Native;

/// <summary>
/// Win32 extended-style helpers for overlay windows.
/// </summary>
internal static class WindowOverlayStyles
{
    /// <summary>
    /// Excludes a window from the Alt+Tab / Alt+Shift+Tab switcher and Task View.
    /// WPF's <c>ShowInTaskbar=false</c> only removes <c>WS_EX_APPWINDOW</c> — Windows still
    /// lists every visible, top-level, unowned window that lacks <c>WS_EX_TOOLWINDOW</c>
    /// (the same rule <see cref="WindowEnumeration"/> mirrors for the dock running list).
    /// </summary>
    internal static void ExcludeFromTaskSwitcher(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return;
        }

        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Makes a short-lived overlay window invisible to input and activation: the window never takes
    /// focus (<c>WS_EX_NOACTIVATE</c>) and every click/drag passes through it (<c>WS_EX_TRANSPARENT</c>).
    /// Used by the theme crossfade, whose overlays sit above the bar/dock/settings for ~180 ms.
    /// </summary>
    internal static void MakeClickThroughNoActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);
    }
}
