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

    /// <summary>Restored-window indicator width at the 24 DIP glyph (DIP). Scales with the glyph.</summary>
    public const double PillWidthDip = 16d;

    /// <summary>Minimized-app indicator width at the 24 DIP glyph (DIP). Scales with the glyph.</summary>
    public const double MiniPillWidthDip = 8d;

    /// <summary>Glyph size the <see cref="PillWidthDip"/> constant is designed against.</summary>
    public const double PillWidthReferenceGlyphDip = 24d;

    /// <summary>Running indicator thickness (DIP).</summary>
    public const double PillThicknessDip = 3d;

    /// <summary>Empty space between the hover plate and the indicator (DIP).</summary>
    public const double PillGapDip = 3d;

    /// <summary>Distance from the button's bottom edge to the indicator (DIP).</summary>
    public const double PillBottomInsetDip = 2d;

    /// <summary>
    /// Bottom of the button reserved for the indicator, so the plate stops above the line.
    /// </summary>
    public static double IndicatorLaneDip => PillBottomInsetDip + PillThicknessDip + PillGapDip;

    /// <summary>Square behind the glyph. Same size for hover and for a running app.</summary>
    public static double HoverPlateSize(double buttonSize) =>
        Math.Max(0d, buttonSize - IndicatorLaneDip);

    /// <summary>Indicator width for a restored window. 16 DIP when the glyph is 24.</summary>
    public static double PillWidth(double glyphSize) =>
        glyphSize * (PillWidthDip / PillWidthReferenceGlyphDip);

    /// <summary>Short indicator under a minimized app. 8 DIP when the glyph is 24.</summary>
    public static double MiniPillWidth(double glyphSize) =>
        glyphSize * (MiniPillWidthDip / PillWidthReferenceGlyphDip);

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
