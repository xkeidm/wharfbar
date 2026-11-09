using System.Windows;

namespace WharfBar.Services;

/// <summary>
/// Hit geometry for the island pill and the panel opened from it. The panel is a separate
/// <c>StaysOpen</c> popup window whose surface (and transparent shadow pad) overlaps the
/// pill's lower edge, so it can take the pointer and raise a fake <c>MouseLeave</c> on the
/// pill while the cursor never left the island. The window samples the real cursor against
/// both rects instead of trusting <c>IsMouseOver</c>; this type is the pure, unit-tested
/// part of that rule. Coordinates are the element-local values
/// <see cref="System.Windows.Input.Mouse.GetPosition"/> returns.
/// </summary>
internal static class IslandHoverRegion
{
    /// <summary>
    /// True when <paramref name="point"/> lies inside a <paramref name="width"/> by
    /// <paramref name="height"/> rect whose top-left corner is the origin. Edges count as
    /// inside. A zero or negative size (collapsed or not-yet-arranged element) never hits.
    /// </summary>
    internal static bool Contains(Point point, double width, double height)
    {
        return width > 0
            && height > 0
            && point.X >= 0
            && point.X <= width
            && point.Y >= 0
            && point.Y <= height;
    }

    /// <summary>
    /// True when the cursor is over the island: the pill, or the open panel's popup child
    /// (its transparent shadow pad included, so resting on the pad keeps a hover panel open).
    /// <paramref name="panelPoint"/> is null while no panel is open.
    /// </summary>
    internal static bool IsOverIsland(Point pillPoint, Size pillSize, Point? panelPoint, Size panelSize)
    {
        if (Contains(pillPoint, pillSize.Width, pillSize.Height))
        {
            return true;
        }

        return panelPoint is Point panel && Contains(panel, panelSize.Width, panelSize.Height);
    }
}
