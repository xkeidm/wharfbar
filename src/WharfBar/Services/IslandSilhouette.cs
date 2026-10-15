namespace WharfBar.Services;

internal enum IslandPathOp { Move, Line, Arc }

/// <summary>One drawing step in panel-local DIP. <see cref="Clockwise"/> is the visual (y-down) sweep.</summary>
internal readonly record struct IslandPathStep(
    IslandPathOp Op,
    double X,
    double Y,
    double Radius = 0,
    bool Clockwise = false);

internal sealed record IslandSilhouetteShape(
    IReadOnlyList<IslandPathStep> Fill,
    IReadOnlyList<IslandPathStep> Outline,
    double StartX,
    double EndX,
    double LeftFillet,
    double RightFillet);

/// <summary>
/// Pure geometry of the unified island: a rounded panel plus two concave fillets that meet the
/// pill's sides. Coordinates are panel-local DIP (origin = panel top-left, y down); the fillets
/// rise into y &lt; 0. No WPF types so it is unit-testable.
/// </summary>
internal static class IslandSilhouette
{
    internal static IslandSilhouetteShape Build(
        double panelWidth,
        double panelHeight,
        double pillX,
        double pillWidth,
        double radius,
        double fillet)
    {
        double w = Math.Max(1, panelWidth);
        double h = Math.Max(1, panelHeight);
        double r = Math.Clamp(radius, 0, Math.Min(w, h) / 2d);
        double f = Math.Max(0, fillet);

        // Where the pill meets the panel's top edge, kept clear of the top corners.
        double lo = r;
        double hi = Math.Max(r, w - r);
        double startX = Math.Clamp(pillX, lo, hi);
        double endX = Math.Clamp(pillX + pillWidth, lo, hi);
        if (endX <= startX)
        {
            // Zero-width or fully off-panel pill: collapse the gap to the panel center so fillets meet there only.
            startX = endX = w / 2d;
        }

        double left = Math.Min(f, Math.Max(0, startX - r));
        double right = Math.Min(f, Math.Max(0, w - r - endX));

        var o = new List<IslandPathStep>();
        o.Add(new(IslandPathOp.Move, startX, -left));
        if (left > 0)
        {
            o.Add(new(IslandPathOp.Arc, startX - left, 0, left, true));
        }

        o.Add(new(IslandPathOp.Line, r, 0));
        if (r > 0)
        {
            o.Add(new(IslandPathOp.Arc, 0, r, r, false));
        }

        o.Add(new(IslandPathOp.Line, 0, h - r));
        if (r > 0)
        {
            o.Add(new(IslandPathOp.Arc, r, h, r, false));
        }

        o.Add(new(IslandPathOp.Line, w - r, h));
        if (r > 0)
        {
            o.Add(new(IslandPathOp.Arc, w, h - r, r, false));
        }

        o.Add(new(IslandPathOp.Line, w, r));
        if (r > 0)
        {
            o.Add(new(IslandPathOp.Arc, w - r, 0, r, false));
        }

        o.Add(new(IslandPathOp.Line, endX + right, 0));
        if (right > 0)
        {
            o.Add(new(IslandPathOp.Arc, endX, -right, right, true));
        }
        else
        {
            o.Add(new(IslandPathOp.Line, endX, 0));
        }

        var fill = new List<IslandPathStep>(o)
        {
            new(IslandPathOp.Line, endX, 0),
            new(IslandPathOp.Line, startX, 0),
        };

        return new IslandSilhouetteShape(fill, o, startX, endX, left, right);
    }
}
