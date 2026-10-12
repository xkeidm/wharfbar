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
}
