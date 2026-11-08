using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Resolves the app name shown in Windows from the SMTC session's AUMID (layered):
/// 1) Start Menu shortcut AUMID match — the name Windows displays (e.g. Zen, YouTube Music);
/// 2) the running process's <c>FileDescription</c> (e.g. "chrome.exe" → "Google Chrome");
/// 3) the simplified exe name. The result is cached per AUMID.
/// </summary>
internal sealed class MediaAppNameResolver
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private Dictionary<string, string>? _shortcutNames;

    internal string Resolve(string appUserModelId)
    {
        if (!_cache.TryGetValue(appUserModelId, out string? name))
        {
            name = ResolveCore(appUserModelId, BuildShortcutIndex(), TryGetFileDescription);
            _cache[appUserModelId] = name;
        }

        return name;
    }

    /// <summary>
    /// Layered resolution — environment-independent core; tests fake the process resolver.
    /// </summary>
    internal static string ResolveCore(
        string appUserModelId,
        IReadOnlyDictionary<string, string> shortcutNames,
        Func<string, string?> processResolver)
    {
        string exeName = SimplifyExeName(appUserModelId);
        if (exeName.Length == 0)
        {
            return exeName;
        }

        if (shortcutNames.TryGetValue(appUserModelId, out string? shortcutName))
        {
            return shortcutName;
        }

        string? description = processResolver(exeName);
        return string.IsNullOrEmpty(description) ? exeName : description;
    }

    /// <summary>Extracts the exe name from the AUMID: the segment after "!", with the ".exe" extension dropped.</summary>
    internal static string SimplifyExeName(string appUserModelId)
    {
        string name = appUserModelId;
        int bang = name.LastIndexOf('!');
        if (bang >= 0 && bang < name.Length - 1)
        {
            name = name[(bang + 1)..];
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    private Dictionary<string, string> BuildShortcutIndex()
    {
        if (_shortcutNames is not null)
        {
            return _shortcutNames;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };

        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                string? aumid;
                try
                {
                    bool ok = AppShortcut.TryGetAppUserModelId(lnk, out aumid);
                    if (!ok || string.IsNullOrEmpty(aumid))
                    {
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[media] could not read shortcut AUMID ({lnk}): {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // Skip derivatives like ";PrivateBrowsingAUMID" — we want the exact AUMID.
                if (aumid.Contains(';'))
                {
                    continue;
                }

                map.TryAdd(aumid, Path.GetFileNameWithoutExtension(lnk));
            }
        }

        _shortcutNames = map;
        return map;
    }

    private static string? TryGetFileDescription(string exeName)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName(exeName))
            {
                using (process)
                {
                    try
                    {
                        string? description = process.MainModule?.FileVersionInfo.FileDescription;
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            return description;
                        }
                    }
                    catch
                    {
                        // Elevated/inaccessible process — move on to the next one.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] could not resolve app name: {ex.Message}");
        }

        return null;
    }
}