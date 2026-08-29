namespace WharfBar.Services;

/// <summary>
/// Pure matcher for replaying a mirrored tray click onto a live tray button list. Names with
/// live state (e.g. "EarTrumpet: 83%") churn, so non-empty keys fall back to bidirectional
/// containment; empty keys must NEVER containment-match (every string contains the empty
/// string — that clicked the wrong app), they resolve exactly against unnamed buttons or by
/// the poll-time index.
/// </summary>
internal static class TrayButtonMatcher
{
    internal static int ResolveIndex(IReadOnlyList<string?> liveNames, string? key, int fallbackIndex)
    {
        string trimmed = key?.Trim() ?? string.Empty;

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
