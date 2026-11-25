namespace WharfBar.Services;

/// <summary>
/// Places a dock popup (tooltip or preview card) next to its target. WPF passes device pixels into
/// the custom placement callback; the gap is design DIP and must be scaled or the bubble sits at the
/// wrong height on a high-DPI monitor.
/// </summary>
internal static class DockTipPlacement
{
    internal const double GapDip = 25;

    /// <summary>Horizontal bottom dock: the bubble is centered above the target.</summary>
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

    /// <summary>
    /// Vertical left dock: the bubble opens to the right of the target, vertically centered on it, so
    /// it stays clear of the screen edge the plate is anchored to.
    /// </summary>
    internal static (double X, double Y) BesideRight(
        double tipWidthPx,
        double tipHeightPx,
        double targetWidthPx,
        double targetHeightPx,
        double dpiScale)
    {
        double scale = dpiScale > 0 ? dpiScale : 1;
        double x = targetWidthPx + (GapDip * scale);
        double y = (targetHeightPx - tipHeightPx) / 2d;
        return (x, y);
    }

    /// <summary>Orientation-aware entry point: above for the bottom dock, beside-right for the left dock.</summary>
    internal static (double X, double Y) ForDock(
        bool vertical,
        double tipWidthPx,
        double tipHeightPx,
        double targetWidthPx,
        double targetHeightPx,
        double dpiScale) =>
        vertical
            ? BesideRight(tipWidthPx, tipHeightPx, targetWidthPx, targetHeightPx, dpiScale)
            : CenterAbove(tipWidthPx, tipHeightPx, targetWidthPx, dpiScale);
}
