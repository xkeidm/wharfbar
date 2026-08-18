using System.Collections.Generic;
using System.Diagnostics;

namespace WharfBar.Services;

/// <summary>
/// SMTC oturumunun AUMID'inden Windows'ta görünen uygulama adını çözer:
/// exe adından çalışan süreci bulup FileDescription kullanır (örn. "chrome.exe" → "Google Chrome");
/// süreç bulunamazsa basitleştirilmiş exe adına döner. Sonuç AUMID başına önbelleklenir.
/// </summary>
internal sealed class MediaAppNameResolver
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);

    internal string Resolve(string appUserModelId)
    {
        if (!_cache.TryGetValue(appUserModelId, out string? name))
        {
            name = ResolveUncached(appUserModelId);
            _cache[appUserModelId] = name;
        }

        return name;
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

    private static string ResolveUncached(string appUserModelId)
    {
        string exeName = SimplifyExeName(appUserModelId);
        if (exeName.Length == 0)
        {
            return exeName;
        }

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

        return exeName;
    }
}