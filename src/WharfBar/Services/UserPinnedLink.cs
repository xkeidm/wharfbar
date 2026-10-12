using System.IO;

namespace WharfBar.Services;

/// <summary>
/// Path rules for a shortcut written into User Pinned\TaskBar.
/// The Taskband registry blob is never part of this.
/// </summary>
internal static class UserPinnedLink
{
    internal static bool TryChoosePath(
        string directory,
        string targetPath,
        IReadOnlyDictionary<string, string> existingTargets,
        out string linkPath)
    {
        linkPath = string.Empty;
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        string target = DockItemStore.Normalize(targetPath);
        if (target.Length == 0)
        {
            return false;
        }

        string stem = Sanitize(Path.GetFileNameWithoutExtension(target));
        if (stem.Length == 0)
        {
            return false;
        }

        for (int n = 0; n < 20; n++)
        {
            string name = n == 0 ? stem + ".lnk" : $"{stem} ({n + 1}).lnk";
            string candidate = Path.Combine(directory, name);
            if (!IsInside(directory, candidate))
            {
                return false;
            }

            if (!existingTargets.TryGetValue(name, out string? occupied))
            {
                linkPath = candidate;
                return true;
            }

            if (string.Equals(DockItemStore.Normalize(occupied), target, StringComparison.OrdinalIgnoreCase))
            {
                linkPath = candidate;
                return true;
            }
        }

        return false;
    }

    internal static bool IsInside(string directory, string candidate)
    {
        try
        {
            string root = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(candidate);
            return full.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                && full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return false;
        }
    }

    internal static bool SameTarget(string pinnedTarget, string requested)
        => string.Equals(
            DockItemStore.Normalize(pinnedTarget),
            DockItemStore.Normalize(requested),
            StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem) || stem is "." or "..")
        {
            return string.Empty;
        }

        char[] chars = stem.Trim().ToCharArray();
        char[] invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        string cleaned = new string(chars).Trim().TrimEnd('.');
        if (cleaned.Length > 80)
        {
            cleaned = cleaned[..80].TrimEnd('.');
        }

        return cleaned is "." or ".." ? string.Empty : cleaned;
    }
}
