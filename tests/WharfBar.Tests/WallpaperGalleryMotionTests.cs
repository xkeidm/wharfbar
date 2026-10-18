using System.Windows.Media.Animation;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperGalleryMotionTests
{
    [Fact]
    public void MotionConstants_MatchDesign()
    {
        Assert.Equal(160, WallpaperGalleryMotion.PreviewCrossfadeMs);
        Assert.Equal(260, WallpaperGalleryMotion.StripScrollMs);
        Assert.Equal(TimeSpan.FromMilliseconds(WallpaperGalleryMotion.PreviewCrossfadeMs),
            WallpaperGalleryMotion.PreviewCrossfade);
        Assert.Equal(TimeSpan.FromMilliseconds(WallpaperGalleryMotion.StripScrollMs),
            WallpaperGalleryMotion.StripScroll);
    }

    [Fact]
    public void CreateEase_IsShortEaseOut()
    {
        var ease = Assert.IsType<CubicEase>(WallpaperGalleryMotion.CreateEase());
        Assert.Equal(EasingMode.EaseOut, ease.EasingMode);
    }

    [Fact]
    public void ThumbMetrics_MatchDesign()
    {
        // ~5 thumbs fill the 388 dip strip viewport; preview shows the picked image.
        Assert.Equal(70, WallpaperGalleryMotion.ThumbWidthDip);
        Assert.Equal(52, WallpaperGalleryMotion.ThumbHeightDip);
        Assert.Equal(8, WallpaperGalleryMotion.ThumbGapDip);
    }

    [Fact]
    public void ScrollStep_IsAboutFourThumbs()
    {
        Assert.Equal(
            4 * (WallpaperGalleryMotion.ThumbWidthDip + WallpaperGalleryMotion.ThumbGapDip),
            WallpaperGalleryMotion.ScrollStepDip);
    }

    [Fact]
    public void NeighborIndex_StartsWithNoneSelected()
    {
        Assert.Equal(0, WallpaperGalleryMotion.NeighborIndex(count: 5, index: -1, direction: 1));
        Assert.Equal(4, WallpaperGalleryMotion.NeighborIndex(count: 5, index: -1, direction: -1));
    }

    [Fact]
    public void NeighborIndex_DoesNotWrap()
    {
        Assert.Equal(3, WallpaperGalleryMotion.NeighborIndex(count: 5, index: 2, direction: 1));
        Assert.Equal(0, WallpaperGalleryMotion.NeighborIndex(count: 5, index: 0, direction: -1));
        Assert.Equal(4, WallpaperGalleryMotion.NeighborIndex(count: 5, index: 4, direction: 1));
    }

    [Fact]
    public void NeighborIndex_NoItemsMeansNothingToSelect()
    {
        Assert.Equal(-1, WallpaperGalleryMotion.NeighborIndex(count: 0, index: 0, direction: 1));
    }

    [Fact]
    public void DecodeWidth_IsTwiceTheDisplaySize()
    {
        // Crisp at 100% DPI for thumb (70) and preview (388) display sizes.
        Assert.Equal(140, WallpaperGalleryMotion.DecodeWidth(70));
        Assert.Equal(776, WallpaperGalleryMotion.DecodeWidth(388));
    }

    [Fact]
    public void DecodeWidth_LargesImagesAreCapped()
    {
        Assert.Equal(1024, WallpaperGalleryMotion.DecodeWidth(4096));
    }

    [Fact]
    public void WheelStep_IsHalfAPage()
    {
        Assert.Equal(WallpaperGalleryMotion.ScrollStepDip * 0.5, WallpaperGalleryMotion.WheelStepDip);
    }

    [Fact]
    public void WheelTarget_CapturesWhenTheStripCanMove()
    {
        const double scrollable = 312;
        (bool Handles, double Target) result = WallpaperGalleryMotion.WheelTarget(scrollable, offset: 100, delta: 120);
        Assert.True(result.Handles);
        Assert.Equal(0, result.Target); // up → left, clamped at the start edge
    }

    [Fact]
    public void WheelTarget_MovesRightWithinBounds()
    {
        (bool Handles, double Target) result = WallpaperGalleryMotion.WheelTarget(312, offset: 200, delta: -120);
        Assert.True(result.Handles);
        Assert.Equal(312, result.Target); // clamped at the end edge
    }

    [Fact]
    public void WheelTarget_FallsThroughAtTheStartEdge()
    {
        Assert.False(WallpaperGalleryMotion.WheelTarget(312, offset: 0, delta: 120).Handles);
    }

    [Fact]
    public void WheelTarget_FallsThroughAtTheEndEdge()
    {
        Assert.False(WallpaperGalleryMotion.WheelTarget(312, offset: 312, delta: -120).Handles);
    }

    [Fact]
    public void WheelTarget_IgnoresStripsWithoutOverflow()
    {
        Assert.False(WallpaperGalleryMotion.WheelTarget(0, offset: 0, delta: 120).Handles);
    }
}
