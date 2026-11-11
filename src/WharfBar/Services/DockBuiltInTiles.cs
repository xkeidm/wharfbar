using System.Diagnostics;
using System.IO;
using WharfBar.Models;
using WharfBar.Resources;

namespace WharfBar.Services;

/// <summary>
/// Launchpad, Task View, Downloads, and Trash are dock tiles, not <c>dock.items</c> and not the
/// User Pinned folder. A pinned directory is marked so a click opens a stack instead of a second
/// Explorer via the app launcher. The pin path itself is left alone so unpin still finds the
/// store key.
/// </summary>
internal static class DockBuiltInTiles
{
    internal const string DownloadsId = "builtin:downloads";

    internal const string TrashId = "builtin:trash";

    internal const string LaunchpadId = "builtin:launchpad";

    internal const string TaskViewId = "builtin:taskview";

    internal static IReadOnlyList<DockAppItem> MarkFolders(
        IReadOnlyList<DockAppItem> items,
        Func<string, bool> isDirectory)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(isDirectory);

        var next = new List<DockAppItem>(items.Count);
        foreach (DockAppItem item in items)
        {
            if (item.Kind != DockTileKind.App || string.IsNullOrWhiteSpace(item.Path))
            {
                next.Add(item);
                continue;
            }

            bool directory;
            try
            {
                directory = isDirectory(item.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                directory = false;
            }

            next.Add(directory ? item with { Kind = DockTileKind.Folder } : item);
        }

        return next;
    }

    /// <summary>
    /// Launchpad is the brand tile at the far left and Task View sits right after it. Downloads
    /// sits after the pinned group. Trash is the last tile, after unpinned running apps.
    /// </summary>
    internal static IReadOnlyList<DockAppItem> Append(IReadOnlyList<DockAppItem> items, string? downloadsPath)
    {
        ArgumentNullException.ThrowIfNull(items);

        var next = new List<DockAppItem>(items.Count + 4);
        next.Add(new DockAppItem(
            LaunchpadId,
            string.Empty,
            string.Empty,
            Strings.Dock_Launchpad,
            IsPinned: false,
            IsRunning: false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty,
            Kind: DockTileKind.Launchpad));
        next.Add(new DockAppItem(
            TaskViewId,
            string.Empty,
            string.Empty,
            Strings.Dock_TaskView,
            IsPinned: false,
            IsRunning: false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty,
            Kind: DockTileKind.TaskView));
        next.AddRange(items);

        int insertAt = 0;
        for (int i = 0; i < next.Count; i++)
        {
            if (next[i].Section != DockSection.Pinned)
            {
                break;
            }

            insertAt = i + 1;
        }

        next.Insert(insertAt, new DockAppItem(
            DownloadsId,
            downloadsPath ?? string.Empty,
            string.Empty,
            Strings.Dock_Downloads,
            IsPinned: false,
            IsRunning: false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty,
            Kind: DockTileKind.Downloads));

        next.Add(new DockAppItem(
            TrashId,
            "shell:RecycleBinFolder",
            string.Empty,
            Strings.Dock_RecycleBin,
            IsPinned: false,
            IsRunning: false,
            IntPtr.Zero,
            DockSection.Running,
            string.Empty,
            Kind: DockTileKind.Trash));

        return next;
    }

    internal static bool IsDirectoryPin(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (Directory.Exists(path))
            {
                return true;
            }

            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                return false;
            }

            if (!ShortcutResolver.TryResolve(path, out string target, out _, out _))
            {
                return false;
            }

            return Directory.Exists(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Debug.WriteLine($"WharfBar dock folder pin check failed: {ex.Message}");
            return false;
        }
    }

    internal static string? ResolveDirectory(string path)
    {
        if (!IsDirectoryPin(path))
        {
            return null;
        }

        if (Directory.Exists(path))
        {
            return path;
        }

        if (ShortcutResolver.TryResolve(path, out string target, out _, out _) && Directory.Exists(target))
        {
            return target;
        }

        return null;
    }
}
