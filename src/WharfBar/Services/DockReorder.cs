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
}
