using System.Windows;
using System.Windows.Threading;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

public class IslandFlyoutMotionTests
{
    [Fact]
    public void PanelSurface_IsInsideThePopupChild()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var flyout = new IslandFlyout();

                // The popup child must stay unscaled. WPF places it on the transformed
                // top-left, so the grow animation lives on an inner surface.
                Assert.NotSame(flyout, flyout.PanelSurface);
                Assert.Same(flyout, FindAncestorFlyout(flyout.PanelSurface));
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

    private static IslandFlyout? FindAncestorFlyout(DependencyObject start)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is IslandFlyout flyout)
            {
                return flyout;
            }

            current = LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
