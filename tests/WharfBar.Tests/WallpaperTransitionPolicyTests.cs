using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperTransitionPolicyTests
{
    [Theory]
    [InlineData("off", true, true, false)]
    [InlineData("off", false, true, false)]
    [InlineData("fade", true, false, false)]
    [InlineData("blurFade", true, false, false)]
    [InlineData("fade", true, true, true)]
    [InlineData("blurFade", true, true, true)]
    [InlineData("fade", false, false, true)]
    [InlineData("blurFade", false, false, true)]
    [InlineData("garbage", true, true, true)] // normalizes to blurFade
    public void ShouldAnimate_Matrix(string mode, bool respect, bool systemAnim, bool expected)
        => Assert.Equal(expected, WallpaperTransitionPolicy.ShouldAnimate(mode, respect, systemAnim));

    [Theory]
    [InlineData("blurFade", true)]
    [InlineData("fade", false)]
    [InlineData("off", false)]
    public void UsesBlur_OnlyBlurFade(string mode, bool expected)
        => Assert.Equal(expected, WallpaperTransitionPolicy.UsesBlur(mode));
}
