namespace WharfBar.Models;

internal static class StatPlacement
{
    internal const string Bar = "bar";
    internal const string Tooltip = "tooltip";
    internal const string Hidden = "hidden";

    internal static string Normalize(string? value, string fallback)
    {
        if (string.Equals(value, Bar, StringComparison.OrdinalIgnoreCase))
        {
            return Bar;
        }

        if (string.Equals(value, Tooltip, StringComparison.OrdinalIgnoreCase))
        {
            return Tooltip;
        }

        if (string.Equals(value, Hidden, StringComparison.OrdinalIgnoreCase))
        {
            return Hidden;
        }

        if (string.Equals(fallback, Tooltip, StringComparison.OrdinalIgnoreCase))
        {
            return Tooltip;
        }

        if (string.Equals(fallback, Hidden, StringComparison.OrdinalIgnoreCase))
        {
            return Hidden;
        }

        return Bar;
    }

    internal static int ToComboIndex(string? value, string fallback) =>
        Normalize(value, fallback) switch
        {
            Hidden => 0,
            Tooltip => 1,
            _ => 2,
        };

    internal static string FromComboIndex(int index) => index switch
    {
        0 => Hidden,
        1 => Tooltip,
        _ => Bar,
    };

    internal static bool IsBar(string? value, string fallback) =>
        Normalize(value, fallback) == Bar;

    internal static bool IsOnTooltip(string? value, string fallback)
    {
        string p = Normalize(value, fallback);
        return p is Bar or Tooltip;
    }
}
