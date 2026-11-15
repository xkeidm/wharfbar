using System;
using System.Windows.Media.Animation;

namespace WharfBar.Services;

/// <summary>
/// Motion numbers for the first-run setup wizard's step change. The pane slides in horizontally
/// from the side the user is travelling towards — forward brings the next step in from the
/// right, back re-enters from the left — while fading in under an ease-out curve. Same 220 ms
/// rhythm as <see cref="QuickSettingsPageMotion"/>, so every page change in the app moves alike.
/// </summary>
internal static class OnboardingStepMotion
{
    /// <summary>Travel direction of one step change, used to pick the side the page enters from.</summary>
    internal enum Direction
    {
        Forward,
        Back,
    }

    /// <summary>Transition duration; matches the quick-settings page change.</summary>
    internal const int DurationMs = 220;

    /// <summary>
    /// Lateral travel (DIP). The pane clips its own content, so the parked page never spills over
    /// the rail or the window edge while it slides in.
    /// </summary>
    internal const double SlideDip = 32;

    internal static TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

    /// <summary>Start offset for <paramref name="direction"/>: positive X is to the right.</summary>
    internal static double SlideOffset(Direction direction) =>
        direction == Direction.Forward ? SlideDip : -SlideDip;

    /// <summary>
    /// Forward when the step index grows, back when it shrinks. A jump in the rail follows the
    /// same rule, so clicking a step ahead slides like Continue, and a step behind like Back.
    /// </summary>
    internal static Direction DirectionFor(int fromIndex, int toIndex) =>
        toIndex >= fromIndex ? Direction.Forward : Direction.Back;

    internal static IEasingFunction CreateEase() =>
        new CubicEase { EasingMode = EasingMode.EaseOut };
}
