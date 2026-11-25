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
        // CPU/RAM read at Task Manager's 1 s resolution; the expensive GPU PDH + network read keeps
        // the 4 s cadence (a 4 s CPU average made the bar disagree with Task Manager under load).
        Assert.Equal(TimeSpan.FromSeconds(1), SystemStatsService.SampleInterval);
        Assert.True(SystemStatsService.GpuSampleInterval >= TimeSpan.FromSeconds(4));
        Assert.True(TrayMirrorService.PollInterval >= TimeSpan.FromSeconds(4));
        Assert.True(BluetoothService.NoticeInterval >= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ClockRearmsAtTheNextMinuteBoundary()
    {
        DateTime now = new(2026, 8, 9, 21, 57, 42, 750);
        TimeSpan untilNextMinute = StatusService.UntilNextMinute(now);

        // The one-shot clock timer must land after the minute boundary, never before it, and the
        // wait stays under a minute plus the small re-arm margin.
        Assert.True(untilNextMinute > TimeSpan.Zero);
        Assert.True(untilNextMinute < TimeSpan.FromSeconds(61));
        DateTime armed = now + untilNextMinute;
        Assert.Equal(0, armed.Second);
        Assert.True(armed > now);

        // Later in the same minute means a shorter wait.
        Assert.True(StatusService.UntilNextMinute(now.AddSeconds(1)) < untilNextMinute);
    }

    [Fact]
    public void SlowSampleWaitsForAnActiveUserOnSlowTicks()
    {
        Assert.True(SystemStatsService.ShouldTakeSlowSample(0, TimeSpan.Zero));
        Assert.True(SystemStatsService.ShouldTakeSlowSample(
            SystemStatsService.TicksPerSlowSample,
            SystemStatsService.SlowSampleIdleLimit - TimeSpan.FromSeconds(1)));

        // Not a slow tick, or the user is away: carry the last GPU/network values.
        Assert.False(SystemStatsService.ShouldTakeSlowSample(1, TimeSpan.Zero));
        Assert.False(SystemStatsService.ShouldTakeSlowSample(0, SystemStatsService.SlowSampleIdleLimit));
        Assert.False(SystemStatsService.ShouldTakeSlowSample(0, TimeSpan.FromMinutes(30)));
    }

    [Fact]
    public void DesktopIconReadBacksOffOnlyWhileIdle()
    {
        Assert.Equal(
            DesktopIconLayoutGuard.LiveRefreshMs,
            DesktopIconLayoutGuard.LiveRefreshIntervalMs(TimeSpan.Zero));
        Assert.Equal(
            DesktopIconLayoutGuard.LiveRefreshMs,
            DesktopIconLayoutGuard.LiveRefreshIntervalMs(TimeSpan.FromSeconds(60)));
        Assert.Equal(
            DesktopIconLayoutGuard.LiveRefreshIdleMs,
            DesktopIconLayoutGuard.LiveRefreshIntervalMs(
                TimeSpan.FromMilliseconds(DesktopIconLayoutGuard.IdleBeforeSlowRefreshMs + 1)));
    }

    [Fact]
    public void BluetoothNoticeWatchSkipsWinRtWhileAway()
    {
        // An open quick-settings page always refreshes (its 2 s cadence is unchanged).
        Assert.True(BluetoothService.ShouldRefresh(pageOpen: true, userIdle: TimeSpan.FromMinutes(30)));
        Assert.True(BluetoothService.ShouldRefresh(pageOpen: false, userIdle: TimeSpan.Zero));
        Assert.True(BluetoothService.ShouldRefresh(
            pageOpen: false,
            userIdle: BluetoothService.IdleSkipLimit - TimeSpan.FromSeconds(1)));
        Assert.False(BluetoothService.ShouldRefresh(pageOpen: false, userIdle: BluetoothService.IdleSkipLimit));

        // The island connect-notice watch backs off to 30 s.
        Assert.True(BluetoothService.NoticeInterval >= TimeSpan.FromSeconds(30));
    }
}
