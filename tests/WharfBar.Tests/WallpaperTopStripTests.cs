using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperTopStripTests
{
    private const double Epsilon = 0.01;

    /// <summary>Produces a BGRA pixel array; every row uses the same solid color (B,G,R,A).</summary>
    private static byte[] SolidPixels(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 0xFF;
        }

        return pixels;
    }

    /// <summary>Array where the top <paramref name="topRows"/> rows use the top color and the rest use the bottom color.</summary>
    private static byte[] TwoTonePixels(int width, int height, int topRows, byte[] topBgra, byte[] bottomBgra)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            byte[] row = y < topRows ? topBgra : bottomBgra;
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                Array.Copy(row, 0, pixels, at, 4);
            }
        }

        return pixels;
    }

    [Fact]
    public void AverageLuma_WhiteStripIsBright()
    {
        byte[] pixels = SolidPixels(8, 8, 0xFF, 0xFF, 0xFF);

        double luma = WallpaperTopStrip.AverageLuma(pixels, 8, 8, stripFraction: 1.0);

        Assert.InRange(luma, 0.99, 1.0);
    }

    [Fact]
    public void AverageLuma_BlackStripIsDark()
    {
        byte[] pixels = SolidPixels(8, 8, 0, 0, 0);

        double luma = WallpaperTopStrip.AverageLuma(pixels, 8, 8, stripFraction: 1.0);

        Assert.InRange(luma, 0.0, Epsilon);
    }

    [Fact]
    public void AverageLuma_MidGrayIsMid()
    {
        // sRGB 128 → luma ~0.502 (Rec.709 weighted average: since the channels are equal,
        // the result is the channel value scaled to 0..1).
        byte[] pixels = SolidPixels(4, 4, 128, 128, 128);

        double luma = WallpaperTopStrip.AverageLuma(pixels, 4, 4, stripFraction: 1.0);

        Assert.InRange(luma, 0.49, 0.52);
    }

    [Fact]
    public void AverageLuma_FractionCoversOnlyTopRows()
    {
        // Top 25% white, bottom 75% black; stripFraction 0.25 → only the white is seen.
        byte[] pixels = TwoTonePixels(4, 8, 2, [0xFF, 0xFF, 0xFF, 0xFF], [0, 0, 0, 0xFF]);

        double luma = WallpaperTopStrip.AverageLuma(pixels, 4, 8, stripFraction: 0.25);

        Assert.InRange(luma, 0.99, 1.0);
    }

    [Fact]
    public void AverageLuma_FractionZeroOrNegativeClampsToOneRow()
    {
        byte[] pixels = TwoTonePixels(4, 8, 2, [0xFF, 0xFF, 0xFF, 0xFF], [0, 0, 0, 0xFF]);

        double luma = WallpaperTopStrip.AverageLuma(pixels, 4, 8, stripFraction: 0.0);

        // At least one row — the top row is white.
        Assert.InRange(luma, 0.99, 1.0);
    }
}
