using System.Collections.Generic;
using System.Linq;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MinimizedWindowsSourceTests
{
    private static AppWindowInfo W(int id, uint pid, bool minimized, string title) =>
        new((IntPtr)id, title, pid, minimized);

    [Fact]
    public void SelectMinimized_ReturnsOnlyIconicWindows()
    {
        List<AppWindowInfo> windows = [W(1, 10, true, "A"), W(2, 20, false, "B")];
        AppWindowInfo[] result = [.. MinimizedWindowsSource.SelectMinimized(windows)];
        Assert.Equal([(IntPtr)1], result.Select(w => w.Handle));
    }

    [Fact]
    public void SelectMinimized_WhenNoneMinimized_IsEmpty()
    {
        List<AppWindowInfo> windows = [W(1, 10, false, "A")];
        Assert.Empty(MinimizedWindowsSource.SelectMinimized(windows));
    }

    [Fact]
    public void SelectMinimized_PreservesScanOrder()
    {
        List<AppWindowInfo> windows =
        [
            W(5, 10, true, "Z"),
            W(6, 20, false, "Y"),
            W(7, 30, true, "X"),
        ];
        Assert.Equal(
            [(IntPtr)5, (IntPtr)7],
            MinimizedWindowsSource.SelectMinimized(windows).Select(w => w.Handle));
    }

    [Fact]
    public void ForProcess_ReturnsEveryWindowOfThatProcessIncludingMinimized()
    {
        List<AppWindowInfo> windows =
        [
            W(1, 10, false, "Main"),
            W(2, 10, true, "Child"),
            W(3, 20, false, "Other"),
        ];
        IntPtr[] result = [.. MinimizedWindowsSource.ForProcess(windows, 10).Select(w => w.Handle)];
        Assert.Equal([(IntPtr)1, (IntPtr)2], result);
    }

    [Fact]
    public void ForProcess_WhenPidIsZero_ReturnsEmpty()
    {
        List<AppWindowInfo> windows = [W(1, 10, false, "Main")];
        Assert.Empty(MinimizedWindowsSource.ForProcess(windows, 0));
    }

    [Fact]
    public void ForProcess_WhenUnknownPid_ReturnsEmpty()
    {
        List<AppWindowInfo> windows = [W(1, 10, false, "Main")];
        Assert.Empty(MinimizedWindowsSource.ForProcess(windows, 999));
    }

    [Fact]
    public void ForProcess_OrdersMinimizedLastForStableDockOrder()
    {
        List<AppWindowInfo> windows =
        [
            W(1, 10, true, "Min"),
            W(2, 10, false, "Main"),
        ];
        Assert.Equal(
            [(IntPtr)2, (IntPtr)1],
            MinimizedWindowsSource.ForProcess(windows, 10).Select(w => w.Handle));
    }
}