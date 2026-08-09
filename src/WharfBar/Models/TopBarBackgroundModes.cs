namespace WharfBar.Models;

internal static class TopBarBackgroundModes
{
    internal const string Mica = "mica";
    internal const string Transparent = "transparent";
    internal const string Tint = "tint";
    internal const int DefaultOpacity = 40;
    internal const int MinOpacity = 0;
    internal const int MaxOpacity = 100;

    internal static string NormalizeMode(string? mode)
    {
        if (string.Equals(mode, Transparent, StringComparison.OrdinalIgnoreCase))
        {
            return Transparent;
        }

        if (string.Equals(mode, Tint, StringComparison.OrdinalIgnoreCase))
        {
            return Tint;
        }

        return Mica;
    }

    internal static int ClampOpacity(int value) =>
        Math.Clamp(value, MinOpacity, MaxOpacity);

    internal static bool NeedsTransparency(string mode)
    {
        mode = NormalizeMode(mode);
        return mode is Transparent or Tint;
    }
}
