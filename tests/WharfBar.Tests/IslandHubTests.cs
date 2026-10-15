using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandHubTests
{
    [Theory]
    [InlineData(null, IslandConfig.HubTabClipboard)]
    [InlineData("", IslandConfig.HubTabClipboard)]
    [InlineData("garbage", IslandConfig.HubTabClipboard)]
    [InlineData("clipboard", IslandConfig.HubTabClipboard)]
    [InlineData("Wallpaper", IslandConfig.HubTabWallpaper)]
    [InlineData("MUSIC", IslandConfig.HubTabMusic)]
    public void Normalize_MapsKnownOrFallsBack(string? input, string expected)
        => Assert.Equal(expected, IslandHub.ToConfig(IslandHub.Normalize(input)));

    [Theory]
    [InlineData("music", true, IslandConfig.HubTabMusic)]
    [InlineData("music", false, IslandConfig.HubTabClipboard)]
    [InlineData("wallpaper", false, IslandConfig.HubTabWallpaper)]
    [InlineData("clipboard", true, IslandConfig.HubTabClipboard)]
    public void Resolve_HidesMusicWhenUnavailable(string saved, bool music, string expected)
        => Assert.Equal(expected, IslandHub.ToConfig(IslandHub.Resolve(saved, music)));

    [Fact]
    public void ToConfig_RoundTrips()
    {
        Assert.Equal(IslandConfig.HubTabWallpaper, IslandHub.ToConfig(IslandHubTab.Wallpaper));
        Assert.Equal(IslandHubTab.Wallpaper, IslandHub.Normalize(IslandHub.ToConfig(IslandHubTab.Wallpaper)));
    }
}
