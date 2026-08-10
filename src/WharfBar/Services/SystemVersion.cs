using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>
/// Provides OS version detection and OS-specific font/icon definitions.
/// Windows 11 (Build >= 22000): Uses Segoe UI Variable for text and Segoe Fluent Icons for icons.
/// Windows 10 (Build < 22000): Uses Segoe UI for text and Segoe MDL2 Assets for icons.
/// </summary>
internal static class SystemVersion
{
    private static readonly Lazy<bool> _isWindows11 = new(() =>
    {
        return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000;
    });

    /// <summary>
    /// Gets a value indicating whether the current operating system is Windows 11 or higher.
    /// </summary>
    public static bool IsWindows11 => _isWindows11.Value;

    /// <summary>
    /// Gets the primary UI text font family for the operating system.
    /// Windows 11: Segoe UI Variable, Segoe UI
    /// Windows 10: Segoe UI
    /// </summary>
    public static FontFamily TextFontFamily { get; } = new(
        IsWindows11 ? "Segoe UI Variable, Segoe UI" : "Segoe UI"
    );

    /// <summary>
    /// Gets the system icon font family for the operating system.
    /// Windows 11: Segoe Fluent Icons, Segoe MDL2 Assets
    /// Windows 10: Segoe MDL2 Assets
    /// </summary>
    public static FontFamily IconFontFamily { get; } = new(
        IsWindows11 ? "Segoe Fluent Icons, Segoe MDL2 Assets" : "Segoe MDL2 Assets"
    );
}
