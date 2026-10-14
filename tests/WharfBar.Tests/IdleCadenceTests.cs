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
