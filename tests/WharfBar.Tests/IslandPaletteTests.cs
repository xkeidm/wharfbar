using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandPaletteTests
{
    private static byte[] Solid(byte r, byte g, byte b, byte a = 255, int pixels = 16)
    {
        var data = new byte[pixels * 4];
        for (int i = 0; i < pixels; i++)
        {
            data[i * 4] = b;
            data[i * 4 + 1] = g;
            data[i * 4 + 2] = r;
            data[i * 4 + 3] = a;
        }

        return data;
    }

    private static readonly IslandRgb Accent = new(0, 120, 212);

    [Fact]
    public void Dominant_OfSolidRed_IsRed()
        => Assert.Equal(new IslandRgb(255, 0, 0), IslandPalette.Dominant(Solid(255, 0, 0)));

    [Fact]
    public void Dominant_IgnoresGrayTransparentAndEmpty()
    {
        Assert.Null(IslandPalette.Dominant(Solid(128, 128, 128)));
        Assert.Null(IslandPalette.Dominant(Solid(255, 0, 0, a: 10)));
        Assert.Null(IslandPalette.Dominant([]));
    }

    [Fact]
    public void Dominant_PrefersTheLargerColorArea()
    {
        byte[] red = Solid(255, 0, 0, pixels: 24);
        byte[] blue = Solid(0, 0, 255, pixels: 8);
        IslandRgb? result = IslandPalette.Dominant([.. red, .. blue]);
        Assert.NotNull(result);
        Assert.True(result.Value.R > result.Value.B);
    }

    [Fact]
    public void Tint_NeutralIsAlwaysNull()
        => Assert.Null(IslandPalette.Tint(IslandConfig.ColorModeNeutral, new IslandRgb(255, 0, 0), Accent, true));

    [Fact]
    public void Tint_SystemIsAccentEvenWithoutMusic()
        => Assert.Equal(Accent, IslandPalette.Tint(IslandConfig.ColorModeSystem, null, Accent, false));

    [Fact]
    public void Tint_AlbumIsNullWithoutMusic()
        => Assert.Null(IslandPalette.Tint(IslandConfig.ColorModeAlbum, new IslandRgb(255, 0, 0), Accent, false));

    [Fact]
    public void Tint_AlbumUsesCoverColor()
    {
        var cover = new IslandRgb(220, 60, 60);
        Assert.Equal(cover, IslandPalette.Tint(IslandConfig.ColorModeAlbum, cover, Accent, true));
    }

    [Theory]
    [InlineData(0, 0, 30)]
    [InlineData(255, 255, 250)]
    public void Tint_AlbumFallsBackToAccentWhenLuminanceIsExtreme(byte r, byte g, byte b)
        => Assert.Equal(Accent, IslandPalette.Tint(IslandConfig.ColorModeAlbum, new IslandRgb(r, g, b), Accent, true));

    [Fact]
    public void Tint_AlbumWithoutCoverColorUsesAccent()
        => Assert.Equal(Accent, IslandPalette.Tint(IslandConfig.ColorModeAlbum, null, Accent, true));

    [Fact]
    public void Tint_UnknownModeBehavesAsAlbum()
        => Assert.Null(IslandPalette.Tint("rainbow", new IslandRgb(255, 0, 0), Accent, false));
}
