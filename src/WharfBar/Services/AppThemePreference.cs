using WharfBar.Models;
using Wpf.Ui.Appearance;

namespace WharfBar.Services;

/// <summary>
/// Process-wide theme preference from <see cref="AppConfig.Theme"/>.
/// <see cref="WindowAppearance"/> reads this instead of always following the system.
/// </summary>
internal static class AppThemePreference
{
    private static string _mode = AppConfig.ThemeSystem;

    internal static string Mode
    {
        get => _mode;
        set => _mode = Normalize(value);
    }

    internal static ApplicationTheme Resolve()
    {
        return Mode switch
        {
            AppConfig.ThemeDark => ApplicationTheme.Dark,
            AppConfig.ThemeLight => ApplicationTheme.Light,
            _ => ResolveSystemTheme(),
        };
    }

    private static string Normalize(string? value)
    {
        if (string.Equals(value, AppConfig.ThemeLight, StringComparison.OrdinalIgnoreCase))
        {
            return AppConfig.ThemeLight;
        }

        if (string.Equals(value, AppConfig.ThemeDark, StringComparison.OrdinalIgnoreCase))
        {
            return AppConfig.ThemeDark;
        }

        return AppConfig.ThemeSystem;
    }

    private static ApplicationTheme ResolveSystemTheme() => ApplicationThemeManager.GetSystemTheme() switch
    {
        SystemTheme.Dark or SystemTheme.CapturedMotion or SystemTheme.Glow => ApplicationTheme.Dark,
        SystemTheme.HC1 or SystemTheme.HC2 or SystemTheme.HCBlack or SystemTheme.HCWhite =>
            ApplicationTheme.HighContrast,
        _ => ApplicationTheme.Light,
    };
}
