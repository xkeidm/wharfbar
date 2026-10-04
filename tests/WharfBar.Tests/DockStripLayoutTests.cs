using System;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockStripLayoutTests
{
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
        for (int count = 1; count <= 12; count++)
        {
            var widths = new double[count];
            var scales = new double[count];
            double budget = DockStripLayout.MaxExtentWidth(count, 28d, 12d, 1.65d);

            for (double pos = 0d; pos <= count - 1d; pos += 0.25d)
            {
                for (double phase = 0d; phase <= 1.0001d; phase += 0.25d)
                {
                    DockStripLayout.ComputeScales(count, pos, phase, 1.65d, scales);
                    DockStripLayout.ComputeOffsets(count, widths, scales, 12d, new double[count], out double live);
                    Assert.True(
                        live <= budget + 1e-6d,
                        $"count={count} pos={pos} phase={phase} live={live} budget={budget}");
                }
            }
        }
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
}
