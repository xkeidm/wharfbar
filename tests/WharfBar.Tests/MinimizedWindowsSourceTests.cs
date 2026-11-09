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
    public void WindowsEqual_WhenListsIdentical_IsTrue()
    {
        List<AppWindowInfo> a = [W(1, 10, false, "Main"), W(2, 20, true, "Child")];
        List<AppWindowInfo> b = [W(1, 10, false, "Main"), W(2, 20, true, "Child")];
        Assert.True(MinimizedWindowsSource.WindowsEqual(a, b));
    }

    /// <summary>
    /// A window-title change is a real change the dock's tooltips reflect, so it must raise
    /// <c>Changed</c> — dropping the <c>Title</c> comparison would freeze those tooltips.
    /// </summary>
    [Fact]
    public void WindowsEqual_WhenOnlyTitleDiffers_IsFalse()
    {
        List<AppWindowInfo> a = [W(1, 10, false, "Main")];
        List<AppWindowInfo> b = [W(1, 10, false, "Renamed")];
        Assert.False(MinimizedWindowsSource.WindowsEqual(a, b));
    }

    [Fact]
    public void WindowsEqual_WhenTitleCaseDiffers_IsFalse()
    {
        List<AppWindowInfo> a = [W(1, 10, false, "Main")];
        List<AppWindowInfo> b = [W(1, 10, false, "main")];
        Assert.False(MinimizedWindowsSource.WindowsEqual(a, b));
    }

    [Fact]
    public void WindowsEqual_WhenMinimizedFlips_IsFalse()
    {
        List<AppWindowInfo> a = [W(1, 10, false, "Main")];
        List<AppWindowInfo> b = [W(1, 10, true, "Main")];
        Assert.False(MinimizedWindowsSource.WindowsEqual(a, b));
    }

    [Fact]
    public void WindowsEqual_WhenPidDiffers_IsFalse()
    {
        List<AppWindowInfo> a = [W(1, 10, false, "Main")];
        List<AppWindowInfo> b = [W(1, 11, false, "Main")];
        Assert.False(MinimizedWindowsSource.WindowsEqual(a, b));
    }

    [Fact]
    public void WindowsEqual_WhenHandleDiffers_IsFalse()
    {
        List<AppWindowInfo> a = [W(1, 10, false, "Main")];
        List<AppWindowInfo> b = [W(2, 10, false, "Main")];
        Assert.False(MinimizedWindowsSource.WindowsEqual(a, b));
    }

    [Fact]
    public void WindowsEqual_WhenCountsDiffer_IsFalse()
    {
        List<AppWindowInfo> a = [W(1, 10, false, "Main")];
        List<AppWindowInfo> b = [W(1, 10, false, "Main"), W(2, 10, true, "Child")];
        Assert.False(MinimizedWindowsSource.WindowsEqual(a, b));
    }

    /// <summary>
    /// <c>ApplyScan</c>'s early return needs a live scan and a dispatcher, so it cannot be
    /// exercised here. What this pins is the property the return depends on: a re-scan of an
    /// unchanged window set compares equal — a fresh list instance with the same contents — so
    /// <c>ApplyScan</c> can skip and <c>Changed</c> stays quiet between real changes instead of
    /// firing every 800 ms.
    /// </summary>
    [Fact]
    public void WindowsEqual_WhenRescanOfUnchangedSetIsANewListInstance_IsTrue()
    {
        List<AppWindowInfo> first = [W(1, 10, false, "Main"), W(2, 20, true, "Child")];
        List<AppWindowInfo> rescan = [W(1, 10, false, "Main"), W(2, 20, true, "Child")];
        Assert.NotSame(first, rescan);
        Assert.True(MinimizedWindowsSource.WindowsEqual(first, rescan));
    }
}
