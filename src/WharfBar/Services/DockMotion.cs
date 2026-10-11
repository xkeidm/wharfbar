using System.Windows.Media.Animation;

namespace WharfBar.Services;

/// <summary>Which cubic easing a dock transition uses.</summary>
internal enum DockMotionEase
{
    /// <summary>Appear: fast start, soft landing. Matches flyout open and dock phase-in.</summary>
    EaseOut,

    /// <summary>Disappear: soft start, fast exit. Matches flyout close.</summary>
    EaseIn,

    /// <summary>Width changes, so growing and shrinking feel the same.</summary>
    EaseInOut,
}

/// <summary>
/// One dock transition: where a double property starts, where it lands, and how it eases.
/// Pure data so tests can pin the motion without a window.
/// </summary>
internal readonly record struct DockMotionSpec(
    double From,
    double To,
    TimeSpan Duration,
    DockMotionEase Ease);

/// <summary>
/// Shared timing for dock presence. One short duration so insert, remove, open, and close
/// feel like the same gesture. Live icon scale stays at 1; <see cref="CollapsedScale"/> is
/// only the enter/exit pose and stays under the 1.15 magnification cap.
/// </summary>
internal static class DockMotion
{
    public const int DurationMs = 160;

    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(DurationMs);

    /// <summary>
    /// Icon scale at presence 0. The slot width uses the same 0–1 range, so the glyph
    /// shrinks with the gap it leaves and neighbours do not draw on top of each other.
    /// Rest is <see cref="RestScale"/>, which stays under the 1.15 magnification cap.
    /// </summary>
    public const double CollapsedScale = 0d;

    public const double RestScale = 1d;

    /// <summary>Opacity or any other fade. Enter eases out, exit eases in.</summary>
    public static DockMotionSpec Fade(double from, double to) =>
        new(from, to, Duration, to < from ? DockMotionEase.EaseIn : DockMotionEase.EaseOut);

    /// <summary>Slot width, plate width, and the running capsule's length.</summary>
    public static DockMotionSpec Span(double from, double to) =>
        new(from, to, Duration, DockMotionEase.EaseInOut);

    /// <summary>Scale that tracks a 0–1 presence value. Never exceeds <see cref="RestScale"/>.</summary>
    public static double ScaleFor(double presence)
    {
        double t = Math.Clamp(presence, 0d, 1d);
        return CollapsedScale + ((RestScale - CollapsedScale) * t);
    }

    public static IEasingFunction CreateEase(DockMotionEase ease) => ease switch
    {
        DockMotionEase.EaseIn => new CubicEase { EasingMode = EasingMode.EaseIn },
        DockMotionEase.EaseInOut => new CubicEase { EasingMode = EasingMode.EaseInOut },
        _ => new CubicEase { EasingMode = EasingMode.EaseOut },
    };

    public static DoubleAnimation Create(DockMotionSpec spec, Action? completed = null)
    {
        var animation = new DoubleAnimation(spec.From, spec.To, spec.Duration)
        {
            EasingFunction = CreateEase(spec.Ease),
            FillBehavior = FillBehavior.HoldEnd,
        };

        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        return animation;
    }
}
