using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Pure motion/gate numbers for the light/dark shell crossfade. The capture and overlay plumbing is
/// verified by the offline UI harness / manual QA; everything here is intentionally WPF-free so it
/// runs in the normal test suite.
/// </summary>
public class ThemeTransitionPolicyTests
{
    [Fact]
    public void Crossfade_IsShortAndPositive()
    {
        Assert.InRange(ThemeTransitionPolicy.CrossfadeMs, 120, 240);
        Assert.Equal(
            TimeSpan.FromMilliseconds(ThemeTransitionPolicy.CrossfadeMs),
            ThemeTransitionPolicy.Crossfade);
    }

    [Fact]
    public void ReactiveSuppression_OutlivesTheFade()
    {
        // The Windows personalization broadcast lands after the apply; the suppression window has to
        // cover it, otherwise a second re-apply repaints windows mid-fade.
        Assert.True(
            ThemeTransitionPolicy.ReactiveSuppressionMs > ThemeTransitionPolicy.CrossfadeMs);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldAnimate_FollowsClientAreaAnimation(bool clientAreaAnimation, bool expected)
    {
        Assert.Equal(expected, ThemeTransitionPolicy.ShouldAnimate(clientAreaAnimation));
    }

    [Fact]
    public void CreateEase_IsEaseOutCubic()
    {
        var ease = Assert.IsType<System.Windows.Media.Animation.CubicEase>(
            ThemeTransitionPolicy.CreateEase());
        Assert.Equal(System.Windows.Media.Animation.EasingMode.EaseOut, ease.EasingMode);
    }

    [Theory]
    [InlineData(true, true, false, 1920, 30, true)]
    [InlineData(false, true, false, 1920, 30, false)] // not loaded yet
    [InlineData(true, false, false, 1920, 30, false)] // hidden
    [InlineData(true, true, true, 1920, 30, false)] // minimized
    [InlineData(true, true, false, 1, 30, false)] // degenerate
    [InlineData(true, true, false, 0, 0, false)]
    public void IsEligible_RequiresAVisibleLiveSurface(
        bool isLoaded,
        bool isVisible,
        bool isMinimized,
        double width,
        double height,
        bool expected)
    {
        Assert.Equal(
            expected,
            ThemeTransitionPolicy.IsEligible(isLoaded, isVisible, isMinimized, width, height));
    }

    [Fact]
    public void SnapshotScale_LeavesSmallSurfacesAtFullResolution()
    {
        Assert.Equal(1, ThemeTransitionPolicy.SnapshotScale(1920, 30, 1.0));
        Assert.Equal(1, ThemeTransitionPolicy.SnapshotScale(1280, 720, 1.5));
    }

    [Fact]
    public void SnapshotScale_DownscalesSurfacesOverThePixelBudget()
    {
        // 3840x2160 at 100% = 8.3 MP > 4 MP budget.
        double scale = ThemeTransitionPolicy.SnapshotScale(3840, 2160, 1.0);

        Assert.True(scale < 1);
        double pixels = 3840 * scale * 2160 * scale;
        Assert.True(pixels <= ThemeTransitionPolicy.MaxSnapshotPixels + 1);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(double.NaN, 100)]
    public void SnapshotScale_HandlesDegenerateInput(double width, double height)
    {
        Assert.Equal(1, ThemeTransitionPolicy.SnapshotScale(width, height, 1.0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.NaN)]
    public void SnapshotScale_HandlesDegenerateDpi(double dpiScale)
    {
        double scale = ThemeTransitionPolicy.SnapshotScale(800, 600, dpiScale);

        Assert.True(scale > 0);
        Assert.True(double.IsFinite(scale));
    }

    [Fact]
    public void SnapshotScale_IsMonotonic()
    {
        Assert.True(ThemeTransitionPolicy.SnapshotScale(2000, 1000, 1.0)
                    >= ThemeTransitionPolicy.SnapshotScale(4000, 2000, 1.0));
    }
}
