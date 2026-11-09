using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Which popup a mirrored tray click may re-anchor, and how concurrent watches share it.
/// 2026-11-09 diag log: a pure geometric match moved ordinary framed app windows to y=33
/// (a tall app frame and an app flyout can have the same bottom-edge rect band), and two
/// overlapping clicks' watchers moved the same <c>#32768</c> menu to 1375,33 and 1345,33 in
/// turns. The classifier therefore reads window class and style bits, and the gate serializes
/// the watchers and gives each moved hwnd exactly one owner.
/// </summary>
public class TrayMenuReanchorTests
{
    // Documented Win32 style bits, spelled out here so the tests do not depend on the
    // implementation's constants.
    private const int PopupStyle = unchecked((int)0x80000000);       // WS_POPUP
    private const int CaptionStyle = 0x00C00000;                     // WS_CAPTION
    private const int ThickFrameStyle = 0x00040000;                  // WS_THICKFRAME
    private const int OverlappedWindowStyle = unchecked((int)0x00CF0000); // WS_OVERLAPPEDWINDOW
    private const int AppWindowExStyle = 0x00040000;                 // WS_EX_APPWINDOW
    private const int ToolWindowExStyle = 0x00000080;                // WS_EX_TOOLWINDOW
    private const uint OtherPid = 4242;

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

    // Click zone for the Monitorian slot (button rect seen in the 2026-11-09 diag log).
    private static RECT MonitorianButton => Rect(1325, 1032, 1357, 1080);

    private static RECT ZoneFor(RECT button, RECT monitor) =>
        TrayMenuReanchor.ComputeClickZone(button, monitor);

    private static CandidateWindow Window(
        string className,
        RECT rect,
        int style = PopupStyle,
        int exStyle = 0,
        uint processId = OtherPid,
        bool visible = true,
        bool cloaked = false,
        bool hung = false) =>
        new(className, style, exStyle, processId, visible, cloaked, hung, rect);

    private static bool IsCandidate(CandidateWindow window, RECT button, uint ownPid = 999) =>
        TrayMenuReanchor.IsReanchorCandidate(
            window, ownPid, Monitor1920x1080, ZoneFor(button, Monitor1920x1080));

    // --- IsReanchorCandidate: the #32768 menu class ---

    [Fact]
    public void MenuClass_IsAcceptedEvenAtTheTopOfTheScreen()
    {
        // AMD case: a Win32 menu anchored at y=0, top edge — far from the click zone.
        Assert.True(IsCandidate(
            Window("#32768", Rect(1461, 0, 1796, 152), style: 0),
            EarTrumpetButton));
    }

    [Fact]
    public void MenuClass_IsAcceptedRegardlessOfStyleBits()
    {
        // The owner's TrackPopupMenu window is the target by class; a menu never needs the
        // popup-style proof (some owners create it with unusual style bits).
        Assert.True(IsCandidate(
            Window("#32768", Rect(1461, 0, 1796, 152), style: OverlappedWindowStyle),
            EarTrumpetButton));
    }

    [Fact]
    public void MenuClass_IsAcceptedEvenBelowTheMinimumFlyoutSize()
    {
        // A one-item tray menu can be shorter than the flyout minimum; the class match wins
        // (this is what "always accept #32768" means — the old size filter timed out on it).
        Assert.True(IsCandidate(
            Window("#32768", Rect(1500, 1000, 1620, 1040), style: 0),
            EarTrumpetButton));
    }

    [Fact]
    public void MenuClass_CloakedWindowRejected() =>
        Assert.False(IsCandidate(
            Window("#32768", Rect(1461, 0, 1796, 152), cloaked: true),
            EarTrumpetButton));

    [Fact]
    public void MenuClass_HungWindowRejected() =>
        Assert.False(IsCandidate(
            Window("#32768", Rect(1461, 0, 1796, 152), hung: true),
            EarTrumpetButton));

    [Fact]
    public void MenuClass_InvisibleWindowRejected() =>
        Assert.False(IsCandidate(
            Window("#32768", Rect(1461, 0, 1796, 152), visible: false),
            EarTrumpetButton));

    // --- IsReanchorCandidate: structural exclusions ---

    [Fact]
    public void OwnProcessWindow_Rejected()
    {
        // WharfBar's own popups (e.g. the island flyout) must never be grabbed.
        Assert.False(IsCandidate(
            Window("HwndWrapper[WharfBar.exe;;...]", Rect(1572, 695, 1920, 1056), processId: 999),
            EarTrumpetButton,
            ownPid: 999));
    }

    [Fact]
    public void FullScreenOverlay_Rejected()
    {
        // IME hosts / vendor overlays / borderless games cover the whole monitor.
        Assert.False(IsCandidate(
            Window("Windows.UI.Core.CoreWindow", Rect(0, 0, 1920, 1080)),
            EarTrumpetButton));
    }

    [Theory]
    [InlineData("CNEventWindowClass", 136, 39)]
    [InlineData("SomeTooltip", 60, 20)]
    [InlineData("Xaml_WindowedPopupClass", 129, 41)]
    public void TinyPopupHelperWindow_Rejected(string className, int width, int height)
    {
        // XAML popup hosts / tooltips are not the app flyout; only a real flyout size can be.
        // Placed INSIDE the click zone so the size rule (not the zone) is what rejects them.
        RECT rect = Rect(1560, 970, 1560 + width, 970 + height);
        Assert.False(IsCandidate(Window(className, rect), EarTrumpetButton));
    }

    // --- IsReanchorCandidate: app flyouts vs normal app windows ---

    [Fact]
    public void AppFlyoutNearClickedButton_Accepted()
    {
        // EarTrumpet case: a real WPF flyout (WS_POPUP, no caption/frame) at the bottom-right
        // corner, in the same slot as the clicked button.
        Assert.True(IsCandidate(
            Window("HwndWrapper[EarTrumpet.exe;;dae9f409]", Rect(1572, 695, 1920, 1056),
                exStyle: ToolWindowExStyle),
            EarTrumpetButton));
    }

    [Fact]
    public void BorderlessTallFlyoutOverTheZone_Accepted()
    {
        // 2026-11-09 diag: MonitorianPlus flyout rect=999,524,1276,1055. The same rect can also
        // belong to an app frame; the popup style is what separates the two.
        Assert.True(IsCandidate(
            Window("HwndWrapper[MonitorianPlus.exe;;9835a111]", Rect(999, 524, 1276, 1055)),
            MonitorianButton));
    }

    [Fact]
    public void FramedAppWindowOverlappingTheZone_IsNeverMoved()
    {
        // The 2026-11-09 regression: the same rect band as the flyout above, but a normal framed
        // app window. It overlaps the click zone and is freshly visible, yet it must stay put.
        Assert.False(IsCandidate(
            Window("HwndWrapper[MonitorianPlus.exe;;9835a111]", Rect(999, 524, 1276, 1055),
                style: OverlappedWindowStyle),
            MonitorianButton));
    }

    [Fact]
    public void FramedBrowserWindowOverlappingTheZone_IsNeverMoved()
    {
        // Another log case: Chrome_WidgetWin_1 rect=1349,947,1568,1092 was moved to y=33. A
        // framed CEF/browser window is an app window, not the tray flyout.
        Assert.False(IsCandidate(
            Window("Chrome_WidgetWin_1", Rect(1349, 947, 1568, 1092), style: OverlappedWindowStyle),
            EarTrumpetButton));
    }

    [Theory]
    [InlineData(CaptionStyle)]
    [InlineData(ThickFrameStyle)]
    [InlineData(CaptionStyle | ThickFrameStyle)]
    public void PopupWithCaptionOrResizeFrame_Rejected(int frameBits)
    {
        // WS_POPUP alone is not proof of a popup: dialogs and custom frames also set it.
        Assert.False(IsCandidate(
            Window("SomeDialog", Rect(1572, 695, 1920, 1056), style: PopupStyle | frameBits),
            EarTrumpetButton));
    }

    [Fact]
    public void PopupMarkedAsTaskbarWindow_Rejected()
    {
        // WS_EX_APPWINDOW means "this is an app's main window"; a click-triggered popup is not.
        Assert.False(IsCandidate(
            Window("SomeBorderlessAppWindow", Rect(1572, 695, 1920, 1056), exStyle: AppWindowExStyle),
            EarTrumpetButton));
    }

    [Fact]
    public void PopupOutsideClickZone_Rejected()
    {
        // A regular borderless window in the middle of the screen must never be grabbed.
        Assert.False(IsCandidate(
            Window("HwndWrapper[SomeApp;;...]", Rect(400, 300, 748, 661)),
            EarTrumpetButton));
    }

    [Fact]
    public void PopupOfAnotherSlot_Rejected()
    {
        // A popup anchored to a DIFFERENT tray button (far left) is out of this click zone.
        Assert.False(IsCandidate(
            Window("HwndWrapper[OtherApp;;...]", Rect(100, 695, 448, 1056)),
            EarTrumpetButton));
    }

    [Fact]
    public void IsRealPopupStyle_OnlyAcceptsBarePopups()
    {
        Assert.True(TrayMenuReanchor.IsRealPopupStyle(PopupStyle, 0));
        Assert.True(TrayMenuReanchor.IsRealPopupStyle(PopupStyle, ToolWindowExStyle));

        Assert.False(TrayMenuReanchor.IsRealPopupStyle(OverlappedWindowStyle, 0));
        Assert.False(TrayMenuReanchor.IsRealPopupStyle(0, 0));
        Assert.False(TrayMenuReanchor.IsRealPopupStyle(PopupStyle | CaptionStyle, 0));
        Assert.False(TrayMenuReanchor.IsRealPopupStyle(PopupStyle | ThickFrameStyle, 0));
        Assert.False(TrayMenuReanchor.IsRealPopupStyle(PopupStyle, AppWindowExStyle));
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

    // --- ShouldReassert ---

    [Fact]
    public void Reassert_WhenTheOwnerMovesThePopupBack()
    {
        var target = new POINT { X = 1500, Y = 33 };

        // EarTrumpet-style: the flyout snaps back to its bottom-edge slot after our move.
        Assert.True(TrayMenuReanchor.ShouldReassert(Rect(1500, 700, 1860, 1032), target, TrayMenuReanchor.ReassertTolerancePx));
    }

    [Fact]
    public void NoReassert_ForSmallDrift_OrAClosedWindow()
    {
        var target = new POINT { X = 1500, Y = 33 };

        Assert.False(TrayMenuReanchor.ShouldReassert(Rect(1504, 30, 1864, 360), target, TrayMenuReanchor.ReassertTolerancePx));
        Assert.False(TrayMenuReanchor.ShouldReassert(default, target, TrayMenuReanchor.ReassertTolerancePx));
    }

    // --- ShouldContinueReassert ---

    [Fact]
    public void Reassert_ContinuesWhileTheWindowIsAliveAndOwned()
    {
        Assert.True(TrayMenuReanchor.ShouldContinueReassert(
            windowAlive: true, ownsWindow: true, moves: 0, elapsedMs: 0));
        Assert.True(TrayMenuReanchor.ShouldContinueReassert(
            windowAlive: true, ownsWindow: true,
            moves: TrayMenuReanchor.ReassertMaxMoves - 1,
            elapsedMs: TrayMenuReanchor.ReassertWindowMs - 1));
    }

    [Fact]
    public void Reassert_StopsAsSoonAsAnotherWatcherOwnsTheWindow()
    {
        // Diag log: two overlapping clicks' watchers fought over hwnd=1247708. Once a newer watch
        // claims the hwnd, the older loop must stop instead of moving it back.
        Assert.False(TrayMenuReanchor.ShouldContinueReassert(
            windowAlive: true, ownsWindow: false, moves: 0, elapsedMs: 0));
    }

    [Fact]
    public void Reassert_StopsWhenTheWindowWasClosed()
    {
        Assert.False(TrayMenuReanchor.ShouldContinueReassert(
            windowAlive: false, ownsWindow: true, moves: 0, elapsedMs: 0));
    }

    [Fact]
    public void Reassert_StopsAtTheMoveOrTimeBudget()
    {
        Assert.False(TrayMenuReanchor.ShouldContinueReassert(
            windowAlive: true, ownsWindow: true,
            moves: TrayMenuReanchor.ReassertMaxMoves, elapsedMs: 0));
        Assert.False(TrayMenuReanchor.ShouldContinueReassert(
            windowAlive: true, ownsWindow: true,
            moves: 0, elapsedMs: TrayMenuReanchor.ReassertWindowMs));
    }

    [Fact]
    public void Reassert_IsBoundedTo1500MsFourMovesAnd8PxTolerance()
    {
        Assert.Equal(1500, TrayMenuReanchor.ReassertWindowMs);
        Assert.Equal(4, TrayMenuReanchor.ReassertMaxMoves);
        Assert.Equal(8, TrayMenuReanchor.ReassertTolerancePx);
    }

    [Fact]
    public void WatchWindow_OutlastsColdOwners()
    {
        // Diag log: Phone Link's popup timed out under the old 1 s budget.
        Assert.True(TrayMenuReanchor.WatchTimeoutMs >= 2000);
    }
}

/// <summary>
/// The process-wide single-watcher gate. One discovery watch at a time, and one owning watch per
/// moved hwnd: a second click's watch may supersede an older claim (the newest click's anchor
/// wins) but the older reassert loop then stops. This replaces the two competing watches that
/// moved the same #32768 menu to 1375,33 and 1345,33 every 30–60 ms.
/// </summary>
public class ReanchorWatchGateTests
{
    private static IntPtr Hwnd(long value) => new(value);

    [Fact]
    public void OnlyOneDiscoveryWatch_RunsAtATime()
    {
        var gate = new ReanchorWatchGate();
        long first = gate.TryBeginWatch();

        Assert.NotEqual(0, first);
        Assert.True(gate.IsWatching(first));
        Assert.Equal(0, gate.TryBeginWatch()); // rejected: no competing watch

        gate.EndWatch(first);
        Assert.False(gate.IsWatching(first));

        long second = gate.TryBeginWatch();
        Assert.NotEqual(0, second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AStaleWatch_CannotReleaseTheDiscoverySlotOfItsSuccessor()
    {
        var gate = new ReanchorWatchGate();
        long first = gate.TryBeginWatch();
        gate.EndWatch(first);

        long second = gate.TryBeginWatch();
        gate.EndWatch(first); // the old attempt's finally block runs late

        Assert.True(gate.IsWatching(second));
        Assert.Equal(0, gate.TryBeginWatch());
    }

    [Fact]
    public void AReassertClaim_OutlivesTheDiscoverySlot()
    {
        var gate = new ReanchorWatchGate();
        var hwnd = Hwnd(1247708);
        long watch = gate.TryBeginWatch();

        Assert.True(gate.TryClaimWindow(hwnd, watch));
        gate.EndWatch(watch); // the move is done, the discovery slot is free again

        Assert.True(gate.OwnsWindow(hwnd, watch));
        Assert.Equal(1, gate.HeldWindowCount);

        gate.ReleaseWindow(hwnd, watch);
        Assert.False(gate.OwnsWindow(hwnd, watch));
        Assert.Equal(0, gate.HeldWindowCount);
    }

    [Fact]
    public void ANewerWatch_SupersedesAnOlderClaim_SoTheOlderReassertStops()
    {
        var gate = new ReanchorWatchGate();
        var hwnd = Hwnd(1247708);

        long older = gate.TryBeginWatch();
        Assert.True(gate.TryClaimWindow(hwnd, older));
        gate.EndWatch(older); // older reassert loop is still running

        long newer = gate.TryBeginWatch();
        Assert.True(gate.TryClaimWindow(hwnd, newer));

        Assert.False(gate.OwnsWindow(hwnd, older)); // the older loop checks this and stops
        Assert.True(gate.OwnsWindow(hwnd, newer));
    }

    [Fact]
    public void AnOlderWatch_NeverTakesAWindowFromANewerOne()
    {
        var gate = new ReanchorWatchGate();
        var hwnd = Hwnd(1247708);

        long older = gate.TryBeginWatch();
        gate.EndWatch(older);
        long newer = gate.TryBeginWatch();
        Assert.True(gate.TryClaimWindow(hwnd, newer));

        Assert.False(gate.TryClaimWindow(hwnd, older));
        Assert.True(gate.OwnsWindow(hwnd, newer));
    }

    [Fact]
    public void ReleasingAWindow_RequiresTheOwningWatch()
    {
        var gate = new ReanchorWatchGate();
        var hwnd = Hwnd(1247708);

        long older = gate.TryBeginWatch();
        gate.EndWatch(older);
        long newer = gate.TryBeginWatch();
        Assert.True(gate.TryClaimWindow(hwnd, older));
        Assert.True(gate.TryClaimWindow(hwnd, newer)); // newer supersedes

        gate.ReleaseWindow(hwnd, older); // the older loop's finally block runs late
        Assert.True(gate.OwnsWindow(hwnd, newer));

        gate.ReleaseWindow(hwnd, newer);
        Assert.False(gate.OwnsWindow(hwnd, newer));
        Assert.Equal(0, gate.HeldWindowCount);
    }

    [Fact]
    public void ClaimingTheSameWindowTwice_ByTheSameWatch_IsIdempotent()
    {
        var gate = new ReanchorWatchGate();
        var hwnd = Hwnd(1247708);
        long watch = gate.TryBeginWatch();

        Assert.True(gate.TryClaimWindow(hwnd, watch));
        Assert.True(gate.TryClaimWindow(hwnd, watch));
        Assert.Equal(1, gate.HeldWindowCount);
    }

    [Fact]
    public void DifferentWindows_AreClaimedIndependently()
    {
        var gate = new ReanchorWatchGate();
        var first = Hwnd(100);
        var second = Hwnd(200);
        long watch = gate.TryBeginWatch();

        Assert.True(gate.TryClaimWindow(first, watch));
        Assert.True(gate.TryClaimWindow(second, watch));
        Assert.True(gate.OwnsWindow(first, watch));
        Assert.True(gate.OwnsWindow(second, watch));

        gate.ReleaseWindow(first, watch);
        Assert.False(gate.OwnsWindow(first, watch));
        Assert.True(gate.OwnsWindow(second, watch));
        Assert.Equal(1, gate.HeldWindowCount);
    }

    [Fact]
    public void ZeroWatchId_IsNeverAccepted()
    {
        var gate = new ReanchorWatchGate();
        var hwnd = Hwnd(100);

        Assert.False(gate.TryClaimWindow(hwnd, watchId: 0));
        Assert.False(gate.OwnsWindow(hwnd, watchId: 0));
        Assert.False(gate.IsWatching(watchId: 0));
        Assert.Equal(0, gate.HeldWindowCount);
    }
}
