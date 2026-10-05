using System;

namespace WharfBar.Services;

/// <summary>
/// Pure magnification math for the dock strip: the distance falloff curve, the
/// influence radius, and a frame-rate-independent easing step.
/// No WPF types — this is the part that must stay unit-testable.
/// </summary>
internal static class DockMagnification
{
    /// <summary>
    /// Hover ceiling at the cursor (1.0 disables magnification). 1.15 is a Windows-taskbar
    /// nudge, not a macOS dock: at this value the falloff clamps after a single step
    /// (1.15 * 0.82 = 0.943 -> 1.0), so only the hovered icon grows and the strip never reflows.
    /// </summary>
    public const double Default = 1.15d;

    public const double Min = 1.0d;

    public const double Max = 2.0d;

    /// <summary>Fraction of the previous step's scale kept by each neighbour step.</summary>
    internal const double Falloff = 0.82d;

    /// <summary>Easing time constant; larger = slower catch-up.</summary>
    internal const double EasingTauMs = 55d;

    private const double SettleEpsilon = 1e-4d;

    /// <summary>Clamps a stored/UI value into the supported range. Non-positive means "unset".</summary>
    public static double Clamp(double value) =>
        Math.Clamp(value <= 0d ? Default : value, Min, Max);

    /// <summary>
    /// Scale of the slot <paramref name="distance"/> icons away from the cursor.
    /// Never returns less than the resting scale, so icons never shrink.
    /// </summary>
    public static double ScaleAtDistance(double distance, double maxScale)
    {
        if (distance <= 0d)
        {
            return maxScale;
        }

        double scaled = maxScale * Math.Pow(Falloff, distance);
        return scaled < 1.0d ? 1.0d : scaled;
    }

    /// <summary>
    /// Smallest radius where the falloff reaches the resting scale — i.e. how many
    /// neighbours on each side can ever grow. Drives buffer sizing only.
    /// </summary>
    public static int RadiusFor(double maxScale)
    {
        if (maxScale <= 1.0d)
        {
            return 0;
        }

        double ratio = Math.Log(1.0d / maxScale) / Math.Log(Falloff);
        int radius = (int)Math.Ceiling(ratio - 1e-9d);
        return radius < 0 ? 0 : radius;
    }

    /// <summary>
    /// Exponential smoothing step. Frame-rate independent: one 48 ms step lands
    /// where three 16 ms steps land, so a slow frame does not overshoot.
    /// </summary>
    public static double Eased(double current, double target, double dtMs)
    {
        if (dtMs <= 0d || Math.Abs(target - current) < SettleEpsilon)
        {
            return Math.Abs(target - current) < SettleEpsilon ? target : current;
        }

        double factor = 1.0d - Math.Exp(-dtMs / EasingTauMs);
        return current + ((target - current) * factor);
    }
}
