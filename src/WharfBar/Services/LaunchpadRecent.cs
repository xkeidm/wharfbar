using System.Globalization;
using System.IO;
using WharfBar.Resources;

namespace WharfBar.Services;

internal readonly record struct LaunchpadRecentItem(string Name, string Path, DateTime Written);

/// <summary>Newest shortcuts in the user's Recent folder. The face only displays them.</summary>
internal static class LaunchpadRecent
{
    internal static IReadOnlyList<LaunchpadRecentItem> List(int max) =>
        List(Environment.GetFolderPath(Environment.SpecialFolder.Recent), max);

    internal static IReadOnlyList<LaunchpadRecentItem> List(string? root, int max)
    {
        if (max <= 0 || string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return [];
        }

        var found = new List<LaunchpadRecentItem>();
        string[] files;
        try
        {
            // Materialize inside the try: enumeration throws on the first MoveNext, not at
            // the call site, so a transient lock must not escape the folder reader.
            files = [.. Directory.EnumerateFiles(root, "*.lnk")];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.Length == 0)
            {
                continue;
            }

            DateTime written;
            try
            {
                written = File.GetLastWriteTime(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            found.Add(new LaunchpadRecentItem(name, file, written));
        }

        found.Sort((a, b) => b.Written.CompareTo(a.Written));
        return Clean(found, max);
    }

    /// <summary>
    /// Windows records protocol jumps (screen snip, Photos spare process, ...) and repeats
    /// every launch as "name (54)". Those shortcuts are not documents; showing them turned
    /// the recent list into noise. Drops them, strips the launch counter, and keeps the
    /// newest entry per display name.
    /// </summary>
    internal static IReadOnlyList<LaunchpadRecentItem> Clean(
        IEnumerable<LaunchpadRecentItem> items,
        int max = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(items);
        var result = new List<LaunchpadRecentItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (LaunchpadRecentItem item in items)
        {
            string name = DisplayName(item.Name);
            if (name.Length == 0 || IsNoise(name) || !seen.Add(name))
            {
                continue;
            }

            result.Add(item with { Name = name });
            if (result.Count >= max)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>A name that is really a shell/protocol jump rather than a document.</summary>
    internal static bool IsNoise(string name)
    {
        string trimmed = name.Trim();
        return trimmed.Length == 0
            || trimmed.StartsWith("ms-", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains(':', StringComparison.Ordinal)
            || trimmed.StartsWith('@');
    }

    /// <summary>Strips the " (54)" launch counter Windows appends to repeated Recent entries.</summary>
    internal static string DisplayName(string name)
    {
        string trimmed = name.Trim();
        int open = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0 || !trimmed.EndsWith(')'))
        {
            return trimmed;
        }

        string suffix = trimmed[(open + 2)..^1];
        if (suffix.Length == 0)
        {
            return trimmed;
        }

        foreach (char digit in suffix)
        {
            if (!char.IsAsciiDigit(digit))
            {
                return trimmed;
            }
        }

        return trimmed[..open].TrimEnd();
    }

    internal static string FormatAge(DateTime written, DateTime now)
    {
        DateTime local = written.Kind == DateTimeKind.Utc ? written.ToLocalTime() : written;
        TimeSpan age = now - local;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalMinutes < 1)
        {
            return Strings.Launchpad_AgeNow;
        }

        if (age.TotalHours < 1)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Launchpad_AgeMinutes, Math.Max(1, (int)age.TotalMinutes));
        }

        if (local.Date == now.Date || age.TotalHours < 24)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Launchpad_AgeHours, Math.Max(1, (int)age.TotalHours));
        }

        if (local.Date == now.Date.AddDays(-1))
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.Launchpad_AgeYesterday, local.ToString("HH:mm", CultureInfo.CurrentCulture));
        }

        return local.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
    }
}
