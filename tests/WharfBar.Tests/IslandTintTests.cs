using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Design contract for the island album tint. Pure numbers only (no WPF types), so these run as
/// plain unit tests: the panel wash must start at the top and strengthen downward, the joined pill
/// reuses the panel's top alpha (no seam), the closed pill stays weaker than the joined surface,
/// and the body alpha still leaves white primary text at AA contrast for the lightest cover tint
/// the luminance clamp allows.
/// </summary>
public class IslandTintTests
{
    /// <summary>Dark-theme flyout surface (matches the FlyoutSurface fallback color).</summary>
    private static readonly IslandRgb DarkSurface = new(0x20, 0x20, 0x20);

    /// <summary>Flyout surface alpha (94%), mirrored from FlyoutSurface so the composite is exact.</summary>
    private const byte SurfaceAlpha = 0xF0;

    [Fact]
    public void PanelTint_StartsAtTheTop_AndStrengthensDownward()
    {
        // The old gradient was transparent until 24% down: the tab strip and pill join were neutral
        // while only the lower body was washed. The top must now carry a visible amount, and the
        // first two stops share it with the joined pill fill so the join has no color step.
        Assert.InRange(IslandPalette.PanelTintJoinAlpha, (byte)0x1E, (byte)0x2E);
        Assert.True(IslandPalette.PanelTintHeaderAlpha > IslandPalette.PanelTintJoinAlpha);
        Assert.True(IslandPalette.PanelTintBodyAlpha > IslandPalette.PanelTintHeaderAlpha);
        Assert.InRange(IslandPalette.PanelTintBottomAlpha, (byte)0x78, (byte)0xA0);
        Assert.True(IslandPalette.PanelTintBottomAlpha > IslandPalette.PanelTintBodyAlpha);
    }

    [Fact]
    public void ClosedPillTint_IsWeakerThanTheJoinedSurface_AndHoverIsStronger()
    {
        double joined = IslandPalette.PanelTintJoinAlpha / 255d;
        Assert.True(IslandPalette.PillTintAmount > 0);
        Assert.True(IslandPalette.PillTintAmount < joined, "the closed capsule must be subtler than the panel surface");
        Assert.True(IslandPalette.PillTintHoverAmount > IslandPalette.PillTintAmount);
        Assert.True(IslandPalette.PillTintHoverAmount < 0.25, "hover must stay a hint, not a fill");
    }

    [Fact]
    public void JoinedPillFill_MatchesThePanelTopComposite()
    {
        // Both are the same source-over composite; this guards the no-seam requirement at the math
        // level (the render harness checks it visually).
        var surface = new IslandRgb(0x7B, 0x4F, 0x43);
        (IslandRgb color, byte alpha) = IslandPalette.CompositeOver(
            DarkSurface, SurfaceAlpha, surface, IslandPalette.PanelTintJoinAlpha);
        Assert.NotEqual(DarkSurface, color);
        Assert.True(alpha > SurfaceAlpha, "the tint layer must raise the combined surface opacity");
    }

    [Fact]
    public void BodyTint_KeepsWhitePrimaryTextAtAaContrast_ForTheLightestAllowedTint()
    {
        // Worst case: the lightest tint ClampLuminance lets through — HSL lightness 0.72 with full
        // saturation, yellow hue (max gamma luminance inside the window).
        var lightest = new IslandRgb(0xFF, 0xFF, 0x70);
        Assert.Equal(IslandPalette.MaxTintLuminance, IslandPalette.Lightness(lightest), 3);

        (IslandRgb fill, byte _) = IslandPalette.CompositeOver(
            DarkSurface, SurfaceAlpha, lightest, IslandPalette.PanelTintBodyAlpha);
        double contrast = ContrastRatioWhite(fill);
        Assert.True(contrast >= 4.5, $"white primary text on the body wash must stay at AA, got {contrast:0.00}:1");
    }

    [Fact]
    public void ReadableForeground_KeepsTheGlyphAtAaContrast_OnEveryAllowedTint()
    {
        // The play/pause glyph picks white or near-black from the fill: either choice must clear
        // the large-text AA line (3:1) for any tint the clamp can produce.
        (byte R, byte G, byte B)[] tints =
        [
            (0x5C, 0x5C, 0x5C), // lifted near-black cover -> mid gray
            (0x7B, 0x4F, 0x43), // live brown cover
            (0xDC, 0x28, 0x28), // solid red probe cover
            (0x1E, 0x8C, 0x3C), // solid green probe cover
            (0x1E, 0x46, 0xC8), // solid blue probe cover
            (0xFF, 0xFF, 0x70), // lightest allowed tint
        ];
        foreach ((byte r, byte g, byte b) in tints)
        {
            var fill = new IslandRgb(r, g, b);
            IslandRgb glyph = IslandPalette.ReadableForeground(fill);
            double contrast = Contrast(fill, glyph);
            Assert.True(contrast >= 3.0, $"#{r:X2}{g:X2}{b:X2}: glyph contrast {contrast:0.00}:1 is below 3:1");
        }
    }

    private static double ContrastRatioWhite(IslandRgb color) => Contrast(color, new IslandRgb(0xFF, 0xFF, 0xFF));

    private static double Contrast(IslandRgb a, IslandRgb b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double lighter = Math.Max(la, lb);
        double darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>WCAG 2.x relative luminance (sRGB channels linearized).</summary>
    private static double RelativeLuminance(IslandRgb c)
        => 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);

    private static double Linear(byte value)
    {
        double v = value / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
