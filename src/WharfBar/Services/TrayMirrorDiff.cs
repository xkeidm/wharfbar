using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>Pure key diff for mirror snapshots (unit-tested; no window access).</summary>
internal static class TrayMirrorDiff
{
    internal static (IReadOnlyList<string> Removed, IReadOnlyList<string> Added) Compute(
        IReadOnlySet<string> previousKeys,
        IReadOnlyList<TrayMirrorItem> next)
    {
        var nextKeys = new HashSet<string>(next.Select(static i => i.Key), StringComparer.Ordinal);
        var removed = previousKeys.Where(k => !nextKeys.Contains(k)).ToList();
        var added = next.Select(static i => i.Key).Where(k => !previousKeys.Contains(k)).ToList();
        return (removed, added);
    }

    /// <summary>
    /// True when publishing <paramref name="next"/> would not change anything the bar draws:
    /// same order, keys, tooltips, indices and the <b>same icon instances</b> (the icon cache
    /// returns the cached instance for unchanged pixels). <c>Tag</c> is ignored — clicks resolve
    /// a live UIA element by key at click time.
    /// </summary>
    internal static bool IsSameSnapshot(IReadOnlyList<TrayMirrorItem> previous, IReadOnlyList<TrayMirrorItem> next)
    {
        if (previous.Count != next.Count)
        {
            return false;
        }

        for (int i = 0; i < next.Count; i++)
        {
            TrayMirrorItem a = previous[i];
            TrayMirrorItem b = next[i];
            if (!string.Equals(a.Key, b.Key, StringComparison.Ordinal)
                || !string.Equals(a.Tooltip, b.Tooltip, StringComparison.Ordinal)
                || a.Index != b.Index
                || a.CommandId != b.CommandId
                || a.ScreenRect != b.ScreenRect
                || !ReferenceEquals(a.Icon, b.Icon))
            {
                return false;
            }
        }

        return true;
    }
}