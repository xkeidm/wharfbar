using WharfBar.Models;

namespace WharfBar.Services;

internal enum IslandHubTab
{
    Clipboard,
    Wallpaper,
    Music,
}

internal static class IslandHub
{
    internal static IslandHubTab Normalize(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            IslandConfig.HubTabWallpaper => IslandHubTab.Wallpaper,
            IslandConfig.HubTabMusic => IslandHubTab.Music,
            _ => IslandHubTab.Clipboard,
        };

    internal static string ToConfig(IslandHubTab tab) => tab switch
    {
        IslandHubTab.Wallpaper => IslandConfig.HubTabWallpaper,
        IslandHubTab.Music => IslandConfig.HubTabMusic,
        _ => IslandConfig.HubTabClipboard,
    };

    internal static IslandHubTab Resolve(string? saved, bool musicAvailable)
    {
        IslandHubTab tab = Normalize(saved);
        return tab == IslandHubTab.Music && !musicAvailable ? IslandHubTab.Clipboard : tab;
    }
}
