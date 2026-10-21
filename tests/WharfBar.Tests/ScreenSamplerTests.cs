using System.Windows.Media;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Pure-seam tests only: the GDI capture itself has no CI shim (native cap, verified by hand in
/// Task 8), so <see cref="ScreenSampler"/> is driven here through its validation path and the
/// pure box-average over injected pixel bytes.
/// </summary>
public class ScreenSamplerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TrySample_NonPositiveSize_ReturnsFalse(int size)
    {
        bool ok = ScreenSampler.TrySample(100, 100, size, out byte[] bgra, out int stride);

        Assert.False(ok);
        Assert.Null(bgra);
        Assert.Equal(0, stride);
    }

    [Fact]
    public void AverageBgra_UniformColor_MeanIsThatColor()
    {
        (byte r, byte g, byte b) = (232, 179, 76);
        byte[] bgra = UniformBgra(3, 3, r, g, b);
        int stride = 3 * 4;

        Color mean = ScreenSampler.AverageBgra(bgra, stride, 3);

        Assert.Equal(Color.FromRgb(r, g, b), mean);
    }

    [Fact]
    public void AverageBgra_MixedPixels_TakesChannelMean()
    {
        // 2×2: (10,20,30) and (20,40,60) — an exact integer mean, no rounding debate.
        byte[] bgra =
        [
            /* (0,0) */ 30, 20, 10, 0,
            /* (1,0) */ 60, 40, 20, 0,
            /* (0,1) */ 30, 20, 10, 0,
            /* (1,1) */ 60, 40, 20, 0,
        ];

        Color mean = ScreenSampler.AverageBgra(bgra, 2 * 4, 2);

        Assert.Equal(Color.FromRgb(15, 30, 45), mean);
    }

    [Fact]
    public void AverageBgra_PaddedStride_ReadsRowsAtStrideNotWidth()
    {
        // stride 12 for a 2px row: 2px of color + 4px padding per row. Padding stays untouched.
        byte[] bgra = new byte[12 * 2];
        FillPixel(bgra, row: 0, col: 0, 10, 20, 30);
        FillPixel(bgra, row: 0, col: 1, 20, 40, 60);
        FillPixel(bgra, row: 1, col: 0, 30, 60, 90);
        FillPixel(bgra, row: 1, col: 1, 40, 80, 120);

        Color mean = ScreenSampler.AverageBgra(bgra, stride: 12, size: 2);

        Assert.Equal(Color.FromRgb(25, 50, 75), mean);
    }

    [Fact]
    public void AverageBgra_ShortBuffer_ReturnsTransparent()
    {
        Color mean = ScreenSampler.AverageBgra(new byte[4], stride: 8, size: 2);

        Assert.Equal(0, mean.A);
    }

    private static byte[] UniformBgra(int size, int rows, byte r, byte g, byte b)
    {
        var pixels = new byte[size * rows * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 0;
        }

        return pixels;
    }

    private static void FillPixel(byte[] bgra, int row, int col, byte r, byte g, byte b)
    {
        int offset = (row * 12) + (col * 4);
        bgra[offset] = b;
        bgra[offset + 1] = g;
        bgra[offset + 2] = r;
        bgra[offset + 3] = 0;
    }
}
