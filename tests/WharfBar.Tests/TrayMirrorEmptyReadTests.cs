using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// What the mirror does on an empty UIA read. Because the parked auto-hide tray virtualizes its XAML
/// buttons, a direct read <b>normally</b> returns empty while WharfBar runs; that is not evidence.
/// A "verified empty" read has also been measured to lie while a fullscreen window (WharfBar's own
/// colour picker) covers the monitor. A populated mirror is therefore never cleared by an empty
/// read — a stale icon is better than a blank bar (and the work-area jump needed to fill it).
/// Pure logic; no Win32.
/// </summary>
public class TrayMirrorEmptyReadTests
{
    /// <summary>
    /// Measured regression: when parked, a direct read returns empty on <b>every</b> poll. The
    /// mirror decision is a tiny state machine — an empty read keeps the held snapshot.
    /// </summary>
    [Fact]
    public void ParkedPollSequence_HoldsTheSnapshotForTheWholeSession()
    {
        int held = 3;
        for (int poll = 1; poll <= 25; poll++)
        {
            Assert.True(
                TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(held),
                $"poll {poll}: the mirror was cleared without a shell restart");
        }

        // A refresh that read buttons replaces the snapshot (PollUia assigns the fresh item count).
        held = 5;
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(held));
    }

    [Fact]
    public void ConfirmedEmptyRead_StillDoesNotClearAPopulatedMirror()
    {
        // 2026-11-09: a verified read of a fully-on-screen tray came back empty while a fullscreen
        // colour picker covered the monitor, and it blanked the mirror for the session. The
        // fullscreen guard prevents that blip; this rule guarantees the mirror stays populated even
        // if a lie slips through. A stale icon beats a blank bar.
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 3));
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 1));
    }

    [Fact]
    public void EmptyRead_NothingHeld_ReportsEmpty()
    {
        // There is no good snapshot: there is nothing to hold on to, and the empty list is correct.
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(heldItemCount: 0));
    }

    /// <summary>
    /// Source of empty-read evidence: only an empty read of a tray that was <b>fully on screen</b>
    /// while the read ran counts as evidence. A refresh that was not attempted because of the gate,
    /// a fullscreen foreground window or a failed peek, and a UIA walk that threw, are NOT evidence.
    /// </summary>
    [Theory]
    // The tray was never shown (the peek did not open / the unpark was never verified).
    [InlineData(false, false, 0)]
    // The tray was shown but the UIA walk blew up — nothing was read.
    [InlineData(true, true, 0)]
    [InlineData(true, true, 7)]
    public void RefreshThatNeverReadTheTray_IsNotEvidence(bool unparkVerified, bool readWalkFailed, int readCount)
    {
        TrayMirrorService.TrayBlipOutcome outcome =
            TrayMirrorService.ClassifyBlipRead(unparkVerified, readWalkFailed, readCount);

        Assert.Equal(TrayMirrorService.TrayBlipOutcome.Inconclusive, outcome);
    }

    [Fact]
    public void TrayNotFullyOnScreen_EmptyReadIsNotConfirmedEmpty()
    {
        // The peek opened but the tray rect was unreadable, explorer was mid slide-out, or the
        // state drop's unpark had not landed yet: "not parked" / "we asked it to unpark" is not
        // the same as "looked at". A read that never saw the tray proves nothing.
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.Inconclusive,
            TrayMirrorService.ClassifyBlipRead(unparkVerified: false, readWalkFailed: false, readCount: 0));
    }

    [Fact]
    public void FullyShownTray_ReadsEmpty_IsConfirmedEmpty()
    {
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty,
            TrayMirrorService.ClassifyBlipRead(unparkVerified: true, readWalkFailed: false, readCount: 0));
    }

    [Fact]
    public void FullyShownTray_WithButtons_IsARefresh()
    {
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.Refreshed,
            TrayMirrorService.ClassifyBlipRead(unparkVerified: true, readWalkFailed: false, readCount: 3));
    }

    [Fact]
    public void SkippedRefresh_IsDistinguishableFromEvidence()
    {
        Assert.NotEqual(TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty, TrayMirrorService.TrayBlipOutcome.Skipped);
        Assert.NotEqual(TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty, TrayMirrorService.TrayBlipOutcome.Inconclusive);
    }
}
