namespace WharfBar.Services;

/// <summary>
/// Resting geometry of the dock plate. Icons sit in a button; the plate wraps that row.
/// There is no magnification headroom — the window is the plate.
/// </summary>
internal static class DockPlateMetrics
{
    /// <summary>Padding on each side of the glyph inside its button (DIP).</summary>
    public const double ButtonPadDip = 8d;

    /// <summary>Gap between buttons (DIP).</summary>
    public const double GapDip = 4d;

    /// <summary>Inset from the plate's left and right edges to the first and last button (DIP).</summary>
    public const double HorizontalPaddingDip = 10d;

    /// <summary>Inset from the plate's top and bottom edges to the button row (DIP).</summary>
    public const double VerticalPaddingDip = 8d;

    /// <summary>WPF corner radius for tint and transparent plates (DIP). Mica uses DWM rounding.</summary>
    public const double CornerRadiusDip = 14d;

    /// <summary>Hover mark corner radius (DIP).</summary>
    public const double HoverCornerRadiusDip = 4d;

    /// <summary>Running indicator width (DIP).</summary>
    public const double PillWidthDip = 8d;

    /// <summary>Running indicator thickness (DIP).</summary>
    public const double PillThicknessDip = 3d;

    /// <summary>Distance from the button's bottom edge to the indicator (DIP).</summary>
    public const double PillInsetDip = 3d;

    public static double ButtonSize(double iconSize) => iconSize + (2d * ButtonPadDip);

    public static double PlateHeight(double iconSize) =>
        ButtonSize(iconSize) + (2d * VerticalPaddingDip);

    public static double StripWidth(int count, double iconSize)
    {
        if (count <= 0)
        {
            return 0d;
        }

        double button = ButtonSize(iconSize);
        return (count * button) + ((count - 1) * GapDip);
    }

    public static double PlateWidth(int count, double iconSize) =>
        StripWidth(count, iconSize) + (2d * HorizontalPaddingDip);
}
