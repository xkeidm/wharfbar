using WharfBar.Models;
using Wpf.Ui.Appearance;

namespace WharfBar.Services;

internal static class WallpaperThemeSyncPolicy
{
    internal static bool ShouldFollowExternalWindows(string? themeMode)
        => string.Equals(themeMode, AppConfig.ThemeSystem, StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldSyncForResolved(ApplicationTheme resolved)
        => resolved is ApplicationTheme.Light or ApplicationTheme.Dark;
}
