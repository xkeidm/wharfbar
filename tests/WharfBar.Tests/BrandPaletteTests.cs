using System.Windows.Media;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class BrandPaletteTests
{
    private static readonly Color WindowsBlue = Color.FromRgb(0x00, 0x78, 0xD4);

    [Fact]
    public void Dark_DefaultWindowsAccent_ExactValues()
    {
        BrandColors colors = BrandPalette.For(WindowsBlue, isDark: true);

        Assert.Equal(Color.FromRgb(0x18, 0x2E, 0x3F), colors.Background);
        Assert.Equal(Color.FromRgb(0xEA, 0xF0, 0xF5), colors.Pier);
        Assert.Equal(Color.FromRgb(0x30, 0x67, 0x91), colors.Lines);
        Assert.Equal(Color.FromRgb(0xF2, 0xB6, 0x6B), colors.Sun);
    }

    [Fact]
    public void Light_DefaultWindowsAccent_ExactValues()
    {
        BrandColors colors = BrandPalette.For(WindowsBlue, isDark: false);

        Assert.Equal(Color.FromRgb(0xEA, 0xF1, 0xF6), colors.Background);
        Assert.Equal(Color.FromRgb(0x17, 0x37, 0x4F), colors.Pier);
        Assert.Equal(Color.FromRgb(0x9B, 0xBB, 0xD4), colors.Lines);
        Assert.Equal(Color.FromRgb(0xE5, 0x9A, 0x45), colors.Sun);
    }

    [Fact]
    public void ToHsl_DefaultWindowsAccent()
    {
        BrandPalette.ToHsl(WindowsBlue, out double h, out double s, out double l);

        Assert.Equal(206.04, h, 2);
        Assert.Equal(1.0, s, 6);
        Assert.Equal(0.4157, l, 4);
    }

    [Theory]
    [InlineData(0x80, 0x80, 0x80)]
    [InlineData(0x00, 0x00, 0x00)]
    [InlineData(0xFF, 0xFF, 0xFF)]
    public void GreyAccent_HasNoHueAndGivesNeutralGreys(byte r, byte g, byte b)
    {
        Color grey = Color.FromRgb(r, g, b);
        BrandPalette.ToHsl(grey, out double h, out double s, out _);
        Assert.Equal(0d, h);
        Assert.Equal(0d, s);
        Assert.False(double.IsNaN(h) || double.IsNaN(s));

        BrandColors dark = BrandPalette.For(grey, isDark: true);
        Assert.Equal(Color.FromRgb(0x2B, 0x2B, 0x2B), dark.Background);
        Assert.Equal(Color.FromRgb(0xF0, 0xF0, 0xF0), dark.Pier);

        BrandColors light = BrandPalette.For(grey, isDark: false);
        Assert.Equal(Color.FromRgb(0x33, 0x33, 0x33), light.Pier);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SaturatedAccent_IsCappedPerRole(bool isDark)
    {
        BrandColors colors = BrandPalette.For(Color.FromRgb(0xFF, 0x00, 0x00), isDark);
        (double bg, double pier, double lines) = isDark ? (0.45, 0.35, 0.50) : (0.40, 0.55, 0.40);
        (double bgL, double pierL, double linesL) = isDark ? (0.17, 0.94, 0.38) : (0.94, 0.20, 0.72);

        Assert.Equal(BrandPalette.FromHsl(0, bg, bgL), colors.Background);
        Assert.Equal(BrandPalette.FromHsl(0, pier, pierL), colors.Pier);
        Assert.Equal(BrandPalette.FromHsl(0, lines, linesL), colors.Lines);
    }

    [Fact]
    public void MutedAccent_BelowCap_KeepsItsSaturation()
    {
        Color muted = BrandPalette.FromHsl(120, 0.20, 0.50);
        BrandPalette.ToHsl(muted, out double h, out double s, out _);

        BrandColors colors = BrandPalette.For(muted, isDark: true);

        Assert.Equal(BrandPalette.FromHsl(h, s, 0.17), colors.Background);
        Assert.Equal(BrandPalette.FromHsl(h, s, 0.38), colors.Lines);
    }

    [Fact]
    public void FromHsl_PrimaryHues()
    {
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), BrandPalette.FromHsl(0, 1, 0.5));
        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0x00), BrandPalette.FromHsl(120, 1, 0.5));
        Assert.Equal(Color.FromRgb(0x00, 0x00, 0xFF), BrandPalette.FromHsl(240, 1, 0.5));
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x00), BrandPalette.FromHsl(360, 1, 0.5));
    }
}
