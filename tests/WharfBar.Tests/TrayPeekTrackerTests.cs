using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayPeekTrackerTests
{
    private static readonly TimeSpan Max = TimeSpan.FromSeconds(45);
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FreshPeek_IsActiveButNotStale_BeforeDeadline()
    {
        var tracker = new TrayPeekTracker(Max);
        tracker.Begin(T0);

        Assert.True(tracker.IsActive);
        Assert.False(tracker.IsStale(T0 + TimeSpan.FromSeconds(44)));
    }

    [Fact]
    public void Peek_IsStale_AfterDeadline()
    {
        var tracker = new TrayPeekTracker(Max);
        tracker.Begin(T0);

        Assert.True(tracker.IsStale(T0 + Max));
    }

    [Fact]
    public void End_ClearsPeek_SoItIsNoLongerStale()
    {
        var tracker = new TrayPeekTracker(Max);
        tracker.Begin(T0);
        tracker.End();

        Assert.False(tracker.IsActive);
        Assert.False(tracker.IsStale(T0 + Max));
    }

    [Fact]
    public void End_WhenNested_RefcountsUntilLastPeekEnds()
    {
        var tracker = new TrayPeekTracker(Max);
        tracker.Begin(T0);
        tracker.Begin(T0 + TimeSpan.FromSeconds(1));

        Assert.False(tracker.End()); // still one nested peek open
        Assert.True(tracker.IsActive);

        Assert.True(tracker.End()); // last one closed
        Assert.False(tracker.IsActive);
    }

    [Fact]
    public void Reset_ForceEnds_EvenWhenNested()
    {
        var tracker = new TrayPeekTracker(Max);
        tracker.Begin(T0);
        tracker.Begin(T0 + TimeSpan.FromSeconds(1));
        tracker.Reset();

        Assert.False(tracker.IsActive);
        Assert.False(tracker.IsStale(T0 + Max));
    }

    [Fact]
    public void Begin_AfterEnd_RestartsDeadlineWindow()
    {
        var tracker = new TrayPeekTracker(Max);
        tracker.Begin(T0);
        tracker.End();

        DateTime later = T0 + TimeSpan.FromSeconds(30);
        tracker.Begin(later);

        Assert.False(tracker.IsStale(later + TimeSpan.FromSeconds(30))); // inside new window
        Assert.True(tracker.IsStale(later + Max));
    }
}