using System.Collections.Generic;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>One rendered slot: either a dock item or a section separator.</summary>
internal sealed record DockStripRow(DockAppItem? Item)
{
    // Cast needed: a bare null is ambiguous between this ctor and the record copy ctor.
    internal static DockStripRow Separator() => new((DockAppItem?)null);

    internal bool IsSeparator => Item is null;
}

/// <summary>Turns composed items into renderable rows, inserting one separator per section change.</summary>
internal static class DockStripBuilder
{
    /// <summary>Resting width of the 1 px divider bar (DIP).</summary>
    internal const double SeparatorSlotDip = 1d;

    /// <summary>Indices (into the item list) where a separator must precede that item.</summary>
    internal static IReadOnlyList<int> SeparatorIndices(IReadOnlyList<DockAppItem> items)
    {
        var indices = new List<int>();
        DockSection? previous = null;

        for (int i = 0; i < items.Count; i++)
        {
            DockSection section = items[i].Section;
            if (previous is not null && previous != section)
            {
                indices.Add(i);
            }

            previous = section;
        }

        return indices;
    }

    internal static IReadOnlyList<DockStripRow> Build(IReadOnlyList<DockAppItem> items)
    {
        var separators = new HashSet<int>(SeparatorIndices(items));
        var rows = new List<DockStripRow>(items.Count + separators.Count);

        for (int i = 0; i < items.Count; i++)
        {
            if (separators.Contains(i))
            {
                rows.Add(DockStripRow.Separator());
            }

            rows.Add(new DockStripRow(items[i]));
        }

        return rows;
    }
}
