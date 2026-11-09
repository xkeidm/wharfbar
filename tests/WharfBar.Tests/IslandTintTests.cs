using System.Windows.Media;
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
    public void ClosedPillTint_AmountGrowsWithThePlateAlpha_AndStaysBounded()
    {
        // The reference plate (dark-theme rest/hover) keeps the historical amounts.
        Assert.Equal(
            IslandPalette.PillTintAmount,
            IslandPalette.ClosedPillAmount(IslandPalette.PillTintReferencePlateAlpha, hover: false),
            6);
        Assert.Equal(
            IslandPalette.PillTintHoverAmount,
            IslandPalette.ClosedPillAmount(IslandPalette.PillTintHoverReferencePlateAlpha, hover: true),
            6);

        // The transparent primary bar's light-wallpaper plate (#B3FFFFFF) is ~70% opaque and needs a
        // stronger layer, or the same album hue washes out there while it stays visible on a theme bar.
        Assert.True(IslandPalette.ClosedPillAmount(0xB3, hover: false) > IslandPalette.PillTintAmount);
        Assert.True(IslandPalette.ClosedPillAmount(0x99, hover: true) > IslandPalette.PillTintHoverAmount);

        // A less opaque plate never produces a weaker layer than the historical amount.
        Assert.True(IslandPalette.ClosedPillAmount(0x0F, hover: true) >= IslandPalette.PillTintHoverAmount);

        // Documented ceiling.
        Assert.True(IslandPalette.ClosedPillAmount(0xFF, hover: false) <= IslandPalette.MaxPillTintAmount);
        Assert.True(IslandPalette.ClosedPillAmount(0xFF, hover: true) <= IslandPalette.MaxPillTintHoverAmount);
        Assert.True(IslandPalette.MaxPillTintAmount <= 0x60 / 255d);
        Assert.True(IslandPalette.MaxPillTintHoverAmount <= 0x80 / 255d);
    }

    /// <summary>
    /// The closed capsule freezes a composite of the album tint over <c>Bar.PillFill</c>; the
    /// wallpaper-driven primary bar can swap that plate (light/dark strip), so the cache must key on
    /// the bar palette revision, not only on tint+hover.
    /// </summary>
    [Fact]
    public void ClosedPillTintCache_InvalidatesWhenTintHoverOrPaletteRevisionChanges()
    {
        var tint = new IslandRgb(0xDC, 0x28, 0x28);
        var state = new ClosedPillTintState(tint, Hover: false, PaletteRevision: 3);

        Assert.True(state.Matches(tint, hover: false, paletteRevision: 3));
        Assert.False(state.Matches(tint, hover: false, paletteRevision: 4), "a new bar palette must repaint");
        Assert.False(state.Matches(tint, hover: true, paletteRevision: 3), "a hover change must repaint");
        Assert.False(state.Matches(null, hover: false, paletteRevision: 3), "a tint change must repaint");
        Assert.False(ClosedPillTintState.Invalid.Matches(null, hover: false, paletteRevision: 0));
    }

    /// <summary>
    /// Regression for the two-monitor color report: the same album tint composited over the theme
    /// plate and over the transparent-primary wallpaper plates must keep the same perceived hue and
    /// a comparable saturation (the light-wallpaper plate previously washed the capsule out).
    /// </summary>
    [Fact]
    public void ClosedPillTint_HasTheSamePerceivedHue_OnEveryPaletteBase()
    {
        var tint = new IslandRgb(0xDC, 0x28, 0x28);
        Color tintColor = Color.FromRgb(tint.R, tint.G, tint.B);
        Color gray = Color.FromRgb(0x80, 0x80, 0x80);

        // Bar.PillFill (rest) and Bar.PillFillHover (hover) plates from TopBarContrastPalette:
        // theme (Mica/Tint + every secondary bar), LightText (dark wallpaper, primary transparent)
        // and DarkText (light wallpaper, primary transparent).
        (string Name, Color Plate, bool Hover)[] cases =
        [
            ("theme rest", Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF), false),
            ("LightText rest", Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF), false),
            ("DarkText rest", Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF), false),
            ("theme hover", Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF), true),
            ("LightText hover", Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), true),
            ("DarkText hover", Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), true),
        ];

        var restSats = new List<double>();
        var restHues = new List<double>();
        var hoverSats = new List<double>();
        var hoverHues = new List<double>();
        foreach ((string _, Color plate, bool hover) in cases)
        {
            Color fill = FlyoutSurface
                .CreateTintedOver(plate, tintColor, IslandPalette.ClosedPillAmount(plate.A, hover))
                .Color;
            Color shown = Composite(fill, gray);
            (double hue, double saturation, double _) = Hsv(shown);
            (hover ? hoverSats : restSats).Add(saturation);
            (hover ? hoverHues : restHues).Add(hue);
        }

        Assert.InRange(restSats.Max() - restSats.Min(), 0, 0.03);
        Assert.InRange(hoverSats.Max() - hoverSats.Min(), 0, 0.03);
        Assert.InRange(HueSpread(restHues), 0, 6);
        Assert.InRange(HueSpread(hoverHues), 0, 6);
    }

    private static double HueSpread(List<double> hues)
    {
        double max = hues.Max();
        double min = hues.Min();
        double direct = max - min;
        return Math.Min(direct, 360 - direct);
    }

    private static Color Composite(Color foreground, Color background)
    {
        double a = foreground.A / 255d;
        return Color.FromRgb(
            (byte)Math.Round(foreground.R * a + background.R * (1 - a)),
            (byte)Math.Round(foreground.G * a + background.G * (1 - a)),
            (byte)Math.Round(foreground.B * a + background.B * (1 - a)));
    }

    /// <summary>HSV hue in degrees (0..360) and saturation 0..1.</summary>
    private static (double Hue, double Saturation, double Value) Hsv(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double saturation = max <= 0 ? 0 : delta / max;
        double hue = 0;
        if (delta > 1e-9)
        {
            if (Math.Abs(max - r) < 1e-9)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (Math.Abs(max - g) < 1e-9)
            {
                hue = 60 * (((b - r) / delta) + 2);
            }
            else
            {
                hue = 60 * (((r - g) / delta) + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return (hue, saturation, max);
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
