namespace WharfBar.Models;

/// <summary>Built-in top-bar status strip item ids (config <c>topBar.statusItems</c>).</summary>
internal static class TopBarStatusItemIds
{
    internal const string TrayMirror = "trayMirror";
    internal const string TrayOverflow = "trayOverflow";
    internal const string DateTime = "dateTime";
    internal const string Notifications = "notifications";
    internal const string ControlCenter = "controlCenter";
    internal const string Volume = "volume";
    internal const string Brightness = "brightness";
    internal const string Network = "network";
    internal const string Battery = "battery";
    internal const string Media = "media";

    /// <summary>
    /// Full show/hide catalog, including items omitted from <see cref="DefaultOrder"/>.
    /// Right-click and the Top Bar settings page iterate this so a hidden item can come back.
    /// </summary>
    internal static readonly IReadOnlyList<string> CatalogOrder =
    [
        TrayMirror,
        TrayOverflow,
        DateTime,
        Notifications,
        ControlCenter,
        Volume,
        Brightness,
        Media,
        Network,
        Battery,
    ];

    /// <summary>
    /// Fresh installs and "reset to default" omit volume, brightness, and media.
    /// Those stay in <see cref="CatalogOrder"/>. A saved <c>statusItems</c> list is not stripped.
    /// </summary>
    internal static readonly IReadOnlyList<string> DefaultOrder =
    [
        TrayMirror,
        TrayOverflow,
        DateTime,
        Notifications,
        ControlCenter,
        Network,
        Battery,
    ];

    private static readonly HashSet<string> Known = new(CatalogOrder, StringComparer.Ordinal);

    /// <summary>
    /// Keeps first occurrence of known ids only; empty/null → default full set.
    /// Unknown ids dropped (soft). Does not auto-insert missing catalog ids (hide must stick).
    /// </summary>
    internal static List<string> Normalize(IEnumerable<string>? items)
    {
        if (items is null)
        {
            return DefaultOrder.ToList();
        }

        var result = new List<string>(DefaultOrder.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string? raw in items)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string id = raw.Trim();
            if (!Known.Contains(id) || !seen.Add(id))
            {
                continue;
            }

            result.Add(id);
        }

        return result.Count == 0 ? DefaultOrder.ToList() : result;
    }

    /// <summary>
    /// Puts <paramref name="id"/> back among the items already visible, next to its
    /// catalog neighbors. The rest of a custom order stays put.
    /// </summary>
    internal static List<string> InsertInCatalogOrder(IReadOnlyList<string> current, string id)
    {
        ArgumentNullException.ThrowIfNull(current);
        var items = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in current)
        {
            if (Known.Contains(raw) && seen.Add(raw))
            {
                items.Add(raw);
            }
        }

        if (!Known.Contains(id) || !seen.Add(id))
        {
            return items;
        }

        int catalogIndex = -1;
        for (int i = 0; i < CatalogOrder.Count; i++)
        {
            if (string.Equals(CatalogOrder[i], id, StringComparison.Ordinal))
            {
                catalogIndex = i;
                break;
            }
        }

        int insertAt = items.Count;
        if (catalogIndex >= 0)
        {
            for (int i = catalogIndex - 1; i >= 0; i--)
            {
                int at = items.FindIndex(x => string.Equals(x, CatalogOrder[i], StringComparison.Ordinal));
                if (at >= 0)
                {
                    insertAt = at + 1;
                    goto Place;
                }
            }

            for (int i = catalogIndex + 1; i < CatalogOrder.Count; i++)
            {
                int at = items.FindIndex(x => string.Equals(x, CatalogOrder[i], StringComparison.Ordinal));
                if (at >= 0)
                {
                    insertAt = at;
                    break;
                }
            }
        }

    Place:
        items.Insert(insertAt, id);
        return items;
    }

    /// <summary>
    /// Turns every catalog id on without moving items that are already visible.
    /// Missing ids land beside their catalog neighbors (<see cref="InsertInCatalogOrder"/>).
    /// </summary>
    internal static List<string> ShowAll(IReadOnlyList<string>? current)
    {
        var items = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (current is not null)
        {
            foreach (string raw in current)
            {
                if (Known.Contains(raw) && seen.Add(raw))
                {
                    items.Add(raw);
                }
            }
        }

        foreach (string id in CatalogOrder)
        {
            if (!seen.Contains(id))
            {
                items = InsertInCatalogOrder(items, id);
                seen.Add(id);
            }
        }

        return items;
    }

    /// <summary>
    /// Soft migration helper: inserts <see cref="ControlCenter"/> before <see cref="Volume"/>
    /// when missing (or appends if volume is absent).
    /// </summary>
    internal static void EnsureControlCenter(List<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Contains(ControlCenter, StringComparer.Ordinal))
        {
            return;
        }

        int volumeIndex = items.FindIndex(static x =>
            string.Equals(x, Volume, StringComparison.Ordinal));
        if (volumeIndex >= 0)
        {
            items.Insert(volumeIndex, ControlCenter);
        }
        else
        {
            items.Add(ControlCenter);
        }
    }

    /// <summary>
    /// Soft migration helper: inserts <see cref="Brightness"/> after <see cref="Volume"/>
    /// when missing (or appends if volume is absent).
    /// </summary>
    internal static void EnsureBrightness(List<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Contains(Brightness, StringComparer.Ordinal))
        {
            return;
        }

        int volumeIndex = items.FindIndex(static x =>
            string.Equals(x, Volume, StringComparison.Ordinal));
        if (volumeIndex >= 0)
        {
            items.Insert(volumeIndex + 1, Brightness);
        }
        else
        {
            items.Add(Brightness);
        }
    }

    /// <summary>
    /// Soft migration helper: inserts a missing <see cref="Media"/> after <see cref="Brightness"/>
    /// (after <see cref="Volume"/> if brightness is absent; at the end if neither is).
    /// </summary>
    internal static void EnsureMedia(List<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Contains(Media, StringComparer.Ordinal))
        {
            return;
        }

        int index = items.FindIndex(static x =>
            string.Equals(x, Brightness, StringComparison.Ordinal));
        if (index < 0)
        {
            index = items.FindIndex(static x =>
                string.Equals(x, Volume, StringComparison.Ordinal));
        }

        if (index >= 0)
        {
            items.Insert(index + 1, Media);
        }
        else
        {
            items.Add(Media);
        }
    }

    /// <summary>
    /// Soft migration helper: inserts a missing <see cref="TrayMirror"/> before <see cref="TrayOverflow"/>
    /// (at the end if there is none).
    /// </summary>
    internal static void EnsureTrayMirror(List<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Contains(TrayMirror, StringComparer.Ordinal))
        {
            return;
        }

        int index = items.FindIndex(static x =>
            string.Equals(x, TrayOverflow, StringComparison.Ordinal));
        if (index >= 0)
        {
            items.Insert(index, TrayMirror);
        }
        else
        {
            items.Add(TrayMirror);
        }
    }

    internal static bool IsKnown(string id) => Known.Contains(id);

    /// <summary>
    /// Moves <paramref name="draggedId"/> to the index <paramref name="targetId"/> had
    /// before the removal. Dropping on the next item swaps with it; dropping on the last
    /// item makes the dragged item last. The same id, or an id that is not in the list,
    /// leaves the order unchanged.
    /// </summary>
    internal static bool Move(List<string> items, string draggedId, string targetId)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (string.Equals(draggedId, targetId, StringComparison.Ordinal))
        {
            return false;
        }

        int from = items.FindIndex(x => string.Equals(x, draggedId, StringComparison.Ordinal));
        int to = items.FindIndex(x => string.Equals(x, targetId, StringComparison.Ordinal));
        if (from < 0 || to < 0)
        {
            return false;
        }

        string moved = items[from];
        items.RemoveAt(from);
        items.Insert(Math.Clamp(to, 0, items.Count), moved);
        return true;
    }

    /// <summary>One visible status slot, in the strip's coordinate space.</summary>
    internal readonly record struct StatusStripSlot(string Id, double Left, double Width);

    /// <summary>
    /// Where <paramref name="draggedId"/> should land among the other slots.
    /// 0 is before the first other slot; the count of other slots appends after the last.
    /// The dragged slot is skipped so its own midpoint does not chase the pointer.
    /// A pointer on a neighbor's center counts as the right half (insert after that neighbor).
    /// An id that is not in <paramref name="slots"/> returns -1.
    /// </summary>
    internal static int ResolveInsertionIndex(
        IReadOnlyList<StatusStripSlot> slots,
        string draggedId,
        double pointerX)
    {
        ArgumentNullException.ThrowIfNull(slots);

        int dragged = -1;
        for (int i = 0; i < slots.Count; i++)
        {
            if (string.Equals(slots[i].Id, draggedId, StringComparison.Ordinal))
            {
                dragged = i;
                break;
            }
        }

        if (dragged < 0)
        {
            return -1;
        }

        int insertAt = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            if (i == dragged)
            {
                continue;
            }

            double center = slots[i].Left + (slots[i].Width / 2d);
            if (pointerX < center)
            {
                return insertAt;
            }

            insertAt++;
        }

        return insertAt;
    }

    /// <summary>
    /// Moves <paramref name="draggedId"/> to gap <paramref name="insertAt"/> among
    /// <paramref name="visibleIds"/> (0 = before the first other visible id). Ids that are
    /// in <paramref name="items"/> but not visible stay in the list. The same gap, or an
    /// id that is not both saved and visible, leaves the order unchanged.
    /// </summary>
    internal static bool MoveToInsertion(
        List<string> items,
        string draggedId,
        IReadOnlyList<string> visibleIds,
        int insertAt)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(visibleIds);

        int from = items.FindIndex(x => string.Equals(x, draggedId, StringComparison.Ordinal));
        if (from < 0)
        {
            return false;
        }

        var visible = new List<string>();
        foreach (string id in visibleIds)
        {
            if (items.Exists(x => string.Equals(x, id, StringComparison.Ordinal))
                && !visible.Contains(id, StringComparer.Ordinal))
            {
                visible.Add(id);
            }
        }

        int visibleIndex = visible.FindIndex(x => string.Equals(x, draggedId, StringComparison.Ordinal));
        if (visibleIndex < 0)
        {
            return false;
        }

        var others = new List<string>(visible.Count);
        foreach (string id in visible)
        {
            if (!string.Equals(id, draggedId, StringComparison.Ordinal))
            {
                others.Add(id);
            }
        }

        insertAt = Math.Clamp(insertAt, 0, others.Count);
        if (insertAt == visibleIndex)
        {
            return false;
        }

        items.RemoveAt(from);

        int dest;
        if (insertAt >= others.Count)
        {
            if (others.Count == 0)
            {
                dest = items.Count;
            }
            else
            {
                int last = items.FindIndex(x => string.Equals(x, others[^1], StringComparison.Ordinal));
                dest = last < 0 ? items.Count : last + 1;
            }
        }
        else
        {
            dest = items.FindIndex(x => string.Equals(x, others[insertAt], StringComparison.Ordinal));
            if (dest < 0)
            {
                dest = items.Count;
            }
        }

        items.Insert(dest, draggedId);
        return true;
    }
}
