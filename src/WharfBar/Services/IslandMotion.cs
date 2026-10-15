namespace WharfBar.Services;

/// <summary>
/// Pure timing numbers for the island pill morphs (width, content fade, satellite pop, press).
/// Kept free of WPF types so durations and the reduced-motion rule are unit-testable.
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

    /// <summary>Scale of a bar for a level in 0..1; never below one DIP of <paramref name="maxDip"/>.</summary>
    internal static double BarScale(float level, int maxDip)
        => Math.Clamp(level, 1f / maxDip, 1f);
}
