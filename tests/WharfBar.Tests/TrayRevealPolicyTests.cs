using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// When the silent reveal refresh moves the cursor to the bottom edge. The UIA buttons of the parked
/// tray are only exposed via hover-reveal; this brief cursor movement must not disturb the user while
/// they are working — so it only runs when the user is idle or the mirror is empty, the empty-mirror
/// bypass is budgeted, and a failing reveal backs off instead of retrying forever.
/// </summary>
public class TrayRevealPolicyTests
{
    [Fact]
    public void IdleUser_AllowsTheReveal()
    {
        Assert.True(TrayRevealPolicy.ShouldReveal(TrayRevealPolicy.MinUserIdle, hasSnapshot: true));
        Assert.True(TrayRevealPolicy.ShouldReveal(TimeSpan.FromMinutes(2), hasSnapshot: true));
    }

    [Fact]
    public void ActiveUser_WithSnapshot_DefersTheReveal()
    {
        Assert.False(TrayRevealPolicy.ShouldReveal(TimeSpan.Zero, hasSnapshot: true));
        Assert.False(TrayRevealPolicy.ShouldReveal(TrayRevealPolicy.MinUserIdle - TimeSpan.FromMilliseconds(1), hasSnapshot: true));
    }

    [Fact]
    public void EmptyMirror_RevealsEvenWhileTheUserIsActive_WithinTheBypassBudget()
    {
        // An empty bar has no stale icon worth keeping: a brief cursor jump is the only way to fill
        // the bar. That exception is finite — a genuinely empty tray must not hop the pointer
        // behind a busy user forever.
        for (int used = 0; used < TrayRevealPolicy.EmptyMirrorBypassAttempts; used++)
        {
            Assert.True(TrayRevealPolicy.ShouldAttemptReveal(
                TimeSpan.Zero,
                hasSnapshot: false,
                consecutiveRevealMisses: 0,
                sinceLastRevealMiss: TimeSpan.Zero,
                emptyMirrorBypassUsed: used));
        }

        Assert.False(TrayRevealPolicy.ShouldAttemptReveal(
            TimeSpan.Zero,
            hasSnapshot: false,
            consecutiveRevealMisses: 0,
            sinceLastRevealMiss: TimeSpan.Zero,
            emptyMirrorBypassUsed: TrayRevealPolicy.EmptyMirrorBypassAttempts));
    }

    [Fact]
    public void EmptyMirror_BypassExpiry_StillRevealsOnceTheUserIsIdle()
    {
        Assert.True(TrayRevealPolicy.ShouldAttemptReveal(
            TrayRevealPolicy.MinUserIdle,
            hasSnapshot: false,
            consecutiveRevealMisses: 0,
            sinceLastRevealMiss: TimeSpan.Zero,
            emptyMirrorBypassUsed: TrayRevealPolicy.EmptyMirrorBypassAttempts));
    }

    [Fact]
    public void QuietReveal_BacksOffAfterTwoMisses_ThenRetriesAfterTheBackoff()
    {
        Assert.False(TrayRevealPolicy.IsRevealBackedOff(consecutiveRevealMisses: 1, TimeSpan.Zero));

        Assert.True(TrayRevealPolicy.IsRevealBackedOff(consecutiveRevealMisses: 2, TimeSpan.Zero));
        Assert.True(TrayRevealPolicy.IsRevealBackedOff(
            consecutiveRevealMisses: 2,
            TrayRevealPolicy.RevealBackoff - TimeSpan.FromSeconds(1)));
        Assert.False(TrayRevealPolicy.IsRevealBackedOff(
            consecutiveRevealMisses: 2,
            TrayRevealPolicy.RevealBackoff));

        // A backed-off reveal skips even a long-idle user (the hover gesture is broken here).
        Assert.False(TrayRevealPolicy.ShouldAttemptReveal(
            TimeSpan.FromMinutes(30),
            hasSnapshot: true,
            consecutiveRevealMisses: 2,
            sinceLastRevealMiss: TimeSpan.FromMinutes(5),
            emptyMirrorBypassUsed: 0));

        // ...and an empty mirror does not override the backoff either.
        Assert.False(TrayRevealPolicy.ShouldAttemptReveal(
            TimeSpan.Zero,
            hasSnapshot: false,
            consecutiveRevealMisses: 2,
            sinceLastRevealMiss: TimeSpan.FromMinutes(5),
            emptyMirrorBypassUsed: 0));
    }

    [Fact]
    public void OneMiss_DoesNotBackOff_TwoInARowDo()
    {
        Assert.False(TrayRevealPolicy.IsRevealBackedOff(consecutiveRevealMisses: 0, TimeSpan.Zero));
        Assert.False(TrayRevealPolicy.IsRevealBackedOff(consecutiveRevealMisses: 1, TimeSpan.Zero));
        Assert.True(TrayRevealPolicy.IsRevealBackedOff(consecutiveRevealMisses: 2, TimeSpan.Zero));
    }
}
