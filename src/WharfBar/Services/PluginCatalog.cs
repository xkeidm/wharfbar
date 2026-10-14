using System.IO;
using System.Text.Json;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>Manifest <c>kind</c>: <c>plugin</c> or <c>widget</c>.</summary>
internal static class PluginKinds
{
    internal const string Plugin = "plugin";

    internal const string Widget = "widget";

    internal static bool TryNormalize(string? kind, out string normalized)
    {
        if (string.Equals(kind, Widget, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Widget;
            return true;
        }

        if (string.Equals(kind, Plugin, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Plugin;
            return true;
        }

        normalized = Plugin;
        return false;
    }

    internal static string Label(string kind) =>
        kind == Widget ? "Widget" : "Eklenti";
}

/// <summary>Where a plugin or widget is meant to appear. v1 draws only the built-ins.</summary>
internal static class PluginSlots
{
    internal const string TopBar = "topBar";

    internal const string Dock = "dock";

    internal const string Desktop = "desktop";

    internal const string Island = "island";

    internal static string DefaultFor(string kind) =>
        kind == PluginKinds.Widget ? Desktop : Island;

    internal static bool TryNormalize(string? slot, out string normalized)
    {
        if (string.Equals(slot, TopBar, StringComparison.OrdinalIgnoreCase))
        {
            normalized = TopBar;
            return true;
        }

        if (string.Equals(slot, Dock, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Dock;
            return true;
        }

        if (string.Equals(slot, Desktop, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Desktop;
            return true;
        }

        if (string.Equals(slot, Island, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Island;
            return true;
        }

        normalized = Island;
        return false;
    }

    internal static string Label(string slot) => slot switch
    {
        TopBar => "Üst çubuk",
        Dock => "Dock",
        Desktop => "Masaüstü",
        _ => "Ada",
    };
}

/// <summary>One <c>manifest.json</c>. Extra fields such as assembly or url are ignored.</summary>
internal sealed record PluginManifest(
    string Id,
    string Name,
    string Summary,
    string Kind,
    string Slot);

/// <summary>A row in the gallery: built-in or discovered from a local folder.</summary>
internal sealed record PluginCard(
    string Id,
    string Name,
    string Summary,
    string Kind,
    string Slot,
    bool BuiltIn,
    bool Enabled);

/// <summary>
/// In-process catalog. Built-ins map onto island flags and desktop widgets.
/// Local folders contribute metadata only — no download and no assembly load.
/// </summary>
internal static class PluginCatalog
{
    internal const string Visualizer = "visualizer";

    internal const string BluetoothNotice = "bluetooth-notice";

    internal const string CameraMirror = "camera-mirror";

    internal const string Clock = "clock";

    internal const string Calendar = "calendar";

    internal const string ManifestFileName = "manifest.json";

    internal const int MaxIdLength = 64;

    internal const int MaxNameLength = 80;

    internal const int MaxSummaryLength = 240;

    internal const int MaxEnabled = 64;

    internal const int MaxManifestBytes = 16 * 1024;

    internal static string PluginsDirectory(string configDirectory) =>
        Path.Combine(configDirectory, "plugins");

    internal static bool IsBuiltIn(string id) => id is Visualizer or BluetoothNotice or CameraMirror or Clock or Calendar;

    internal static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength)
        {
            return false;
        }

        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            bool ok = c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';
            if (!ok || (c == '-' && (i == 0 || i == id.Length - 1)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses a manifest object. Returns null when id, name, kind, or slot is unusable.
    /// <c>assembly</c>, <c>entryPoint</c>, <c>url</c>, and <c>download</c> are not read.
    /// </summary>
    internal static PluginManifest? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            JsonElement root = doc.RootElement;
            if (!TryString(root, "id", out string id))
            {
                return null;
            }

            id = id.Trim().ToLowerInvariant();
            if (!IsValidId(id))
            {
                return null;
            }

            if (!TryString(root, "name", out string name))
            {
                return null;
            }

            name = name.Trim();
            if (name.Length > MaxNameLength)
            {
                name = name[..MaxNameLength].TrimEnd();
            }

            if (name.Length == 0)
            {
                return null;
            }

            string summary = string.Empty;
            if (TryString(root, "description", out string description) || TryString(root, "summary", out description))
            {
                summary = description.Trim();
                if (summary.Length > MaxSummaryLength)
                {
                    summary = summary[..MaxSummaryLength].TrimEnd();
                }
            }

            string kind = PluginKinds.Plugin;
            if (TryProperty(root, "kind", out JsonElement kindElement))
            {
                if (kindElement.ValueKind != JsonValueKind.String
                    || !PluginKinds.TryNormalize(kindElement.GetString(), out kind))
                {
                    return null;
                }
            }

            string slot = PluginSlots.DefaultFor(kind);
            if (TryProperty(root, "slot", out JsonElement slotElement))
            {
                if (slotElement.ValueKind != JsonValueKind.String
                    || !PluginSlots.TryNormalize(slotElement.GetString(), out slot))
                {
                    return null;
                }
            }

            return new PluginManifest(id, name, summary, kind, slot);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads <c>manifest.json</c> from each immediate child folder.
    /// Reparse points, oversized files, built-in ids, and duplicate ids are skipped.
    /// </summary>
    internal static IReadOnlyList<PluginManifest> Discover(string? root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var found = new List<PluginManifest>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string directory in directories)
        {
            if (found.Count >= MaxEnabled)
            {
                break;
            }

            if (IsReparsePoint(directory))
            {
                continue;
            }

            PluginManifest? manifest = ReadManifest(Path.Combine(directory, ManifestFileName));
            if (manifest is null || IsBuiltIn(manifest.Id) || !seen.Add(manifest.Id))
            {
                continue;
            }

            found.Add(manifest);
        }

        found.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return found;
    }

    internal static IReadOnlyList<PluginCard> Compose(AppConfig config, IReadOnlyList<PluginManifest> local)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Island ??= new IslandConfig();
        config.Plugins ??= new PluginsConfig();
        var open = new HashSet<string>(NormalizeOpenWidgets(config.Island.OpenWidgets), StringComparer.Ordinal);
        var enabled = new HashSet<string>(NormalizeEnabled(config.Plugins.Enabled), StringComparer.Ordinal);
        var cards = new List<PluginCard>
        {
            BuiltIn(
                Visualizer,
                "Görselleştirici",
                "Müzik çalarken ada hapında beş çubuk.",
                PluginKinds.Plugin,
                PluginSlots.Island,
                config.Island.Visualizer),
            BuiltIn(
                BluetoothNotice,
                "Bluetooth bildirimi",
                "Bağlanan cihazın adı hapta kısa süre durur.",
                PluginKinds.Plugin,
                PluginSlots.Island,
                config.Island.BluetoothNotice),
            BuiltIn(
                CameraMirror,
                "Kamera aynası",
                "Hapta canlı önizleme. İzin yoksa görüntü yok.",
                PluginKinds.Plugin,
                PluginSlots.Island,
                config.Island.CameraMirror),
            BuiltIn(
                Clock,
                "Saat",
                "Masaüstünde saat penceresi. Ada hapına girmez.",
                PluginKinds.Widget,
                PluginSlots.Desktop,
                open.Contains(Clock)),
            BuiltIn(
                Calendar,
                "Takvim",
                "Bu ayın ızgarası. Ada hapına girmez.",
                PluginKinds.Widget,
                PluginSlots.Desktop,
                open.Contains(Calendar)),
        };

        foreach (PluginManifest manifest in local)
        {
            if (IsBuiltIn(manifest.Id))
            {
                continue;
            }

            cards.Add(new PluginCard(
                manifest.Id,
                manifest.Name,
                manifest.Summary,
                manifest.Kind,
                manifest.Slot,
                BuiltIn: false,
                Enabled: enabled.Contains(manifest.Id)));
        }

        return cards;
    }

    internal static IReadOnlyList<PluginCard> Filter(IReadOnlyList<PluginCard> cards, string? kind)
    {
        if (string.IsNullOrEmpty(kind))
        {
            return cards;
        }

        var list = new List<PluginCard>();
        foreach (PluginCard card in cards)
        {
            if (card.Kind == kind)
            {
                list.Add(card);
            }
        }

        return list;
    }

    /// <summary>
    /// Turns a built-in on or off via island fields, or a local id via <c>plugins.enabled</c>.
    /// Returns false when <paramref name="id"/> is not a usable key.
    /// </summary>
    internal static bool SetEnabled(AppConfig config, string id, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        string key = id.Trim().ToLowerInvariant();
        config.Island ??= new IslandConfig();
        config.Plugins ??= new PluginsConfig();
        config.Island.OpenWidgets ??= [];
        config.Plugins.Enabled ??= [];

        switch (key)
        {
            case Visualizer:
                config.Island.Visualizer = enabled;
                return true;
            case BluetoothNotice:
                config.Island.BluetoothNotice = enabled;
                return true;
            case CameraMirror:
                config.Island.CameraMirror = enabled;
                return true;
            case Clock:
            case Calendar:
                SetFlag(config.Island.OpenWidgets, key, enabled);
                return true;
            default:
                if (!IsValidId(key) || IsBuiltIn(key))
                {
                    return false;
                }

                SetFlag(config.Plugins.Enabled, key, enabled);
                return true;
        }
    }

    internal static List<string> NormalizeEnabled(IList<string>? ids)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (ids is null)
        {
            return result;
        }

        foreach (string? raw in ids)
        {
            if (raw is null)
            {
                continue;
            }

            string id = raw.Trim().ToLowerInvariant();
            if (!IsValidId(id) || IsBuiltIn(id) || !seen.Add(id))
            {
                continue;
            }

            result.Add(id);
            if (result.Count == MaxEnabled)
            {
                break;
            }
        }

        return result;
    }

    internal static List<string> NormalizeOpenWidgets(IList<string>? ids)
    {
        var result = new List<string>();
        if (ids is null)
        {
            return result;
        }

        foreach (string? raw in ids)
        {
            if (raw is null)
            {
                continue;
            }

            string id = raw.Trim().ToLowerInvariant();
            if ((id == Clock || id == Calendar) && !result.Contains(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static PluginCard BuiltIn(string id, string name, string summary, string kind, string slot, bool enabled) =>
        new(id, name, summary, kind, slot, BuiltIn: true, enabled);

    private static void SetFlag(List<string> ids, string key, bool enabled)
    {
        ids.RemoveAll(item => item is not null && string.Equals(item.Trim(), key, StringComparison.OrdinalIgnoreCase));
        if (enabled)
        {
            ids.Add(key);
        }
    }

    private static PluginManifest? ReadManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > MaxManifestBytes)
            {
                return null;
            }

            return TryParse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsReparsePoint(string directory)
    {
        try
        {
            return (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool TryString(JsonElement obj, string name, out string value)
    {
        value = string.Empty;
        if (!TryProperty(obj, name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (JsonProperty property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
