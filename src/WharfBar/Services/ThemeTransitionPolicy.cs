using System.Windows.Media.Animation;

namespace WharfBar.Services;

/// <summary>
/// Pure numbers and gates for the light/dark theme crossfade: how long the captured old-theme
/// frame fades out, which windows are worth capturing, and how much a snapshot may be downscaled
/// when a window is very large. Kept free of WPF window state so it can be unit tested.
/// </summary>
internal static class ThemeTransitionPolicy
{
    /// <summary>
    /// Fade-out duration of the captured pre-switch frame. Short enough to feel like a crossfade
    /// rather than an animation the user has to wait for, and shorter than the 220 ms page
    /// transition used by the setup wizard so a theme switch never looks slower than a page move.
    /// </summary>
    internal const int CrossfadeMs = 180;

    /// <summary>
    /// How long reactive (system-broadcast-driven) re-applies stay suppressed after a transition.
    /// The in-app switch already re-asserts every surface, and the Windows personalization write is
    /// broadcast asynchronously; without this window the echo could repaint windows in the middle of
    /// the fade. It only has to outlive the crossfade — a later echo is harmless because the theme no
    /// longer changes, and a longer window would swallow a genuine accent/colorization refresh.
    /// </summary>
    internal const int ReactiveSuppressionMs = 320;

    /// <summary>Smallest window dimension that still produces a meaningful snapshot.</summary>
    internal const double MinSurfaceDip = 2;

    /// <summary>
    /// Upper bound for one snapshot's pixel buffer (32-bit BGRA). 4 MP ≈ 16 MB per window — enough
    /// for a 2560×1600 monitor's bar/settings window, small enough that a transition never becomes
    /// a memory spike.
    /// </summary>
    internal const long MaxSnapshotPixels = 4_000_000;

    internal static TimeSpan Crossfade => TimeSpan.FromMilliseconds(CrossfadeMs);

    internal static TimeSpan ReactiveSuppression => TimeSpan.FromMilliseconds(ReactiveSuppressionMs);

    /// <summary>Windows animations off (or a remote session policy) → apply instantly, as before.</summary>
    internal static bool ShouldAnimate(bool clientAreaAnimation) => clientAreaAnimation;

    internal static IEasingFunction CreateEase() => new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Whether a window's content is worth capturing for the crossfade.</summary>
    internal static bool IsEligible(
        bool isLoaded,
        bool isVisible,
        bool isMinimized,
        double widthDip,
        double heightDip) =>
        isLoaded
        && isVisible
        && !isMinimized
        && widthDip >= MinSurfaceDip
        && heightDip >= MinSurfaceDip;

    /// <summary>
    /// Scale that keeps a <paramref name="widthDip"/>×<paramref name="heightDip"/> surface rendered
    /// at <paramref name="dpiScale"/> under <see cref="MaxSnapshotPixels"/>; 1 when it already is.
    /// </summary>
    internal static double SnapshotScale(double widthDip, double heightDip, double dpiScale)
    {
        if (widthDip <= 0 || heightDip <= 0 || double.IsNaN(widthDip) || double.IsNaN(heightDip))
        {
            return 1;
        }

        if (double.IsNaN(dpiScale) || dpiScale <= 0)
        {
            dpiScale = 1;
        }

        double pixels = widthDip * dpiScale * heightDip * dpiScale;
        if (pixels <= MaxSnapshotPixels)
        {
            return 1;
        }

        return Math.Sqrt(MaxSnapshotPixels / pixels);
    }
}
