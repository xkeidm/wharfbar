using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>
/// The color picker's "recent colors" store. Session-only: it is not written to disk and does not
/// enter the island schema — every click of the screen picker lands here, and the body rebuilds it
/// on every presentation (no notification like <c>IslandService.Changed</c>: the list changes only
/// after the picker closes, and the picker route already re-renders the body).
/// Add/prune logic is pure (<see cref="Pushed"/>) — tests exercise it directly.
/// </summary>
internal static class IslandPickedColors
{
    /// <summary>Maximum number of colors kept at once (the body strip uses the same count).</summary>
    internal const int MaxItems = 8;

    private static IReadOnlyList<Color> _colors = [];

    internal static IReadOnlyList<Color> Items => _colors;

    /// <summary>The newest pick; transparent if nothing was ever picked (the caller also exercises the empty-list path).</summary>
    internal static Color Last => _colors.Count > 0 ? _colors[0] : default;

    /// <summary>Called on the UI thread. Returns the list as if the pick had been added.</summary>
    internal static void Add(Color color) => _colors = new List<Color>(Pushed(_colors, color, MaxItems));

    /// <summary>
    /// Pure add rule: the new color goes to the front, an exact duplicate is not repeated (it is moved
    /// to the front), and on capacity overflow the oldest are pruned first. A transparent color
    /// (meaning a sample failed) never enters the list.
    /// </summary>
    internal static IReadOnlyList<Color> Pushed(IReadOnlyList<Color>? existing, Color added, int capacity)
    {
        if (capacity <= 0 || added.A == 0)
        {
            return existing ?? [];
        }

        var result = new List<Color>(Math.Min(capacity, (existing?.Count ?? 0) + 1)) { added };
        if (existing is not null)
        {
            foreach (Color color in existing)
            {
                if (color != added)
                {
                    result.Add(color);
                }
            }
        }

        if (result.Count > capacity)
        {
            result.RemoveRange(capacity, result.Count - capacity);
        }

        return result;
    }
}
