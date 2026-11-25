using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// The name the shell shows for a file. A shortcut named <c>File Explorer.lnk</c>
/// still comes back as the localized label (for example <c>Dosya Gezgini</c>).
/// </summary>
/// <remarks>
/// The cache is queried from the UI and filled from a background census, so it is concurrent. It is
/// bounded: when it is full the map is cleared instead of growing with the whole file system.
/// </remarks>
internal static class ShellDisplayName
{
    /// <summary>Upper bound for the cached display names; a full map is cleared on the next miss.</summary>
    internal const int MaxEntries = 2048;

    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static string? TryGet(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out string? cached))
        {
            return cached;
        }

        string? name = Query(path);
        if (Cache.Count >= MaxEntries)
        {
            // Cheap bounded reset: the census may write from another thread, so "clear when full"
            // keeps the map from growing without an insertion-order bookkeeping pass.
            Cache.Clear();
        }

        Cache[path] = name;
        return name;
    }

    private static string? Query(string path)
    {
        try
        {
            var info = new SHFILEINFO();
            IntPtr result = NativeMethods.SHGetFileInfo(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                NativeMethods.SHGFI_DISPLAYNAME);
            if (result == IntPtr.Zero || string.IsNullOrWhiteSpace(info.szDisplayName))
            {
                return null;
            }

            return info.szDisplayName.Trim();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar display name failed: {path} — {ex.Message}");
            return null;
        }
    }
}
