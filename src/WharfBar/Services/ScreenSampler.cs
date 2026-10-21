using System.Windows.Media;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Thin screen sampling service for the island color picker; all GDI work is in
/// <see cref="ScreenBitBlt"/>. Reads a small box around a point (physical pixels) and
/// averages it so a stray sub-pixel edge does not dominate.
/// </summary>
internal static class ScreenSampler
{
    /// <summary>Box edge in pixels. 3 keeps the loupe honest without smearing edges.</summary>
    internal const int BoxSize = 3;

    /// <summary>
    /// Captures a <paramref name="size"/>-by-<paramref name="size"/> box at physical screen
    /// coordinates (<paramref name="x"/>, <paramref name="y"/>). The origin is clamped into the
    /// virtual screen so off-screen requests stay whole-ish instead of failing; a box larger
    /// than the virtual screen cannot fit and returns false.
    /// </summary>
    internal static bool TrySample(int x, int y, int size, out byte[] bgra, out int stride)
    {
        bgra = null!;
        stride = 0;
        if (size <= 0)
        {
            return false;
        }

        (int boundsX, int boundsY, int width, int height) = ScreenBitBlt.VirtualScreenBounds();
        if (size > width || size > height)
        {
            return false;
        }

        int clampedX = Math.Clamp(x, boundsX, boundsX + width - size);
        int clampedY = Math.Clamp(y, boundsY, boundsY + height - size);
        return ScreenBitBlt.TryCapture(clampedX, clampedY, size, out bgra, out stride);
    }

    /// <summary>
    /// Pure box-average over captured bytes (B-G-R-X per pixel, top-down rows). Transparent
    /// output for unusable input. Exposed as an internal seam so tests drive the math without
    /// a real capture (native GDI has no CI shim).
    /// </summary>
    internal static Color AverageBgra(byte[] bgra, int stride, int size)
    {
        if (bgra is null || size <= 0 || stride < size * 4 || bgra.Length < stride * size)
        {
            return Color.FromArgb(0, 0, 0, 0);
        }

        long redSum = 0;
        long greenSum = 0;
        long blueSum = 0;
        for (int row = 0; row < size; row++)
        {
            int rowStart = row * stride;
            for (int col = 0; col < size; col++)
            {
                int offset = rowStart + (col * 4);
                blueSum += bgra[offset];
                greenSum += bgra[offset + 1];
                redSum += bgra[offset + 2];
            }
        }

        int count = size * size;
        return Color.FromRgb(
            (byte)Math.Round(redSum / (double)count),
            (byte)Math.Round(greenSum / (double)count),
            (byte)Math.Round(blueSum / (double)count));
    }

    /// <summary>
    /// Average color of a <see cref="BoxSize"/> box centered at physical screen coordinates
    /// (<paramref name="x"/>, <paramref name="y"/>). Transparent color when capture failed —
    /// the picker treats that as "no sample this tick".
    /// </summary>
    internal static Color RgbAt(int x, int y)
    {
        int origin = BoxSize / 2;
        if (!TrySample(x - origin, y - origin, BoxSize, out byte[] bgra, out int stride))
        {
            return Color.FromArgb(0, 0, 0, 0);
        }

        return AverageBgra(bgra, stride, BoxSize);
    }
}
