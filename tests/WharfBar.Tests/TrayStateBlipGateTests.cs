using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The only tray read that changes the work area. Normal use (user active, mirror populated) must
/// never be allowed to run it, whatever the elapsed time.
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
            TrayStateBlipGate.Decide(hasSnapshot: false, blippedThisShell: false, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.Zero, userIdle: Active));
    }

    [Fact]
    public void ActiveUser_WithSnapshot_NeverBlips_EvenAfterHours()
    {
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: true, blippedThisShell: true, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.FromHours(8), userIdle: Active));
    }

    [Fact]
    public void ActiveUser_WithEmptyMirror_NeverRetries()
    {
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: false, blippedThisShell: true, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.FromHours(8), userIdle: Active));
    }

    [Fact]
    public void Maintenance_NeedsBothTheIntervalAndAnIdleUser()
    {
        TimeSpan justUnder = TrayStateBlipGate.MaintenanceInterval - TimeSpan.FromSeconds(1);
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(true, true, false, justUnder, Away));
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(true, true, false, TrayStateBlipGate.MaintenanceInterval, Away - TimeSpan.FromSeconds(1)));
        Assert.Equal(
            TrayStateBlipReason.Maintenance,
            TrayStateBlipGate.Decide(true, true, false, TrayStateBlipGate.MaintenanceInterval, Away));
    }

    [Fact]
    public void BootstrapRetry_NeedsTheRetryIntervalAndAnIdleUser()
    {
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(false, true, false, TrayStateBlipGate.BootstrapRetryInterval - TimeSpan.FromSeconds(1), Away));
        Assert.Equal(
            TrayStateBlipReason.BootstrapRetry,
            TrayStateBlipGate.Decide(false, true, false, TrayStateBlipGate.BootstrapRetryInterval, Away));
    }

    [Fact]
    public void ProvenEmptyTray_IsNotRetried()
    {
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(false, true, trayConfirmedEmpty: true, TimeSpan.FromHours(8), TimeSpan.FromHours(1)));
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
        // work area. `blippedThisShell` is still false then; the gate must look at the snapshot,
        // not at "has a blip ever run", or it would jump the desktop for pixels already shown.
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: true, blippedThisShell: false, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.Zero, userIdle: Active));
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(hasSnapshot: true, blippedThisShell: false, trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.FromMinutes(30), userIdle: Away));
    }

    [Fact]
    public void OnlyTheFirstBootstrap_SkipsTheFullWindowCheck()
    {
        Assert.False(TrayStateBlipGate.ChecksAllAffectedWindows(TrayStateBlipReason.Bootstrap));
        Assert.True(TrayStateBlipGate.ChecksAllAffectedWindows(TrayStateBlipReason.BootstrapRetry));
        Assert.True(TrayStateBlipGate.ChecksAllAffectedWindows(TrayStateBlipReason.Maintenance));
    }
}
