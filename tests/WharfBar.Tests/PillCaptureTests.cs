using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class PillCaptureTests
{
    [Fact]
    public void ShouldPark_OnlyTheLastClipPreview()
    {
        Assert.True(PillCapture.ShouldPark(IslandPillFace.Idle, pillShown: true, "kopya", lastClipIdle: true));
    }

    [Fact]
    public void ShouldPark_LeavesTheBarWhenThePillIsALabelOrMeter()
    {
        Assert.False(PillCapture.ShouldPark(IslandPillFace.Idle, true, "Pano", lastClipIdle: false));
        Assert.False(PillCapture.ShouldPark(IslandPillFace.NowPlaying, true, "Parça", lastClipIdle: true));
        Assert.False(PillCapture.ShouldPark(IslandPillFace.Volume, true, "kopya", lastClipIdle: true));
        Assert.False(PillCapture.ShouldPark(IslandPillFace.Idle, false, "kopya", lastClipIdle: true));
        Assert.False(PillCapture.ShouldPark(IslandPillFace.Idle, true, "  ", lastClipIdle: true));
    }
}
