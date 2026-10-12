using System.IO;

namespace WharfBar.Services;

/// <summary>
/// Picks a package logo file or an indirect resource string from fixture-shaped inputs.
/// Missing or malformed data yields no path, so the caller keeps the letter glyph instead
/// of a broken image.
/// </summary>
internal static class PackageLogoSelection
{
    private static readonly string[] ScaleSuffixes =
    [
        ".scale-200",
        ".scale-150",
        ".scale-100",
        ".targetsize-256",
        ".targetsize-48",
        ".targetsize-32",
    ];

    internal static bool TrySplitAumid(string? id, out string familyName, out string appId)
    {
        familyName = string.Empty;
        appId = string.Empty;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        string trimmed = id.Trim();
        if (trimmed.Contains('\\') || trimmed.Contains('/') || Path.IsPathRooted(trimmed))
        {
            return false;
        }

        int bang = trimmed.IndexOf('!');
        if (bang <= 0 || bang != trimmed.LastIndexOf('!') || bang >= trimmed.Length - 1)
        {
            return false;
        }

        familyName = trimmed[..bang];
        appId = trimmed[(bang + 1)..];
        return familyName.Length > 0 && appId.Length > 0;
    }

    /// <summary>
    /// Wraps an <c>ms-resource</c> logo for <c>SHLoadIndirectString</c>. File and
    /// <c>ms-appx</c> logos are not indirect strings.
    /// </summary>
    internal static string? IndirectSource(string? packageFullName, string? logo)
    {
        if (string.IsNullOrWhiteSpace(logo))
        {
            return null;
        }

        string value = logo.Trim();
        if (value.StartsWith('@'))
        {
            return value;
        }

        if (value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(packageFullName))
        {
            return "@{" + packageFullName.Trim() + "?" + value + "}";
        }

        return null;
    }

    /// <summary>
    /// Ordered filesystem candidates for a logo. The unscaled file is last. An install
    /// location is required for <c>ms-appx</c> relatives; without it the list is empty.
    /// </summary>
    internal static IReadOnlyList<string> CandidateFiles(string? installLocation, string? logo)
    {
        if (string.IsNullOrWhiteSpace(logo) || logo.TrimStart().StartsWith('@'))
        {
            return [];
        }

        string value = logo.Trim();
        if (value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (!TryAbsoluteOrRelative(installLocation, value, out string? absolute))
        {
            return [];
        }

        string? directory = Path.GetDirectoryName(absolute);
        string extension = Path.GetExtension(absolute);
        string fileName = Path.GetFileNameWithoutExtension(absolute);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
        {
            return [];
        }

        var list = new List<string>(ScaleSuffixes.Length + 1);
        foreach (string suffix in ScaleSuffixes)
        {
            list.Add(Path.Combine(directory, fileName + suffix + extension));
        }

        list.Add(absolute);
        return list;
    }

    internal static string? FirstExisting(IReadOnlyList<string> candidates, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(exists);

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            bool found;
            try
            {
                found = exists(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                found = false;
            }

            if (found)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool TryAbsoluteOrRelative(string? installLocation, string logo, out string? absolute)
    {
        absolute = null;
        if (logo.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(installLocation))
            {
                return false;
            }

            string relative = MsAppxRelative(logo);
            if (relative.Length == 0)
            {
                return false;
            }

            absolute = Path.Combine(installLocation.Trim(), relative);
            return true;
        }

        if (logo.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(logo, UriKind.Absolute, out Uri? uri) || !uri.IsFile)
            {
                return false;
            }

            absolute = uri.LocalPath;
            return absolute.Length > 0;
        }

        if (Path.IsPathRooted(logo))
        {
            absolute = logo;
            return true;
        }

        if (string.IsNullOrWhiteSpace(installLocation))
        {
            return false;
        }

        absolute = Path.Combine(installLocation.Trim(), logo.Replace('/', Path.DirectorySeparatorChar));
        return true;
    }

    private static string MsAppxRelative(string logo)
    {
        int scheme = logo.IndexOf(':');
        string rest = scheme >= 0 ? logo[(scheme + 1)..] : logo;
        rest = rest.TrimStart('/');
        int slash = rest.IndexOf('/');
        if (slash >= 0 && rest.IndexOf('.') < 0)
        {
            // ms-appx://PackageName/Assets/Logo.png — drop the authority.
            rest = rest[(slash + 1)..];
        }
        else if (slash > 0 && rest[..slash].Contains('.', StringComparison.Ordinal))
        {
            rest = rest[(slash + 1)..];
        }

        return rest.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
    }
}
