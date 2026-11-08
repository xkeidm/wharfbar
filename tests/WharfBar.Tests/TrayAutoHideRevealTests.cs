using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

/// <summary>Pure geometry behind the hover reveal and the state-blip window check.</summary>
public class TrayAutoHideRevealTests
{
    private static readonly RECT Monitor = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

    private static RECT Rect(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    [Fact]
    public void ParkedAutoHideTray_IsDetected()
    {
        // Diag log: parked tray sits at y=1078 with a 48 px height.
        RECT parked = Rect(0, 1078, 1920, 1126);

        Assert.True(TrayAutoHideReveal.IsParked(parked, Monitor));
        Assert.False(TrayAutoHideReveal.IsFullyOnScreen(parked, Monitor));
    }

    [Fact]
    public void RevealedTray_IsFullyOnScreen()
    {
        RECT revealed = Rect(0, 1032, 1920, 1080);

        Assert.False(TrayAutoHideReveal.IsParked(revealed, Monitor));
        Assert.True(TrayAutoHideReveal.IsFullyOnScreen(revealed, Monitor));
    }

    [Fact]
    public void HalfwaySlide_IsNeitherParkedNorRevealed()
    {
        RECT sliding = Rect(0, 1056, 1920, 1104);

        Assert.False(TrayAutoHideReveal.IsParked(sliding, Monitor));
        Assert.False(TrayAutoHideReveal.IsFullyOnScreen(sliding, Monitor));
    }

    [Fact]
    public void MaximizedWindow_IsAlwaysAffected()
    {
        Assert.True(TrayAutoHideReveal.IsAffectedByBottomReservation(Rect(100, 100, 300, 300), Monitor, 72, maximized: true));
    }

    [Fact]
    public void WindowReachingTheBottomStrip_IsAffected_OneAboveItIsNot()
    {
        Assert.True(TrayAutoHideReveal.IsAffectedByBottomReservation(Rect(0, 40, 960, 1080), Monitor, 72, maximized: false));
        Assert.False(TrayAutoHideReveal.IsAffectedByBottomReservation(Rect(200, 200, 900, 800), Monitor, 72, maximized: false));
    }

    [Fact]
    public void WindowOnAnotherMonitor_IsNotAffected()
    {
        Assert.False(TrayAutoHideReveal.IsAffectedByBottomReservation(Rect(1920, 40, 3840, 1080), Monitor, 72, maximized: false));
    }

    [Fact]
    public void CursorLease_RestoresOnlyWhileThePointerStillSitsWhereTheRevealParkedIt()
    {
        // A reveal parks the pointer at the bottom edge; the lease may put it back only while it
        // is still there. A pointer the user moved in the meantime is left alone. A one-pixel
        // tolerance absorbs the absolute-input quantisation round trip.
        Assert.True(SendInputMouse.CursorStillParked(cursorX: 1700, cursorY: 1079, parkedX: 1700, parkedY: 1079));
        Assert.True(SendInputMouse.CursorStillParked(cursorX: 1701, cursorY: 1078, parkedX: 1700, parkedY: 1079));
        Assert.False(SendInputMouse.CursorStillParked(cursorX: 1700, cursorY: 500, parkedX: 1700, parkedY: 1079));
        Assert.False(SendInputMouse.CursorStillParked(cursorX: 100, cursorY: 1079, parkedX: 1700, parkedY: 1079));
    }
}
