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

/// <summary>Runs an action once at a time; nested calls (e.g. from ImmersiveColorSet WndProc) no-op.</summary>
internal sealed class ReentrancyGuard
{
    private bool _active;

    /// <summary>Returns false (without running) if already inside <paramref name="action"/>.</summary>
    internal bool TryRun(Action action)
    {
        if (_active)
        {
            return false;
        }

        _active = true;
        try
        {
            action();
            return true;
        }
        finally
        {
            _active = false;
        }
    }
}
