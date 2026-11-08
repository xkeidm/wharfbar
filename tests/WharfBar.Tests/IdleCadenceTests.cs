using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IdleCadenceTests
{
    [Fact]
    public void HiddenDockPollsSlowerThanAVisibleOne()
    {
        Assert.Equal(DockAutoHideController.IdlePollMs, DockAutoHideController.PollIntervalMs(DockAutoHideVisualState.Hidden));
        Assert.Equal(DockAutoHideController.PollMs, DockAutoHideController.PollIntervalMs(DockAutoHideVisualState.Visible));
        Assert.True(DockAutoHideController.IdlePollMs > DockAutoHideController.PollMs);
    }

    [Fact]
    public void TrayPeekSuppressesTheDockEdgeBandShow()
    {
        // The quiet mirror reveal parks the real cursor in the bottom show band for a few hundred
        // ms. Without this suppression the dock would pop every 20–60 s during the refresh.
        Assert.False(DockAutoHideController.ShouldShowForCursor(trayPeekActive: true, inEdgeBand: true, inDockRect: false));
        Assert.False(DockAutoHideController.ShouldShowForCursor(trayPeekActive: true, inEdgeBand: false, inDockRect: true));
        Assert.True(DockAutoHideController.ShouldShowForCursor(trayPeekActive: false, inEdgeBand: true, inDockRect: false));
        Assert.True(DockAutoHideController.ShouldShowForCursor(trayPeekActive: false, inEdgeBand: false, inDockRect: true));
        Assert.False(DockAutoHideController.ShouldShowForCursor(trayPeekActive: false, inEdgeBand: false, inDockRect: false));
    }

    [Fact]
    public void ProgressWalkBacksOffUntilAButtonIsExposed()
    {
        Assert.Equal(DockTaskbarProgress.QuietInterval, DockTaskbarProgress.NextInterval(anyRunning: false, trayExposed: true));
        Assert.Equal(DockTaskbarProgress.QuietInterval, DockTaskbarProgress.NextInterval(anyRunning: true, trayExposed: false));
        Assert.Equal(DockTaskbarProgress.ActiveInterval, DockTaskbarProgress.NextInterval(anyRunning: true, trayExposed: true));
    }

    [Fact]
    public void SharedCensusIsSlowerThanTheOldPerDockScans()
    {
        Assert.True(WindowCensus.Interval >= TimeSpan.FromSeconds(3));
        Assert.True(SystemStatsService.SampleInterval >= TimeSpan.FromSeconds(4));
        Assert.True(TrayMirrorService.PollInterval >= TimeSpan.FromSeconds(4));
        Assert.True(BluetoothService.NoticeInterval >= TimeSpan.FromSeconds(10));
    }
}
