using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// The name the shell shows for a file. A shortcut named <c>File Explorer.lnk</c>
/// still comes back as the localized label (for example <c>Dosya Gezgini</c>).
/// </summary>
internal static class ShellDisplayName
{
    private static readonly Dictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

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
