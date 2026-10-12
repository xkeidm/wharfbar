using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockAttentionTests
{
    [Fact]
    public void Start_PulsesUntilTheWindowEnds()
    {
        var tracker = new DockAttentionTracker();
        tracker.StartOrRestart("notepad", 1_000);
        Assert.True(tracker.IsPulsing("notepad", 1_000));
        Assert.True(tracker.IsPulsing("notepad", 1_000 + DockAttentionTracker.PulseMs - 1));
        Assert.False(tracker.IsPulsing("notepad", 1_000 + DockAttentionTracker.PulseMs));
    }

    [Fact]
    public void Restart_ExtendsTheSamePulse()
    {
        var tracker = new DockAttentionTracker();
        tracker.StartOrRestart("notepad", 0);
        tracker.StartOrRestart("notepad", 1_000);
        Assert.Equal(1_000 + DockAttentionTracker.PulseMs, tracker.Until("notepad"));
        Assert.True(tracker.IsPulsing("notepad", 1_000 + DockAttentionTracker.PulseMs - 1));
        Assert.False(tracker.IsPulsing("notepad", 1_000 + DockAttentionTracker.PulseMs));
    }

    [Fact]
    public void Expire_DropsAFinishedPulse()
    {
        var tracker = new DockAttentionTracker();
        tracker.StartOrRestart("notepad", 0);
        tracker.Expire(DockAttentionTracker.PulseMs);
        Assert.False(tracker.IsPulsing("notepad", DockAttentionTracker.PulseMs));
        Assert.Equal(0, tracker.Until("notepad"));
    }

    [Fact]
    public void Clear_StopsAPulseImmediately()
    {
        var tracker = new DockAttentionTracker();
        tracker.StartOrRestart("notepad", 50);
        tracker.Clear("notepad");
        Assert.False(tracker.IsPulsing("notepad", 50));
    }

    [Fact]
    public void ShouldNotice_IgnoresWharfBarAndAZeroPid()
    {
        Assert.False(DockAttentionTracker.ShouldNotice(0, 10));
        Assert.False(DockAttentionTracker.ShouldNotice(10, 10));
        Assert.True(DockAttentionTracker.ShouldNotice(11, 10));
    }
}
