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
    public void Dominant_TransparentAndEmpty_AreNull()
    {
        Assert.Null(IslandPalette.Dominant(Solid(255, 0, 0, a: 10)));
        Assert.Null(IslandPalette.Dominant([]));
    }

    [Fact]
    public void Dominant_GrayCoverFallsBackToItsAverageColor()
    {
        // No saturated pixel at all: the album still yields a representative color instead of null.
        Assert.Equal(new IslandRgb(128, 128, 128), IslandPalette.Dominant(Solid(128, 128, 128)));
    }

    [Fact]
    public void Dominant_MonochromeDarkCoverFallsBackToItsAverageColor()
    {
        Assert.Equal(new IslandRgb(12, 12, 12), IslandPalette.Dominant(Solid(12, 12, 12)));
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
    public void Dominant_MixesTheAverageIntoASmallColorSpotSoItDoesNotHijackTheTint()
    {
        // A big gray field with a small red logo: red must not become the whole tint.
        byte[] gray = Solid(200, 200, 200, pixels: 60);
        byte[] red = Solid(255, 0, 0, pixels: 4);
        IslandRgb? result = IslandPalette.Dominant([.. gray, .. red]);
        Assert.NotNull(result);
        Assert.True(result.Value.R > result.Value.G);
        Assert.True(result.Value.G > 10);
        Assert.Equal(result.Value.G, result.Value.B);
    }

    [Fact]
    public void Dominant_KeepsTheHueOfADimColorfulCover()
    {
        IslandRgb? result = IslandPalette.Dominant(Solid(30, 40, 120));
        Assert.NotNull(result);
        Assert.True(result.Value.B > result.Value.R);
        Assert.True(result.Value.B > result.Value.G);
    }

    [Fact]
    public void Saturation_RedIsOne_GrayAndBlackAreZero()
    {
        Assert.Equal(1.0, IslandPalette.Saturation(new IslandRgb(255, 0, 0)), 3);
        Assert.Equal(0.0, IslandPalette.Saturation(new IslandRgb(128, 128, 128)), 3);
        Assert.Equal(0.0, IslandPalette.Saturation(new IslandRgb(0, 0, 0)), 3);
    }

    [Fact]
    public void Blend_HitsBothEndpointsAndTheMiddle()
    {
        var black = new IslandRgb(0, 0, 0);
        var white = new IslandRgb(255, 255, 255);
        Assert.Equal(black, IslandPalette.Blend(black, white, 0));
        Assert.Equal(white, IslandPalette.Blend(black, white, 1));
        Assert.Equal(new IslandRgb(128, 128, 128), IslandPalette.Blend(black, white, 0.5));
        // Out-of-range amounts clamp instead of extrapolating.
        Assert.Equal(black, IslandPalette.Blend(black, white, -2));
        Assert.Equal(white, IslandPalette.Blend(black, white, 3));
    }

    [Fact]
    public void ClampLuminance_LeavesAReadableColorUntouched()
    {
        var color = new IslandRgb(220, 60, 60);
        Assert.Equal(color, IslandPalette.ClampLuminance(
            color, IslandPalette.MinTintLuminance, IslandPalette.MaxTintLuminance));
    }

    [Fact]
    public void ClampLuminance_LiftsAVeryDarkColorIntoTheWindow()
    {
        var dark = new IslandRgb(6, 0, 30);
        IslandRgb lifted = IslandPalette.ClampLuminance(
            dark, IslandPalette.MinTintLuminance, IslandPalette.MaxTintLuminance);
        Assert.InRange(
            IslandPalette.Lightness(lifted),
            IslandPalette.MinTintLuminance,
            IslandPalette.MaxTintLuminance);
        Assert.True(lifted.B > lifted.R);
        Assert.True(lifted.R > lifted.G);
    }

    [Fact]
    public void ClampLuminance_PullsABlownOutColorDownIntoTheWindow()
    {
        var nearlyWhite = new IslandRgb(255, 250, 250);
        IslandRgb pulled = IslandPalette.ClampLuminance(
            nearlyWhite, IslandPalette.MinTintLuminance, IslandPalette.MaxTintLuminance);
        Assert.InRange(
            IslandPalette.Lightness(pulled),
            IslandPalette.MinTintLuminance,
            IslandPalette.MaxTintLuminance);
        Assert.True(pulled.R >= pulled.G);
        Assert.True(pulled.G < 200);
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
    public void Tint_AlbumRemapsExtremeLuminanceInsteadOfFallingBackToAccent(byte r, byte g, byte b)
    {
        IslandRgb? tint = IslandPalette.Tint(
            IslandConfig.ColorModeAlbum, new IslandRgb(r, g, b), Accent, true);
        Assert.NotNull(tint);
        Assert.NotEqual(Accent, tint.Value);
        Assert.InRange(
            IslandPalette.Lightness(tint.Value),
            IslandPalette.MinTintLuminance,
            IslandPalette.MaxTintLuminance);
    }

    [Fact]
    public void Tint_AlbumWithoutCoverColorUsesAccent()
        => Assert.Equal(Accent, IslandPalette.Tint(IslandConfig.ColorModeAlbum, null, Accent, true));

    [Fact]
    public void Tint_UnknownModeBehavesAsAlbum()
        => Assert.Null(IslandPalette.Tint("rainbow", new IslandRgb(255, 0, 0), Accent, false));

    [Fact]
    public void ReadableForeground_IsWhiteOnADarkFillAndNearBlackOnABrightOne()
    {
        Assert.Equal(new IslandRgb(0xFF, 0xFF, 0xFF), IslandPalette.ReadableForeground(new IslandRgb(0x7B, 0x4F, 0x43)));
        Assert.Equal(new IslandRgb(0xFF, 0xFF, 0xFF), IslandPalette.ReadableForeground(new IslandRgb(0xDC, 0x28, 0x28)));

        IslandRgb onBright = IslandPalette.ReadableForeground(new IslandRgb(0xDB, 0xDB, 0x94));
        Assert.True(onBright.R < 0x20 && onBright.G < 0x20 && onBright.B < 0x20, "bright tint must get the near-black glyph");
    }

    [Fact]
    public void ReadableForeground_FlipsAtTheDocumentedLuminance()
    {
        // #7F7F7F is just below the threshold, #808080 just above it: white wins below, near-black above.
        Assert.Equal(0.5, IslandPalette.ReadableLuminanceThreshold, 3);
        Assert.Equal(new IslandRgb(0xFF, 0xFF, 0xFF), IslandPalette.ReadableForeground(new IslandRgb(0x7F, 0x7F, 0x7F)));
        IslandRgb above = IslandPalette.ReadableForeground(new IslandRgb(0x80, 0x80, 0x80));
        Assert.True(above.R < 0x20 && above.G < 0x20 && above.B < 0x20);
    }

    [Fact]
    public void CompositeOver_FullyTransparentTintKeepsTheSurface()
    {
        var surface = new IslandRgb(0x20, 0x20, 0x20);
        (IslandRgb color, byte alpha) = IslandPalette.CompositeOver(
            surface, 0xF0, new IslandRgb(0xDC, 0x28, 0x28), tintAlpha: 0);
        Assert.Equal(surface, color);
        Assert.Equal(0xF0, alpha);
    }

    [Fact]
    public void CompositeOver_OpaqueTintReplacesTheSurface()
    {
        var tint = new IslandRgb(0xDC, 0x28, 0x28);
        (IslandRgb color, byte alpha) = IslandPalette.CompositeOver(
            new IslandRgb(0x20, 0x20, 0x20), 0xF0, tint, tintAlpha: 0xFF);
        Assert.Equal(tint, color);
        Assert.Equal(0xFF, alpha);
    }

    [Fact]
    public void CompositeOver_AtTheJoinAlphaMatchesThePanelTopPixel()
    {
        // What the joined pill fill (and the panel's top gradient stop) must resolve to: a 0x28
        // tint layer over the 0xF0 flyout surface. Both alpha and color are hand-checked values.
        (IslandRgb color, byte alpha) = IslandPalette.CompositeOver(
            new IslandRgb(0x20, 0x20, 0x20), 0xF0, new IslandRgb(0xDC, 0x28, 0x28), IslandPalette.PanelTintJoinAlpha);
        Assert.Equal(0xF2, alpha);
        Assert.Equal(new IslandRgb(0x3F, 0x21, 0x21), color);
    }
}
