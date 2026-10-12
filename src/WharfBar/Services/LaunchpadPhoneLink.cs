using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WharfBar.Models;

namespace WharfBar.Services;

internal readonly record struct LaunchpadPhone(string Name);

/// <summary>One Phone Link notification line: app name and a short count, never a message body.</summary>
internal sealed record LaunchpadPhoneNotice(string AppName, string Summary, string? IconDataUri = null);

/// <summary>
/// Read-only Phone Link snapshot. Null fields were not in a supported store and must stay hidden.
/// </summary>
internal sealed record LaunchpadPhoneSnapshot(
    string Name,
    int? BatteryPercent,
    bool? BluetoothOn,
    bool Connected,
    bool SendFiles,
    IReadOnlyList<LaunchpadPhoneNotice> Recent,
    string? DeviceImageUri = null,
    string? DeviceFrameUri = null);

/// <summary>
/// Phone Link row. <see cref="Resolve"/> keeps the old name-only rule.
/// <see cref="Read"/> shows a device only when a name is actually stored.
/// </summary>
internal static partial class LaunchpadPhoneLink
{
    internal const string MessagesUri = "ms-phone:messages";

    /// <summary>Phone Link's own card uses <c>ms-phone:calling</c>.</summary>
    internal const string CallsUri = "ms-phone:calling";

    internal const string PhotosUri = "ms-phone:photos";

    /// <summary>Confirmed on the Phone Link start-menu card. Omitted when that card has no file action.</summary>
    internal const string FilesUri = "ms-phone:filetransfer";

    private const int NoticeCap = 4;

    internal static LaunchpadPhone? Resolve(string? deviceName, bool hostRunning)
    {
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            return new LaunchpadPhone(deviceName.Trim());
        }

        return hostRunning ? new LaunchpadPhone("Telefon") : null;
    }

    internal static string? FormatBattery(int? percent) =>
        percent is >= 0 and <= 100 ? $"%{percent.Value}" : null;

    /// <summary>Linked device from the companion card, device metadata, or the YourPhone registry name.</summary>
    internal static LaunchpadPhone? Current()
    {
        LaunchpadPhoneSnapshot? snapshot = Read();
        return snapshot is null ? null : new LaunchpadPhone(snapshot.Name);
    }

    internal static LaunchpadPhoneSnapshot? Read()
    {
        string? companion = TryRead(CompanionPath());
        string? metadata = TryRead(MetadataPath()) ?? TryRead(CrossDeviceMetadataPath());
        return Compose(companion, metadata, ReadRegistryDeviceName());
    }

    internal static LaunchpadPhoneSnapshot? Compose(string? companionJson, string? metadataJson, string? registryName)
    {
        LaunchpadPhoneSnapshot? card = ParseCompanion(companionJson);
        if (card is not null)
        {
            return card;
        }

        string? linked = ParseLinkedDeviceName(metadataJson);
        if (!string.IsNullOrWhiteSpace(linked))
        {
            return NameOnly(linked);
        }

        return string.IsNullOrWhiteSpace(registryName) ? null : NameOnly(registryName);
    }

    internal static LaunchpadPhoneSnapshot? ParseCompanion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return ParseCompanion(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Display name of a linked Android / Link-to-Windows device. The PC entry is ignored.</summary>
    internal static string? ParseLinkedDeviceName(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return ParseLinkedDeviceName(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LaunchpadPhoneSnapshot? ParseCompanion(JsonElement root)
    {
        var texts = new List<(string Text, string? Weight)>();
        var images = new List<string>();
        var urls = new List<string>();
        Walk(root, texts, images, urls);

        string? name = null;
        foreach ((string text, string? weight) in texts)
        {
            if (weight is null || !weight.Contains("bold", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string trimmed = text.Trim();
            if (!IsDeviceName(trimmed))
            {
                continue;
            }

            name = trimmed;
            break;
        }

        if (name is null)
        {
            return null;
        }

        (string? screen, string? frame) = FindDeviceImages(root);
        return new LaunchpadPhoneSnapshot(
            name,
            ReadBattery(texts, images),
            ReadBluetooth(images),
            ReadConnected(images),
            urls.Any(url => SamePhonePath(url, FilesUri)),
            ReadNotices(root),
            screen,
            frame);
    }

    private static string? ParseLinkedDeviceName(JsonElement root)
    {
        if (!root.TryGetProperty("DeviceMetadatas", out JsonElement accounts)
            || accounts.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? bestName = null;
        string? bestSeen = null;
        foreach (JsonProperty account in accounts.EnumerateObject())
        {
            if (account.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement device in account.Value.EnumerateArray())
            {
                if (!IsLinkedPhone(device, out string? displayName, out string? seen))
                {
                    continue;
                }

                if (bestName is null || string.CompareOrdinal(seen, bestSeen) > 0)
                {
                    bestName = displayName;
                    bestSeen = seen;
                }
            }
        }

        return bestName;
    }

    private static bool IsLinkedPhone(JsonElement device, out string? displayName, out string? seen)
    {
        displayName = null;
        seen = null;
        if (!device.TryGetProperty("IsLinked", out JsonElement linked) || linked.ValueKind != JsonValueKind.True)
        {
            return false;
        }

        if (!device.TryGetProperty("Metadata", out JsonElement metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("Metadata", out JsonElement inner)
            || inner.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (inner.TryGetProperty("IsEnabled", out JsonElement enabled) && enabled.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        string client = ReadString(inner, "ClientType");
        string os = ReadString(inner, "OsName");
        bool phone = client.Equals("LTW", StringComparison.OrdinalIgnoreCase)
            || os.Equals("Android", StringComparison.OrdinalIgnoreCase);
        if (!phone)
        {
            return false;
        }

        string name = ReadString(inner, "DisplayName").Trim();
        if (!IsDeviceName(name))
        {
            return false;
        }

        displayName = name;
        seen = ReadString(metadata, "LastSeenTime");
        return true;
    }

    private static int? ReadBattery(List<(string Text, string? Weight)> texts, List<string> images)
    {
        foreach ((string text, _) in texts)
        {
            if (PercentOnly().IsMatch(text.Trim()) && TryPercent(text, out int percent))
            {
                return percent;
            }
        }

        foreach (string image in images)
        {
            if (!image.Contains("pil", StringComparison.OrdinalIgnoreCase)
                && !image.Contains("battery", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryPercent(image, out int percent))
            {
                return percent;
            }
        }

        return null;
    }

    private static bool? ReadBluetooth(List<string> images)
    {
        foreach (string image in images)
        {
            if (image.Contains("bluetooth", StringComparison.OrdinalIgnoreCase))
            {
                return OnOff(image);
            }
        }

        return null;
    }

    private static bool ReadConnected(List<string> images)
    {
        foreach (string image in images)
        {
            if (image.Contains("bluetooth", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ContainsAny(image, "bağlı değil", "bagli degil", "not connected", "disconnected"))
            {
                return false;
            }

            if (ContainsAny(image, "bağlı", "bagli", "connected"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool? OnOff(string text)
    {
        if (ContainsAny(text, "kapalı", "kapali", "bağlı değil", "bagli degil", "not connected", "disconnected", "disabled")
            || OffWord().IsMatch(text))
        {
            return false;
        }

        if (ContainsAny(text, "açık", "acik", "bağlı", "bagli", "connected", "enabled") || OnWord().IsMatch(text))
        {
            return true;
        }

        return null;
    }

    private static List<LaunchpadPhoneNotice> ReadNotices(JsonElement root)
    {
        var notices = new List<LaunchpadPhoneNotice>();
        CollectNotices(root, notices);
        return notices;
    }

    private static void CollectNotices(JsonElement element, List<LaunchpadPhoneNotice> notices)
    {
        if (notices.Count >= NoticeCap || element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var texts = new List<string>();
            GatherTexts(element, texts);
            if (texts.Count == 2
                && IsSummary(texts[1])
                && !IsSummary(texts[0])
                && IsDeviceName(texts[0]))
            {
                notices.Add(new LaunchpadPhoneNotice(texts[0].Trim(), texts[1].Trim(), FindAppIcon(element)));
                return;
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                CollectNotices(child, notices);
            }

            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            CollectNotices(property.Value, notices);
        }
    }

    private static string? FindAppIcon(JsonElement element)
    {
        string? labeled = null;
        string? plain = null;
        CollectIcons(element, ref labeled, ref plain);
        return plain ?? labeled;
    }

    private static void CollectIcons(JsonElement element, ref string? labeled, ref string? plain)
    {
        if (plain is not null || element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                CollectIcons(child, ref labeled, ref plain);
                if (plain is not null)
                {
                    return;
                }
            }

            return;
        }

        if (IsImage(element) && DataImage(element) is string uri)
        {
            if (string.IsNullOrWhiteSpace(ReadString(element, "altText"))
                && string.IsNullOrWhiteSpace(ReadString(element, "tooltip")))
            {
                plain = uri;
                return;
            }

            labeled ??= uri;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            CollectIcons(property.Value, ref labeled, ref plain);
            if (plain is not null)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Phone Link stacks the screen under a transparent bezel. The first ImageSet image is the
    /// screenshot; the second is the phone frame.
    /// </summary>
    private static (string? Screen, string? Frame) FindDeviceImages(JsonElement element)
    {
        if (!TryFindImageSet(element, out JsonElement set)
            || !set.TryGetProperty("images", out JsonElement images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        string? screen = null;
        string? frame = null;
        foreach (JsonElement image in images.EnumerateArray())
        {
            if (DataImage(image) is not string uri)
            {
                continue;
            }

            if (screen is null)
            {
                screen = uri;
                continue;
            }

            frame = uri;
            break;
        }

        return (screen, frame);
    }

    private static bool TryFindImageSet(JsonElement element, out JsonElement set)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                if (TryFindImageSet(child, out set))
                {
                    return true;
                }
            }

            set = default;
            return false;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "ImageSet")
            {
                set = element;
                return true;
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (TryFindImageSet(property.Value, out set))
                {
                    return true;
                }
            }
        }

        set = default;
        return false;
    }

    private static bool IsImage(JsonElement element) =>
        element.TryGetProperty("type", out JsonElement type)
        && type.ValueKind == JsonValueKind.String
        && type.GetString() == "Image";

    private static string? DataImage(JsonElement element)
    {
        string url = ReadString(element, "url");
        return url.StartsWith("data:image", StringComparison.OrdinalIgnoreCase) ? url : null;
    }

    private static void GatherTexts(JsonElement element, List<string> texts)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                GatherTexts(child, texts);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (element.TryGetProperty("type", out JsonElement type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "TextBlock"
            && element.TryGetProperty("text", out JsonElement text)
            && text.ValueKind == JsonValueKind.String)
        {
            string? value = text.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                texts.Add(value.Trim());
            }
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            GatherTexts(property.Value, texts);
        }
    }

    private static void Walk(
        JsonElement element,
        List<(string Text, string? Weight)> texts,
        List<string> images,
        List<string> urls)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                Walk(child, texts, images, urls);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string? type = element.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;
        if (type == "TextBlock"
            && element.TryGetProperty("text", out JsonElement text)
            && text.ValueKind == JsonValueKind.String)
        {
            string? weight = element.TryGetProperty("weight", out JsonElement weightElement) && weightElement.ValueKind == JsonValueKind.String
                ? weightElement.GetString()
                : null;
            texts.Add((text.GetString() ?? string.Empty, weight));
        }
        else if (type == "Image")
        {
            images.Add(ReadString(element, "altText") + " " + ReadString(element, "tooltip"));
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.NameEquals("url")
                && property.Value.ValueKind == JsonValueKind.String
                && property.Value.GetString() is string url
                && url.StartsWith("ms-phone:", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(url);
            }

            Walk(property.Value, texts, images, urls);
        }
    }

    private static LaunchpadPhoneSnapshot NameOnly(string name) =>
        new(name.Trim(), null, null, false, false, []);

    private static bool IsDeviceName(string text) =>
        text.Length is > 0 and <= 64
        && !text.Equals("En son", StringComparison.OrdinalIgnoreCase)
        && !IsSummary(text);

    private static bool IsSummary(string text) => SummaryPattern().IsMatch(text.Trim());

    private static bool TryPercent(string text, out int percent)
    {
        Match match = Percent().Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out percent))
        {
            percent = 0;
            return false;
        }

        return percent is >= 0 and <= 100;
    }

    private static bool SamePhonePath(string url, string path)
    {
        int query = url.IndexOf('?', StringComparison.Ordinal);
        string bare = query >= 0 ? url[..query] : url;
        return bare.Trim().Equals(path, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? ReadRegistryDeviceName()
    {
        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\YourPhone",
                "DeviceName",
                null) as string;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar phone link: {ex.Message}");
            return null;
        }
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar phone link: {ex.Message}");
            return null;
        }
    }

    private static string CompanionPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.YourPhone_8wekyb3d8bbwe\LocalState\StartMenu\StartMenuCompanion.json");

    private static string MetadataPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.YourPhone_8wekyb3d8bbwe\LocalCache\DeviceMetadataStorage.json");

    private static string CrossDeviceMetadataPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\MicrosoftWindows.CrossDevice_cw5n1h2txyewy\LocalCache\DeviceMetadataStorage.json");

    [GeneratedRegex(@"^%\d{1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex PercentOnly();

    [GeneratedRegex(@"%(\d{1,3})", RegexOptions.CultureInvariant)]
    private static partial Regex Percent();

    [GeneratedRegex(@"^\d{1,4}\s+yeni\s+\S.{0,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryPattern();

    [GeneratedRegex(@"(?<!\p{L})off(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OffWord();

    [GeneratedRegex(@"(?<!\p{L})on(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OnWord();
}

internal static class LaunchpadPins
{
    internal static IReadOnlyList<LaunchpadApp> FromDock(DockConfig? dock)
    {
        if (dock?.Items is null)
        {
            return [];
        }

        var pins = new List<LaunchpadApp>();
        foreach (PinnedApp pin in dock.Items)
        {
            if (string.IsNullOrWhiteSpace(pin.Path))
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(pin.Label)
                ? Path.GetFileNameWithoutExtension(pin.Path)
                : pin.Label;
            if (name.Length == 0)
            {
                continue;
            }

            pins.Add(new LaunchpadApp(name, pin.Path));
        }

        return pins;
    }
}
