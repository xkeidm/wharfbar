using System.Collections.Generic;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// One rendered slot: a single dock item.
/// </summary>
/// <remarks>
/// A row is always an item — there is no divider row type. The section boundaries that
/// <see cref="DockSection"/> marks still order the strip, they just are not drawn
/// (Windows taskbar behaviour: one continuous run of tiles).
/// </remarks>
internal sealed record DockStripRow(DockAppItem Item);

/// <summary>Turns composed items into renderable rows, one row per item.</summary>
internal static class DockStripBuilder
{
    internal static IReadOnlyList<DockStripRow> Build(IReadOnlyList<DockAppItem> items)
    {
        var rows = new List<DockStripRow>(items.Count);

        foreach (DockAppItem item in items)
        {
            rows.Add(new DockStripRow(item));
        }

        return rows;
    }
}
