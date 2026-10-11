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
    /// Puts <paramref name="id"/> back into the visible set and reorders by
    /// <see cref="CatalogOrder"/> so a re-enabled volume/brightness/media icon is not dropped.
    /// </summary>
    internal static List<string> InsertInCatalogOrder(IReadOnlyList<string> current, string id)
    {
        ArgumentNullException.ThrowIfNull(current);
        var set = new HashSet<string>(current, StringComparer.Ordinal) { id };
        return CatalogOrder.Where(set.Contains).ToList();
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
    /// Soft migration helper: eksik <see cref="Media"/>'yı <see cref="Brightness"/> sonrasına ekler
    /// (brightness yoksa <see cref="Volume"/> sonrası; o da yoksa sona).
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
    /// Soft migration helper: eksik <see cref="TrayMirror"/>'ı <see cref="TrayOverflow"/> öncesine ekler
    /// (yoksa sona).
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
}
