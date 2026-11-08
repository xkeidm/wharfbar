using WharfBar.Services;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// When the shelf and color picker tabs are wired into the hub, the tag→body match is verified
/// through a pure helper: <c>IslandFlyout.TabIdForBody</c> produces the id stored in the body Tag for each tab.
/// </summary>
public class IslandFlyoutHubTabsTests
{
    [Fact]
    public void TabIdForBody_ShelfId_MatchesTag()
    {
        Assert.Equal("shelf", IslandFlyout.TabIdForBody(IslandHubTab.Shelf));
    }

    [Fact]
    public void TabIdForBody_ColorPickerId_MatchesTag()
    {
        Assert.Equal("colorpicker", IslandFlyout.TabIdForBody(IslandHubTab.ColorPicker));
    }

    [Fact]
    public void TabIdForBody_CameraId_MatchesTag()
    {
        Assert.Equal("camera", IslandFlyout.TabIdForBody(IslandHubTab.Camera));
    }

    [Fact]
    public void TabIdForBody_PrompterId_MatchesTag()
    {
        Assert.Equal("prompter", IslandFlyout.TabIdForBody(IslandHubTab.Prompter));
    }
}
