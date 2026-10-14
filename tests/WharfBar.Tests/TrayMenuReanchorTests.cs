using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class TrayMenuReanchorTests
{
    private static RECT Rect(int left, int top, int right, int bottom) => new()
    {
        Left = left,
        Top = top,
        Right = right,
        Bottom = bottom,
    };

    private static RECT Monitor1920x1080 => Rect(0, 0, 1920, 1080);

    // Click zone for a button at 1647..1679 on the bottom edge (EarTrumpet's slot).
    private static RECT EarTrumpetButton => Rect(1647, 1032, 1679, 1080);

    private static RECT ZoneFor(RECT button, RECT monitor) =>
        TrayMenuReanchor.ComputeClickZone(button, monitor);

    // --- IsMenuCandidate ---

    [Fact]
    public void IsMenuCandidate_MenuClassIsAlwaysCandidateEvenAtScreenTop()
    {
        // AMD case: Win32 menu anchored at y=0, top edge — far from the click zone.
        bool candidate = TrayMenuReanchor.IsMenuCandidate(
            "#32768", 335, 152, cloaked: false,
            Rect(1461, 0, 1796, 152), Monitor1920x1080, ZoneFor(EarTrumpetButton, Monitor1920x1080));
        Assert.True(candidate);
    }

    [Fact]
    public void IsMenuCandidate_CloakedWindowRejected() =>
        Assert.False(TrayMenuReanchor.IsMenuCandidate(
            "#32768", 335, 152, cloaked: true,
            Rect(1461, 0, 1796, 152), Monitor1920x1080, ZoneFor(EarTrumpetButton, Monitor1920x1080)));

    [Theory]
    [InlineData("CNEventWindowClass", 136, 39)]
    [InlineData("SomeTooltip", 60, 20)]
    public void IsMenuCandidate_TinyHelperWindowRejected(string className, int width, int height) =>
        Assert.False(TrayMenuReanchor.IsMenuCandidate(
            className, width, height, cloaked: false,
            Rect(182, 182, 318, 221), Monitor1920x1080, ZoneFor(EarTrumpetButton, Monitor1920x1080)));

    [Fact]
    public void IsMenuCandidate_AppFlyoutNearClickedButtonAccepted()
    {
        // EarTrumpet case: WPF flyout at bottom-right corner, same slot as the clicked button.
        RECT zone = ZoneFor(EarTrumpetButton, Monitor1920x1080);
        bool candidate = TrayMenuReanchor.IsMenuCandidate(
            "HwndWrapper[EarTrumpet.exe;;dae9f409]", 348, 361, cloaked: false,
            Rect(1572, 695, 1920, 1056), Monitor1920x1080, zone);
        Assert.True(candidate);
    }

    [Fact]
    public void IsMenuCandidate_AppWindowOutsideClickZoneRejected()
    {
        // A regular app window in the middle of the screen must never be grabbed.
        RECT zone = ZoneFor(EarTrumpetButton, Monitor1920x1080);
        bool candidate = TrayMenuReanchor.IsMenuCandidate(
            "HwndWrapper[SomeApp;;...]", 348, 361, cloaked: false,
            Rect(400, 300, 748, 661), Monitor1920x1080, zone);
        Assert.False(candidate);
    }

    [Fact]
    public void IsMenuCandidate_FullScreenOverlayRejected()
    {
        // IME hosts / overlays cover the whole monitor — never grab those.
        RECT zone = ZoneFor(EarTrumpetButton, Monitor1920x1080);
        bool candidate = TrayMenuReanchor.IsMenuCandidate(
            "Windows.UI.Core.CoreWindow", 1920, 1080, cloaked: false,
            Rect(0, 0, 1920, 1080), Monitor1920x1080, zone);
        Assert.False(candidate);
    }

    [Fact]
    public void IsMenuCandidate_FlyoutOfAnotherSlotRejected()
    {
        // A popup anchored to a DIFFERENT tray button (far left) is out of this click zone.
        RECT zone = ZoneFor(EarTrumpetButton, Monitor1920x1080);
        bool candidate = TrayMenuReanchor.IsMenuCandidate(
            "HwndWrapper[OtherApp;;...]", 348, 361, cloaked: false,
            Rect(100, 695, 448, 1056), Monitor1920x1080, zone);
        Assert.False(candidate);
    }

    // --- ComputeClickZone ---

    [Fact]
    public void ComputeClickZone_ExpandsButtonIntoBottomBand()
    {
        RECT zone = TrayMenuReanchor.ComputeClickZone(Rect(1647, 1032, 1679, 1080), Monitor1920x1080);
        Assert.True(zone.Left <= 1647 - 300);
        Assert.True(zone.Right >= 1679 + 300);
        Assert.Equal(1080, zone.Bottom);
        Assert.True(zone.Top <= 1080 - 440);
    }

    // --- ComputeAnchoredPosition ---

    [Fact]
    public void ComputeAnchoredPosition_CentersMenuBelowAnchor()
    {
        POINT pos = TrayMenuReanchor.ComputeAnchoredPosition(
            Rect(666, 34, 966, 234), Rect(800, 0, 832, 30), Rect(0, 0, 1920, 1080));
        Assert.Equal(666, pos.X);
        Assert.Equal(34, pos.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_ClampsToLeftWorkAreaEdge()
    {
        POINT pos = TrayMenuReanchor.ComputeAnchoredPosition(
            Rect(0, 34, 300, 234), Rect(0, 0, 32, 30), Rect(0, 0, 1920, 1080));
        Assert.Equal(0, pos.X);
        Assert.Equal(34, pos.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_ClampsToRightWorkAreaEdge()
    {
        POINT pos = TrayMenuReanchor.ComputeAnchoredPosition(
            Rect(1616, 34, 2016, 234), Rect(1800, 0, 1832, 30), Rect(0, 0, 1920, 1080));
        Assert.Equal(1520, pos.X);
    }

    [Fact]
    public void ComputeAnchoredPosition_ClampsToWorkAreaTopWhenMenuTallerThanArea()
    {
        POINT pos = TrayMenuReanchor.ComputeAnchoredPosition(
            Rect(0, 34, 300, 2034), Rect(800, 0, 832, 30), Rect(0, 0, 1920, 1040));
        Assert.Equal(0, pos.Y);
    }

    [Fact]
    public void ComputeAnchoredPosition_RespectsDockReservedBottom()
    {
        // Menu bottom must stay above the reserved strip.
        POINT pos = TrayMenuReanchor.ComputeAnchoredPosition(
            Rect(666, 34, 966, 1034), Rect(800, 0, 832, 30), Rect(0, 0, 1920, 1040));
        Assert.Equal(34, pos.Y);
        Assert.Equal(666, pos.X);
    }

    [Fact]
    public void ShouldDetachPopupFromTray_OnlyWhenTheTrayOwnsIt()
    {
        var tray = new IntPtr(100);
        Assert.True(TrayMenuReanchor.ShouldDetachPopupFromTray(tray, tray));
        Assert.False(TrayMenuReanchor.ShouldDetachPopupFromTray(new IntPtr(200), tray));
        Assert.False(TrayMenuReanchor.ShouldDetachPopupFromTray(IntPtr.Zero, tray));
        Assert.False(TrayMenuReanchor.ShouldDetachPopupFromTray(tray, IntPtr.Zero));
    }
}
