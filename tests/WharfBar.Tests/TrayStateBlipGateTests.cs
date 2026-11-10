using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The only tray read that changes the work area. Normal use (user active, mirror populated) must
/// never be allowed to run it, whatever the elapsed time. The one exception is a mirror that is
/// still blank because a bootstrap never read the tray: that is not evidence of an empty tray, so
/// a bounded series of short retries is allowed (2026-11-09 regression: a single lost unpark race
/// left the bar empty for the whole session because the next retry needed a 10-minute interval and
/// a 3-minute idle user).
/// </summary>
public class TrayStateBlipGateTests
{
    private static readonly TimeSpan Active = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Away = TrayStateBlipGate.MinUserIdle;

    [Fact]
    public void FreshShell_Bootstraps_Once()
    {
        Assert.Equal(
            TrayStateBlipReason.Bootstrap,
            TrayStateBlipGate.Decide(hasSnapshot: false, bootstrapAttempts: 0, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.Zero, userIdle: Active));
    }

    [Fact]
    public void ActiveUser_WithSnapshot_NeverBlips_EvenAfterHours()
    {
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: true, bootstrapAttempts: 1, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.FromHours(8), userIdle: Active));
    }

    [Fact]
    public void ActiveUser_WithEmptyMirror_NeverRetries_AfterTheBootstrapBudget()
    {
        // The long-interval rule is unchanged once the bounded bootstrap series is spent.
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: false,
                bootstrapAttempts: TrayStateBlipGate.MaxBootstrapAttempts, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.FromHours(8), userIdle: Active));
    }

    [Fact]
    public void EmptyMirror_UnreadBootstrap_RetriesQuickly_WhileTheUserIsActive()
    {
        // An unread bootstrap proves nothing about the tray. While the bar is blank a short retry
        // is a better trade than a blank bar until the user happens to idle for 3 minutes.
        for (int attempts = 1; attempts < TrayStateBlipGate.MaxBootstrapAttempts; attempts++)
        {
            Assert.Equal(
                TrayStateBlipReason.BootstrapRetry,
                TrayStateBlipGate.Decide(hasSnapshot: false, bootstrapAttempts: attempts,
                    trayConfirmedEmpty: false,
                    sinceLastStateChange: TrayStateBlipGate.BootstrapShortRetryInterval, userIdle: Active));
        }

        // ...but never before the short interval has passed.
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: false, bootstrapAttempts: 1, trayConfirmedEmpty: false,
                sinceLastStateChange: TrayStateBlipGate.BootstrapShortRetryInterval - TimeSpan.FromSeconds(1),
                userIdle: Active));
    }

    [Fact]
    public void QuickBootstrapRetry_IsBoundedToThreeAttemptsPerShell()
    {
        // The budget is per shell session and the whole desktop pays for each flip: after the
        // third blip the old 10-minute rule takes over, so a machine that can never read the tray
        // cannot flap the work area forever.
        Assert.Equal(TrayStateBlipGate.MaxBootstrapAttempts, 3);
        Assert.True(TrayStateBlipGate.BootstrapShortRetryInterval <= TimeSpan.FromMinutes(1));

        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: false,
                bootstrapAttempts: TrayStateBlipGate.MaxBootstrapAttempts, trayConfirmedEmpty: false,
                sinceLastStateChange: TrayStateBlipGate.BootstrapShortRetryInterval, userIdle: Active));
    }

    [Fact]
    public void VerifiedEmptyTray_IsNotRetried()
    {
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: false, bootstrapAttempts: 1, trayConfirmedEmpty: true,
                TimeSpan.FromHours(8), TimeSpan.FromHours(1)));

        // A bootstrap that verified the tray empty must stop the whole series at once.
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: false, bootstrapAttempts: 1, trayConfirmedEmpty: true,
                TrayStateBlipGate.BootstrapShortRetryInterval, Active));
    }

    [Fact]
    public void Maintenance_NeedsBothTheIntervalAndAnIdleUser()
    {
        TimeSpan justUnder = TrayStateBlipGate.MaintenanceInterval - TimeSpan.FromSeconds(1);
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(true, 1, false, justUnder, Away));
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(true, 1, false, TrayStateBlipGate.MaintenanceInterval, Away - TimeSpan.FromSeconds(1)));
        Assert.Equal(
            TrayStateBlipReason.Maintenance,
            TrayStateBlipGate.Decide(true, 1, false, TrayStateBlipGate.MaintenanceInterval, Away));
    }

    [Fact]
    public void LongBootstrapRetry_StillNeedsTheTenMinuteIntervalAndAnIdleUser()
    {
        // After the bounded series, the old (safe) rule stays: 10 minutes AND a 3-minute idle user.
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(false,
                TrayStateBlipGate.MaxBootstrapAttempts, false,
                TrayStateBlipGate.BootstrapRetryInterval - TimeSpan.FromSeconds(1), Away));
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(false,
                TrayStateBlipGate.MaxBootstrapAttempts, false,
                TrayStateBlipGate.BootstrapRetryInterval, Away - TimeSpan.FromSeconds(1)));
        Assert.Equal(
            TrayStateBlipReason.BootstrapRetry,
            TrayStateBlipGate.Decide(false,
                TrayStateBlipGate.MaxBootstrapAttempts, false,
                TrayStateBlipGate.BootstrapRetryInterval, Away));
    }

    [Fact]
    public void Intervals_AreAtLeastTenMinutes()
    {
        Assert.True(TrayStateBlipGate.BootstrapRetryInterval >= TimeSpan.FromMinutes(10));
        Assert.True(TrayStateBlipGate.MaintenanceInterval >= TimeSpan.FromMinutes(10));
        Assert.True(TrayStateBlipGate.MinUserIdle >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void QuietRefreshFilledTheMirror_NoStateBlipIsNeeded()
    {
        // The quiet reveal refresh can supply the bootstrap snapshot without ever flipping the
        // work area. `bootstrapAttempts` is still 0 then; the gate must look at the snapshot,
        // not at "a blip ever ran", or it would jump the desktop for pixels already shown.
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: true, bootstrapAttempts: 0, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.Zero, userIdle: Active));
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: true, bootstrapAttempts: 0, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.FromMinutes(30), userIdle: Away));
    }

    [Fact]
    public void OnlyTheFirstBootstrap_SkipsTheFullWindowCheck()
    {
        Assert.False(TrayStateBlipGate.ChecksAllAffectedWindows(TrayStateBlipReason.Bootstrap));
        Assert.True(TrayStateBlipGate.ChecksAllAffectedWindows(TrayStateBlipReason.BootstrapRetry));
        Assert.True(TrayStateBlipGate.ChecksAllAffectedWindows(TrayStateBlipReason.Maintenance));
    }

    [Fact]
    public void BoundedBootstrapRetries_KeepTheCheapMaximizedOnlyCheck()
    {
        // The bounded retries exist to unstick a blank bar; requiring every snapped/bottom-touching
        // window to move first would let the blank bar sit until the whole desktop changes.
        Assert.False(TrayStateBlipGate.ChecksAllAffectedWindows(
            TrayStateBlipReason.BootstrapRetry, bootstrapAttempts: 1));
        Assert.False(TrayStateBlipGate.ChecksAllAffectedWindows(
            TrayStateBlipReason.BootstrapRetry, bootstrapAttempts: TrayStateBlipGate.MaxBootstrapAttempts - 1));

        // Past the budget (the 10-minute retry) and for maintenance, the full check applies.
        Assert.True(TrayStateBlipGate.ChecksAllAffectedWindows(
            TrayStateBlipReason.BootstrapRetry, bootstrapAttempts: TrayStateBlipGate.MaxBootstrapAttempts));
        Assert.True(TrayStateBlipGate.ChecksAllAffectedWindows(
            TrayStateBlipReason.Maintenance, bootstrapAttempts: 1));
        Assert.False(TrayStateBlipGate.ChecksAllAffectedWindows(
            TrayStateBlipReason.Bootstrap, bootstrapAttempts: 0));
    }

    [Fact]
    public void BlockedBootstrap_IsForcedAfterTheBoundedWait()
    {
        // The blocked paths never advance the attempt counters (a deferred blip proves nothing
        // about the tray), so a maximized window on the primary monitor used to defer the only
        // read forever and the bar showed no tray icons at all (2026-11-10 field report).
        Assert.False(TrayStateBlipGate.ShouldForceBlockedBootstrap(
            hasSnapshot: false, TrayStateBlipReason.Bootstrap, TimeSpan.Zero));
        Assert.False(TrayStateBlipGate.ShouldForceBlockedBootstrap(
            hasSnapshot: false, TrayStateBlipReason.Bootstrap,
            TrayStateBlipGate.MaxBlockedBootstrapWait - TimeSpan.FromSeconds(1)));
        Assert.True(TrayStateBlipGate.ShouldForceBlockedBootstrap(
            hasSnapshot: false, TrayStateBlipReason.Bootstrap,
            TrayStateBlipGate.MaxBlockedBootstrapWait));
        Assert.True(TrayStateBlipGate.ShouldForceBlockedBootstrap(
            hasSnapshot: false, TrayStateBlipReason.BootstrapRetry,
            TrayStateBlipGate.MaxBlockedBootstrapWait));
    }

    [Fact]
    public void BlockedBootstrap_IsNotForcedOnceTheMirrorHasIcons()
    {
        // A populated mirror already has its icons; a deferred maintenance/retry blip must stay
        // deferred rather than flap the desktop for pixels that are already on the bar.
        Assert.False(TrayStateBlipGate.ShouldForceBlockedBootstrap(
            hasSnapshot: true, TrayStateBlipReason.Bootstrap, TimeSpan.FromHours(1)));
        Assert.False(TrayStateBlipGate.ShouldForceBlockedBootstrap(
            hasSnapshot: false, TrayStateBlipReason.None, TimeSpan.FromHours(1)));
        Assert.False(TrayStateBlipGate.ShouldForceBlockedBootstrap(
            hasSnapshot: false, TrayStateBlipReason.Maintenance, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void BlockedBootstrapWait_IsShortAndBounded()
    {
        // The whole point is to fill a blank bar quickly; a multi-minute wait would reproduce the
        // original "never appeared" complaint.
        Assert.True(TrayStateBlipGate.MaxBlockedBootstrapWait <= TimeSpan.FromMinutes(1));
        Assert.True(TrayStateBlipGate.MaxBlockedBootstrapWait >= TimeSpan.FromSeconds(10));
    }
}
