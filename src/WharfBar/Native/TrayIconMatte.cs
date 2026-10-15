namespace WharfBar.Native;

/// <summary>
/// Removes the taskbar paint from a captured tray icon. <c>PrintWindow</c> renders the tray with
/// its own opaque background (dark or light theme fill), so a raw crop shows as a coloured square
/// on the top bar. The background colour is sampled from a ring just <b>outside</b> the icon art
/// (the button's padding — the art itself can touch every edge of its own rect), then every pixel
/// is "colour-to-alpha"-unblended against it: alpha is the smallest value that explains the pixel
/// as <c>fg·a + bg·(1−a)</c> with a valid <c>fg</c>, which keeps anti-aliased edges soft in both
/// themes. Pure; unit-tested in <c>TrayIconMatteTests</c>.
/// </summary>
internal static class TrayIconMatte
{
    /// <summary>Max channel distance treated as background noise (acrylic grain, JPEG-ish dither).</summary>
    internal const int NoiseTolerance = 12;

    /// <summary>Ring width sampled around the icon rect.</summary>
    internal const int RingPx = 3;

    /// <summary>
    /// <paramref name="bgrx"/>: 4 bytes per pixel, B,G,R,(ignored). Returns straight-alpha Bgra32.
    /// Pixels within <paramref name="tolerance"/> of the background become fully transparent; the
    /// next <paramref name="tolerance"/> levels ramp in so noise does not leave a speckled halo.
    /// </summary>
    internal static byte[] RemoveBackground(ReadOnlySpan<byte> bgrx, BgrColor background, int tolerance = NoiseTolerance)
    {
        var output = new byte[bgrx.Length - (bgrx.Length % 4)];
        int tol = Math.Max(1, tolerance);
        for (int i = 0; i + 3 < bgrx.Length; i += 4)
        {
            int b = bgrx[i];
            int g = bgrx[i + 1];
            int r = bgrx[i + 2];
            int distance = Math.Max(
                Math.Abs(b - background.B),
                Math.Max(Math.Abs(g - background.G), Math.Abs(r - background.R)));
            if (distance <= tol)
            {
                continue; // stays 0,0,0,0
            }

            double alpha = Math.Max(
                ChannelAlpha(b, background.B),
                Math.Max(ChannelAlpha(g, background.G), ChannelAlpha(r, background.R)));
            if (alpha <= 0)
            {
                continue;
            }

            output[i] = Unblend(b, background.B, alpha);
            output[i + 1] = Unblend(g, background.G, alpha);
            output[i + 2] = Unblend(r, background.R, alpha);
            double ramp = Math.Min(1.0, (distance - tol) / (double)tol);
            output[i + 3] = (byte)Math.Clamp((int)Math.Round(255 * alpha * ramp), 0, 255);
        }

        return output;
    }

    /// <summary>Per-channel median — robust against a neighbour icon or hover edge in the ring.</summary>
    internal static BgrColor MedianColor(IReadOnlyList<BgrColor> samples)
    {
        if (samples.Count == 0)
        {
            return default;
        }

        var bs = new byte[samples.Count];
        var gs = new byte[samples.Count];
        var rs = new byte[samples.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            bs[i] = samples[i].B;
            gs[i] = samples[i].G;
            rs[i] = samples[i].R;
        }

        Array.Sort(bs);
        Array.Sort(gs);
        Array.Sort(rs);
        int mid = samples.Count / 2;
        return new BgrColor(bs[mid], gs[mid], rs[mid]);
    }

    /// <summary>True when at least one pixel kept some opacity.</summary>
    internal static bool HasVisiblePixels(ReadOnlySpan<byte> bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static double ChannelAlpha(int c, int bg)
    {
        if (c > bg)
        {
            return bg >= 255 ? 0 : (c - bg) / (255.0 - bg);
        }

        if (c < bg)
        {
            return bg <= 0 ? 0 : (bg - c) / (double)bg;
        }

        return 0;
    }

    private static byte Unblend(int c, int bg, double alpha) =>
        (byte)Math.Clamp((int)Math.Round(bg + ((c - bg) / alpha)), 0, 255);
}

internal readonly record struct BgrColor(byte B, byte G, byte R);
