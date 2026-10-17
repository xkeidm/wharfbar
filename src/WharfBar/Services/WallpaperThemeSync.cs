using System.IO;
using WharfBar.Models;
using Wpf.Ui.Appearance;

namespace WharfBar.Services;

/// <summary>Remembers the last hub wallpaper and swaps to its light/dark partner when the resolved theme changes.</summary>
internal sealed class WallpaperThemeSync
{
    private readonly ConfigService _config;
    private readonly WallpaperService _wallpaper;

    internal WallpaperThemeSync(ConfigService config, WallpaperService wallpaper)
    {
        _config = config;
        _wallpaper = wallpaper;
    }

    internal void Remember(string path)
    {
        if (!_wallpaper.IsAllowedPath(path))
        {
            return;
        }

        string full = Path.GetFullPath(path);
        AppConfig cfg = _config.Current;
        if (string.Equals(cfg.Island.LastHubWallpaper, full, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        cfg.Island.LastHubWallpaper = full;
        _config.Save(cfg);
    }

    private readonly ReentrancyGuard _followGuard = new();

    internal void FollowResolvedTheme(bool fromExternalWindows)
        => _followGuard.TryRun(() => FollowCore(fromExternalWindows));

    private void FollowCore(bool fromExternalWindows)
    {
        AppConfig cfg = _config.Current;
        if (fromExternalWindows
            && !WallpaperThemeSyncPolicy.ShouldFollowExternalWindows(cfg.Theme))
        {
            return;
        }

        ApplicationTheme resolved = AppThemePreference.Resolve();
        if (!WallpaperThemeSyncPolicy.ShouldSyncForResolved(resolved))
        {
            return;
        }

        string last = cfg.Island.LastHubWallpaper;
        if (string.IsNullOrWhiteSpace(last) || !_wallpaper.IsAllowedPath(last))
        {
            return;
        }

        bool wantDark = resolved == ApplicationTheme.Dark;
        string? partner = WallpaperPair.TryResolvePartner(last, wantDark);
        if (partner is null)
        {
            return;
        }

        _ = _wallpaper.TryApply(partner);
    }
}
