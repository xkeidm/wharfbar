namespace WharfBar.Services;

/// <summary>
/// Pure matcher for replaying a mirrored tray click onto a live tray button list. Names with
/// live state (e.g. "EarTrumpet: 83%") churn, so non-empty keys fall back to bidirectional
/// containment; empty keys must NEVER containment-match (every string contains the empty
/// string — that clicked the wrong app), they resolve exactly against unnamed buttons or by
/// the poll-time index.
/// <para>
/// Keys are not always the bare tooltip: <see cref="TrayMirrorOrder.UniqueKeys"/> suffixes
/// unnamed buttons (<c>\0#index</c>) and repeated names (<c>name\0#index</c>) so the bar can show
/// every tray button. The suffix is an index hint, not part of the name — it is stripped before
/// matching, and an indexed key prefers its own poll-time index when the live name still equals
/// the key's name (two apps with the same tooltip must not exchange clicks).
/// </para>
/// </summary>
internal static class TrayButtonMatcher
{
    internal static int ResolveIndex(IReadOnlyList<string?> liveNames, string? key, int fallbackIndex)
    {
        string raw = key ?? string.Empty;
        int separator = raw.IndexOf(TrayMirrorOrder.KeySuffixSeparator);
        bool indexed = separator >= 0;
        string trimmed = (indexed ? raw[..separator] : raw).Trim();

        if (indexed && trimmed.Length == 0)
        {
            // Unnamed button: there is no name to match on, so the poll-time index is the
            // identity. Never containment-match an empty key.
            return fallbackIndex >= 0 && fallbackIndex < liveNames.Count ? fallbackIndex : -1;
        }

        if (indexed
            && fallbackIndex >= 0
            && fallbackIndex < liveNames.Count
            && string.Equals((liveNames[fallbackIndex] ?? string.Empty).Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return fallbackIndex;
        }

        for (int i = 0; i < liveNames.Count; i++)
        {
            string? name = liveNames[i];
            if (name is not null && string.Equals(name.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        if (trimmed.Length == 0)
        {
            for (int i = 0; i < liveNames.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(liveNames[i]))
                {
                    return i;
                }
            }

            return fallbackIndex >= 0 && fallbackIndex < liveNames.Count ? fallbackIndex : -1;
        }

        for (int i = 0; i < liveNames.Count; i++)
        {
            string? name = liveNames[i];
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
