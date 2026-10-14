namespace WharfBar.Services;

/// <summary>
/// Centers a dock popup on its target. WPF passes device pixels into the custom
/// placement callback; the gap is design DIP and must be scaled or the bubble
/// sits at the wrong height on a high-DPI monitor.
/// </summary>
internal static class DockTipPlacement
{
    internal const double GapDip = 25;

    internal static (double X, double Y) CenterAbove(
        double tipWidthPx,
        double tipHeightPx,
        double targetWidthPx,
        double dpiScale)
    {
        double scale = dpiScale > 0 ? dpiScale : 1;
        double x = (targetWidthPx - tipWidthPx) / 2d;
        double y = -tipHeightPx - (GapDip * scale);
        return (x, y);
    }
}
