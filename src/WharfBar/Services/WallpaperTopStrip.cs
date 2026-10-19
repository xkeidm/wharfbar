namespace WharfBar.Services;

/// <summary>
/// Pure pixel math for measuring the wallpaper band the top bar covers. Works on BGRA byte
/// layout (WPF <c>Bgra32</c>) so tests can build synthetic images without WPF decode.
/// </summary>
internal static class WallpaperTopStrip
{
    /// <summary>
    /// Average Rec.709 sRGB luma (0..1) over the top <paramref name="stripFraction"/> of the
    /// image height. Fractions &lt;= 0 clamp to a single top row; 1 uses the whole image.
    /// </summary>
    internal static double AverageLuma(ReadOnlySpan<byte> pixels, int width, int height, double stripFraction)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height * 4)
        {
            return 0;
        }

        int rows = Math.Clamp((int)Math.Ceiling(height * stripFraction), 1, height);
        double sum = 0;
        for (int y = 0; y < rows; y++)
        {
            int rowStart = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                int at = rowStart + x * 4;
                // BGRA layout: B, G, R, A.
                sum += LumaOf(pixels[at + 2], pixels[at + 1], pixels[at]);
            }
        }

        return sum / (rows * width);
    }

    private static double LumaOf(byte r, byte g, byte b)
        => (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
}
