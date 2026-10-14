namespace WharfBar.Services;

/// <summary>
/// Which mirrored tray keys sit in the WharfBar overflow box.
/// A key that is not listed stays on the bar: unknown and new icons default to visible.
/// This list is independent of <c>topBar.statusItems</c>.
/// </summary>
internal static class TrayOverflowMembership
{
    internal static IReadOnlyList<string> Normalize(IEnumerable<string?>? keys)
    {
        var result = new List<string>();
        if (keys is null)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? raw in keys)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string key = raw.Trim();
            if (seen.Add(key))
            {
                result.Add(key);
            }
        }

        return result;
    }

    internal static bool IsInBox(IEnumerable<string>? box, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || box is null)
        {
            return false;
        }

        string trimmed = key.Trim();
        foreach (string? entry in box)
        {
            if (string.Equals(entry?.Trim(), trimmed, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Known keys that stay on the bar, in <paramref name="known"/> order.</summary>
    internal static IReadOnlyList<string> VisibleKeys(IEnumerable<string>? known, IEnumerable<string>? box)
    {
        var result = new List<string>();
        if (known is null)
        {
            return result;
        }

        foreach (string? raw in known)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string key = raw.Trim();
            if (!IsInBox(box, key))
            {
                result.Add(key);
            }
        }

        return result;
    }

    /// <summary>
    /// Known keys that are parked, in box order. Entries whose icon is not currently
    /// mirrored are omitted here and kept in the persisted list.
    /// </summary>
    internal static IReadOnlyList<string> BoxedKeys(IEnumerable<string>? known, IEnumerable<string>? box)
    {
        var result = new List<string>();
        if (known is null || box is null)
        {
            return result;
        }

        var knownSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? raw in known)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                knownSet.Add(raw.Trim());
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? raw in box)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string key = raw.Trim();
            if (knownSet.Contains(key) && seen.Add(key))
            {
                result.Add(key);
            }
        }

        return result;
    }

    internal static List<string> MoveIn(IEnumerable<string>? box, string? key)
    {
        var next = new List<string>(Normalize(box));
        if (string.IsNullOrWhiteSpace(key))
        {
            return next;
        }

        string trimmed = key.Trim();
        if (!next.Contains(trimmed, StringComparer.Ordinal))
        {
            next.Add(trimmed);
        }

        return next;
    }

    internal static List<string> MoveOut(IEnumerable<string>? box, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return new List<string>(Normalize(box));
        }

        string trimmed = key.Trim();
        return Normalize(box)
            .Where(entry => !string.Equals(entry, trimmed, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// Drag starts only after the pointer moves at least the system minimum on one axis.
    /// A shorter motion stays a click.
    /// </summary>
    internal static bool ExceedsDragThreshold(double deltaX, double deltaY, double minimumX, double minimumY)
    {
        if (double.IsNaN(deltaX) || double.IsNaN(deltaY) || double.IsInfinity(deltaX) || double.IsInfinity(deltaY))
        {
            return false;
        }

        return Math.Abs(deltaX) >= minimumX || Math.Abs(deltaY) >= minimumY;
    }
}
