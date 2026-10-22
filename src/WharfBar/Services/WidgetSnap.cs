namespace WharfBar.Services;

internal readonly record struct DipPoint(double X, double Y);

internal readonly record struct DipSize(double Width, double Height);

internal readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    internal DipRect At(DipPoint origin) => new(origin.X, origin.Y, Width, Height);
}

internal static class WidgetSnap
{
    internal static DipRect Inflate(DipRect rect, double gap) =>
        new(rect.X - gap, rect.Y - gap, rect.Width + (gap * 2), rect.Height + (gap * 2));

    internal static IReadOnlyList<DipRect> Obstacles(IReadOnlyList<DipRect> icons, DipRect? other)
    {
        var list = new List<DipRect>(icons.Count + 1);
        foreach (DipRect icon in icons)
        {
            list.Add(Inflate(icon, WidgetMetrics.Gap));
        }

        if (other is DipRect widget)
        {
            list.Add(Inflate(widget, WidgetMetrics.Gap));
        }

        return list;
    }

    internal static DipPoint Snap(DipPoint desired, DipSize card, DipRect work, IReadOnlyList<DipRect> obstacles)
    {
        List<DipPoint> candidates = Candidates(desired, card, work);
        DipPoint bestFree = default;
        double bestFreeDistance = double.MaxValue;
        bool foundFree = false;
        DipPoint bestOverlap = candidates[0];
        double bestArea = double.MaxValue;
        double bestOverlapDistance = double.MaxValue;

        foreach (DipPoint point in candidates)
        {
            var placed = new DipRect(point.X, point.Y, card.Width, card.Height);
            double area = Overlap(placed, obstacles);
            double distance = Distance(desired, point);
            if (area <= 0)
            {
                if (distance < bestFreeDistance)
                {
                    bestFreeDistance = distance;
                    bestFree = point;
                    foundFree = true;
                }
            }
            else if (area < bestArea || (area == bestArea && distance < bestOverlapDistance))
            {
                bestArea = area;
                bestOverlapDistance = distance;
                bestOverlap = point;
            }
        }

        return foundFree ? bestFree : bestOverlap;
    }

    private static List<DipPoint> Candidates(DipPoint desired, DipSize card, DipRect work)
    {
        var list = new List<DipPoint>();
        double maxX = work.X + work.Width - card.Width;
        double maxY = work.Y + work.Height - card.Height;
        if (desired.X >= work.X && desired.Y >= work.Y && desired.X <= maxX && desired.Y <= maxY)
        {
            list.Add(desired);
        }

        if (maxX < work.X || maxY < work.Y)
        {
            if (list.Count == 0)
            {
                list.Add(new DipPoint(work.X, work.Y));
            }

            return list;
        }

        int stepsX = (int)Math.Floor((maxX - work.X) / WidgetMetrics.Gap);
        int stepsY = (int)Math.Floor((maxY - work.Y) / WidgetMetrics.Gap);
        for (int iy = 0; iy <= stepsY; iy++)
        {
            double y = work.Y + (iy * WidgetMetrics.Gap);
            for (int ix = 0; ix <= stepsX; ix++)
            {
                list.Add(new DipPoint(work.X + (ix * WidgetMetrics.Gap), y));
            }
        }

        list.Add(new DipPoint(maxX, maxY));
        return list;
    }

    private static double Overlap(DipRect card, IReadOnlyList<DipRect> obstacles)
    {
        double area = 0;
        foreach (DipRect obstacle in obstacles)
        {
            double left = Math.Max(card.X, obstacle.X);
            double top = Math.Max(card.Y, obstacle.Y);
            double right = Math.Min(card.X + card.Width, obstacle.X + obstacle.Width);
            double bottom = Math.Min(card.Y + card.Height, obstacle.Y + obstacle.Height);
            double width = right - left;
            double height = bottom - top;
            if (width > 0 && height > 0)
            {
                area += width * height;
            }
        }

        return area;
    }

    private static double Distance(DipPoint a, DipPoint b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
