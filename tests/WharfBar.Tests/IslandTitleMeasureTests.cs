using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

public class IslandTitleMeasureTests
{
    [Fact]
    public void CollapsedTitle_StillReportsTextWidth()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var block = new TextBlock
                {
                    Text = "Dawn",
                    FontSize = 12,
                    Visibility = Visibility.Collapsed,
                    MaxWidth = 1000,
                };

                block.Measure(new Size(1000, 24));
                Assert.Equal(0, block.DesiredSize.Width);

                double dip = IslandTitleMeasure.Dip(block);

                Assert.True(dip > 0, $"measured {dip}");
                Assert.Equal(Visibility.Collapsed, block.Visibility);
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
                    // Measure may not have started a dispatcher.
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
