namespace WharfBar.Services;

/// <summary>
/// Horizontal placement for the island popup. <c>Placement=Bottom</c> lines up the
/// popup's left edge with the pill, so a wider panel sits to the right of the capsule
/// until this offset pulls its center back onto the pill. Widths are DIP; screen and
/// work-area edges are physical pixels.
/// </summary>
internal static class IslandPopupAlign
{
    /// <summary>
    /// Arranged width to pass into <see cref="HorizontalOffset"/>. Zero means the popup
    /// has not been arranged yet. The XAML width must not be substituted: a fallback
    /// wider than the real panel makes the offset too negative and the panel opens
    /// to the left of the pill.
    /// </summary>
    internal static double WidthForPlacement(double arrangedWidthDip)
    {
        if (double.IsNaN(arrangedWidthDip) || arrangedWidthDip <= 1)
        {
            return 0;
        }

        return arrangedWidthDip;
    }

    /// <summary>
    /// DIP <c>HorizontalOffset</c> that centers <paramref name="panelWidthDip"/> on the pill,
    /// then shifts it so the panel stays inside the work area. A non-positive panel or pill
    /// width, or a non-positive DPI, returns 0 (layout has not been measured yet).
    /// When the work area is empty or inverted, the unclamped center is returned.
    /// </summary>
    internal static double HorizontalOffset(
        double pillWidthDip,
        double panelWidthDip,
        double pillLeftPx,
        double dpi,
        double workLeftPx,
        double workRightPx)
    {
        if (pillWidthDip <= 0 || panelWidthDip <= 0 || dpi <= 0)
        {
            return 0;
        }

        double offset = (pillWidthDip - panelWidthDip) / 2d;
        if (!(workRightPx > workLeftPx))
        {
            return offset;
        }

        double scale = dpi / 96d;
        double leftPx = pillLeftPx + (offset * scale);
        if (leftPx < workLeftPx)
        {
            offset += (workLeftPx - leftPx) / scale;
        }

        leftPx = pillLeftPx + (offset * scale);
        double rightPx = leftPx + (panelWidthDip * scale);
        if (rightPx > workRightPx)
        {
            offset -= (rightPx - workRightPx) / scale;
        }

        return offset;
    }
}
