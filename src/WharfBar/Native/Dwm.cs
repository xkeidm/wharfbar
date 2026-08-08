namespace WharfBar.Native;

/// <summary>Values for <c>DWMWA_WINDOW_CORNER_PREFERENCE</c>.</summary>
internal enum DwmCornerPreference
{
    Default = 0,
    DoNotRound = 1,
    Round = 2,
    RoundSmall = 3,
}

/// <summary>Values for <c>DWMWA_SYSTEMBACKDROP_TYPE</c>.</summary>
internal enum DwmSystemBackdrop
{
    Auto = 0,
    None = 1,
    Mica = 2,
    Acrylic = 3,
    MicaAlt = 4,
}

/// <summary>
/// Thin wrapper over the DWM window attributes.
///
/// WPF-UI owns the backdrop for normal cases; this exists for the two things it does not expose
/// on a plain <see cref="System.Windows.Window"/> — corner preference and frame extension — plus
/// a raw fallback if its backdrop call fails. Do not drive the backdrop from both layers at once.
/// </summary>
internal static class Dwm
{
    internal static bool IsCompositionEnabled()
    {
        return NativeMethods.DwmIsCompositionEnabled(out bool enabled) == NativeMethods.S_OK && enabled;
    }

    /// <summary>
    /// Applies to all four corners at once — DWM offers no per-corner control.
    /// The top bar uses <see cref="DwmCornerPreference.DoNotRound"/> (see ADR-004); the floating
    /// dock in Faz 4 is the surface this exists for.
    /// </summary>
    internal static bool SetCornerPreference(IntPtr hwnd, DwmCornerPreference preference)
    {
        int value = (int)preference;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref value,
            sizeof(int)) == NativeMethods.S_OK;
    }

    internal static bool SetImmersiveDarkMode(IntPtr hwnd, bool enabled)
    {
        int value = enabled ? 1 : 0;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref value,
            sizeof(int)) == NativeMethods.S_OK;
    }

    internal static bool SetSystemBackdrop(IntPtr hwnd, DwmSystemBackdrop backdrop)
    {
        int value = (int)backdrop;
        return NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
            ref value,
            sizeof(int)) == NativeMethods.S_OK;
    }

    internal static bool TryGetSystemBackdrop(IntPtr hwnd, out DwmSystemBackdrop backdrop)
    {
        int result = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE,
            out int value,
            sizeof(int));

        backdrop = (DwmSystemBackdrop)value;
        return result == NativeMethods.S_OK;
    }

    /// <summary>
    /// Extends the glass frame across the whole client area (<c>MARGINS = -1</c>).
    /// Required for system backdrop on a <c>WindowStyle=None</c> window, which has no
    /// non-client glass region of its own.
    /// </summary>
    internal static bool ExtendFrameIntoClientArea(IntPtr hwnd)
    {
        var margins = new MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1,
        };

        return NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins) == NativeMethods.S_OK;
    }
}
