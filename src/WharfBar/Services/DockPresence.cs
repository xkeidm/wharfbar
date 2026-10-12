using WharfBar.Models;

namespace WharfBar.Services;

internal enum DockSlotKind
{
    Stay,
    Enter,
    Exit,
}

/// <summary>One step of a strip diff: the slot's identity and whether it is arriving or leaving.</summary>
internal readonly record struct DockSlotChange(string Key, DockSlotKind Kind);

/// <summary>
/// Identity and ordering for dock slots that animate in and out.
/// A pinned tile keeps its path when the app starts, so the open state eases on the same
/// cell instead of looking like a remove plus an insert.
/// </summary>
internal static class DockPresence
{
    public static string KeyFor(DockAppItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Kind is DockTileKind.Downloads or DockTileKind.Trash or DockTileKind.Launchpad)
        {
            return "id:" + (item.Id ?? string.Empty);
        }

        string path = item.Path?.Trim() ?? string.Empty;
        if (path.Length > 0)
        {
            string normalized = DockItemStore.Normalize(path);
            if (normalized.Length > 0)
            {
                return "path:" + normalized;
            }
        }

        return "id:" + (item.Id ?? string.Empty);
    }

    /// <summary>
    /// Visual order is <paramref name="next"/>, with keys that disappeared spliced in front of
    /// the surviving neighbour that used to follow them so they can shrink in place.
    /// </summary>
    public static IReadOnlyList<DockSlotChange> Diff(
        IReadOnlyList<string> previous,
        IReadOnlyList<string> next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        var nextIndex = new Dictionary<string, int>(next.Count, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < next.Count; i++)
        {
            nextIndex.TryAdd(next[i], i);
        }

        var previousSet = new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
        var exits = new List<(string Key, int Before)>();
        var seenPrevious = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < previous.Count; i++)
        {
            string key = previous[i];
            if (!seenPrevious.Add(key) || nextIndex.ContainsKey(key))
            {
                continue;
            }

            int before = next.Count;
            for (int j = i + 1; j < previous.Count; j++)
            {
                if (nextIndex.TryGetValue(previous[j], out int at))
                {
                    before = at;
                    break;
                }
            }

            exits.Add((key, before));
        }

        var result = new List<DockSlotChange>(next.Count + exits.Count);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int exitCursor = 0;
        for (int i = 0; i < next.Count; i++)
        {
            while (exitCursor < exits.Count && exits[exitCursor].Before == i)
            {
                Emit(result, emitted, exits[exitCursor].Key, DockSlotKind.Exit);
                exitCursor++;
            }

            string key = next[i];
            DockSlotKind kind = previousSet.Contains(key) ? DockSlotKind.Stay : DockSlotKind.Enter;
            Emit(result, emitted, key, kind);
        }

        while (exitCursor < exits.Count)
        {
            Emit(result, emitted, exits[exitCursor].Key, DockSlotKind.Exit);
            exitCursor++;
        }

        return result;
    }

    private static void Emit(
        List<DockSlotChange> result,
        HashSet<string> emitted,
        string key,
        DockSlotKind kind)
    {
        if (emitted.Add(key))
        {
            result.Add(new DockSlotChange(key, kind));
        }
    }
}
