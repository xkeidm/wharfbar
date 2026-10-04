using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockMagnificationTests
{
    [Fact]
    public void Clamp_WhenValueInRange_ReturnsUnchanged()
    {
        Assert.Equal(1.4d, DockMagnification.Clamp(1.4d), 6);
    }

    [Fact]
    public void Clamp_WhenAboveMax_ReturnsMax()
    {
        Assert.Equal(DockMagnification.Max, DockMagnification.Clamp(5d), 6);
    }

    [Fact]
    public void Clamp_WhenBelowMin_ReturnsMin()
    {
        Assert.Equal(DockMagnification.Min, DockMagnification.Clamp(0.2d), 6);
    }

    [Fact]
    public void Clamp_WhenZeroOrNegative_ReturnsDefault()
    {
        Assert.Equal(DockMagnification.Default, DockMagnification.Clamp(0d), 6);
        Assert.Equal(DockMagnification.Default, DockMagnification.Clamp(-3d), 6);
    }

    [Fact]
    public void ScaleAtDistance_WhenDistanceIsZero_ReturnsMaxScale()
    {
        Assert.Equal(1.65d, DockMagnification.ScaleAtDistance(0d, 1.65d), 6);
    }

    [Fact]
    public void ScaleAtDistance_WhenNegative_ReturnsMaxScale()
    {
        Assert.Equal(1.65d, DockMagnification.ScaleAtDistance(-2d, 1.65d), 6);
    }

    [Fact]
    public void ScaleAtDistance_WhenOneStepAway_DropsToFalloffCurve()
    {
        // 1.65 * 0.82 = 1.353
        Assert.Equal(1.353d, DockMagnification.ScaleAtDistance(1d, 1.65d), 3);
    }

    [Fact]
    public void ScaleAtDistance_WhenFar_IsClampedToRestingScale()
    {
        Assert.Equal(1.0d, DockMagnification.ScaleAtDistance(3d, 1.65d), 6);
        Assert.Equal(1.0d, DockMagnification.ScaleAtDistance(40d, 1.65d), 6);
    }

    [Fact]
    public void ScaleAtDistance_NeverReturnsBelowRestingScale()
    {
        for (double d = 0d; d <= 8d; d += 0.25d)
        {
            foreach (double max in new[] { 1.0d, 1.2d, 1.65d, 2.0d })
            {
                Assert.True(DockMagnification.ScaleAtDistance(d, max) >= 1.0d);
            }
        }
    }

    [Fact]
    public void RadiusFor_WhenMagnificationIsDefault_ReturnsThree()
    {
        Assert.Equal(3, DockMagnification.RadiusFor(1.65d));
    }

    [Fact]
    public void RadiusFor_WhenMagnificationIsOff_ReturnsZero()
    {
        Assert.Equal(0, DockMagnification.RadiusFor(1.0d));
    }

    [Fact]
    public void RadiusFor_IsTheSmallestRadiusThatReachesRestingScale()
    {
        foreach (double max in new[] { 1.05d, 1.3d, 1.65d, 2.0d })
        {
            int r = DockMagnification.RadiusFor(max);
            Assert.True(DockMagnification.ScaleAtDistance(r, max) >= 1.0d - 1e-9d);
            if (r > 0)
            {
                Assert.True(DockMagnification.ScaleAtDistance(r - 1, max) > 1.0d);
            }
        }
    }

    [Fact]
    public void Eased_WhenAlreadyAtTarget_StaysAtTarget()
    {
        Assert.Equal(1.25d, DockMagnification.Eased(1.25d, 1.25d, 16d), 6);
    }

    [Fact]
    public void Eased_WhenNoTimeElapsed_ReturnsCurrent()
    {
        Assert.Equal(1.0d, DockMagnification.Eased(1.0d, 2.0d, 0d), 6);
    }

    [Fact]
    public void Eased_MovesTowardTargetWithoutOvershooting()
    {
        double value = 1.0d;
        for (int i = 0; i < 20; i++)
        {
            value = DockMagnification.Eased(value, 2.0d, 16d);
            Assert.InRange(value, 1.0d, 2.0d);
        }
    }

    [Fact]
    public void Eased_IsFrameRateIndependent()
    {
        // One 48 ms step must land in the same place as three 16 ms steps.
        double single = DockMagnification.Eased(1.0d, 2.0d, 48d);
        double stepped = 1.0d;
        stepped = DockMagnification.Eased(stepped, 2.0d, 16d);
        stepped = DockMagnification.Eased(stepped, 2.0d, 16d);
        stepped = DockMagnification.Eased(stepped, 2.0d, 16d);
        Assert.Equal(single, stepped, 3);
    }

    [Fact]
    public void Eased_WhenTargetIsBelowCurrent_MovesDown()
    {
        Assert.True(DockMagnification.Eased(2.0d, 1.0d, 16d) < 2.0d);
        Assert.True(DockMagnification.Eased(2.0d, 1.0d, 16d) > 1.0d);
    }
}
