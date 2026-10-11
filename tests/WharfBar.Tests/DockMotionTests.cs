using System.Windows.Media.Animation;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockMotionTests
{
    [Fact]
    public void Duration_IsShortEnoughToStayResponsive()
    {
        Assert.Equal(160, DockMotion.DurationMs);
        Assert.Equal(TimeSpan.FromMilliseconds(160), DockMotion.Duration);
        Assert.InRange(DockMotion.DurationMs, 120, 200);
    }

    [Fact]
    public void Fade_EasesOutOnEnterAndInOnExit()
    {
        DockMotionSpec enter = DockMotion.Fade(0d, 1d);
        DockMotionSpec exit = DockMotion.Fade(1d, 0d);

        Assert.Equal(0d, enter.From);
        Assert.Equal(1d, enter.To);
        Assert.Equal(DockMotionEase.EaseOut, enter.Ease);
        Assert.Equal(DockMotion.Duration, enter.Duration);

        Assert.Equal(1d, exit.From);
        Assert.Equal(0d, exit.To);
        Assert.Equal(DockMotionEase.EaseIn, exit.Ease);
        Assert.Equal(DockMotion.Duration, exit.Duration);
    }

    [Fact]
    public void Span_UsesEaseInOutSoGrowAndShrinkMatch()
    {
        DockMotionSpec span = DockMotion.Span(8d, 16d);

        Assert.Equal(8d, span.From);
        Assert.Equal(16d, span.To);
        Assert.Equal(DockMotionEase.EaseInOut, span.Ease);
        Assert.Equal(DockMotion.Duration, span.Duration);
    }

    [Fact]
    public void Scale_TracksPresenceAndStaysAtOrBelowRest()
    {
        Assert.Equal(DockMotion.CollapsedScale, DockMotion.ScaleFor(0d));
        Assert.Equal(DockMotion.RestScale, DockMotion.ScaleFor(1d));
        Assert.Equal(0.5d, DockMotion.ScaleFor(0.5d), 6);
        Assert.True(DockMotion.CollapsedScale <= DockMotion.RestScale);
        Assert.True(DockMotion.RestScale <= DockMagnification.Default);
    }

    [Fact]
    public void Create_CopiesSpecOntoACubicAnimation()
    {
        DockMotionSpec spec = DockMotion.Fade(0.25d, 1d);
        DoubleAnimation animation = DockMotion.Create(spec);

        Assert.Equal(0.25d, animation.From);
        Assert.Equal(1d, animation.To);
        Assert.Equal(DockMotion.Duration, animation.Duration);
        Assert.Equal(FillBehavior.HoldEnd, animation.FillBehavior);
        var ease = Assert.IsType<CubicEase>(animation.EasingFunction);
        Assert.Equal(EasingMode.EaseOut, ease.EasingMode);

        DoubleAnimation exit = DockMotion.Create(DockMotion.Fade(1d, 0d));
        Assert.Equal(EasingMode.EaseIn, Assert.IsType<CubicEase>(exit.EasingFunction).EasingMode);

        DoubleAnimation span = DockMotion.Create(DockMotion.Span(1d, 0d));
        Assert.Equal(EasingMode.EaseInOut, Assert.IsType<CubicEase>(span.EasingFunction).EasingMode);
    }
}
