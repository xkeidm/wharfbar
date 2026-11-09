using WharfBar.Models;

namespace WharfBar.Services;

internal readonly record struct IslandRgb(byte R, byte G, byte B);

/// <summary>
/// Closed-pill tint cache key: the resolved session tint, the pointer hover state and the bar
/// palette revision. Pure so the "the capsule must repaint when the bar palette changes" rule is
/// unit-testable; the window keeps one instance (<see cref="Invalid"/> forces the next paint).
/// </summary>
internal readonly record struct ClosedPillTintState(IslandRgb? Tint, bool Hover, int PaletteRevision)
{
    /// <summary>State that never matches, so the next closed-pill update always paints.</summary>
    internal static ClosedPillTintState Invalid => new(null, false, -1);

    internal bool Matches(IslandRgb? tint, bool hover, int paletteRevision)
        => Tint == tint && Hover == hover && PaletteRevision == paletteRevision;
}

/// <summary>
/// Pure color rules for the island tint. No WPF types so they stay unit-testable.
/// </summary>
internal static class IslandPalette
{
    /// <summary>Edge (px) of the downscaled cover the extractor reads.</summary>
    internal const int SampleSize = 32;

    /// <summary>Minimum saturation for a pixel to vote for the vibrant dominant color.</summary>
    private const double VividSaturation = 0.22;

    /// <summary>How much the plain average tempers the vibrant dominant (0..1, share of the average).</summary>
    private const double AverageMix = 0.16;

    /// <summary>Luminance window used by <see cref="WallpaperAccent"/> (kept for its existing contract).</summary>
    internal const double MinLuminance = 0.12;
    internal const double MaxLuminance = 0.90;

    /// <summary>Luminance window the final island tint is remapped into so it stays readable on either theme.</summary>
    internal const double MinTintLuminance = 0.36;
    internal const double MaxTintLuminance = 0.72;

    // ---- Island tint strength design ---------------------------------------------------------
    // Tint-layer alphas (not white/black mixes), so the wash keeps the cover's own hue: each
    // value is painted as a color layer with that alpha over the flyout surface. The panel
    // gradient walks top to bottom; the joined pill fill reuses the join amount verbatim (same
    // composite = no seam), and the closed pill uses the weaker pair so the collapsed capsule
    // only hints at the album color.

    /// <summary>
    /// Panel tint alpha at the panel's top edge and the pill join line. The joined pill fill uses
    /// this same amount, and the two top gradient stops keep it too: the panel top edge, the
    /// fillets and the capsule then resolve to one identical composite (no seam). The wash
    /// strengthens from here down.
    /// </summary>
    internal const byte PanelTintJoinAlpha = 0x28;

    /// <summary>Panel tint alpha over the tab strip / header band.</summary>
    internal const byte PanelTintHeaderAlpha = 0x32;

    /// <summary>
    /// Panel tint alpha over the body (cover, title, transport). Kept at the strongest value that
    /// still leaves white primary text at ≥4.5:1 over the dark flyout surface for the lightest
    /// cover tint <see cref="MaxTintLuminance"/> allows (documented by IslandTintTests).
    /// </summary>
    internal const byte PanelTintBodyAlpha = 0x60;

    /// <summary>Panel tint alpha at the bottom edge; the deepest wash (below the content).</summary>
    internal const byte PanelTintBottomAlpha = 0x8A;

    /// <summary>Share of black blended into the bottom stop so the panel keeps depth.</summary>
    internal const double PanelTintDeepShade = 0.16;

    /// <summary>
    /// Closed-pill tint amount (~12% layer alpha); weaker than the joined panel surface so the
    /// collapsed capsule is a hint, but visible next to the plain fill in the dark theme.
    /// </summary>
    internal const double PillTintAmount = 0x1E / 255d;

    /// <summary>Closed-pill tint amount on hover (the local fill bypasses the style hover fill).</summary>
    internal const double PillTintHoverAmount = 0x36 / 255d;

    /// <summary>
    /// Capsule plate alpha the closed-pill amounts above are calibrated for: the dark theme's
    /// translucent rest plate (<c>ControlFillColorDefaultBrush #0FFFFFFF</c>). A more opaque plate
    /// (the transparent primary bar's wallpaper-driven light-wallpaper plate is <c>#B3FFFFFF</c>)
    /// dilutes the same tint layer, so <see cref="ClosedPillAmount"/> scales the amount up to keep
    /// the perceived hue shift the same on every bar.
    /// </summary>
    internal const byte PillTintReferencePlateAlpha = 0x0F;

    /// <summary>Hover counterpart of <see cref="PillTintReferencePlateAlpha"/> (dark theme hover plate, <c>#24FFFFFF</c>).</summary>
    internal const byte PillTintHoverReferencePlateAlpha = 0x24;

    /// <summary>
    /// How fast the closed-pill tint grows with the plate alpha. Calibrated so the perceived
    /// saturation delta of the capsule matches the theme case across the palette bases
    /// (asserted by <c>IslandTintTests.ClosedPillTint_HasTheSamePerceivedHue_OnEveryPaletteBase</c>).
    /// </summary>
    private const double PillTintPlateAlphaSlope = 0.71;

    /// <summary>Upper bound of the normalized closed-pill tint (rest).</summary>
    internal const double MaxPillTintAmount = 0x60 / 255d;

    /// <summary>Upper bound of the normalized closed-pill tint (hover).</summary>
    internal const double MaxPillTintHoverAmount = 0x80 / 255d;

    /// <summary>
    /// Closed-pill tint amount for the given capsule plate alpha and hover state. The base amounts
    /// are calibrated for the theme's near-transparent plate; a wallpaper-driven opaque plate needs
    /// a stronger layer or the album hue washes out there while it stays visible on the other bar.
    /// The result never drops below the base amount (a less opaque plate keeps today's behaviour)
    /// and stays under the documented ceiling.
    /// </summary>
    internal static double ClosedPillAmount(byte plateAlpha, bool hover)
    {
        double reference = (hover ? PillTintHoverReferencePlateAlpha : PillTintReferencePlateAlpha) / 255d;
        double baseAmount = hover ? PillTintHoverAmount : PillTintAmount;
        double scaled = baseAmount * (1 + PillTintPlateAlphaSlope * (plateAlpha / 255d - reference));
        double max = hover ? MaxPillTintHoverAmount : MaxPillTintAmount;
        return Math.Clamp(scaled, baseAmount, max);
    }

    /// <summary>
    /// Luminance at which <see cref="ReadableForeground"/> flips from white to near-black.
    /// The (gamma-encoded) mid-point is where white and black reach the same WCAG contrast
    /// against the fill, so neither side of it loses more contrast than it has to.
    /// </summary>
    internal const double ReadableLuminanceThreshold = 0.5;

    /// <summary>Near-black glyph on a bright tint (not pure black, so the icon does not look punched out).</summary>
    private static readonly IslandRgb GlyphOnBright = new(0x12, 0x12, 0x12);

    /// <summary>White glyph on a dark tint (the dark theme's on-accent color).</summary>
    private static readonly IslandRgb GlyphOnDark = new(0xFF, 0xFF, 0xFF);

    /// <summary>
    /// Weighted dominant color of tightly packed BGRA pixels: colorful pixels with a mid
    /// brightness win, then the plain average tempers the result so a tiny neon spot does not
    /// hijack the whole tint. When the cover has no colorful pixel at all (gray, monochrome,
    /// dark photo) the average color is returned instead of null, so the island still follows
    /// the album. Null only when nothing opaque remains (empty, fully transparent).
    /// </summary>
    internal static IslandRgb? Dominant(ReadOnlySpan<byte> bgra)
    {
        double vividR = 0, vividG = 0, vividB = 0, vividWeight = 0;
        double averageR = 0, averageG = 0, averageB = 0;
        int opaque = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
            if (a < 128)
            {
                continue;
            }

            opaque++;
            averageR += r;
            averageG += g;
            averageB += b;

            int max = Math.Max(r, Math.Max(g, b));
            if (max == 0)
            {
                // Pure black: it counts toward the average (a dark cover is still the cover) but has no hue.
                continue;
            }

            int min = Math.Min(r, Math.Min(g, b));
            double saturation = (max - min) / (double)max;
            if (saturation < VividSaturation)
            {
                continue;
            }

            // Prefer saturated pixels and a mid brightness; very dark/very bright pixels weigh less.
            double value = max / 255.0;
            double weight = saturation * saturation * (1 - Math.Abs(value - 0.6));
            if (weight <= 0)
            {
                continue;
            }

            vividR += r * weight;
            vividG += g * weight;
            vividB += b * weight;
            vividWeight += weight;
        }

        if (opaque == 0)
        {
            return null;
        }

        var average = new IslandRgb(
            ClampByte(averageR / opaque),
            ClampByte(averageG / opaque),
            ClampByte(averageB / opaque));
        if (vividWeight <= 0)
        {
            return average;
        }

        var dominant = new IslandRgb(
            ClampByte(vividR / vividWeight),
            ClampByte(vividG / vividWeight),
            ClampByte(vividB / vividWeight));
        return Blend(dominant, average, AverageMix);
    }

    /// <summary>Relative brightness 0..1 (Rec. 709 weights on gamma-encoded values).</summary>
    internal static double Luminance(IslandRgb c)
        => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>HSL lightness 0..1 of a color; this is the axis <see cref="ClampLuminance"/> remaps.</summary>
    internal static double Lightness(IslandRgb c)
        => (Math.Max(c.R, Math.Max(c.G, c.B)) + Math.Min(c.R, Math.Min(c.G, c.B))) / (2.0 * 255);

    /// <summary>HSV saturation 0..1 of a color.</summary>
    internal static double Saturation(IslandRgb c)
    {
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        if (max == 0)
        {
            return 0;
        }

        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        return (max - min) / (double)max;
    }

    /// <summary>Linear blend: <paramref name="amount"/> 0 keeps <paramref name="from"/>, 1 returns <paramref name="to"/>.</summary>
    internal static IslandRgb Blend(IslandRgb from, IslandRgb to, double amount)
    {
        double t = Math.Clamp(amount, 0, 1);
        return new IslandRgb(
            BlendChannel(from.R, to.R, t),
            BlendChannel(from.G, to.G, t),
            BlendChannel(from.B, to.B, t));
    }

    /// <summary>
    /// Keeps hue and saturation while moving the HSL lightness into <paramref name="min"/>..<paramref name="max"/>.
    /// A color that is already inside the window is returned unchanged; a very dark cover is lifted and a
    /// blown-out one is pulled down so the tint stays legible instead of falling back to the accent.
    /// </summary>
    internal static IslandRgb ClampLuminance(IslandRgb color, double min, double max)
    {
        double low = Math.Clamp(Math.Min(min, max), 0, 1);
        double high = Math.Clamp(Math.Max(min, max), 0, 1);
        ToHsl(color, out double hue, out double saturation, out double lightness);
        double clamped = Math.Clamp(lightness, low, high);
        if (Math.Abs(clamped - lightness) < 1e-6)
        {
            return color;
        }

        return FromHsl(hue, saturation, clamped);
    }

    /// <summary>
    /// Final tint for a color mode. Null means "no tint" (theme resources only).
    /// <c>album</c> tints only while music plays and uses the cover color with its luminance
    /// remapped into a readable window; it falls back to the accent only when no cover color
    /// exists at all. <c>system</c> always uses the accent.
    /// </summary>
    internal static IslandRgb? Tint(string? mode, IslandRgb? album, IslandRgb accent, bool musicPlaying)
    {
        switch (IslandNormalizer.NormalizeColorMode(mode))
        {
            case IslandConfig.ColorModeNeutral:
                return null;
            case IslandConfig.ColorModeSystem:
                return accent;
            default:
                if (!musicPlaying)
                {
                    return null;
                }

                if (album is not IslandRgb cover)
                {
                    return accent;
                }

                return ClampLuminance(cover, MinTintLuminance, MaxTintLuminance);
        }
    }

    /// <summary>
    /// Source-over composite of a tint layer on a surface layer. Both layers carry their own alpha
    /// (0..255); the result is the straight color and combined alpha a renderer would show for
    /// "tint painted over surface". The panel paints the top of its gradient exactly this way over
    /// the flyout surface, so <see cref="FlyoutSurface.CreateTinted"/> can build one flat pill fill
    /// from the same numbers and the joined pill cannot show a color seam against the panel.
    /// </summary>
    internal static (IslandRgb Color, byte Alpha) CompositeOver(
        IslandRgb surface, byte surfaceAlpha, IslandRgb tint, byte tintAlpha)
    {
        double sa = surfaceAlpha / 255.0;
        double ta = tintAlpha / 255.0;
        double resultAlpha = ta + sa * (1 - ta);
        if (resultAlpha <= 1e-6)
        {
            return (new IslandRgb(0, 0, 0), 0);
        }

        return (
            new IslandRgb(
                CompositeChannel(surface.R, tint.R, sa, ta, resultAlpha),
                CompositeChannel(surface.G, tint.G, sa, ta, resultAlpha),
                CompositeChannel(surface.B, tint.B, sa, ta, resultAlpha)),
            ClampByte(resultAlpha * 255));
    }

    /// <summary>
    /// Contrast-aware icon color for a filled circle: white on a dark fill, near-black on a bright
    /// one. Used for the play/pause glyph on the album-tinted transport button; the system accent
    /// fallback keeps its own theme on-accent color.
    /// </summary>
    internal static IslandRgb ReadableForeground(IslandRgb background)
        => Luminance(background) >= ReadableLuminanceThreshold ? GlyphOnBright : GlyphOnDark;

    private static byte CompositeChannel(byte surface, byte tint, double surfaceAlpha, double tintAlpha, double resultAlpha)
        => ClampByte((tint * tintAlpha + surface * surfaceAlpha * (1 - tintAlpha)) / resultAlpha);

    private static byte BlendChannel(byte from, byte to, double amount)
        => ClampByte(from + (to - from) * amount);

    private static byte ClampByte(double value)
        => (byte)Math.Round(Math.Clamp(value, 0, 255));

    private static void ToHsl(IslandRgb c, out double hue, out double saturation, out double lightness)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        lightness = (max + min) / 2;
        double delta = max - min;
        if (delta <= 1e-9)
        {
            hue = 0;
            saturation = 0;
            return;
        }

        saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        if (max == r)
        {
            hue = ((g - b) / delta + (g < b ? 6 : 0)) / 6;
        }
        else if (max == g)
        {
            hue = ((b - r) / delta + 2) / 6;
        }
        else
        {
            hue = ((r - g) / delta + 4) / 6;
        }
    }

    private static IslandRgb FromHsl(double hue, double saturation, double lightness)
    {
        if (saturation <= 1e-9)
        {
            byte gray = ClampByte(lightness * 255);
            return new IslandRgb(gray, gray, gray);
        }

        double q = lightness < 0.5
            ? lightness * (1 + saturation)
            : lightness + saturation - lightness * saturation;
        double p = 2 * lightness - q;
        return new IslandRgb(
            ClampByte(HueToChannel(p, q, hue + 1.0 / 3) * 255),
            ClampByte(HueToChannel(p, q, hue) * 255),
            ClampByte(HueToChannel(p, q, hue - 1.0 / 3) * 255));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }
        else if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + (q - p) * 6 * t;
        }

        if (t < 0.5)
        {
            return q;
        }

        if (t < 2.0 / 3)
        {
            return p + (q - p) * (2.0 / 3 - t) * 6;
        }

        return p;
    }
}
