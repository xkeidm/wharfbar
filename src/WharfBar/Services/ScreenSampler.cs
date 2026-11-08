using System.Windows.Media;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Thin screen sampling service for the island color picker; all GDI work is in
/// <see cref="ScreenBitBlt"/>. Crops the loupe box out of the frozen monitor frame
/// (<see cref="TryCropBox"/>); when no frame is available, <see cref="TrySample"/> captures a
/// live screen box as the low-quality fallback.
/// </summary>
internal static class ScreenSampler
{
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
    /// Crops a <paramref name="size"/>×<paramref name="size"/> box centered on the cursor's relative
    /// position (<paramref name="relX"/>, <paramref name="relY"/>) from the frozen frame (B-G-R-X top-down,
    /// stepped by <paramref name="frameStride"/>). If the box overruns the frame edge, the start is
    /// clamped inside the frame; the output is copied with a tight stride (<c>size*4</c>).
    /// Returns false + null if the cursor is outside the frame, the box is non-positive or larger
    /// than the frame, or the buffer does not match.
    /// </summary>
    internal static bool TryCropBox(
        byte[] frame, int frameStride, int frameWidth, int frameHeight,
        int relX, int relY, int size, out byte[]? crop)
    {
        crop = null;
        if (frame is null || size <= 0 || size > frameWidth || size > frameHeight
            || relX < 0 || relY < 0
            || relX >= frameWidth || relY >= frameHeight
            || frameStride < (long)frameWidth * 4 || frame.Length < (long)frameStride * frameHeight)
        {
            return false;
        }

        int x0 = Math.Clamp(relX - size / 2, 0, Math.Max(0, frameWidth - size));
        int y0 = Math.Clamp(relY - size / 2, 0, Math.Max(0, frameHeight - size));
        var box = new byte[size * size * 4];
        for (int row = 0; row < size; row++)
        {
            Buffer.BlockCopy(frame, (y0 + row) * frameStride + x0 * 4, box, row * 4 * size, 4 * size);
        }

        crop = box;
        return true;
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
}
