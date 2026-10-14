using System.Reflection;

namespace WharfBar.Services;

/// <summary>
/// Assembly version for About UI: InformationalVersion without git metadata.
/// </summary>
internal static class AppVersion
{
    private static readonly Lazy<(string Version, bool IsPrerelease)> _info = new(Read);

    /// <summary>Display version (e.g. <c>1.0.0-beta</c>), with optional <c>+gitsha</c> stripped.</summary>
    internal static string Current => _info.Value.Version;

    /// <summary>True when the SemVer string has a prerelease segment (e.g. <c>-beta</c>).</summary>
    internal static bool IsPrerelease => _info.Value.IsPrerelease;

    private static (string Version, bool IsPrerelease) Read()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip optional "+gitsha" / build metadata.
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            string cleaned = plus >= 0 ? informational[..plus] : informational;
            bool prerelease = cleaned.Contains('-', StringComparison.Ordinal);
            return (cleaned, prerelease);
        }

        Version? v = assembly.GetName().Version;
        string fallback = v is null ? "—" : $"{v.Major}.{v.Minor}.{v.Build}";
        return (fallback, IsPrerelease: false);
    }
}
