using System.Globalization;
using System.IO;

namespace WharfBar.Services;

internal readonly record struct LaunchpadApp(string Name, string ShortcutPath);

internal readonly record struct LaunchpadLetterGroup(string Letter, IReadOnlyList<LaunchpadApp> Apps);

/// <summary>Start Menu shortcuts, filtered by a search box. Nothing is launched here.</summary>
internal static class LaunchpadCatalog
{
    internal static IReadOnlyList<LaunchpadApp> List() =>
        Scan(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));

    internal static IReadOnlyList<LaunchpadApp> Scan(params string?[] roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        var apps = new List<LaunchpadApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? root in roots)
        {
            if (!string.IsNullOrWhiteSpace(root))
            {
                AddFolder(apps, seen, root);
            }
        }

        apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return apps;
    }

    internal static IReadOnlyList<LaunchpadApp> Filter(IReadOnlyList<LaunchpadApp> apps, string? query)
    {
        ArgumentNullException.ThrowIfNull(apps);
        if (string.IsNullOrWhiteSpace(query))
        {
            return apps;
        }

        string needle = query.Trim();
        var next = new List<LaunchpadApp>();
        foreach (LaunchpadApp app in apps)
        {
            if (LaunchpadSearch.Contains(app.Name, needle))
            {
                next.Add(app);
            }
        }

        return Rank(next, needle);
    }

    /// <summary>
    /// Sorts search hits so the obvious target is first: an exact name, then a name that
    /// starts with the query, then a word inside the name, then any substring. Ties keep
    /// the current-culture alphabetical order.
    /// </summary>
    internal static IReadOnlyList<LaunchpadApp> Rank(IEnumerable<LaunchpadApp> apps, string needle)
    {
        ArgumentNullException.ThrowIfNull(apps);
        var ranked = new List<LaunchpadApp>(apps);
        ranked.Sort((a, b) =>
        {
            int rank = MatchRank(a.Name, needle).CompareTo(MatchRank(b.Name, needle));
            return rank != 0
                ? rank
                : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
        return ranked;
    }

    /// <summary>0 exact, 1 prefix, 2 word start, 3 substring; <see cref="int.MaxValue"/> when absent.</summary>
    internal static int MatchRank(string name, string needle)
    {
        string trimmed = name.Trim();
        if (LaunchpadSearch.Equals(trimmed, needle))
        {
            return 0;
        }

        if (LaunchpadSearch.StartsWith(trimmed, needle))
        {
            return 1;
        }

        int at = LaunchpadSearch.IndexOf(trimmed, needle);
        if (at < 0)
        {
            return int.MaxValue;
        }

        return at == 0 || !char.IsLetterOrDigit(trimmed[at - 1]) ? 2 : 3;
    }

    /// <summary>
    /// Groups an already filtered list under its first letter. Names that do not start
    /// with a letter share <c>#</c>. Order follows the current culture.
    /// </summary>
    internal static IReadOnlyList<LaunchpadLetterGroup> GroupByLetter(IReadOnlyList<LaunchpadApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);
        var ordered = new List<LaunchpadApp>(apps);
        ordered.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

        var groups = new List<LaunchpadLetterGroup>();
        string? letter = null;
        List<LaunchpadApp>? bucket = null;
        foreach (LaunchpadApp app in ordered)
        {
            string next = LetterOf(app.Name);
            if (!string.Equals(letter, next, StringComparison.CurrentCulture))
            {
                if (bucket is not null && letter is not null)
                {
                    groups.Add(new LaunchpadLetterGroup(letter, bucket));
                }

                letter = next;
                bucket = [];
            }

            bucket!.Add(app);
        }

        if (bucket is not null && letter is not null)
        {
            groups.Add(new LaunchpadLetterGroup(letter, bucket));
        }

        return groups;
    }

    internal static string LetterOf(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || !char.IsLetter(trimmed[0]))
        {
            return "#";
        }

        return char.ToUpper(trimmed[0], CultureInfo.CurrentCulture).ToString();
    }

    private static void AddFolder(List<LaunchpadApp> apps, HashSet<string> seen, string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        // One directory at a time. SearchOption.AllDirectories throws on MoveNext when it
        // reaches a child it cannot open, and that throw sits outside the call that created
        // the enumerator. On a Turkish Windows install that child is the hidden Programlar
        // junction beside Programs; the exception is unhandled on the dock click thread.
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in ListOrEmpty(current, directories: false))
            {
                if (IsStartupFolder(file))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(file);
                if (name.Length == 0 || !seen.Add(name))
                {
                    continue;
                }

                apps.Add(new LaunchpadApp(name, file));
            }

            foreach (string child in ListOrEmpty(current, directories: true))
            {
                if (SkipDirectory(child))
                {
                    continue;
                }

                pending.Push(child);
            }
        }
    }

    private static bool SkipDirectory(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    /// Startup-folder shortcuts are autostart entries, not apps: WharfBar's own autostart
    /// shortcut was listed in the pad. Start Menu programmers do not show them under All apps.
    /// </summary>
    private static bool IsStartupFolder(string file) =>
        file.Contains(@"\Startup\", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ListOrEmpty(string directory, bool directories)
    {
        try
        {
            if (directories)
            {
                return Directory.GetDirectories(directory);
            }

            // .url shortcuts (Steam / web-installed apps) sit beside .lnk entries in the
            // Start Menu and were invisible to the launchpad before.
            return
            [
                .. Directory.GetFiles(directory, "*.lnk"),
                .. Directory.GetFiles(directory, "*.url"),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
