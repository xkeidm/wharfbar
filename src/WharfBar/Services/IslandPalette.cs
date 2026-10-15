using WharfBar.Models;

namespace WharfBar.Services;

internal readonly record struct IslandRgb(byte R, byte G, byte B);

/// <summary>
/// Pure color rules for the island tint. No WPF types so they stay unit-testable.
/// </summary>
internal static class IslandPalette
{
    /// <summary>Edge (px) of the downscaled cover the extractor reads.</summary>
    internal const int SampleSize = 32;

    private const double MinSaturation = 0.25;
    internal const double MinLuminance = 0.12;
    internal const double MaxLuminance = 0.90;

    /// <summary>
    /// Saturation- and mid-brightness-weighted average of tightly packed BGRA pixels.
    /// Returns null when nothing colorful and opaque is found (gray, transparent, empty).
    /// </summary>
    internal static IslandRgb? Dominant(ReadOnlySpan<byte> bgra)
    {
        double sumR = 0, sumG = 0, sumB = 0, sumW = 0;
        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
            if (a < 128)
            {
                continue;
            }

            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            if (max == 0)
            {
                continue;
            }

            double saturation = (max - min) / (double)max;
            if (saturation < MinSaturation)
            {
                continue;
            }

            double weight = saturation * (1 - Math.Abs(max / 255.0 - 0.6));
            sumR += r * weight;
            sumG += g * weight;
            sumB += b * weight;
            sumW += weight;
        }

        if (sumW <= 0)
        {
            return null;
        }

        return new IslandRgb(
            (byte)Math.Round(sumR / sumW),
            (byte)Math.Round(sumG / sumW),
            (byte)Math.Round(sumB / sumW));
    }

    /// <summary>Relative brightness 0..1 (Rec. 709 weights on gamma-encoded values).</summary>
    internal static double Luminance(IslandRgb c)
        => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>
    /// Final tint for a color mode. Null means "no tint" (theme resources only).
    /// <c>album</c> tints only while music plays and falls back to the accent when the
    /// cover has no usable color or its brightness is too extreme to read.
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

                double lum = Luminance(cover);
                return lum is < MinLuminance or > MaxLuminance ? accent : cover;
        }
    }
}
