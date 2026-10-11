using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// WPF-UI wraps a page in a DynamicScrollViewer while ScrollViewer.CanContentScroll is true.
/// That measures the page with infinite height, so a root ScrollViewer reports no scroll range
/// and still eats the mouse wheel. Settings pages opt out so their own ScrollViewer is bounded.
/// </summary>
public class SettingsPageScrollTests
{
    [Fact]
    public void PageScrollViewer_ScrollsOnlyWhenNavigationHostDoesNotWrapIt()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Application app = Application.Current
                    ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ThemesDictionary());
                app.Resources.MergedDictionaries.Add(new ControlsDictionary());

                double wrapped = MeasureScrollableHeight(canContentScroll: true);
                double unwrapped = MeasureScrollableHeight(canContentScroll: false);

                Assert.Equal(0d, wrapped);
                Assert.True(unwrapped > 0d, $"expected a scrollable page, got {unwrapped}");
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                try
                {
                    Application.Current?.Shutdown();
                }
                catch (InvalidOperationException)
                {
                }

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

    private static double MeasureScrollableHeight(bool canContentScroll)
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new Border { Height = 2000, Width = 100 },
        };
        var page = new Page { Content = scroll };
        ScrollViewer.SetCanContentScroll(page, canContentScroll);

        var presenter = new NavigationViewContentPresenter
        {
            Width = 400,
            Height = 300,
        };
        var host = new Grid { Width = 400, Height = 300 };
        host.Children.Add(presenter);

        var window = new Window
        {
            Width = 400,
            Height = 300,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Content = host,
        };
        window.Show();
        Assert.True(presenter.Navigate(page));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        host.UpdateLayout();

        double result = scroll.ScrollableHeight;
        window.Close();
        return result;
    }
}
