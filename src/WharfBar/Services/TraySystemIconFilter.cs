namespace WharfBar.Services;

/// <summary>
/// Decides whether a tray toolbar button is a system icon (already simulated in the status
/// strip) or WharfBar's own tray icon and should be skipped by the mirror.
/// Only a fixed, known set is filtered — unknown tooltips always pass through.
/// </summary>
internal static class TraySystemIconFilter
{
    private static readonly HashSet<string> SystemTokens = new(StringComparer.Ordinal)
    {
        // tr-TR
        "ses", "ağ", "internet", "pil", "saat", "tarih", "güvenlik",
        "gizli simgeleri göster", "görev çubuğu", "giriş dili", "bildirim", "tepsi girişi",
        // en-US
        "volume", "network", "internet", "battery", "clock", "date", "security",
        "show hidden icons", "taskbar", "input", "notification", "input indicator",
    };

    internal static string Normalize(string? tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            tooltip.Trim().ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    internal static bool IsSystemIcon(string? tooltip)
    {
        string normalized = Normalize(tooltip);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (SystemTokens.Contains(normalized))
        {
            return true;
        }

        string[] words = normalized.Split(' ');
        foreach (string token in SystemTokens)
        {
            if (token.Contains(' '))
            {
                if (normalized.Contains(token, StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (words.Contains(token, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsWharfBarIcon(string? tooltip) =>
        Normalize(tooltip).StartsWith("wharfbar", StringComparison.Ordinal);
}