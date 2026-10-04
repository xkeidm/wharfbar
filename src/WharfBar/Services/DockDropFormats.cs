using System;
using System.IO;

namespace WharfBar.Services;

/// <summary>Which dropped files the dock accepts, and how to turn one into a list entry.</summary>
internal static class DockDropFormats
{
    internal static readonly string[] Extensions = [".lnk", ".exe", ".bat", ".cmd"];

    internal static bool IsSupported(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        foreach (string candidate in Extensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a dropped file into a dock list entry. Labels are left empty on purpose so
    /// <see cref="DockItemStore.TryAddCore"/> derives them from the file name in one place.
    /// </summary>
    internal static (string Path, string Args) Resolve(string path)
    {
        if (!IsSupported(path))
        {
            return (string.Empty, string.Empty);
        }

        // An unreadable shortcut falls through to the .lnk path itself rather than being
        // dropped: the list entry still carries a launchable-looking path and a label.
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            && ShortcutResolver.TryResolve(path, out string target, out string args, out _))
        {
            return (target, args);
        }

        return (path, string.Empty);
    }
}