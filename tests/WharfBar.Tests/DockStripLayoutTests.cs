using System;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockStripLayoutTests
{
    private const double SlotIconWidth = 28d;
    private const double SlotGap = 12d;
    private const double PeakScale = 1.65d;

    [Fact]
    public void ComputeScales_WhenPhaseIsZero_AllSlotsResting()
    {
        var scales = new double[3];
        DockStripLayout.ComputeScales(3, cursorPosition: 1d, phase: 0d, maxScale: 1.65d, scales);
        Assert.Equal(1d, scales[0], 6);
        Assert.Equal(1d, scales[1], 6);
        Assert.Equal(1d, scales[2], 6);
    }

    [Fact]
    public void ComputeScales_WhenPhaseIsOne_CursorSlotHitsMax()
    {
        var scales = new double[5];
        DockStripLayout.ComputeScales(5, cursorPosition: 2d, phase: 1d, maxScale: 1.65d, scales);
        Assert.Equal(1.65d, scales[2], 6);
    }

    [Fact]
    public void ComputeScales_WhenPhaseIsOutOfRange_ClampsToTheUnitRange()
    {
        var clamped = new double[4];
        var above = new double[4];
        var below = new double[4];

        DockStripLayout.ComputeScales(4, cursorPosition: 2d, phase: 1d, maxScale: PeakScale, clamped);
        DockStripLayout.ComputeScales(4, cursorPosition: 2d, phase: 2d, maxScale: PeakScale, above);
        DockStripLayout.ComputeScales(4, cursorPosition: 2d, phase: -1d, maxScale: PeakScale, below);

        // Non-vacuity: the cursor slot must really be magnified, or the equalities below would
        // hold for an all-resting strip whatever ComputeScales did with the phase.
        Assert.Equal(PeakScale, clamped[2], 6);

        // The clamp is what makes MaxExtentWidth a valid budget, so a phase that overshoots
        // must not scale past the peak the window was sized for, and one below zero must not
        // dip under the resting scale.
        for (int i = 0; i < clamped.Length; i++)
        {
            Assert.Equal(clamped[i], above[i], 6);
            Assert.Equal(1d, below[i], 6);
        }
    }

    [Fact]
    public void ComputeScales_IsSymmetricAroundFractionalCursor()
    {
        var left = new double[5];
        var right = new double[5];
        DockStripLayout.ComputeScales(5, cursorPosition: 2.5d, phase: 1d, maxScale: 1.65d, left);
        DockStripLayout.ComputeScales(5, cursorPosition: 2.5d, phase: 1d, maxScale: 1.65d, right);
        // cursor 2.5 sits between slots 2 and 3: those mirror each other at 0.5 away
        Assert.Equal(left[2], right[3], 6);
        // and the next pair out is 1 / 4, both 1.5 away
        Assert.Equal(left[1], right[4], 6);
    }

    [Fact]
    public void ComputeScales_WhenCursorBetweenTwoSlots_SplitsTheDifference()
    {
        var scales = new double[2];
        DockStripLayout.ComputeScales(2, cursorPosition: 0.5d, phase: 1d, maxScale: 1.65d, scales);
        // distance 0.5 from both slots -> identical scale on each side
        Assert.Equal(scales[0], scales[1], 6);
        Assert.InRange(scales[0], 1.0d, 1.65d);
    }

    [Fact]
    public void ComputeScales_NeverDipsBelowRestingScale()
    {
        var scales = new double[9];
        for (double pos = -1d; pos <= 9d; pos += 0.37d)
        {
            DockStripLayout.ComputeScales(9, pos, phase: 1d, maxScale: 2.0d, scales);
            foreach (double s in scales)
            {
                Assert.True(s >= 1.0d);
            }
        }
    }

    [Fact]
    public void ComputeOffsets_PlacesSlotsLeftToRight()
    {
        double[] widths = { 28, 28, 28 };
        double[] scales = { 1.65, 1.353, 1.0 };
        var offsets = new double[3];

        DockStripLayout.ComputeOffsets(3, widths, scales, gap: 12d, offsets, out double total);

        Assert.Equal(0d, offsets[0], 6);
        Assert.Equal(28d * 1.65d + 12d, offsets[1], 6);
        Assert.Equal((28d * 1.65d + 12d) + (28d * 1.353d + 12d), offsets[2], 6);
        Assert.Equal(offsets[2] + (28d * 1.0d), total, 6);
    }

    [Fact]
    public void ComputeOffsets_ForEmptyStrip_IsZero()
    {
        var offsets = new double[1];
        DockStripLayout.ComputeOffsets(0, Array.Empty<double>(), Array.Empty<double>(), gap: 12d, offsets, out double total);
        Assert.Equal(0d, total, 6);
    }

    [Fact]
    public void ComputeOffsets_OrdersSlotsStrictlyIncreasing()
    {
        var widths = new double[6];
        var scales = new double[6];
        for (int i = 0; i < 6; i++)
        {
            widths[i] = i == 3 ? 1d : 28d; // slot 3 is a separator
        }

        DockStripLayout.ComputeScales(6, cursorPosition: 0d, phase: 1d, maxScale: 1.65d, scales);
        var offsets = new double[6];
        DockStripLayout.ComputeOffsets(6, widths, scales, gap: 12d, offsets, out double total);

        for (int i = 1; i < 6; i++)
        {
            Assert.True(offsets[i] > offsets[i - 1]);
        }

        Assert.True(total > offsets[5]);
    }

    [Fact]
    public void MaxExtentWidth_WhenEmpty_IsZero()
    {
        Assert.Equal(0d, DockStripLayout.MaxExtentWidth(0, 28d, 12d, 1.65d), 6);
    }

    [Fact]
    public void MaxExtentWidth_MatchesBasePlusBothSidedGrowth()
    {
        // base 6*(28+12)-12 = 228 ; growth 28*0.65 + 2*(28*0.353 + 28*0.1095) = 44.1
        double width = DockStripLayout.MaxExtentWidth(6, widestSlot: 28d, gap: 12d, maxScale: 1.65d);
        Assert.Equal(272.1d, width, 1);
    }

    [Fact]
    public void MaxExtentWidth_ExceedsAnyLiveStripWidth()
    {
        double tightest = AssertBudgetDominatesLiveWidth("all icon slots", _ => SlotIconWidth);

        // the all-icon row is the tight case: the budget is meant to hug the live
        // strip, not float far above it
        Assert.True(
            tightest <= 1d,
            $"all icon slots: budget drifts {tightest}px away from the tightest live strip");
    }

    [Fact]
    public void MaxExtentWidth_ExceedsAnyLiveStripWidth_WithSeparatorSlots()
    {
        double tightest = AssertBudgetDominatesLiveWidth(
            "every third slot a separator", i => i % 3 == 1 ? 1d : SlotIconWidth);

        // separators are charged the icon width, so this row keeps real slack - never zero
        Assert.True(
            tightest > 0d,
            $"separator row: expected slack over the budget, tightest margin was {tightest}px");
    }

    [Fact]
    public void MaxExtentWidth_DominatesStripOfMostlySeparators()
    {
        const int Count = 9;
        var widths = new double[Count];
        var restingScales = new double[Count];
        var scales = new double[Count];
        var offsets = new double[Count];
        Array.Fill(restingScales, 1.0d);

        for (int i = 0; i < Count; i++)
        {
            widths[i] = i is 3 or 7 ? SlotIconWidth : 1d;   // two icons, seven separators
        }

        double budget = DockStripLayout.MaxExtentWidth(Count, SlotIconWidth, SlotGap, PeakScale);
        DockStripLayout.ComputeOffsets(Count, widths, restingScales, SlotGap, offsets, out double resting);
        DockStripLayout.ComputeScales(Count, cursorPosition: 3d, phase: 1d, maxScale: PeakScale, scales);
        DockStripLayout.ComputeOffsets(Count, widths, scales, SlotGap, offsets, out double live);

        // the budget knows nothing about these widths - it comes from the icon width
        // alone - so a mostly-separator strip must still fit, and fit with room
        Assert.True(live > resting, $"live={live} resting={resting}");
        Assert.True(live < budget, $"live={live} budget={budget}");
    }

    // Sweeps cursor position and phase for every strip length up to 12 and asserts the
    // MaxExtentWidth budget never falls below the live ComputeOffsets width. Slot widths
    // come from widthAt, so the same sweep covers an all-icon row and a row with
    // separators. Returns the tightest margin (budget - live) it saw.
    private static double AssertBudgetDominatesLiveWidth(string shape, Func<int, double> widthAt)
    {
        double tightest = double.MaxValue;

        for (int count = 1; count <= 12; count++)
        {
            var widths = new double[count];
            var restingScales = new double[count];
            var scales = new double[count];
            var offsets = new double[count];
            Array.Fill(restingScales, 1.0d);

            for (int i = 0; i < count; i++)
            {
                widths[i] = widthAt(i);
            }

            double budget = DockStripLayout.MaxExtentWidth(count, SlotIconWidth, SlotGap, PeakScale);

            // Non-vacuity: full magnification over the row must actually widen it. Without
            // this the sweep below would pass on a strip whose widths zero the scales out.
            DockStripLayout.ComputeOffsets(count, widths, restingScales, SlotGap, offsets, out double restWidth);
            DockStripLayout.ComputeScales(count, cursorPosition: 0d, phase: 1d, maxScale: PeakScale, scales);
            DockStripLayout.ComputeOffsets(count, widths, scales, SlotGap, offsets, out double magnified);
            Assert.True(
                magnified > restWidth,
                $"{shape}: count={count} magnification widened nothing (resting={restWidth} magnified={magnified})");

            for (double pos = 0d; pos <= count - 1d; pos += 0.25d)
            {
                // In-range steps plus the two ends and past them: ComputeScales clamps the
                // phase, so an overshooting easing curve still has to fit the budget.
                foreach (double phase in new[] { -1d, -0.25d, 0d, 0.25d, 0.5d, 0.75d, 1d, 1.25d, 2d })
                {
                    DockStripLayout.ComputeScales(count, pos, phase, PeakScale, scales);
                    DockStripLayout.ComputeOffsets(count, widths, scales, gap: SlotGap, offsets, out double live);

                    Assert.True(
                        live <= budget + 1e-6d,
                        $"{shape}: count={count} pos={pos} phase={phase} live={live} budget={budget}");

                    tightest = Math.Min(tightest, budget - live);
                }
            }
        }

        Assert.True(tightest >= -1e-6d, $"{shape}: tightest margin went negative ({tightest}px)");
        return tightest;
    }

    [Fact]
    public void MaxExtentWidth_WhenMagnificationOff_EqualsBaseWidth()
    {
        Assert.Equal(6d * (28d + 12d) - 12d, DockStripLayout.MaxExtentWidth(6, 28d, 12d, 1.0d), 6);
    }

    [Fact]
    public void MaxExtentHeight_IsPlatePlusIconRise()
    {
        // 2*6 padding + 28 icon + 2 gap + 4 indicator + 28*0.65 rise = 64.2
        double height = DockStripLayout.MaxExtentHeight(
            icon: 28d, indicatorGap: 2d, indicatorSize: 4d, verticalPadding: 6d, maxScale: 1.65d);
        Assert.Equal(64.2d, height, 6);
    }

    [Fact]
    public void MaxExtentHeight_WhenMagnificationOff_EqualsPlateHeight()
    {
        double height = DockStripLayout.MaxExtentHeight(
            icon: 28d, indicatorGap: 2d, indicatorSize: 4d, verticalPadding: 6d, maxScale: 1.0d);
        Assert.Equal(46d, height, 6);
    }

    [Fact]
    public void ComputePresenceOffsets_WhenFullyPresent_MatchesConstantGap()
    {
        const int count = 3;
        const double slot = 40d;
        const double gap = 4d;
        var presence = new[] { 1d, 1d, 1d };
        var scales = new[] { 1d, 1d, 1d };
        var offsets = new double[count];
        var widths = new double[count];
        var classicOffsets = new double[count];
        var baseWidths = new[] { slot, slot, slot };

        DockStripLayout.ComputePresenceOffsets(count, slot, presence, scales, gap, offsets, widths, out double total);
        DockStripLayout.ComputeOffsets(count, baseWidths, scales, gap, classicOffsets, out double classic);

        Assert.Equal(classic, total, 6);
        Assert.Equal(128d, total, 6);
        Assert.Equal(classicOffsets[0], offsets[0], 6);
        Assert.Equal(classicOffsets[1], offsets[1], 6);
        Assert.Equal(classicOffsets[2], offsets[2], 6);
    }

    [Fact]
    public void ComputePresenceOffsets_WhenMiddleSlotLeaves_WidthEasesLinearly()
    {
        const double slot = 40d;
        const double gap = 4d;
        var scales = new[] { 1d, 1d, 1d };
        var full = new[] { 1d, 1d, 1d };
        var half = new[] { 1d, 0.5d, 1d };
        var gone = new[] { 1d, 0d, 1d };
        var offsets = new double[3];
        var widths = new double[3];

        DockStripLayout.ComputePresenceOffsets(3, slot, full, scales, gap, offsets, widths, out double start);
        DockStripLayout.ComputePresenceOffsets(3, slot, half, scales, gap, offsets, widths, out double mid);
        DockStripLayout.ComputePresenceOffsets(3, slot, gone, scales, gap, offsets, widths, out double end);

        Assert.Equal(128d, start, 6);
        Assert.Equal(84d, end, 6);
        Assert.Equal((start + end) / 2d, mid, 6);
        Assert.Equal(slot + gap, offsets[2], 6);
    }
}
