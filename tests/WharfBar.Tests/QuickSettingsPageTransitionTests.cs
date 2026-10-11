using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WharfBar.Services;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

public class QuickSettingsPageTransitionTests
{
    [Fact]
    public void PageHost_ClipsSlidingPages()
    {
        RunOnSta(() =>
        {
            var flyout = new QuickSettingsFlyout();
            Assert.True(flyout.PageHost.ClipToBounds);
        });
    }

    [Fact]
    public void ShowPage_BeforeOpen_SwapsWithoutLeavingThePreviousPageUp()
    {
        RunOnSta(() =>
        {
            var flyout = new QuickSettingsFlyout();

            flyout.ShowPage(QuickSettingsPage.Wifi);

            Assert.Equal(Visibility.Visible, flyout.WifiPage.Visibility);
            Assert.Equal(Visibility.Collapsed, flyout.MainPage.Visibility);
            Assert.Equal(1d, flyout.WifiPage.Opacity);
            Assert.True(flyout.WifiPage.IsHitTestVisible);
        });
    }

    [Fact]
    public void ShowPage_WhileOpen_BringsTheNextPageInFromTheRight()
    {
        RunOnSta(() =>
        {
            var flyout = new QuickSettingsFlyout();
            flyout.OnFlyoutOpened(QuickSettingsPage.Main);

            flyout.ShowPage(QuickSettingsPage.Wifi);

            Assert.Equal(Visibility.Visible, flyout.MainPage.Visibility);
            Assert.Equal(Visibility.Visible, flyout.WifiPage.Visibility);
            Assert.False(flyout.MainPage.IsHitTestVisible);
            Assert.True(flyout.WifiPage.IsHitTestVisible);
            Assert.True(OffsetX(flyout.WifiPage) > 0d);
            Assert.Equal(Visibility.Collapsed, flyout.MixerPage.Visibility);
        });
    }

    [Fact]
    public void ShowPage_WhileOpen_BringsMainBackFromTheLeft()
    {
        RunOnSta(() =>
        {
            var flyout = new QuickSettingsFlyout();
            flyout.OnFlyoutOpened(QuickSettingsPage.Bluetooth);

            flyout.ShowPage(QuickSettingsPage.Main);

            Assert.Equal(Visibility.Visible, flyout.BluetoothPage.Visibility);
            Assert.False(flyout.BluetoothPage.IsHitTestVisible);
            Assert.True(flyout.MainPage.IsHitTestVisible);
            Assert.True(OffsetX(flyout.MainPage) < 0d);
        });
    }

    [Fact]
    public void ShowPage_MidTransition_KeepsOnlyTheLatestPair()
    {
        RunOnSta(() =>
        {
            var flyout = new QuickSettingsFlyout();
            flyout.OnFlyoutOpened(QuickSettingsPage.Main);
            flyout.ShowPage(QuickSettingsPage.Wifi);

            flyout.ShowPage(QuickSettingsPage.Bluetooth);

            Assert.Equal(Visibility.Collapsed, flyout.MainPage.Visibility);
            Assert.Equal(1d, flyout.MainPage.Opacity);
            Assert.Equal(Visibility.Visible, flyout.WifiPage.Visibility);
            Assert.False(flyout.WifiPage.IsHitTestVisible);
            Assert.Equal(Visibility.Visible, flyout.BluetoothPage.Visibility);
            Assert.True(flyout.BluetoothPage.IsHitTestVisible);
            Assert.Equal(Visibility.Collapsed, flyout.ProjectPage.Visibility);
        });
    }

    private static double OffsetX(FrameworkElement page)
    {
        var transform = page.RenderTransform as TranslateTransform;
        Assert.NotNull(transform);
        return transform.X;
    }

    private static void RunOnSta(Action action)
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }
}
