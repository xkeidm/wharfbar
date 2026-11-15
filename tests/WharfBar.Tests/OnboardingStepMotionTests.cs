using System.Windows.Media.Animation;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class OnboardingStepMotionTests
{
    [Fact]
    public void SlideOffset_Forward_EntersFromTheRight()
        => Assert.Equal(OnboardingStepMotion.SlideDip, OnboardingStepMotion.SlideOffset(
            OnboardingStepMotion.Direction.Forward));

    [Fact]
    public void SlideOffset_Back_EntersFromTheLeft()
        => Assert.Equal(-OnboardingStepMotion.SlideDip, OnboardingStepMotion.SlideOffset(
            OnboardingStepMotion.Direction.Back));

    [Fact]
    public void DirectionFor_NextStep_IsForward()
        => Assert.Equal(
            OnboardingStepMotion.Direction.Forward,
            OnboardingStepMotion.DirectionFor(1, 2));

    [Fact]
    public void DirectionFor_PreviousStep_IsBack()
        => Assert.Equal(
            OnboardingStepMotion.Direction.Back,
            OnboardingStepMotion.DirectionFor(4, 1));

    [Fact]
    public void DirectionFor_RailJumpAhead_IsForward_AndAJumpBehind_IsBack()
    {
        Assert.Equal(OnboardingStepMotion.Direction.Forward, OnboardingStepMotion.DirectionFor(0, 5));
        Assert.Equal(OnboardingStepMotion.Direction.Back, OnboardingStepMotion.DirectionFor(6, 2));
    }

    [Fact]
    public void DirectionFor_FirstNavigation_IsForward()
        => Assert.Equal(
            OnboardingStepMotion.Direction.Forward,
            OnboardingStepMotion.DirectionFor(-1, 0));

    [Fact]
    public void Duration_MatchesTheQuickSettingsPageChange()
    {
        Assert.Equal(QuickSettingsPageMotion.DurationMs, OnboardingStepMotion.DurationMs);
        Assert.Equal(TimeSpan.FromMilliseconds(OnboardingStepMotion.DurationMs), OnboardingStepMotion.Duration);
    }

    [Fact]
    public void CreateEase_IsAnEaseOutCubic()
    {
        var ease = Assert.IsType<CubicEase>(OnboardingStepMotion.CreateEase());
        Assert.Equal(EasingMode.EaseOut, ease.EasingMode);
    }
}
