using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Maps a settled dock slot to a <see cref="DockItemStore.Move"/> index.
/// Launchpad sits in front of the pins and Downloads sits behind them, so the
/// visual slot is not a store index.
/// </summary>
internal static class DockReorder
{
    /// <summary>
    /// How many store pins sit strictly before <paramref name="slot"/> in the settled strip.
    /// That count is the pre-removal index <see cref="DockItemStore.MoveCore"/> expects:
    /// a drop on a pin lands beside it, and a drop on Downloads or later appends.
    /// </summary>
    internal static int StoreIndexBeforeSlot(
        IReadOnlyList<DockAppItem> settled,
        int slot,
        Func<DockAppItem, bool> isStorePin)
    {
        ArgumentNullException.ThrowIfNull(settled);
        ArgumentNullException.ThrowIfNull(isStorePin);

        if (slot < 0)
        {
            slot = 0;
        }

        int count = 0;
        int limit = Math.Min(slot, settled.Count);
        for (int i = 0; i < limit; i++)
        {
            if (isStorePin(settled[i]))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// True when <paramref name="item"/> is a pin row the store can move.
    /// Taskbar-only pins and built-in tiles are not store rows.
    /// </summary>
    internal static bool IsStoredPin(DockAppItem item, IReadOnlyList<PinnedApp> storeItems)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(storeItems);

        if (!item.IsPinned || item.Kind is not (DockTileKind.App or DockTileKind.Folder))
        {
            return false;
        }

        string key = DockItemStore.Normalize(item.Path);
        return key.Length > 0 && DockItemStore.ContainsById(storeItems, key);
    }

    /// <summary>
    /// The left-group display sequence after dropping <paramref name="dragged"/> before
    /// pinned position <paramref name="insertAt"/>: the dragged key is removed and
    /// re-inserted, everything else keeps strip order. Keys are normalized paths. A
    /// dragged key that is not in the sequence returns the input unchanged — the freeze
    /// must not invent a tile the strip did not show.
    /// </summary>
    internal static IReadOnlyList<string> OrderAfterDrop(
        IReadOnlyList<string> keys,
        string dragged,
        int insertAt)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (string.IsNullOrEmpty(dragged))
        {
            return keys;
        }

        var result = new List<string>(keys.Count);
        bool removed = false;
        foreach (string key in keys)
        {
            if (!removed && string.Equals(key, dragged, StringComparison.OrdinalIgnoreCase))
            {
                removed = true;
                continue;
            }

            result.Add(key);
        }

        if (!removed)
        {
            return keys;
        }

        result.Insert(Math.Clamp(insertAt, 0, result.Count), dragged);
        return result;
    }
}
