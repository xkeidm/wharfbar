using System.IO;

namespace WharfBar.Services;

/// <summary>One child of a folder stack, already classified. Reparse points never appear here.</summary>
internal readonly record struct DockFolderEntry(string Name, string FullPath, bool IsDirectory);

/// <summary>A directory child before the fail-closed filter.</summary>
internal readonly record struct DockFolderCandidate(
    string Name,
    string FullPath,
    bool IsDirectory,
    bool AttributesReadable,
    bool IsReparse);

/// <summary>The visible page of a folder stack.</summary>
internal readonly record struct DockFolderPage(IReadOnlyList<DockFolderEntry> Entries, bool Truncated);

/// <summary>
/// Lists a pinned folder or the Downloads tile. Reparse points and unreadable attributes
/// are skipped (fail closed). The visible row count is capped.
/// </summary>
internal static class DockFolderListing
{
    internal const int MaxVisibleRows = 12;

    internal static DockFolderPage Select(IReadOnlyList<DockFolderCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var kept = new List<DockFolderEntry>(Math.Min(candidates.Count, MaxVisibleRows));
        foreach (DockFolderCandidate candidate in candidates
            .Where(static c => c.AttributesReadable && !c.IsReparse && !string.IsNullOrWhiteSpace(c.FullPath))
            .OrderByDescending(static c => c.IsDirectory)
            .ThenBy(static c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            kept.Add(new DockFolderEntry(candidate.Name, candidate.FullPath, candidate.IsDirectory));
        }

        bool truncated = kept.Count > MaxVisibleRows;
        if (truncated)
        {
            kept.RemoveRange(MaxVisibleRows, kept.Count - MaxVisibleRows);
        }

        return new DockFolderPage(kept, truncated);
    }

    /// <summary>
    /// Reads one directory. A reparse-point directory, or a path whose attributes cannot be
    /// read, yields a rejected page so the caller does not follow the link.
    /// </summary>
    internal static bool TryRead(string? directory, out DockFolderPage page)
    {
        page = new DockFolderPage([], false);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(directory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!TryDescribe(full, out DockFolderCandidate self)
            || !self.AttributesReadable
            || self.IsReparse
            || !self.IsDirectory)
        {
            return false;
        }

        List<DockFolderCandidate> children;
        try
        {
            children = [];
            foreach (string child in Directory.EnumerateFileSystemEntries(full))
            {
                if (!TryDescribe(child, out DockFolderCandidate described))
                {
                    continue;
                }

                children.Add(described);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        page = Select(children);
        return true;
    }

    internal static bool TryDescribe(string path, out DockFolderCandidate candidate)
    {
        candidate = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(path);
            bool reparse = (attributes & FileAttributes.ReparsePoint) != 0;
            bool directory = (attributes & FileAttributes.Directory) != 0;
            candidate = new DockFolderCandidate(
                Path.GetFileName(path),
                path,
                directory,
                AttributesReadable: true,
                IsReparse: reparse);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            candidate = new DockFolderCandidate(
                Path.GetFileName(path),
                path,
                IsDirectory: false,
                AttributesReadable: false,
                IsReparse: false);
            return true;
        }
    }
}
