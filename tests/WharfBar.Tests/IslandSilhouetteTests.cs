using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandSilhouetteTests
{
    private const double W = 420, H = 300, R = 24, F = 10;

    [Fact]
    public void CenteredPill_OutlineSpansFilletTopToFilletTop()
    {
        IslandSilhouetteShape s = IslandSilhouette.Build(W, H, 150, 120, R, F);
        IslandPathStep first = s.Outline[0];
        IslandPathStep last = s.Outline[^1];
        Assert.Equal(IslandPathOp.Move, first.Op);
        Assert.Equal(150, first.X, 6);
        Assert.Equal(-F, first.Y, 6);
        Assert.Equal(270, last.X, 6);
        Assert.Equal(-F, last.Y, 6);
        Assert.Equal(F, s.LeftFillet, 6);
        Assert.Equal(F, s.RightFillet, 6);
    }

    [Fact]
    public void CenteredPill_FilletArcsAreClockwise_CornerArcsAreNot()
    {
        IslandSilhouetteShape s = IslandSilhouette.Build(W, H, 150, 120, R, F);
        IslandPathStep leftFillet = FindArcEndingAt(s.Outline, 140, 0);
        IslandPathStep rightFillet = FindArcEndingAt(s.Outline, 270, -F);
        Assert.Equal(F, leftFillet.Radius, 6);
        Assert.Equal(F, rightFillet.Radius, 6);
        Assert.True(leftFillet.Clockwise);
        Assert.True(rightFillet.Clockwise);

        var cornerArcs = s.Outline.Where(step => step.Op == IslandPathOp.Arc && Math.Abs(step.Radius - R) < 1e-9).ToList();
        Assert.Equal(4, cornerArcs.Count);
        Assert.All(cornerArcs, step => Assert.False(step.Clockwise));
    }

    [Fact]
    public void PillNearLeftEdge_ClampedLeftFillet_RightFilletUnchanged()
    {
        IslandSilhouetteShape s = IslandSilhouette.Build(W, H, 30, 120, R, F);
        Assert.Equal(30, s.StartX, 6);
        Assert.Equal(6, s.LeftFillet, 6);
        Assert.Equal(F, s.RightFillet, 6);
        IslandPathStep leftFillet = FindArcEndingAt(s.Outline, 24, 0);
        Assert.Equal(6, leftFillet.Radius, 6);
        Assert.True(leftFillet.Clockwise);
        IslandPathStep rightFillet = FindArcEndingAt(s.Outline, 150, -F);
        Assert.Equal(F, rightFillet.Radius, 6);
        Assert.True(rightFillet.Clockwise);
    }

    private static IslandPathStep FindArcEndingAt(IReadOnlyList<IslandPathStep> steps, double x, double y)
    {
        IslandPathStep match = steps.First(step =>
            step.Op == IslandPathOp.Arc
            && Math.Abs(step.X - x) < 1e-9
            && Math.Abs(step.Y - y) < 1e-9);
        Assert.NotEqual(default, match);
        return match;
    }

    [Fact]
    public void BothFilletsAndFourCorners_YieldSixArcs()
    {
        IslandSilhouetteShape s = IslandSilhouette.Build(W, H, 150, 120, R, F);
        Assert.Equal(6, s.Outline.Count(step => step.Op == IslandPathOp.Arc));
    }

    [Fact]
    public void EveryPoint_StaysInsideTheBounds()
    {
        IslandSilhouetteShape s = IslandSilhouette.Build(W, H, 150, 120, R, F);
        foreach (IslandPathStep step in s.Fill.Concat(s.Outline))
        {
            Assert.InRange(step.X, 0, W);
            Assert.InRange(step.Y, -F, H);
        }
    }

    [Fact]
    public void PillCloseToTheLeftEdge_DropsTheLeftFillet()
    {
        IslandSilhouetteShape s = IslandSilhouette.Build(W, H, 10, 120, R, F);
        Assert.Equal(0, s.LeftFillet, 6);
        Assert.Equal(0, s.Outline[0].Y, 6);
        Assert.True(s.StartX >= R);
    }

    [Fact]
    public void PillWiderThanThePanel_DoesNotThrowAndStaysOrdered()
    {
        IslandSilhouetteShape s = IslandSilhouette.Build(W, H, -50, 600, R, F);
        Assert.True(s.StartX <= s.EndX);
        Assert.Equal(0, s.LeftFillet, 6);
        Assert.Equal(0, s.RightFillet, 6);
    }

    [Fact]
    public void RadiusIsClampedToHalfOfTheSmallerSide()
    {
        const double panelHeight = 20;
        const double clampedRadius = 10;
        IslandSilhouetteShape s = IslandSilhouette.Build(W, panelHeight, 150, 120, 24, F);
        var panelCornerArcs = s.Outline.Where(step => step.Op == IslandPathOp.Arc
            && (step.X < 1e-9 || Math.Abs(step.X - W) < 1e-9
                || Math.Abs(step.Y - panelHeight) < 1e-9
                || (Math.Abs(step.Y) < 1e-9 && step.X > W / 2))).ToList();
        Assert.Equal(4, panelCornerArcs.Count);
        Assert.All(panelCornerArcs, step => Assert.Equal(clampedRadius, step.Radius, 6));
    }

    [Fact]
    public void Fill_IsClosedOverThePillGapWithoutCoveringIt()
    {
        IslandSilhouetteShape s = IslandSilhouette.Build(W, H, 150, 120, R, F);
        IslandPathStep[] tail = [.. s.Fill.TakeLast(2)];
        Assert.Equal(IslandPathOp.Line, tail[0].Op);
        Assert.Equal(270, tail[0].X, 6);
        Assert.Equal(0, tail[0].Y, 6);
        Assert.Equal(150, tail[1].X, 6);
        Assert.Equal(0, tail[1].Y, 6);
    }
}
