using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// SMTC oturumunun AUMID'inden Windows'ta görünen uygulama adını çözer (katmanlı):
/// 1) Start Menü kısayolu AUMID eşleşmesi — Windows'un gösterdiği ad (ör. Zen, YouTube Music);
/// 2) çalışan sürecin <c>FileDescription</c>'ı (ör. "chrome.exe" → "Google Chrome");
/// 3) basitleştirilmiş exe adı. Sonuç AUMID başına önbelleklenir.
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
    /// Katmanlı çözüm — ortam bağımsız çekirdek; testler süreç çözücüsünü taklit eder.
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

    /// <summary>AUMID'den exe adını çıkarır: "!" sonrası segment, ".exe" uzantısı düşülür.</summary>
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
                    Debug.WriteLine($"[media] kısayol AUMID okunamadı ({lnk}): {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // ";PrivateBrowsingAUMID" gibi türevleri atla — tam AUMID isteriz.
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
                        // Yükseltilmiş/erişilemeyen süreç — sonrakine geç.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] uygulama adı çözülemedi: {ex.Message}");
        }

        return null;
    }
}