using System.IO;

namespace WharfBar.Services;

internal enum WallpaperSource { Bundled, User }

internal readonly record struct WallpaperItem(string Path, WallpaperSource Source, string DisplayName);

internal static class WallpaperCatalog
{
    private static readonly HashSet<string> Ext = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp",
    };

    internal static bool IsImageExtension(string path)
        => Ext.Contains(Path.GetExtension(path));

    internal static IReadOnlyList<WallpaperItem> List(string bundledRoot, string userRoot)
    {
        var items = new List<WallpaperItem>();
        Add(items, bundledRoot, WallpaperSource.Bundled);
        Add(items, userRoot, WallpaperSource.User);
        return items;
    }

    private static void Add(List<WallpaperItem> items, string root, WallpaperSource source)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(root)
                     .Where(IsImageExtension)
                     .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(new WallpaperItem(Path.GetFullPath(file), source, Path.GetFileName(file)));
        }
    }
}
