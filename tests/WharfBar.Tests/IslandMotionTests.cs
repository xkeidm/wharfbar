using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandMotionTests
{
    [Fact]
    public void WidthMs_IsInstantWhenAnimationsAreOff()
        => Assert.Equal(0, IslandMotion.WidthMs(108, 220, animationsEnabled: false));

    [Fact]
    public void WidthMs_IsInstantForSubPixelChange()
        => Assert.Equal(0, IslandMotion.WidthMs(108, 108.3, animationsEnabled: true));

    [Fact]
    public void WidthMs_StaysInsideTheBounds()
    {
        double tiny = IslandMotion.WidthMs(108, 112, true);
        double huge = IslandMotion.WidthMs(108, 900, true);
        Assert.InRange(tiny, IslandMotion.MinWidthMs, IslandMotion.GrowMaxMs);
        Assert.Equal(IslandMotion.GrowMaxMs, huge, 3);
    }

    [Fact]
    public void WidthMs_LongerTravelTakesLonger_AndShrinkIsQuicker()
    {
        Assert.True(IslandMotion.WidthMs(108, 220, true) > IslandMotion.WidthMs(108, 130, true));
        Assert.True(IslandMotion.WidthMs(108, 400, true) > IslandMotion.WidthMs(400, 108, true));
    }

    [Fact]
    public void Gate_ZeroesWhenAnimationsAreOff()
    {
        Assert.Equal(0, IslandMotion.Gate(160, false));
        Assert.Equal(160, IslandMotion.Gate(160, true));
    }

    [Fact]
    public void BarScale_NeverDropsBelowOneDip()
    {
        Assert.Equal(1d / 14, IslandMotion.BarScale(0f, 14), 6);
        Assert.Equal(1d, IslandMotion.BarScale(3f, 14), 6);
    }

    [Fact]
    public void Step_RisesWithoutOvershootAndFallsToZero()
    {
        VisualizerBars.Reset();
        float[] first = VisualizerBars.Step(1f);
        float[] second = VisualizerBars.Step(1f);
        for (int i = 0; i < VisualizerBars.Count; i++)
        {
            Assert.InRange(first[i], 0f, 1f);
            Assert.True(second[i] >= first[i]);
        }

        float[] last = second;
        for (int i = 0; i < 60; i++)
        {
            last = VisualizerBars.Step(0f);
        }

        Assert.All(last, level => Assert.Equal(0f, level));
    }

    [Fact]
    public void MorphOpenMs_Enabled_VariedByRatio_AndLerps()
    {
        var fast = IslandMotion.MorphOpenMs(1.0, true);
        var slow = IslandMotion.MorphOpenMs(0.18, true);
        Assert.InRange(fast.TotalMilliseconds, 160, 180);
        Assert.InRange(slow.TotalMilliseconds, 220, 240);
    }

    [Fact]
    public void MorphOpenMs_Disabled_IsZero()
    {
        Assert.Equal(TimeSpan.Zero, IslandMotion.MorphOpenMs(0.5, false));
    }

    [Fact]
    public void MorphCloseMs_Enabled_VariedByRatio_AndLerps()
    {
        var fast = IslandMotion.MorphCloseMs(1.0, true);
        var slow = IslandMotion.MorphCloseMs(0.18, true);
        Assert.InRange(fast.TotalMilliseconds, 100, 120);
        Assert.InRange(slow.TotalMilliseconds, 140, 160);
    }

    [Fact]
    public void MorphCloseMs_Disabled_IsZero()
    {
        Assert.Equal(TimeSpan.Zero, IslandMotion.MorphCloseMs(0.5, false));
    }

    [Fact]
    public void StaggerDelay_StepsPerIndexAndIsZeroWhenAnimationsAreOff()
    {
        Assert.Equal(0, IslandMotion.StaggerDelayMs(0, true));
        Assert.Equal(2 * IslandMotion.StaggerStepMs, IslandMotion.StaggerDelayMs(2, true));
        Assert.Equal(0, IslandMotion.StaggerDelayMs(2, false));
    }
}
