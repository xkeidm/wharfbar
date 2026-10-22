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

    [Fact]
    public void Normalize_Self_ReturnsShelfTab()
    {
        Assert.Equal(IslandHubTab.Shelf, IslandHub.Normalize("shelf"));
    }
    [Fact]
    public void Normalize_ColorPicker_ReturnsColorPickerTab()
    {
        Assert.Equal(IslandHubTab.ColorPicker, IslandHub.Normalize("colorpicker"));
    }
    [Fact]
    public void ToConfig_Shelf_WritesShelfId()
    {
        Assert.Equal("shelf", IslandHub.ToConfig(IslandHubTab.Shelf));
    }
    [Fact]
    public void Resolve_UnknownSavedString_FallsBackToClipboardTab()
    {
        Assert.Equal(IslandHubTab.Clipboard, IslandHub.Resolve("nope", musicAvailable: false));
    }

    [Theory]
    [InlineData("prompter", IslandHubTab.Prompter)]
    [InlineData(" PROMPTER ", IslandHubTab.Prompter)]
    [InlineData("prompterX", IslandHubTab.Clipboard)]
    [InlineData("camera", IslandHubTab.Camera)]
    [InlineData(" CAMERA ", IslandHubTab.Camera)]
    [InlineData("cameraX", IslandHubTab.Clipboard)]
    internal void Normalize_StringValue_ReturnsExpectedTab(string value, IslandHubTab expected)
    {
        Assert.Equal(expected, IslandHub.Normalize(value));
    }

    [Fact]
    public void Resolve_SavedPrompter_MusicAvailable_Resolves()
    {
        Assert.Equal(IslandHubTab.Prompter, IslandHub.Resolve("prompter", musicAvailable: true));
    }

    [Fact]
    public void ToConfig_Camera_WritesCameraId()
    {
        Assert.Equal("camera", IslandHub.ToConfig(IslandHubTab.Camera));
    }

    [Fact]
    public void Resolve_SavedCamera_MusicAvailable_Resolves()
    {
        Assert.Equal(IslandHubTab.Camera, IslandHub.Resolve("camera", musicAvailable: true));
    }
}
