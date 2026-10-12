namespace WharfBar.Services;

/// <summary>Persisted launchpad face. Unknown text loads as Windows 11.</summary>
internal static class LaunchpadStyles
{
    internal const string Win11 = "win11";

    internal const string Mac = "mac";

    internal const string Win8 = "win8";

    internal static string Normalize(string? style)
    {
        if (string.Equals(style, Mac, StringComparison.OrdinalIgnoreCase)
            || string.Equals(style, "macos", StringComparison.OrdinalIgnoreCase))
        {
            return Mac;
        }

        if (string.Equals(style, Win8, StringComparison.OrdinalIgnoreCase)
            || string.Equals(style, "windows8", StringComparison.OrdinalIgnoreCase))
        {
            return Win8;
        }

        return Win11;
    }
}
