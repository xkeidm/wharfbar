using System;
using System.Collections.Generic;
using System.IO;
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

    internal DockItemStore(ConfigService config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _items = Snapshot(config.Current.Dock.Items);
    }

    internal IReadOnlyList<PinnedApp> Items => _items;

    internal event EventHandler? Changed;

    /// <summary>Canonical comparison key for an item's target path.</summary>
    internal static string Normalize(string path)
    {
        string trimmed = path?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

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

        Persist();
        return true;
    }

    internal void Reload() => _items = Snapshot(_config.Current.Dock.Items);

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
        _config.Save(_config.Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
