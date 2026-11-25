using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The direct tray read is a desktop-root UI Automation walk (~0.4 s of CPU per pass, measured).
/// It may only run while explorer actually shows the tray; a hidden/parked default tray is
/// virtualized, so the walk would cost that time and return nothing. Reads then belong to the
/// gated state blip (<see cref="TrayStateBlipGate"/>).
/// </summary>
public class TrayMirrorUiaGateTests
{
    [Fact]
    public void DirectUiaWalkIsSkippedWhileTheTrayIsNotShown()
    {
        Assert.False(TrayMirrorService.ShouldWalkTrayUia(trayVisible: false, trayFullyOnScreen: false));
        Assert.False(TrayMirrorService.ShouldWalkTrayUia(trayVisible: true, trayFullyOnScreen: false));
    }

    [Fact]
    public void DirectUiaWalkRunsWhileTheTrayIsFullyOnScreen()
    {
        Assert.True(TrayMirrorService.ShouldWalkTrayUia(trayVisible: true, trayFullyOnScreen: true));
    }

    [Fact]
    public void ABootstrapStillHasARealReadPathWithoutTheDirectWalk()
    {
        // The skipped walk must not strand a blank mirror: the first blip of a shell instance is
        // always allowed, and its read happens after the tray is verified on screen.
        Assert.Equal(
            TrayStateBlipReason.Bootstrap,
            TrayStateBlipGate.Decide(
                hasSnapshot: false,
                bootstrapAttempts: 0,
                trayConfirmedEmpty: false,
                sinceLastStateChange: TimeSpan.Zero,
                userIdle: TimeSpan.Zero));
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 0));
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 1));
    }
}
