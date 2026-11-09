using System.Windows;
using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>
/// Flyout surface: a semi-transparent version of the theme background color (like the dock's Acrylic surface).
/// The content behind shows through faintly; it is regenerated on every open, so theme changes are followed.
/// </summary>
internal static class FlyoutSurface
{
    /// <summary>Surface opacity (~94%) — the remaining ~6% lets the content behind show through faintly.</summary>
    private const byte Alpha = 0xF0;

    /// <summary>The flat theme surface (no tint). Used by the panel silhouette and the idle pill.</summary>
    internal static SolidColorBrush Create()
    {
        Color color = SurfaceColor();
        color.A = Alpha;
        return Frozen(color);
    }

    /// <summary>
    /// The same surface with an album tint folded in, for the joined island pill.
    /// <paramref name="amount"/> is the tint layer's alpha (0..1); the composite is the exact
    /// source-over result of painting a tint path over the surface (see
    /// <see cref="IslandPalette.CompositeOver"/>), which is how the panel's top gradient stop is
    /// drawn. The joined pill therefore uses this helper with the panel's join alpha and reads as
    /// one continuous surface with the panel. Null/zero returns the plain surface, so restoring the
    /// neutral fill is the same one call.
    /// </summary>
    internal static SolidColorBrush CreateTinted(Color? tint, double amount)
    {
        if (tint is not Color tintColor || amount <= 0)
        {
            return Create();
        }

        // The same 94% surface Create() paints with: the pill fill must composite over the same
        // base alpha the panel's silhouette fill uses, or the join line picks up an opacity step.
        Color surface = SurfaceColor();
        surface.A = Alpha;
        return CreateTintedOver(surface, tintColor, amount);
    }

    /// <summary>
    /// Tint over an explicit base color: the closed pill must keep the bar's own fill (which can
    /// follow the wallpaper in transparent mode), so its base is the current
    /// <c>Bar.PillFill</c>/<c>Bar.PillFillHover</c> brush, not the flyout background. Same
    /// source-over composite, so the hue is added without changing what the capsule is made of.
    /// </summary>
    internal static SolidColorBrush CreateTintedOver(Color baseColor, Color? tint, double amount)
    {
        double t = Math.Clamp(amount, 0, 1);
        if (tint is not Color c || t <= 0)
        {
            return Frozen(baseColor);
        }

        (IslandRgb color, byte alpha) = IslandPalette.CompositeOver(
            new IslandRgb(baseColor.R, baseColor.G, baseColor.B),
            baseColor.A,
            new IslandRgb(c.R, c.G, c.B),
            (byte)Math.Round(t * 255));
        return Frozen(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color SurfaceColor()
        => (Application.Current?.TryFindResource("ApplicationBackgroundBrush") as SolidColorBrush)?.Color
            ?? Color.FromRgb(0x20, 0x20, 0x20);
}
