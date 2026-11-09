using System.Globalization;

namespace WharfBar.Services;

/// <summary>
/// Culture-independent "contains" for the launchpad search box. CurrentCulture comparisons
/// under tr-TR break ASCII I/i (a Turkish user typing "instagram" could not match
/// "Instagram"), so both sides are folded to a neutral form first; sorting still uses the
/// current culture.
/// </summary>
internal static class LaunchpadSearch
{
    internal static bool Contains(string text, string needle) =>
        needle.Length == 0 || Fold(text).Contains(Fold(needle), StringComparison.OrdinalIgnoreCase);

    internal static bool StartsWith(string text, string needle) =>
        needle.Length == 0 || Fold(text).StartsWith(Fold(needle), StringComparison.OrdinalIgnoreCase);

    internal static int IndexOf(string text, string needle) =>
        needle.Length == 0 ? 0 : Fold(text).IndexOf(Fold(needle), StringComparison.OrdinalIgnoreCase);

    internal static bool Equals(string text, string needle) =>
        string.Equals(Fold(text), Fold(needle), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps the four Turkish I code points to a single neutral <c>i</c>. The replacement is
    /// one char to one char so indexes returned by <see cref="IndexOf"/> stay valid.
    /// </summary>
    internal static string Fold(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        return value
            .Replace('\u0130', 'i')
            .Replace('\u0131', 'i')
            .Replace('I', 'i');
    }

    /// <summary>Culture-aware ordering used for app lists; keeps Turkish collation for names.</summary>
    internal static int CompareNames(string left, string right) =>
        string.Compare(left, right, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
}
