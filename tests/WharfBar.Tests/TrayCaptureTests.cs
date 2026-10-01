using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class TrayCaptureTests
{
    /// <summary>Solid-fill bitmap in Bgra32 — the shape a failed PrintWindow leaves behind.</summary>
    private static BitmapSource Solid(int width, int height, byte b, byte g, byte r, byte a)
    {
        int stride = width * 4;
        var pixels = new byte[stride * height];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }

        var source = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        source.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
        source.Freeze();
        return source;
    }

    /// <summary>Bitmap with one differing pixel — stands in for a rendered glyph.</summary>
    private static BitmapSource WithGlyphPixel(int width, int height)
    {
        var writable = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 32;
            pixels[i + 1] = 32;
            pixels[i + 2] = 32;
            pixels[i + 3] = 255;
        }

        pixels[0] = 200;
        pixels[3] = 255;
        writable.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        writable.Freeze();
        return writable;
    }

    [Fact]
    public void IsBlankCapture_True_ForSolidBlackDib()
    {
        BitmapSource source = Solid(16, 16, 0, 0, 0, 255);
        Assert.True(TrayCapture.IsBlankCapture(source, new Int32Rect(0, 0, 16, 16)));
    }

    [Fact]
    public void IsBlankCapture_True_ForSolidTaskbarBackground()
    {
        BitmapSource source = Solid(16, 16, 43, 43, 43, 255);
        Assert.True(TrayCapture.IsBlankCapture(source, new Int32Rect(0, 0, 16, 16)));
    }

    [Fact]
    public void IsBlankCapture_True_ForFullyTransparent()
    {
        BitmapSource source = Solid(16, 16, 0, 0, 0, 0);
        Assert.True(TrayCapture.IsBlankCapture(source, new Int32Rect(0, 0, 16, 16)));
    }

    [Fact]
    public void IsBlankCapture_False_WhenGlyphPresent()
    {
        BitmapSource source = WithGlyphPixel(16, 16);
        Assert.False(TrayCapture.IsBlankCapture(source, new Int32Rect(0, 0, 16, 16)));
    }

    [Fact]
    public void IsBlankCapture_OnlyJudgesTheRequestedRegion()
    {
        BitmapSource source = WithGlyphPixel(16, 16);
        // The glyph lives in the top-left pixel; the untouched bottom-right quadrant is solid.
        Assert.True(TrayCapture.IsBlankCapture(source, new Int32Rect(8, 8, 8, 8)));
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(-1, 0, 4, 4)]
    [InlineData(0, -1, 4, 4)]
    [InlineData(14, 14, 8, 8)]
    public void IsBlankCapture_True_ForInvalidOrOutOfBoundsRegion(int x, int y, int w, int h)
    {
        BitmapSource source = Solid(16, 16, 10, 20, 30, 255);
        Assert.True(TrayCapture.IsBlankCapture(source, new Int32Rect(x, y, w, h)));
    }

    [Fact]
    public void TryComputeCropRect_ConvertsScreenToWindowCoords()
    {
        var win = new RECT { Left = 0, Top = 1088, Right = 1920, Bottom = 1136 };
        var btn = new RECT { Left = 1800, Top = 1104, Right = 1830, Bottom = 1124 };

        bool ok = TrayCapture.TryComputeCropRect(win, btn, 1920, 48, out Int32Rect crop);

        Assert.True(ok);
        Assert.Equal(new Int32Rect(1800, 16, 30, 20), crop);
    }

    [Fact]
    public void TryComputeCropRect_ClampsToCaptureBounds()
    {
        var win = new RECT { Left = 0, Top = 1088, Right = 1920, Bottom = 1136 };
        var btn = new RECT { Left = 1900, Top = 1104, Right = 1960, Bottom = 1150 };

        bool ok = TrayCapture.TryComputeCropRect(win, btn, 1920, 48, out Int32Rect crop);

        Assert.True(ok);
        Assert.Equal(new Int32Rect(1900, 16, 20, 32), crop);
    }

    [Fact]
    public void TryComputeCropRect_False_WhenOutsideCapture()
    {
        var win = new RECT { Left = 0, Top = 1088, Right = 1920, Bottom = 1136 };
        var btn = new RECT { Left = 2000, Top = 1104, Right = 2100, Bottom = 1124 };

        Assert.False(TrayCapture.TryComputeCropRect(win, btn, 1920, 48, out _));
    }

    [Fact]
    public void TryComputeCropRect_False_WhenWindowInvalid()
    {
        var win = new RECT();
        var btn = new RECT { Left = 10, Top = 10, Right = 30, Bottom = 30 };

        Assert.False(TrayCapture.TryComputeCropRect(win, btn, 100, 50, out _));
    }

    [Fact]
    public void TryComputeCropRect_False_WhenZeroSized()
    {
        var win = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 50 };
        var btn = new RECT { Left = 10, Top = 10, Right = 10, Bottom = 30 };

        Assert.False(TrayCapture.TryComputeCropRect(win, btn, 100, 50, out _));
    }
}
