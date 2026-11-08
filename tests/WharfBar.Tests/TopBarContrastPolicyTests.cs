using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TopBarContrastPolicyTests
{
    [Theory]
    [InlineData(true, "transparent", true)]
    [InlineData(true, "mica", false)]
    [InlineData(true, "tint", false)]
    [InlineData(false, "transparent", false)]
    [InlineData(true, null, false)]
    public void IsWallpaperDriven_OnlyPrimaryTransparent(bool isPrimary, string? mode, bool expected)
        => Assert.Equal(expected, TopBarContrastPolicy.IsWallpaperDriven(mode, isPrimary));

    [Fact]
    public void Decide_FullyTransparentResemblance()
    {
        // Dark wallpaper (black) → light text.
        Assert.Equal(BarTextTone.LightText, TopBarContrastPolicy.Decide(0.0));
        // Fully light wallpaper (white) → dark text.
        Assert.Equal(BarTextTone.DarkText, TopBarContrastPolicy.Decide(1.0));
    }

    [Fact]
    public void Decide_ThresholdBoundaryUsesDarkText()
    {
        // At or above the threshold (0.55) → dark text; below the threshold → light text.
        Assert.Equal(BarTextTone.DarkText, TopBarContrastPolicy.Decide(TopBarContrastPolicy.LightWallpaperThreshold));
        Assert.Equal(BarTextTone.DarkText, TopBarContrastPolicy.Decide(TopBarContrastPolicy.LightWallpaperThreshold + 0.01));
        Assert.Equal(BarTextTone.LightText, TopBarContrastPolicy.Decide(TopBarContrastPolicy.LightWallpaperThreshold - 0.01));
    }
}
