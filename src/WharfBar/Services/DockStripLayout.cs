using System;

namespace WharfBar.Services;

/// <summary>
/// Pure horizontal layout math for the dock strip. All arrays are supplied by the caller
/// so the per-frame path allocates nothing.
/// <para>
/// Two layers, deliberately separate:
/// <list type="bullet">
/// <item><description>slot <b>scale</b> — how much bigger a magnified icon looks (visual only)</description></item>
/// <item><description>slot <b>offset</b> — where neighbours end up once bigger icons push them (reflow)</description></item>
/// </list>
/// </para>
/// </summary>
internal static class DockStripLayout
{
    /// <summary>
    /// Writes the resting-to-magnified scale of every slot into <paramref name="into"/>.
    /// <paramref name="cursorPosition"/> is fractional, so moving between two icons
    /// transfers the peak smoothly instead of snapping.
    /// </summary>
    public static void ComputeScales(int count, double cursorPosition, double phase, double maxScale, double[] into)
    {
        ArgumentNullException.ThrowIfNull(into);

        // The clamp is what actually enforces [0, 1]: MaxExtentWidth is only a valid budget
        // inside it, and an overshooting easing function would otherwise scale the strip past
        // the width DockWindow sized the window to and clip it.
        double t = Math.Clamp(phase, 0d, 1d);

        for (int i = 0; i < count; i++)
        {
            double peak = DockMagnification.ScaleAtDistance(Math.Abs(i - cursorPosition), maxScale);
            into[i] = 1.0d + ((peak - 1.0d) * t);
        }
    }

    /// <summary>
    /// Writes each slot's left edge into <paramref name="offsets"/> and reports the
    /// live strip width. Gaps sit between every pair of slots.
    /// </summary>
    public static void ComputeOffsets(
        int count,
        double[] baseWidths,
        double[] scales,
        double gap,
        double[] offsets,
        out double totalWidth)
    {
        ArgumentNullException.ThrowIfNull(baseWidths);
        ArgumentNullException.ThrowIfNull(scales);
        ArgumentNullException.ThrowIfNull(offsets);

        double x = 0d;
        for (int i = 0; i < count; i++)
        {
            offsets[i] = x;
            x += (baseWidths[i] * scales[i]) + gap;
        }

        totalWidth = count <= 0 ? 0d : x - gap;
    }

    /// <summary>
    /// Upper bound on the strip width for any cursor position: the resting width plus
    /// every slot that could ever grow. Sizing the window to this is what keeps
    /// <c>SetWindowPos</c> off the per-frame path.
    /// <para>
    /// <paramref name="widestSlot"/> is the icon width, so separator slots are charged
    /// the icon width too — a deliberate over-estimate, never an under-estimate.
    /// </para>
    /// <para>
    /// Valid only while no slot scale exceeds <paramref name="maxScale"/>. That means the
    /// <c>phase</c> handed to <see cref="ComputeScales"/> must stay in [0, 1]: an overshooting
    /// phase scales the strip past this budget and clips it. <c>ComputeScales</c> clamps the
    /// phase it is given, so this holds even if a second writer or a bouncy easing function
    /// appears; <c>DockStrip</c> eases with <c>CubicEase</c> so the clamp is a no-op today.
    /// </para>
    /// </summary>
    public static double MaxExtentWidth(int count, double widestSlot, double gap, double maxScale)
    {
        if (count <= 0)
        {
            return 0d;
        }

        double resting = (count * (widestSlot + gap)) - gap;
        int radius = DockMagnification.RadiusFor(maxScale);
        double growth = 0d;

        for (int d = 0; d <= radius; d++)
        {
            double slot = widestSlot * (DockMagnification.ScaleAtDistance(d, maxScale) - 1.0d);

            // d == 0 has no mirror partner; every other step can grow both sides.
            growth += d == 0 ? slot : slot * 2.0d;
        }

        return resting + growth;
    }

    /// <summary>
    /// Plate height plus how far the tallest magnified icon rises above it.
    /// The plate itself stays at resting height — only the icon overflows upward.
    /// </summary>
    public static double MaxExtentHeight(
        double icon,
        double indicatorGap,
        double indicatorSize,
        double verticalPadding,
        double maxScale)
    {
        double plate = (2.0d * verticalPadding) + icon + indicatorGap + indicatorSize;
        return plate + (icon * (maxScale - 1.0d));
    }
}
