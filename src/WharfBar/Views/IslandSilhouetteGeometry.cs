using System.Windows;
using System.Windows.Media;
using WharfBar.Services;

namespace WharfBar.Views;

internal static class IslandSilhouetteGeometry
{
    /// <summary>Turns builder steps into a frozen WPF geometry. Fills are closed figures, outlines stay open.</summary>
    internal static StreamGeometry Create(IReadOnlyList<IslandPathStep> steps, bool filled)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            foreach (IslandPathStep step in steps)
            {
                var point = new Point(step.X, step.Y);
                switch (step.Op)
                {
                    case IslandPathOp.Move:
                        ctx.BeginFigure(point, filled, filled);
                        break;
                    case IslandPathOp.Line:
                        ctx.LineTo(point, true, true);
                        break;
                    case IslandPathOp.Arc:
                        ctx.ArcTo(
                            point,
                            new Size(step.Radius, step.Radius),
                            0,
                            false,
                            step.Clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                            true,
                            true);
                        break;
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }
}