using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperAccentTests
{
    [Fact]
    public void FromDominant_Null_IsNull()
        => Assert.Null(WallpaperAccent.FromDominant(null));

    [Fact]
    public void FromDominant_MidRed_Passes()
        => Assert.Equal(new IslandRgb(200, 40, 40), WallpaperAccent.FromDominant(new IslandRgb(200, 40, 40)));

    [Fact]
    public void FromDominant_NearBlack_IsNull()
        => Assert.Null(WallpaperAccent.FromDominant(new IslandRgb(5, 5, 5)));

    [Fact]
    public void FromDominant_Gray_IsNull()
        => Assert.Null(WallpaperAccent.FromDominant(new IslandRgb(128, 128, 128)));

    [Fact]
    public void FromDominant_NearWhite_IsNull()
        => Assert.Null(WallpaperAccent.FromDominant(new IslandRgb(250, 250, 250)));

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ShouldApply_RequiresEnabledAndColor(bool enabled, bool hasColor, bool expected)
    {
        IslandRgb? c = hasColor ? new IslandRgb(200, 40, 40) : null;
        Assert.Equal(expected, WallpaperAccent.ShouldApply(enabled, c));
    }

    [Fact]
    public void TryFromPath_SolidRedPng_ReturnsHighR()
    {
        string temp = Path.GetTempFileName();
        string path = Path.ChangeExtension(temp, ".png");
        try
        {
            File.Delete(temp);
            WriteSolidPng(path, Colors.Red);
            IslandRgb? rgb = WallpaperAccent.TryFromPath(path);
            Assert.NotNull(rgb);
            Assert.True(rgb.Value.R > 180);
            Assert.True(rgb.Value.G < 80);
            Assert.True(rgb.Value.B < 80);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static void WriteSolidPng(string path, Color color)
    {
        var bitmap = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[8 * 8 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = 255;
        }

        bitmap.WritePixels(new Int32Rect(0, 0, 8, 8), pixels, 8 * 4, 0);
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}
