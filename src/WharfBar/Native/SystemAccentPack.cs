using WharfBar.Services;

namespace WharfBar.Native;

internal static class SystemAccentPack
{
    internal static uint ToAccentDword(IslandRgb c)
        => (uint)(c.R | (c.G << 8) | (c.B << 16) | (0xFF << 24));

    /// <summary>Eight BGRA colors (A=0). Index 3 is the base; others are light/dark steps.</summary>
    internal static byte[] BuildAccentPalette(IslandRgb c)
    {
        var palette = new byte[32];
        IslandRgb[] shades =
        [
            Lighten(c, 0.6),
            Lighten(c, 0.4),
            Lighten(c, 0.2),
            c,
            Darken(c, 0.2),
            Darken(c, 0.4),
            Darken(c, 0.6),
            Darken(c, 0.8),
        ];
        for (int i = 0; i < 8; i++)
        {
            int o = i * 4;
            IslandRgb s = shades[i];
            palette[o] = s.B;
            palette[o + 1] = s.G;
            palette[o + 2] = s.R;
            palette[o + 3] = 0;
        }

        return palette;
    }

    private static IslandRgb Lighten(IslandRgb c, double t)
        => new(
            (byte)Math.Round(c.R + (255 - c.R) * t),
            (byte)Math.Round(c.G + (255 - c.G) * t),
            (byte)Math.Round(c.B + (255 - c.B) * t));

    private static IslandRgb Darken(IslandRgb c, double t)
        => new(
            (byte)Math.Round(c.R * (1 - t)),
            (byte)Math.Round(c.G * (1 - t)),
            (byte)Math.Round(c.B * (1 - t)));
}
