using System.Globalization;
using System.IO;

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
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.lnk");
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
        if (found.Count > max)
        {
            found.RemoveRange(max, found.Count - max);
        }

        return found;
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
            return "Az önce";
        }

        if (age.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)age.TotalMinutes)} dakika önce";
        }

        if (local.Date == now.Date || age.TotalHours < 24)
        {
            return $"{Math.Max(1, (int)age.TotalHours)} saat önce";
        }

        if (local.Date == now.Date.AddDays(-1))
        {
            return $"Dün saat {local:HH:mm}";
        }

        return local.ToString("d MMM HH:mm", CultureInfo.CurrentCulture);
    }
}
