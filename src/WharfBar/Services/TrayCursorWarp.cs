using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Pure rule for undoing the pointer warp a tray menu owner performs when it opens its context
/// menu. Measured on 25H2: right-clicking a tray button through UI Automation (focus + Menu key)
/// makes the owner — or the shell's menu helper — call <c>SetCursorPos</c> to anchor the menu at
/// the real tray icon 0�20 ms after the gesture (measured). The physical click the mirror replaces would
/// have done the same, but the user clicked in the top bar, so the pointer must not be thrown to
/// the bottom. Only a pointer that moved into the tray's anchor band is pulled back; a pointer the
/// user moved anywhere else is left alone.
/// </summary>
internal static class TrayCursorWarp
{
    /// <summary>Side margin around the real tray button that still counts as the anchor band.</summary>
    internal const int AnchorSideMarginPx = 160;

    /// <summary>Bottom band of the monitor (above the hidden taskbar slot) that counts as the anchor.</summary>
    internal const int AnchorBottomBandPx = 260;

    /// <summary>
    /// True when <paramref name="current"/> sits where an owner would warp the pointer to open its
    /// menu at the real tray icon (i.e. the pointer was thrown towards the bottom edge), and must
    /// therefore be put back to where the user clicked. The caller has already established that
    /// the pointer actually moved.
    /// </summary>
    internal static bool ShouldRestore(POINT current, RECT trayButton, RECT monitor)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0)
        {
            return false;
        }

        if (current.Y >= monitor.Bottom - AnchorBottomBandPx
            && current.Y <= monitor.Bottom + AnchorBottomBandPx
            && current.X >= monitor.Left - AnchorSideMarginPx
            && current.X <= monitor.Right + AnchorSideMarginPx)
        {
            return true;
        }

        if (trayButton.Width <= 0 || trayButton.Height <= 0)
        {
            return false;
        }

        return current.X >= trayButton.Left - AnchorSideMarginPx
            && current.X <= trayButton.Right + AnchorSideMarginPx
            && current.Y >= trayButton.Top - AnchorBottomBandPx
            && current.Y <= trayButton.Bottom + AnchorSideMarginPx;
    }
}
