namespace WharfBar.Models;

/// <summary>Built-in top-bar status strip item ids (config <c>topBar.statusItems</c>).</summary>
internal static class TopBarStatusItemIds
{
    internal const string TrayOverflow = "trayOverflow";
    internal const string Network = "network";
    internal const string Battery = "battery";
    internal const string Volume = "volume";
    internal const string DateTime = "dateTime";
    internal const string Notifications = "notifications";

    internal static readonly IReadOnlyList<string> DefaultOrder =
    [
        TrayOverflow,
        Network,
        Battery,
        Volume,
        DateTime,
        Notifications,
    ];

    private static readonly HashSet<string> Known = new(DefaultOrder, StringComparer.Ordinal);

    /// <summary>
    /// Keeps first occurrence of known ids only; empty/null → default full set.
    /// Unknown ids dropped (soft).
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

    internal static bool IsKnown(string id) => Known.Contains(id);
}
