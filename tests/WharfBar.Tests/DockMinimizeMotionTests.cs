using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockMinimizeMotionTests
{
    [Fact]
    public void ShouldPlay_RequiresAnimationAndACapture()
    {
        Assert.True(DockMinimizeMotion.ShouldPlay(clientAreaAnimation: true, captureSucceeded: true));
        Assert.False(DockMinimizeMotion.ShouldPlay(clientAreaAnimation: false, captureSucceeded: true));
        Assert.False(DockMinimizeMotion.ShouldPlay(clientAreaAnimation: true, captureSucceeded: false));
        Assert.False(DockMinimizeMotion.ShouldPlay(clientAreaAnimation: false, captureSucceeded: false));
    }

    [Fact]
    public void Duration_IsAShortScale()
    {
        Assert.Equal(200, DockMinimizeMotion.DurationMs);
    }

    [Fact]
    public void CueScaleMin_MatchesMinimizeEndScale()
    {
        Assert.Equal(0.12, DockMinimizeMotion.CueScaleMin);
    }

    [Fact]
    public void RestoreStartOffset_MatchesMinimizeEndOffset()
    {
        var icon = new System.Windows.Point(100, 200);
        var window = new System.Windows.Rect(0, 0, 400, 300);
        System.Windows.Point end = DockMinimizeMotion.MinimizeEndOffset(icon, window);
        System.Windows.Point start = DockMinimizeMotion.RestoreStartOffset(icon, window);
        Assert.Equal(end, start);
        Assert.Equal(100 - (400 * 0.06), start.X);
        Assert.Equal(200 - (300 * 0.06), start.Y);
    }
}
