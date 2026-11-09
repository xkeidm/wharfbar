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

    [Fact]
    public void DragPreviewOpenMs_UsesTheDelayOnlyWithAnimationsOn()
    {
        Assert.Equal(IslandMotion.DragPreviewOpenDelayMs, IslandMotion.DragPreviewOpenMs(true));
        Assert.Equal(0, IslandMotion.DragPreviewOpenMs(false));
        Assert.InRange(IslandMotion.DragPreviewOpenDelayMs, 100, 250);
    }

    [Fact]
    public void DragLeaveGraceMs_IsAHandOffDebounce_NotAnimated()
    {
        // The debounce is not reduced by "animations off": it protects the pill -> panel OLE
        // hand-off, where both drag events race across the popup's overlapping edge.
        Assert.InRange(IslandMotion.DragLeaveGraceDelayMs, 60, 240);
    }
}
