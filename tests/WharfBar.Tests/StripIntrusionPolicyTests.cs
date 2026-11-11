using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class StripIntrusionPolicyTests
{
    private static RECT Box(int left, int top, int right, int bottom)
        => new() { Left = left, Top = top, Right = right, Bottom = bottom };

    [Fact]
    public void WindowDraggedUnderTheBar_ClampsToWorkAreaTop()
    {
        // 1920x1080 monitor, bar reserves the top 30 px; the window's top slid to y=12.
        int? top = StripIntrusionPolicy.ClampTop(
            Box(100, 12, 900, 600),
            Box(0, 0, 1920, 1080),
            Box(0, 30, 1920, 1025));

        Assert.Equal(30, top);
    }

    [Fact]
    public void ClearWindow_IsLeftAlone()
    {
        Assert.Null(StripIntrusionPolicy.ClampTop(
            Box(100, 200, 900, 800),
            Box(0, 0, 1920, 1080),
            Box(0, 30, 1920, 1025)));
    }

    [Fact]
    public void WindowTopOnTheWorkAreaEdge_IsClear()
    {
        Assert.Null(StripIntrusionPolicy.ClampTop(
            Box(100, 30, 900, 800),
            Box(0, 0, 1920, 1080),
            Box(0, 30, 1920, 1025)));
    }

    [Fact]
    public void FullscreenWindow_IsLeftAlone()
    {
        // A fullscreen app owns the monitor (the bar hides itself via ABN_FULLSCREENAPP).
        Assert.Null(StripIntrusionPolicy.ClampTop(
            Box(0, 0, 1920, 1080),
            Box(0, 0, 1920, 1080),
            Box(0, 30, 1920, 1025)));
    }

    [Fact]
    public void MonitorWithoutATopBar_HasNoStrip()
    {
        Assert.Null(StripIntrusionPolicy.ClampTop(
            Box(100, 5, 900, 600),
            Box(0, 0, 1920, 1080),
            Box(0, 0, 1920, 1080)));
    }

    [Fact]
    public void WindowDraggedAboveTheScreen_IsLeftAlone()
    {
        // Nothing of it is drawn in the strip anyway.
        Assert.Null(StripIntrusionPolicy.ClampTop(
            Box(100, -400, 900, -40),
            Box(0, 0, 1920, 1080),
            Box(0, 30, 1920, 1025)));
    }

    [Fact]
    public void WindowEntirelyInsideTheStrip_Clamps()
    {
        int? top = StripIntrusionPolicy.ClampTop(
            Box(100, 2, 400, 20),
            Box(0, 0, 1920, 1080),
            Box(0, 30, 1920, 1025));

        Assert.Equal(30, top);
    }

    [Fact]
    public void SecondaryMonitorStrip_UsesThatMonitorsWorkArea()
    {
        // A second monitor to the right, its own reserved 30 px strip.
        int? top = StripIntrusionPolicy.ClampTop(
            Box(2500, 1085, 3200, 1500),
            Box(1920, 1080, 3840, 2160),
            Box(1920, 1110, 3840, 2105));

        Assert.Equal(1110, top);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]      // exactly the monitor
    [InlineData(-10, -10, 1930, 1090, true)]  // covers it, hangs over
    [InlineData(0, 0, 1920, 1079, false)]     // one row short
    [InlineData(1, 0, 1920, 1080, false)]     // one column in
    public void IsFullscreen_MatchesCoveringWindows(int left, int top, int right, int bottom, bool expected)
    {
        Assert.Equal(
            expected,
            StripIntrusionPolicy.IsFullscreen(
                Box(left, top, right, bottom),
                Box(0, 0, 1920, 1080)));
    }
}
