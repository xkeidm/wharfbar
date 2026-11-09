using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Regression cover for the 2026-11-09 session that blanked the tray mirror for a whole run:
/// the loud state blip read the tray while <c>ABM_SETSTATE(0)</c> had not unparked it yet (rect
/// <c>y=1078</c>, 2 px sliver), the UIA walk therefore returned 0 app buttons, and the old code —
/// which simply assumed "the state drop unparks the tray" — classified that as
/// <see cref="TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty"/>. That cleared the mirror and the
/// gate never bootstrapped again for the shell. The rules below are the fix: an unverified unpark is
/// never evidence, and an unread bootstrap is retried a bounded number of times.
/// </summary>
public class TrayMirrorBootstrapTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void BlipThatNeverVerifiedTheUnpark_IsNeverEvidence(int readCount)
    {
        // A parked (virtualized) tray returns whatever the walk finds — including buttons left over
        // from a stale tree. None of it is evidence about the tray, empty or not.
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.Inconclusive,
            TrayMirrorService.ClassifyBlipRead(unparkVerified: false, readWalkFailed: false, readCount));
    }

    [Fact]
    public void UnverifiedUnpark_DoesNotClearAHeldSnapshot()
    {
        // The failure the user saw: an empty read of a tray nobody actually looked at must keep the
        // last good icons on the bar ("a stale icon beats a blank bar").
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 8));
    }

    [Fact]
    public void LostUnparkRace_OnAFreshShell_IsRetriedAndThenFillsTheMirror()
    {
        // Replay of the measured sequence: bootstrap #1 loses the unpark race (Inconclusive, nothing
        // published), the bounded short retry is allowed ~30 s later, and that attempt — with the
        // state drop settled — verifies the unpark and reads the buttons.
        int published = 0;
        int bootstrapAttempts = 0;

        // Poll 1: the bootstrap runs and fails to verify the unpark; nothing is read, so the
        // (still empty) mirror stays empty.
        bootstrapAttempts++;
        TrayMirrorService.TrayBlipOutcome first = TrayMirrorService.ClassifyBlipRead(
            unparkVerified: false, readWalkFailed: false, readCount: 0);
        Assert.Equal(TrayMirrorService.TrayBlipOutcome.Inconclusive, first);
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(published));
        Assert.Equal(0, published);

        // The old gate locked the shell out for 10 minutes (and needed a 3-minute idle user); the
        // fixed one allows the retry as soon as the short interval passed.
        TimeSpan lastFlipAt = TimeSpan.Zero;
        TimeSpan at = TrayStateBlipGate.BootstrapShortRetryInterval - TimeSpan.FromSeconds(4);
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(
                hasSnapshot: false,
                bootstrapAttempts,
                trayConfirmedEmpty: false,
                sinceLastStateChange: at - lastFlipAt,
                userIdle: TimeSpan.Zero));

        at = TrayStateBlipGate.BootstrapShortRetryInterval;
        Assert.Equal(
            TrayStateBlipReason.BootstrapRetry,
            TrayStateBlipGate.Decide(
                hasSnapshot: false,
                bootstrapAttempts,
                trayConfirmedEmpty: false,
                sinceLastStateChange: at - lastFlipAt,
                userIdle: TimeSpan.Zero));

        // Poll 2: the retry verifies the unpark and reads buttons.
        bootstrapAttempts++;
        TrayMirrorService.TrayBlipOutcome second = TrayMirrorService.ClassifyBlipRead(
            unparkVerified: true, readWalkFailed: false, readCount: 12);
        Assert.Equal(TrayMirrorService.TrayBlipOutcome.Refreshed, second);
        published = 12;

        // The mirror is populated again and no further blip is allowed while the snapshot is held.
        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(published > 0, bootstrapAttempts, false, TimeSpan.FromSeconds(30), TimeSpan.Zero));
    }

    [Fact]
    public void VerifiedEmptyUnpark_StopsTheBootstrapSeriesImmediately()
    {
        // The tray really was shown and has no app icons (every icon lives in Windows' hidden area,
        // ADR-009): no work-area flapping may follow.
        TrayMirrorService.TrayBlipOutcome outcome = TrayMirrorService.ClassifyBlipRead(
            unparkVerified: true, readWalkFailed: false, readCount: 0);
        Assert.Equal(TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty, outcome);

        Assert.Equal(
            TrayStateBlipReason.None,
            TrayStateBlipGate.Decide(
                hasSnapshot: false,
                bootstrapAttempts: 1,
                trayConfirmedEmpty: true,
                sinceLastStateChange: TrayStateBlipGate.BootstrapRetryInterval,
                userIdle: TrayStateBlipGate.MinUserIdle));
    }

    [Fact]
    public void ReadWalkFailure_IsInconclusiveEvenWhenTheUnparkWasVerified()
    {
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.Inconclusive,
            TrayMirrorService.ClassifyBlipRead(unparkVerified: true, readWalkFailed: true, readCount: 0));
    }
}
