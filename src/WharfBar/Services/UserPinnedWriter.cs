using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WharfBar.Services;

/// <summary>
/// Writes and deletes <c>.lnk</c> files in User Pinned\TaskBar only.
/// Does not touch the Taskband registry value.
/// </summary>
internal static class UserPinnedWriter
{
    internal static bool TryPin(string targetPath, string? args, string? label)
        => TryPin(TaskbarPinnedAppsSource.PinnedDirectory, targetPath, args, label);

    internal static bool TryUnpin(string targetPath)
        => TryUnpin(TaskbarPinnedAppsSource.PinnedDirectory, targetPath);

    internal static bool TryPin(string directory, string targetPath, string? args, string? label)
    {
        string target = DockItemStore.Normalize(targetPath);
        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar pin folder create failed: {ex.Message}");
            return false;
        }

        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string lnk in EnumerateLinks(directory))
        {
            if (ShortcutResolver.TryResolve(lnk, out string resolved, out _, out _))
            {
                existing[Path.GetFileName(lnk)] = resolved;
            }
        }

        if (!UserPinnedLink.TryChoosePath(directory, target, existing, out string linkPath))
        {
            return false;
        }

        return TrySaveShortcut(linkPath, target, args ?? string.Empty, label ?? string.Empty);
    }

    internal static bool TryUnpin(string directory, string targetPath)
    {
        bool removed = false;
        foreach (string lnk in EnumerateLinks(directory))
        {
            if (!UserPinnedLink.IsInside(directory, lnk))
            {
                continue;
            }

            if (!ShortcutResolver.TryResolve(lnk, out string resolved, out _, out _)
                || !UserPinnedLink.SameTarget(resolved, targetPath))
            {
                continue;
            }

            try
            {
                File.Delete(lnk);
                removed = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar pin folder delete failed: {ex.Message}");
            }
        }

        return removed;
    }

    private static IEnumerable<string> EnumerateLinks(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*.lnk").ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar pin folder list failed: {ex.Message}");
            return [];
        }
    }

    private static bool TrySaveShortcut(string linkPath, string target, string args, string label)
    {
        if (!UserPinnedLink.IsInside(Path.GetDirectoryName(linkPath) ?? string.Empty, linkPath))
        {
            return false;
        }

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            object shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("WScript.Shell create failed");
            try
            {
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    [linkPath])!;
                try
                {
                    Type shortcutType = shortcut.GetType();
                    shortcutType.InvokeMember(
                        "TargetPath",
                        System.Reflection.BindingFlags.SetProperty,
                        null,
                        shortcut,
                        [target]);
                    shortcutType.InvokeMember(
                        "Arguments",
                        System.Reflection.BindingFlags.SetProperty,
                        null,
                        shortcut,
                        [args]);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        shortcutType.InvokeMember(
                            "Description",
                            System.Reflection.BindingFlags.SetProperty,
                            null,
                            shortcut,
                            [label]);
                    }

                    shortcutType.InvokeMember(
                        "Save",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        shortcut,
                        null);
                    return File.Exists(linkPath);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar pin shortcut save failed: {ex.Message}");
            return false;
        }
    }
}
