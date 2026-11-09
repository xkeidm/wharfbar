using System.Windows;
using System.Windows.Threading;
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

    /// <summary>
    /// The music body's clipboard-history affordance is primary-only (docs: "The clipboard
    /// affordance on the music body (primary only)"). It used to stay collapsed everywhere because
    /// nothing ever set its Visibility.
    /// </summary>
    [Fact]
    public void ClipboardAffordance_IsVisibleOnThePrimarySurface_Only()
    {
        RunSta(() =>
        {
            var flyout = new IslandFlyout();

            flyout.SetClipboardChrome(available: true);
            flyout.PresentHub(IslandHubTab.Music, focus: false, crossfade: false);
            Assert.Equal(Visibility.Visible, flyout.ClipboardAffordance.Visibility);

            flyout.SetClipboardChrome(available: false);
            flyout.PresentHub(IslandHubTab.Music, focus: false, crossfade: false);
            Assert.Equal(Visibility.Collapsed, flyout.ClipboardAffordance.Visibility);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                try
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
                catch (InvalidOperationException)
                {
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }
}
