using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayIconSignatureTests
{
    private static byte[] Pixels(int width, int height, byte fill)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, fill);
        return pixels;
    }

    [Fact]
    public void Compute_IsStable_ForIdenticalPixels()
    {
        Assert.Equal(
            TrayIconSignature.Compute(Pixels(16, 16, 9), 16, 16),
            TrayIconSignature.Compute(Pixels(16, 16, 9), 16, 16));
    }

    [Fact]
    public void Compute_ChangesWithOnePixel()
    {
        byte[] a = Pixels(16, 16, 9);
        byte[] b = Pixels(16, 16, 9);
        b[17 * 4 + 2] = 200; // e.g. the red slash of a mute badge

        Assert.NotEqual(TrayIconSignature.Compute(a, 16, 16), TrayIconSignature.Compute(b, 16, 16));
    }

    [Fact]
    public void Compute_IncludesTheSize()
    {
        byte[] pixels = Pixels(16, 16, 9);
        Assert.NotEqual(
            TrayIconSignature.Compute(pixels, 16, 16),
            TrayIconSignature.Compute(pixels, 32, 8));
    }

    [Fact]
    public void Of_MatchesCompute_OverTheSamePixels()
    {
        byte[] pixels = Pixels(4, 4, 77);
        BitmapSource bmp = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);

        Assert.Equal(TrayIconSignature.Compute(pixels, 4, 4), TrayIconSignature.Of(bmp));
    }

    /// <summary>
    /// The crop is a standalone bitmap (the full-taskbar frame is not kept alive per icon) and its
    /// pixels are exactly what the signature hashes.
    /// </summary>
    [Fact]
    public void TryCropIcon_ReturnsStandalonePixels_AndRejectsBlankCrops()
    {
        const int width = 40;
        const int height = 10;
        byte[] frame = Pixels(width, height, 30);
        // A "glyph" pixel inside the button at screen (1012, 503) → frame (12, 3).
        int glyph = ((3 * width) + 12) * 4;
        frame[glyph] = 250;
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, frame, width * 4);
        bitmap.Freeze();
        var capture = new TrayCaptureFrame(bitmap, new RECT { Left = 1000, Top = 500, Right = 1040, Bottom = 510 });

        Assert.True(TrayCapture.TryCropIcon(
            capture,
            new RECT { Left = 1010, Top = 500, Right = 1020, Bottom = 510 },
            out TrayIconCrop? crop));
        Assert.NotNull(crop);
        Assert.Equal(10, crop.Icon.PixelWidth);
        Assert.Equal(10, crop.Icon.PixelHeight);
        Assert.True(crop.Icon.IsFrozen);
        Assert.Equal(TrayIconSignature.Of(crop.Icon), TrayIconSignature.Compute(crop.Pixels, 10, 10));

        // A button over plain taskbar paint: nothing to show, the caller keeps its cached icon.
        Assert.False(TrayCapture.TryCropIcon(
            capture,
            new RECT { Left = 1025, Top = 500, Right = 1035, Bottom = 510 },
            out _));
    }
}
