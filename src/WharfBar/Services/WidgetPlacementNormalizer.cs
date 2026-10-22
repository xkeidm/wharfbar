using WharfBar.Models;

namespace WharfBar.Services;

internal static class WidgetDefaults
{
    internal static DipPoint Clock(DipRect primaryWork) =>
        new(primaryWork.X + WidgetMetrics.Margin, primaryWork.Y + WidgetMetrics.Margin);

    internal static DipPoint Under(DipPoint snappedClock, DipSize clock) =>
        new(snappedClock.X, snappedClock.Y + clock.Height + WidgetMetrics.StackGap);
}

internal static class WidgetPlacementNormalizer
{
    internal static List<WidgetPlacement> Normalize(IList<WidgetPlacement>? placements)
    {
        var result = new List<WidgetPlacement>();
        if (placements is null)
        {
            return result;
        }

        foreach (WidgetPlacement? raw in placements)
        {
            if (raw is null || !TryAccept(raw, out string id, out double x, out double y))
            {
                continue;
            }

            int index = result.FindIndex(item => item.Id == id);
            if (index >= 0)
            {
                result[index].X = x;
                result[index].Y = y;
                continue;
            }

            result.Add(new WidgetPlacement { Id = id, X = x, Y = y });
        }

        return result;
    }

    internal static List<WidgetPlacement> Upsert(IList<WidgetPlacement>? placements, string id, double x, double y)
    {
        var next = Normalize(placements);
        var incoming = new WidgetPlacement { Id = id, X = x, Y = y };
        return Normalize([.. next, incoming]);
    }

    private static bool TryAccept(WidgetPlacement raw, out string id, out double x, out double y)
    {
        id = raw.Id.Trim().ToLowerInvariant();
        x = raw.X;
        y = raw.Y;
        return (id == PluginCatalog.Clock || id == PluginCatalog.Calendar)
            && double.IsFinite(x)
            && double.IsFinite(y);
    }
}
