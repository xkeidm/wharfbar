using WharfBar.Models;

namespace WharfBar.Services;

internal enum IslandHubTab
{
    Clipboard,
    Wallpaper,
    Music,
    Shelf,
    Prompter,
    ColorPicker,
}

internal static class IslandHub
{
    internal static IslandHubTab Normalize(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            IslandConfig.HubTabWallpaper => IslandHubTab.Wallpaper,
            IslandConfig.HubTabMusic => IslandHubTab.Music,
            IslandConfig.HubTabShelf => IslandHubTab.Shelf,
            IslandConfig.HubTabPrompter => IslandHubTab.Prompter,
            IslandConfig.HubTabColorPicker => IslandHubTab.ColorPicker,
            _ => IslandHubTab.Clipboard,
        };

    internal static string ToConfig(IslandHubTab tab) => tab switch
    {
        IslandHubTab.Wallpaper => IslandConfig.HubTabWallpaper,
        IslandHubTab.Music => IslandConfig.HubTabMusic,
        IslandHubTab.Shelf => IslandConfig.HubTabShelf,
        IslandHubTab.Prompter => IslandConfig.HubTabPrompter,
        IslandHubTab.ColorPicker => IslandConfig.HubTabColorPicker,
        _ => IslandConfig.HubTabClipboard,
    };

    internal static IslandHubTab Resolve(string? saved, bool musicAvailable)
    {
        IslandHubTab tab = Normalize(saved);
        return tab == IslandHubTab.Music && !musicAvailable ? IslandHubTab.Clipboard : tab;
    }
}
