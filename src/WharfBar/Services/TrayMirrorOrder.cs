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
    /// <summary>
    /// Prefix separator for index-suffixed keys. The UIA tray exposes unnamed buttons (no
    /// tooltip) and sometimes two buttons with the same name; a bare name/empty key would make
    /// them collide — <see cref="UniqueByKey"/> would silently drop all but the first and the
    /// bar would show fewer icons than the tray.
    /// <para>
    /// Limitation: the suffix is the button's current tray index, so unnamed/repeated keys are
    /// stable only while the tray order is. If a button before it appears or disappears, the
    /// same physical button gets a different <c>\0#index</c> key. Only the plain tooltip-name
    /// keys (the common case) can be persisted in <c>topBar.trayOverflowIcons</c> / matched across
    /// polls; dragging an unnamed button out of the box is not stable across reorders and is
    /// accepted as a known cost of not inventing synthetic identities for buttons UIA does not
    /// name.
    /// </para>
    /// </summary>
    internal const char KeySuffixSeparator = '\0';

    /// <summary>
    /// One unique mirror key per tray button, in tray order. A unique name is kept as-is
    /// (persisted overflow keys are tooltip names); unnamed buttons and repeated names get
    /// <c>name\0#index</c> / <c>\0#index</c> so no button is lost to de-duplication. The index is
    /// the tray position at read time: those keys change when the tray order changes (see
    /// <see cref="KeySuffixSeparator"/>), while a unique tooltip name stays stable.
    /// </summary>
    internal static IReadOnlyList<string> UniqueKeys(IReadOnlyList<string?> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keys = new List<string>(names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i]?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                keys.Add($"{KeySuffixSeparator}#{i}");
                continue;
            }

            keys.Add(seen.Add(name) ? name : $"{name}{KeySuffixSeparator}#{i}");
        }

        return keys;
    }

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
