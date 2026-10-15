using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>Colour roles of the themed dock logo tile.</summary>
internal readonly record struct BrandColors(Color Background, Color Pier, Color Lines, Color Sun);

/// <summary>
/// Pure palette for the themed logo tile: hue from the accent, lightness from the theme.
/// Saturation is capped per role so a loud accent still reads as a calm tile (ADR-019).
/// </summary>
internal static class BrandPalette
{
    internal static readonly Color DarkSun = Color.FromRgb(0xF2, 0xB6, 0x6B);

    internal static readonly Color LightSun = Color.FromRgb(0xE5, 0x9A, 0x45);

    internal static BrandColors For(Color accent, bool isDark)
    {
        ToHsl(accent, out double h, out double s, out _);
        return isDark
            ? new BrandColors(
                FromHsl(h, Math.Min(s, 0.45), 0.17),
                FromHsl(h, Math.Min(s, 0.35), 0.94),
                FromHsl(h, Math.Min(s, 0.50), 0.38),
                DarkSun)
            : new BrandColors(
                FromHsl(h, Math.Min(s, 0.40), 0.94),
                FromHsl(h, Math.Min(s, 0.55), 0.20),
                FromHsl(h, Math.Min(s, 0.40), 0.72),
                LightSun);
    }

    /// <summary>h in 0-360, s and l in 0-1. A grey input has h = 0 and s = 0.</summary>
    internal static void ToHsl(Color color, out double h, out double s, out double l)
    {
        double r = color.R / 255d;
        double g = color.G / 255d;
        double b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        l = (max + min) / 2d;

        if (d <= 0d)
        {
            h = 0d;
            s = 0d;
            return;
        }

        s = d / (1d - Math.Abs((2d * l) - 1d));
        if (max == r)
        {
            h = 60d * (((g - b) / d) % 6d);
        }
        else if (max == g)
        {
            h = 60d * (((b - r) / d) + 2d);
        }
        else
        {
            h = 60d * (((r - g) / d) + 4d);
        }

        if (h < 0d)
        {
            h += 360d;
        }
    }

    internal static Color FromHsl(double h, double s, double l)
    {
        double c = (1d - Math.Abs((2d * l) - 1d)) * s;
        double hp = (((h % 360d) + 360d) % 360d) / 60d;
        double x = c * (1d - Math.Abs((hp % 2d) - 1d));
        (double r, double g, double b) = hp switch
        {
            < 1d => (c, x, 0d),
            < 2d => (x, c, 0d),
            < 3d => (0d, c, x),
            < 4d => (0d, x, c),
            < 5d => (x, 0d, c),
            _ => (c, 0d, x),
        };
        double m = l - (c / 2d);
        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double unit) =>
        (byte)Math.Clamp(Math.Round(unit * 255d, MidpointRounding.AwayFromZero), 0d, 255d);
}
