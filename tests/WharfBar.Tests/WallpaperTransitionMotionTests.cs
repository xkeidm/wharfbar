using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperTransitionMotionTests
{
    [Fact]
    public void MotionConstants_MatchDesign()
    {
        Assert.Equal(1000, WallpaperTransitionMotion.FadeMs);
        Assert.Equal(1250, WallpaperTransitionMotion.BlurFadeMs);
        Assert.Equal(28, WallpaperTransitionMotion.BlurRadiusEnd);
        Assert.Equal(0.18, WallpaperTransitionMotion.BlurDimPeak);
        Assert.Equal(1.06, WallpaperTransitionMotion.FadeScaleEnd);
        Assert.Equal(0.75, WallpaperTransitionMotion.DecodeWidthFraction);
    }

    [Fact]
    public void DurationMs_BlurIsLongerThanFade()
    {
        Assert.Equal(WallpaperTransitionMotion.FadeMs, WallpaperTransitionMotion.DurationMs(useBlur: false));
        Assert.Equal(WallpaperTransitionMotion.BlurFadeMs, WallpaperTransitionMotion.DurationMs(useBlur: true));
        Assert.True(WallpaperTransitionMotion.BlurFadeMs > WallpaperTransitionMotion.FadeMs);
    }

    [Fact]
    public void DecodePixelWidth_ClampsThreeQuartersMonitorWidth()
    {
        Assert.Equal(1440, WallpaperTransitionMotion.DecodePixelWidth(1920)); // 0.75×
        Assert.Equal(640, WallpaperTransitionMotion.DecodePixelWidth(400)); // half-ish → min 640
        Assert.Equal(2560, WallpaperTransitionMotion.DecodePixelWidth(5000)); // 3750 → max 2560
    }
}
