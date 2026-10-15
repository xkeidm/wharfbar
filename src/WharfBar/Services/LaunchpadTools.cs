using WharfBar.Resources;

namespace WharfBar.Services;

/// <summary>
/// Footer tools on the launchpad card (<c>launchpad.tools</c>).
/// A missing list means every default. An empty list hides the strip.
/// </summary>
internal static class LaunchpadTools
{
    internal const string Documents = "documents";
    internal const string Downloads = "downloads";
    internal const string Pictures = "pictures";
    internal const string Settings = "settings";
    internal const string Widgets = "widgets";
    internal const string Plugins = "plugins";
    internal const string Power = "power";

    internal static readonly IReadOnlyList<string> DefaultIds =
    [
        Documents,
        Downloads,
        Pictures,
        Settings,
        Widgets,
        Plugins,
        Power,
    ];

    private static readonly HashSet<string> Known = new(DefaultIds, StringComparer.Ordinal);

    /// <summary>
    /// Keeps the first occurrence of each known id, in the order given.
    /// Blank and unknown ids are dropped. Null means the list was missing, so every default stays.
    /// An empty list stays empty.
    /// </summary>
    internal static List<string> Normalize(IEnumerable<string>? items)
    {
        if (items is null)
        {
            return DefaultIds.ToList();
        }

        var result = new List<string>(DefaultIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? raw in items)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string id = raw.Trim().ToLowerInvariant();
            if (!Known.Contains(id) || !seen.Add(id))
            {
                continue;
            }

            result.Add(id);
        }

        return result;
    }

    internal static string Label(string id) => id switch
    {
        Documents => Strings.Launchpad_ToolDocuments,
        Downloads => Strings.Launchpad_ToolDownloads,
        Pictures => Strings.Launchpad_ToolPictures,
        Settings => Strings.Launchpad_ToolSettings,
        Widgets => Strings.Launchpad_ToolWidgets,
        Plugins => Strings.Launchpad_ToolPlugins,
        Power => Strings.Launchpad_ToolPower,
        _ => id,
    };

    internal static string Glyph(string id) => id switch
    {
        Documents => "\uE8B7",
        Downloads => "\uE896",
        Pictures => "\uE91B",
        Settings => "\uE713",
        Widgets => "\uE771",
        Plugins => "\uE912",
        Power => "\uE7E8",
        _ => string.Empty,
    };
}
