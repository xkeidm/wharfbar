using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Owns the dock's ordered, de-duplicated, persisted pinned list.
/// <para>
/// List operations are pure statics (<c>*Core</c>) so they can be unit-tested without
/// touching disk; the instance methods add persistence and change notification on top.
/// </para>
/// </summary>
internal sealed class DockItemStore
{
    private readonly ConfigService _config;
    private List<PinnedApp> _items;
    private List<string> _itemOrder;

    internal DockItemStore(ConfigService config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _items = Snapshot(config.Current.Dock.Items);
        _itemOrder = SnapshotOrder(config.Current.Dock.ItemOrder);
    }

    internal IReadOnlyList<PinnedApp> Items => _items;

    /// <summary>
    /// Frozen left-group display order (normalized paths, taskbar pins included). Empty
    /// means "no override" — the composer falls back to taskbar order, then Items order.
    /// </summary>
    internal IReadOnlyList<string> ItemOrder => _itemOrder;

    internal event EventHandler? Changed;

    /// <summary>Canonical comparison key for an item's target path.</summary>
    internal static string Normalize(string path)
    {
        string trimmed = path?.Trim() ?? string.Empty;
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // QueryFullProcessImageName can hand back an extended path. A pin stored as a
        // normal Win32 path must still match that process.
        if (trimmed.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..];
            if (trimmed.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = @"\\" + trimmed[4..];
            }
        }
        else if (trimmed.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            trimmed = trimmed[4..];
        }

        // Exists for the inputs Path.GetFullPath refuses outright: paths past the Windows
        // ~32 767-char limit, paths with an illegal character (embedded NUL), and I/O or
        // security failures. Those are rejected, not normalized, so the caller would get an
        // exception instead of a usable comparison key; degrade to the trimmed input instead.
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException
            or System.Security.SecurityException)
        {
            return trimmed;
        }
    }

    internal static string IdFor(PinnedApp app) => Normalize(app.Path);

    internal static bool ContainsById(IReadOnlyList<PinnedApp> items, string id)
    {
        string key = Normalize(id);
        foreach (PinnedApp item in items)
        {
            if (string.Equals(IdFor(item), key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryAddCore(List<PinnedApp> items, string path, string args, string label)
    {
        string normalized = Normalize(path);
        if (normalized.Length == 0 || ContainsById(items, normalized))
        {
            return false;
        }

        string resolvedLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(normalized)
            : label.Trim();

        // Never leave an empty label. A trailing separator survives GetFullPath, so a folder
        // pin ("C:\tools\", "C:\", "\\server\share\") derives an empty name; fall back to the
        // separator-trimmed path. An all-separator input normalizes to a bare root ("////"
        // becomes "\"), which trims away to nothing, so the raw path is the final floor.
        // Same rule in ConfigService.NormalizePinnedList — keep the two in step.
        if (string.IsNullOrWhiteSpace(resolvedLabel))
        {
            string trimmedPath = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            resolvedLabel = trimmedPath.Length == 0 ? normalized : trimmedPath;
        }

        items.Add(new PinnedApp(normalized, args?.Trim() ?? string.Empty, resolvedLabel));
        return true;
    }

    internal static bool RemoveCore(List<PinnedApp> items, string id)
    {
        int index = IndexOfId(items, id);
        if (index < 0)
        {
            return false;
        }

        items.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Moves the item to <paramref name="toIndex"/> in the resulting list. Indices past
    /// either end clamp, so a drop beyond the last icon appends instead of failing.
    /// </summary>
    internal static bool MoveCore(List<PinnedApp> items, string id, int toIndex)
    {
        int index = IndexOfId(items, id);
        if (index < 0)
        {
            return false;
        }

        PinnedApp moved = items[index];
        items.RemoveAt(index);
        items.Insert(Math.Clamp(toIndex, 0, items.Count), moved);
        return true;
    }

    internal bool TryAdd(string path, string args, string label)
    {
        if (!TryAddCore(_items, path, args, label))
        {
            return false;
        }

        Persist();
        return true;
    }

    internal bool Remove(string id)
    {
        if (!RemoveCore(_items, id))
        {
            return false;
        }

        Persist();
        return true;
    }

    internal bool Move(string id, int toIndex)
    {
        if (!MoveCore(_items, id, toIndex))
        {
            return false;
        }

        // Unfreeze the moved key: a frozen entry would pin this tile to its old spot and
        // the settings-page drag would look dead. Unmentioned, its display follows the
        // new Items order again.
        _itemOrder = NormalizeOrder(_itemOrder.Where(key =>
            !string.Equals(Normalize(key), Normalize(id), StringComparison.OrdinalIgnoreCase)));
        Persist();
        return true;
    }

    /// <summary>
    /// Freezes a full left-group sequence written by the dock strip's own reorder.
    /// </summary>
    internal void SetOrder(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _itemOrder = NormalizeOrder(keys);
        Persist();
    }

    /// <summary>
    /// Dock-strip reorder of any pinned tile — taskbar-only pins included. Keeps Items in
    /// the same relative shape (so the settings page stays honest) and freezes the whole
    /// visual sequence. One persist, one change event.
    /// </summary>
    internal void ReorderOnStrip(string draggedId, IReadOnlyList<string> displayOrder, int storeIndex)
    {
        ArgumentNullException.ThrowIfNull(draggedId);
        ArgumentNullException.ThrowIfNull(displayOrder);

        // Absent id → MoveCore is a no-op; the order still freezes, which is the point
        // for taskbar pins the store does not own.
        _ = MoveCore(_items, draggedId, storeIndex);
        _itemOrder = NormalizeOrder(displayOrder);
        Persist();
    }

    /// <summary>
    /// Re-reads the list from config after a change made behind the store's back (external
    /// edit, migration). Raises <see cref="Changed"/> exactly like the mutating operations,
    /// so event-driven consumers — the composer — rebuild instead of reading a stale list.
    /// </summary>
    internal void Reload()
    {
        _items = Snapshot(_config.Current.Dock.Items);
        _itemOrder = SnapshotOrder(_config.Current.Dock.ItemOrder);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<PinnedApp> Snapshot(IReadOnlyList<PinnedApp> source)
    {
        var copy = new List<PinnedApp>(source?.Count ?? 0);
        if (source is null)
        {
            return copy;
        }

        foreach (PinnedApp app in source)
        {
            if (app is not null && !string.IsNullOrWhiteSpace(app.Path))
            {
                copy.Add(new PinnedApp(app.Path, app.Args ?? string.Empty, app.Label ?? string.Empty));
            }
        }

        return copy;
    }

    private static int IndexOfId(IReadOnlyList<PinnedApp> items, string id)
    {
        string key = Normalize(id);
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(IdFor(items[i]), key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void Persist()
    {
        _config.Current.Dock.Items = new List<PinnedApp>(_items);
        _config.Current.Dock.ItemOrder = new List<string>(_itemOrder);
        _config.Save(_config.Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<string> SnapshotOrder(IReadOnlyList<string>? source)
        => NormalizeOrder(source);

    /// <summary>
    /// Dedupes and canonicalizes order entries: keys are written from strip tiles (already
    /// normalized) but a hand-edited config may hold raw or duplicated paths. First
    /// occurrence wins; empty keys drop.
    /// </summary>
    private static List<string> NormalizeOrder(IEnumerable<string>? keys)
    {
        var result = new List<string>();
        if (keys is null)
        {
            return result;
        }

        foreach (string key in keys)
        {
            string normalized = Normalize(key);
            if (normalized.Length == 0 || result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }
}
