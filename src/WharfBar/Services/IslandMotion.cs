using System;
using System.Windows.Media.Animation;

namespace WharfBar.Services;

/// <summary>
/// Pure timing numbers for the island pill morphs (width, content fade, satellite pop, press).
/// Durations and the reduced-motion rule stay free of WPF types so they are unit-testable;
/// only <see cref="MorphEase"/> touches an easing type.
/// </summary>
internal static class IslandMotion
{
    internal const double MinWidthMs = 140;
    internal const double GrowMaxMs = 260;
    internal const double ShrinkMaxMs = 200;
    internal const double ContentFadeMs = 160;
    internal const double SatelliteInMs = 180;
    internal const double SatelliteOutMs = 120;
    internal const double PressInMs = 80;
    internal const double PressOutMs = 220;
    internal const double PressScale = 0.97;
    internal const double SatelliteFromScale = 0.6;
    internal const double ContentFromOpacity = 0.35;

    /// <summary>Delay between consecutive parts of the panel's staggered reveal.</summary>
    internal const double StaggerStepMs = 30;

    /// <summary>
    /// Delay between the pill's drag-target chrome landing and the shelf preview opening under
    /// it. The pill's badge/glow grow is the first half of the gesture; opening the panel on the
    /// same frame would hide that animation behind the new surface, so the preview waits a beat.
    /// </summary>
    internal const double DragPreviewOpenDelayMs = 160;

    /// <summary>Drag-preview open delay; zero with animations off (the open is posted to the next dispatcher turn).</summary>
    internal static double DragPreviewOpenMs(bool animationsEnabled)
        => Gate(DragPreviewOpenDelayMs, animationsEnabled);

    /// <summary>
    /// Debounce after the drag leaves the pill before the preview collapses. This is a
    /// cross-window hand-off guard (the pill's DragLeave and the panel's DragEnter race over the
    /// popup's overlapping top edge), not an animation, so it stays on with animations off; the
    /// close animation itself is still gated by <c>ClientAreaAnimation</c>.
    /// </summary>
    internal const double DragLeaveGraceDelayMs = 120;

    /// <summary>
    /// Delay before a landed drop's new chip slides in when the panel still has to open first
    /// (a fast drag that landed before the preview delay). The chip animation then starts after
    /// the panel's grow-from-the-pill reveal instead of playing behind its fade.
    /// </summary>
    internal const double ShelfArrivalAfterRevealMs = 210;

    /// <summary>Start delay of part <paramref name="index"/>; zero when animations are off.</summary>
    internal static double StaggerDelayMs(int index, bool animationsEnabled)
        => animationsEnabled ? index * StaggerStepMs : 0;

    /// <summary>Distance (DIP) at which the width morph reaches its longest duration.</summary>
    private const double FullTravelDip = 140;

    /// <summary>
    /// Width morph duration. Longer travel takes longer (so speed feels constant), growing is a
    /// little slower than shrinking, and reduced motion is instant (0).
    /// </summary>
    internal static double WidthMs(double from, double to, bool animationsEnabled)
    {
        if (!animationsEnabled)
        {
            return 0;
        }

        double delta = Math.Abs(to - from);
        if (delta < 0.5)
        {
            return 0;
        }

        double max = to > from ? GrowMaxMs : ShrinkMaxMs;
        double t = Math.Clamp(delta / FullTravelDip, 0, 1);
        return MinWidthMs + (max - MinWidthMs) * t;
    }

    /// <summary>Applies the reduced-motion rule to a fixed duration.</summary>
    internal static double Gate(double ms, bool animationsEnabled) => animationsEnabled ? ms : 0;

    /// <summary>Pill/panel width ratio at and below which the morph is slowest.</summary>
    private const double MorphMinRatio = 0.18;
    private const double MorphOpenSlowMs = 230; // ratio 0.18: most travel to cover
    private const double MorphOpenFastMs = 170; // ratio 1: pill already panel-wide
    private const double MorphCloseSlowMs = 150;
    private const double MorphCloseFastMs = 110;

    /// <summary>
    /// Pill→panel morph duration. A wider opening pill means less horizontal growth to
    /// animate, so the duration lerps down as the pill/panel ratio rises toward 1.
    /// </summary>
    internal static TimeSpan MorphOpenMs(double ratio, bool animationsEnabled)
        => TimeSpan.FromMilliseconds(MorphMs(ratio, MorphOpenSlowMs, MorphOpenFastMs, animationsEnabled));

    /// <summary>Close counterpart; shorter than open so the panel snaps shut crisply.</summary>
    internal static TimeSpan MorphCloseMs(double ratio, bool animationsEnabled)
        => TimeSpan.FromMilliseconds(MorphMs(ratio, MorphCloseSlowMs, MorphCloseFastMs, animationsEnabled));

    private static double MorphMs(double ratio, double slowMs, double fastMs, bool animationsEnabled)
    {
        if (!animationsEnabled)
        {
            return 0;
        }

        double t = Math.Clamp((Math.Clamp(ratio, MorphMinRatio, 1) - MorphMinRatio) / (1 - MorphMinRatio), 0, 1);
        return slowMs + (fastMs - slowMs) * t;
    }

    /// <summary>
    /// Morph easing for the scale axis. Open uses a subtle ease-out back so the surface
    /// settles with a slight overshoot; close uses plain ease-out. Never apply the back
    /// ease to opacity — a fade must not overshoot — keep fades on their own easing.
    /// </summary>
    internal static IEasingFunction MorphEase(bool opening)
    {
        return opening
            ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.28 }
            : new CubicEase { EasingMode = EasingMode.EaseOut };
    }
}
