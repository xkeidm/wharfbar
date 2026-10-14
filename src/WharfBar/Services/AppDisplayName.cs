using System.IO;

namespace WharfBar.Services;

/// <summary>
/// Dock label: the name Windows shows, not a raw window title or an English shortcut file name.
/// </summary>
internal static class AppDisplayName
{
    /// <summary>
    /// Prefers a shell display name that is not just the file name. Otherwise uses the
    /// application segment of a window title (<c>wharfbar - Dosya Gezgini</c> → <c>Dosya Gezgini</c>).
    /// </summary>
    internal static string Choose(string? shellName, string? windowTitle, string? path)
    {
        if (IsUsableShellName(shellName, path))
        {
            return shellName!.Trim();
        }

        string segment = ApplicationSegment(windowTitle);
        if (segment.Length > 0)
        {
            return segment;
        }

        return Stem(path);
    }

    /// <summary>
    /// Text after the last <c> - </c> or <c> — </c>. A title with no separator is returned whole.
    /// </summary>
    internal static string ApplicationSegment(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        string trimmed = title.Trim();
        int hyphen = trimmed.LastIndexOf(" - ", StringComparison.Ordinal);
        int em = trimmed.LastIndexOf(" — ", StringComparison.Ordinal);
        int cut = Math.Max(hyphen, em);
        if (cut < 0)
        {
            return trimmed;
        }

        string tail = trimmed[(cut + 3)..].Trim();
        return tail.Length > 0 ? tail : trimmed;
    }

    /// <summary>
    /// A shell name that only repeats the file name is not a localized display name.
    /// </summary>
    internal static bool IsUsableShellName(string? shellName, string? path)
    {
        if (string.IsNullOrWhiteSpace(shellName))
        {
            return false;
        }

        string name = shellName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string stem = Stem(path);
        if (stem.Length > 0 && name.Equals(stem, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string file = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path.Trim());
        return file.Length == 0 || !name.Equals(file, StringComparison.OrdinalIgnoreCase);
    }

    private static string Stem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFileNameWithoutExtension(path.Trim()) ?? string.Empty;
    }
}
