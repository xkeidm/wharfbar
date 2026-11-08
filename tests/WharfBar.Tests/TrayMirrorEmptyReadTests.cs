using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// What the mirror does on an empty UIA read. Because the parked auto-hide tray virtualizes its XAML
/// buttons, a direct read <b>normally</b> returns empty while WharfBar is running; that is not
/// evidence and must never clear the mirror. Only a refresh that reads a genuinely shown tray as
/// empty (<see cref="TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty"/>) clears it —
/// a stale icon is better than an empty bar (and the work-area jump needed to fill it).
/// Pure logic; no Win32.
/// </summary>
public class TrayMirrorEmptyReadTests
{
    /// <summary>
    /// Measured regression: when parked, a direct read returns empty on <b>every</b> poll. The
    /// mirror decision is a tiny state machine — an empty read only changes the held snapshot when
    /// the tray was revealed and proved empty; a later parked read must not resurrect or blank it.
    /// </summary>
    [Fact]
    public void ParkedPollSequence_HoldsTheSnapshotUntilEvidenceClearsIt()
    {
        // A good snapshot is on the bar while every direct read comes back empty with no evidence.
        int held = 3;
        for (int poll = 1; poll <= 25; poll++)
        {
            bool keep = TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(held, trayConfirmedEmptyByRefresh: false);
            held = keep ? held : 0;
            Assert.True(keep, $"poll {poll}: the mirror was cleared without evidence");
            Assert.Equal(3, held);
        }

        // A refresh that read buttons replaces the snapshot (PollUia assigns the fresh item count).
        held = 5;

        // A revealed tray that read back empty is evidence: the snapshot goes away.
        bool keepAfterEvidence = TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(held, trayConfirmedEmptyByRefresh: true);
        Assert.False(keepAfterEvidence);
        held = keepAfterEvidence ? held : 0;
        Assert.Equal(0, held);

        // And a later parked/inconclusive read cannot resurrect the cleared mirror.
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(held, trayConfirmedEmptyByRefresh: false));
    }

    [Fact]
    public void ConfirmedEmptyRefresh_ClearsTheMirror()
    {
        // The tray really was shown and read as empty: the user has closed all tray applications.
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 3, trayConfirmedEmptyByRefresh: true));
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 1, trayConfirmedEmptyByRefresh: true));
    }

    [Fact]
    public void EmptyRead_NothingHeld_ReportsEmpty()
    {
        // There is no good snapshot: there is nothing to hold on to, and the empty list is already correct.
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 0, trayConfirmedEmptyByRefresh: false));
    }

    /// <summary>
    /// Source of empty-read evidence: only an empty read of a tray that was <b>fully on screen</b>
    /// while the read ran counts as evidence. A refresh that was not attempted because of the
    /// throttle, the user being busy, full screen or a failed reveal, and a peek that never opened,
    /// are NOT evidence.
    /// </summary>
    [Theory]
    // The tray was never shown (the peek did not open / the reveal timed out).
    [InlineData(false, false, 0)]
    // The tray was shown but the UIA walk blew up — nothing was read.
    [InlineData(true, true, 0)]
    [InlineData(true, true, 7)]
    public void RefreshThatNeverReadTheTray_IsNotEvidence(bool trayFullyOnScreen, bool readWalkFailed, int readCount)
    {
        TrayMirrorService.TrayBlipOutcome outcome =
            TrayMirrorService.ClassifyBlipRead(trayFullyOnScreen, readWalkFailed, readCount);

        Assert.Equal(TrayMirrorService.TrayBlipOutcome.Inconclusive, outcome);
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 3, trayConfirmedEmptyByRefresh: false));
    }

    [Fact]
    public void TrayNotFullyOnScreen_EmptyReadIsNotConfirmedEmpty()
    {
        // The peek opened but the tray rect was unreadable or explorer was mid slide-out:
        // "not parked" is not the same as "looked at". A read that never saw the tray must hold
        // the held snapshot instead of wiping it.
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.Inconclusive,
            TrayMirrorService.ClassifyBlipRead(trayFullyOnScreen: false, readWalkFailed: false, readCount: 0));
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 3, trayConfirmedEmptyByRefresh: false));
    }

    [Fact]
    public void FullyShownTray_ReadsEmpty_ConfirmsTheTrayIsEmpty()
    {
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty,
            TrayMirrorService.ClassifyBlipRead(trayFullyOnScreen: true, readWalkFailed: false, readCount: 0));
    }

    [Fact]
    public void FullyShownTray_WithButtons_IsARefresh()
    {
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.Refreshed,
            TrayMirrorService.ClassifyBlipRead(trayFullyOnScreen: true, readWalkFailed: false, readCount: 3));
    }

    [Fact]
    public void SkippedRefresh_IsDistinguishableFromEvidence()
    {
        Assert.NotEqual(TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty, TrayMirrorService.TrayBlipOutcome.Skipped);
        Assert.NotEqual(TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty, TrayMirrorService.TrayBlipOutcome.Inconclusive);
    }
}
