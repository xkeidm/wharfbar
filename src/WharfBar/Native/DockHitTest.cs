namespace WharfBar.Native;

/// <summary>
/// Placeholder for future edge-band / WS_EX_TRANSPARENT hit-testing used by auto-hide.
/// Faz 4 keeps the dock fully interactive — do not call SetWindowLong to strip hit-testing.
/// </summary>
internal static class DockHitTest
{
    /// <summary>Reserved. No-op until auto-hide is enabled.</summary>
    internal static void ApplyInteractive(IntPtr hwnd)
    {
        _ = hwnd;
        // Future: ensure WS_EX_TRANSPARENT is cleared so clicks reach the dock.
    }

    /// <summary>Reserved. No-op until auto-hide is enabled.</summary>
    internal static void ApplyPassthrough(IntPtr hwnd)
    {
        _ = hwnd;
        // Future: set WS_EX_TRANSPARENT while the dock is hidden/collapsed for click-through.
    }
}
