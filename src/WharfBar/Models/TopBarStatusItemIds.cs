namespace WharfBar.Models;

/// <summary>Built-in top-bar status strip item ids (config <c>topBar.statusItems</c>).</summary>
internal static class TopBarStatusItemIds
{
    internal const string TrayOverflow = "trayOverflow";
    internal const string DateTime = "dateTime";
    internal const string Notifications = "notifications";
    internal const string ControlCenter = "controlCenter";
    internal const string Volume = "volume";
    internal const string Brightness = "brightness";
    internal const string Network = "network";
    internal const string Battery = "battery";

    internal static readonly IReadOnlyList<string> DefaultOrder =
    [
        TrayOverflow,
        DateTime,
        Notifications,
        ControlCenter,
        Volume,
        Brightness,
        Network,
        Battery,
    ];

    private static readonly HashSet<string> Known = new(DefaultOrder, StringComparer.Ordinal);

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

    internal static bool IsKnown(string id) => Known.Contains(id);
}
