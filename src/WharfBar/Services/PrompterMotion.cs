using System;

namespace WharfBar.Services;

/// <summary>
/// Pure motion engine for the island prompter (auto-scroll reading view):
/// play/pause state machine, speed in dips per second, and offset advance/clamp.
/// Math only — the caller supplies elapsed time and the animations-enabled gate.
/// </summary>
internal static class PrompterMotion
{
    /// <summary>Scroll speed at 1× multiplier, in dips per second.</summary>
    internal const double BaseSpeedDip = 66;

    /// <summary>Font size, in dips, of the reading text while playing.</summary>
    internal const int ReadingFontDip = 17;

    /// <summary>
    /// "Reached the end" counting tolerance (dips): end detection and the playback gate
    /// share the same sensitivity.
    /// </summary>
    internal const double EndEpsilonDip = 0.25;

    /// <summary>Playback state of the prompter; toggling starts or flips playback.</summary>
    internal enum Playback
    {
        Editing,
        Playing,
        Paused,
    }

    /// <summary>Editing starts playing; otherwise playback flips between playing and paused.</summary>
    internal static Playback Toggle(Playback state) =>
        state == Playback.Playing ? Playback.Paused : Playback.Playing;

    /// <summary>
    /// Speed multiplier clamped to the supported range [0.5, 3.0]. Negative/positive
    /// infinity clamp to the bounds; NaN (which <see cref="Math.Clamp"/> would pass
    /// through) is treated as lowest by double ordering and maps to the minimum.
    /// </summary>
    internal static double ClampMultiplier(double value) =>
        double.IsNaN(value) ? 0.5 : Math.Clamp(value, 0.5, 3.0);

    /// <summary>
    /// Effective scroll speed in dips per second. Zero when animations are disabled,
    /// so the prompter holds still on systems with client-area animation off.
    /// </summary>
    internal static double SpeedDipPerSec(double multiplier, bool animationsEnabled) =>
        animationsEnabled ? BaseSpeedDip * ClampMultiplier(multiplier) : 0;

    /// <summary>Offset after one tick: negative elapsed time is ignored (ticks may overshoot).</summary>
    internal static double UpdateOffset(double offsetDip, double elapsedSeconds, double multiplier, bool animationsEnabled) =>
        offsetDip + SpeedDipPerSec(multiplier, animationsEnabled) * Math.Max(0, elapsedSeconds);

    /// <summary>
    /// Scroll offset clamped to [0, content − viewport]; a content at or shorter than the
    /// viewport has nothing to scroll.
    /// </summary>
    internal static double ClampOffset(double offsetDip, double contentHeightDip, double viewportHeightDip) =>
        contentHeightDip <= viewportHeightDip ? 0 : Math.Clamp(offsetDip, 0, contentHeightDip - viewportHeightDip);

    /// <summary>
    /// Playback can start only while there is scrollable room below the current
    /// offset: content that fully fits (scrollable ≤ 0) or a scroll resting at the
    /// bottom (within <see cref="EndEpsilonDip"/>) has nothing to play. The play
    /// gate and the button affordance share this single decision.
    /// </summary>
    internal static bool CanStartPlayback(double scrollableHeightDip, double offsetDip) =>
        scrollableHeightDip > 0 && offsetDip < scrollableHeightDip - EndEpsilonDip;
}
