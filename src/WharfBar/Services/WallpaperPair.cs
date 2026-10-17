using System.IO;

namespace WharfBar.Services;

/// <summary>
/// Resolves <c>name-light.ext</c> ↔ <c>name-dark.ext</c> siblings in the same folder.
/// </summary>
internal static class WallpaperPair
{
    private const string LightSuffix = "-light";
    private const string DarkSuffix = "-dark";

    internal static bool IsLightVariant(string path) => Variant(path) == ThemeVariant.Light;

    internal static bool IsDarkVariant(string path) => Variant(path) == ThemeVariant.Dark;

    /// <summary>
    /// Partner full path when <paramref name="path"/> is the opposite variant and the sibling file exists.
    /// Null when unpaired, already the desired variant, or sibling missing.
    /// </summary>
    internal static string? TryResolvePartner(string path, bool wantDark)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        ThemeVariant variant = Variant(full);
        if (variant == ThemeVariant.None)
        {
            return null;
        }

        bool isDark = variant == ThemeVariant.Dark;
        if (isDark == wantDark)
        {
            return null; // already correct
        }

        string? partner = BuildPartnerPath(full, wantDark);
        if (partner is null || !File.Exists(partner))
        {
            return null;
        }

        return partner;
    }

    private static string? BuildPartnerPath(string full, bool wantDark)
    {
        string? dir = Path.GetDirectoryName(full);
        string name = Path.GetFileNameWithoutExtension(full);
        string ext = Path.GetExtension(full);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        string stem;
        if (name.EndsWith(LightSuffix, StringComparison.OrdinalIgnoreCase))
        {
            stem = name[..^LightSuffix.Length];
        }
        else if (name.EndsWith(DarkSuffix, StringComparison.OrdinalIgnoreCase))
        {
            stem = name[..^DarkSuffix.Length];
        }
        else
        {
            return null;
        }

        string suffix = wantDark ? DarkSuffix : LightSuffix;
        return Path.Combine(dir, stem + suffix + ext);
    }

    private static ThemeVariant Variant(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(DarkSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeVariant.Dark;
        }

        if (name.EndsWith(LightSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeVariant.Light;
        }

        return ThemeVariant.None;
    }

    private enum ThemeVariant { None, Light, Dark }
}
