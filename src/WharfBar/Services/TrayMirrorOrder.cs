using System.Collections;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Pure tray-mirror list order helpers (unit-tested; no window access).
/// Keeps ApplyTrayMirror from Insert-index crashes when UIA publishes duplicate
/// keys or RemoveAt shrinks the collection before Insert.
/// </summary>
internal static class TrayMirrorOrder
{
    internal static IReadOnlyList<TrayMirrorItem> UniqueByKey(IEnumerable<TrayMirrorItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<TrayMirrorItem>();
        foreach (TrayMirrorItem item in items)
        {
            if (seen.Add(item.Key))
            {
                unique.Add(item);
            }
        }

        return unique;
    }

    /// <summary>
    /// Moves each desired item to index <c>i</c>. Desired is de-duplicated by reference;
    /// Insert is clamped to <c>[0, items.Count]</c> after any RemoveAt.
    /// </summary>
    internal static void SyncOrder(IList items, IReadOnlyList<object> desired)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(desired);

        var uniqueDesired = new List<object>(desired.Count);
        foreach (object host in desired)
        {
            if (!ContainsRef(uniqueDesired, host))
            {
                uniqueDesired.Add(host);
            }
        }

        for (int i = 0; i < uniqueDesired.Count; i++)
        {
            object host = uniqueDesired[i];
            int current = IndexOfRef(items, host);
            if (current == i)
            {
                continue;
            }

            if (current >= 0)
            {
                items.RemoveAt(current);
            }

            items.Insert(Math.Min(i, items.Count), host);
        }
    }

    private static bool ContainsRef(List<object> list, object host)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], host))
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOfRef(IList items, object host)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], host))
            {
                return i;
            }
        }

        return -1;
    }
}
