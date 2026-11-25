using System.Windows.Media;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class AppIconServiceTests
{
    [Fact]
    public void GetIcon_OverCap_StaysBounded()
    {
        RunSta(() =>
        {
            var icons = new AppIconService();
            ImageSource? first = null;
            for (int i = 0; i < AppIconService.MaxEntries + 40; i++)
            {
                ImageSource icon = icons.GetIcon($@"C:\wharfbar-missing-icon-{i}.ico", 32);
                first ??= icon;
            }

            // The 296 distinct keys cannot all be cached: the oldest entries are evicted, so the map
            // settles exactly on the cap.
            Assert.Equal(AppIconService.MaxEntries, icons.CacheCount);
            Assert.NotNull(first);
            Assert.True(first.IsFrozen);
        });
    }

    [Fact]
    public void GetIcon_RepeatedPath_IsServedFromCache()
    {
        RunSta(() =>
        {
            var icons = new AppIconService();
            ImageSource first = icons.GetIcon(@"C:\wharfbar-missing-icon.ico", 32);
            ImageSource second = icons.GetIcon(@"C:\wharfbar-missing-icon.ico", 32);

            Assert.Same(first, second);
            Assert.True(first.IsFrozen);
            Assert.Equal(1, icons.CacheCount);
        });
    }

    [Fact]
    public void GetIcon_SamePathDifferentSize_UsesSeparateKeys()
    {
        RunSta(() =>
        {
            var icons = new AppIconService();
            icons.GetIcon(@"C:\wharfbar-missing-icon.ico", 32);
            icons.GetIcon(@"C:\wharfbar-missing-icon.ico", 48);

            Assert.Equal(2, icons.CacheCount);
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
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null)
        {
            throw error;
        }
    }
}
