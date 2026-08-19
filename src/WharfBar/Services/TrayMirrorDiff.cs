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
}