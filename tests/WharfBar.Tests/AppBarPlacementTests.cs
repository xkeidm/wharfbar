using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class AppBarPlacementTests
{
    private static RECT Rect(int l, int t, int r, int b) => new()
    {
        Left = l,
        Top = t,
        Right = r,
        Bottom = b,
    };

    [Fact]
    public void BottomStrip_SpansTheMonitorWidthAtTheBottom()
    {
        RECT monitor = Rect(0, 0, 1920, 1080);
        RECT strip = AppBarPlacement.BottomStrip(monitor, 56);

        Assert.Equal(0, strip.Left);
        Assert.Equal(1024, strip.Top);
        Assert.Equal(1920, strip.Right);
        Assert.Equal(1080, strip.Bottom);
    }

    [Fact]
    public void BottomStrip_ClampsHeightToTheMonitor()
    {
        RECT monitor = Rect(100, 200, 1000, 800);
        RECT strip = AppBarPlacement.BottomStrip(monitor, 10_000);

        Assert.Equal(100, strip.Left);
        Assert.Equal(200, strip.Top);
        Assert.Equal(1000, strip.Right);
        Assert.Equal(800, strip.Bottom);
    }

    [Fact]
    public void BottomStrip_ZeroHeightSitsOnTheBottomEdge()
    {
        RECT strip = AppBarPlacement.BottomStrip(Rect(0, 0, 1920, 1080), 0);
        Assert.Equal(0, strip.Height);
        Assert.Equal(1080, strip.Bottom);
        Assert.Equal(1080, strip.Top);
    }

    [Fact]
    public void NeedsShellSetPos_WhenNeverCommitted_IsTrue()
    {
        RECT approved = Rect(0, 0, 1920, 48);
        Assert.True(AppBarPlacement.NeedsShellSetPos(false, default, approved));
    }

    [Fact]
    public void NeedsShellSetPos_WhenApprovedUnchanged_IsFalse()
    {
        RECT approved = Rect(0, 0, 1920, 48);
        Assert.False(AppBarPlacement.NeedsShellSetPos(true, approved, approved));
    }

    [Fact]
    public void NeedsShellSetPos_WhenApprovedMoved_IsTrue()
    {
        RECT last = Rect(0, 0, 1920, 48);
        RECT next = Rect(0, 0, 1920, 60);
        Assert.True(AppBarPlacement.NeedsShellSetPos(true, last, next));
    }

    [Fact]
    public void RectsEqual_DetectsAnyEdgeChange()
    {
        RECT a = Rect(0, 0, 100, 30);
        Assert.True(AppBarPlacement.RectsEqual(a, Rect(0, 0, 100, 30)));
        Assert.False(AppBarPlacement.RectsEqual(a, Rect(1, 0, 100, 30)));
        Assert.False(AppBarPlacement.RectsEqual(a, Rect(0, 1, 100, 30)));
        Assert.False(AppBarPlacement.RectsEqual(a, Rect(0, 0, 101, 30)));
        Assert.False(AppBarPlacement.RectsEqual(a, Rect(0, 0, 100, 31)));
    }

    [Fact]
    public void TryAlignToRect_WhenShellPushesBarIntoWorkArea_MovesBackToApproved()
    {
        RECT approved = Rect(0, 0, 1920, 30);
        bool changed = AppBarPlacement.TryAlignToRect(
            x: 0,
            y: 30,
            cx: 1920,
            cy: 30,
            flags: NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE,
            currentX: 0,
            currentY: 0,
            currentW: 1920,
            currentH: 30,
            approved,
            out int nx,
            out int ny,
            out int ncx,
            out int ncy,
            out uint nflags);

        Assert.True(changed);
        Assert.Equal(0, nx);
        Assert.Equal(0, ny);
        Assert.Equal(1920, ncx);
        Assert.Equal(30, ncy);
        Assert.Equal(0u, nflags & NativeMethods.SWP_NOMOVE);
        Assert.Equal(NativeMethods.SWP_NOSIZE, nflags & NativeMethods.SWP_NOSIZE);
        Assert.Equal(NativeMethods.SWP_NOACTIVATE, nflags & NativeMethods.SWP_NOACTIVATE);
    }

    [Fact]
    public void TryAlignToRect_WhenNoMoveButCurrentAlreadyPushed_ClearsNoMove()
    {
        RECT approved = Rect(0, 0, 1920, 30);
        bool changed = AppBarPlacement.TryAlignToRect(
            x: 0,
            y: 0,
            cx: 0,
            cy: 0,
            flags: NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE,
            currentX: 0,
            currentY: 30,
            currentW: 1920,
            currentH: 30,
            approved,
            out int nx,
            out int ny,
            out int ncx,
            out int ncy,
            out uint nflags);

        Assert.True(changed);
        Assert.Equal(0, nx);
        Assert.Equal(0, ny);
        Assert.Equal(1920, ncx);
        Assert.Equal(30, ncy);
        Assert.Equal(0u, nflags & NativeMethods.SWP_NOMOVE);
    }

    [Fact]
    public void TryAlignToRect_WhenAlreadyApproved_IsUnchanged()
    {
        RECT approved = Rect(0, 0, 1920, 30);
        uint flags = NativeMethods.SWP_NOACTIVATE;
        bool changed = AppBarPlacement.TryAlignToRect(
            0,
            0,
            1920,
            30,
            flags,
            0,
            0,
            1920,
            30,
            approved,
            out int nx,
            out int ny,
            out int ncx,
            out int ncy,
            out uint nflags);

        Assert.False(changed);
        Assert.Equal(0, nx);
        Assert.Equal(0, ny);
        Assert.Equal(1920, ncx);
        Assert.Equal(30, ncy);
        Assert.Equal(flags, nflags);
    }

    [Fact]
    public void ClampSlideY_WhenPushedAboveVisible_ClampsToVisible()
    {
        Assert.Equal(1034, AppBarPlacement.ClampSlideY(986, visibleY: 1034, hiddenY: 1080));
    }

    [Fact]
    public void ClampSlideY_WhenHiddenAndPushedIntoWorkArea_StaysHidden()
    {
        Assert.Equal(1080, AppBarPlacement.ClampSlideY(986, visibleY: 1080, hiddenY: 1080));
    }

    [Fact]
    public void ClampSlideY_WhenBetweenVisibleAndHidden_LeavesY()
    {
        Assert.Equal(1050, AppBarPlacement.ClampSlideY(1050, visibleY: 1034, hiddenY: 1080));
    }

    [Fact]
    public void ClampSlideY_WhenBelowHidden_ClampsToHidden()
    {
        Assert.Equal(1080, AppBarPlacement.ClampSlideY(1100, visibleY: 1034, hiddenY: 1080));
    }

    [Fact]
    public void NeedsBottomExpand_WhenPhantomTaskbarStrip_IsTrue()
    {
        Assert.True(AppBarPlacement.NeedsBottomExpand(workBottom: 1032, monitorBottom: 1080));
        Assert.False(AppBarPlacement.NeedsBottomExpand(workBottom: 1080, monitorBottom: 1080));
        Assert.False(AppBarPlacement.NeedsBottomExpand(workBottom: 1080, monitorBottom: 1079));
    }

    [Fact]
    public void ShouldNotifyShellWindowPosChanged_WhenAlreadyOnApproved_IsFalse()
    {
        RECT approved = Rect(0, 0, 1920, 30);
        Assert.False(AppBarPlacement.ShouldNotifyShellWindowPosChanged(
            isApplyingPlacement: false,
            haveApproved: true,
            approved,
            approved));
    }

    [Fact]
    public void ShouldNotifyShellWindowPosChanged_WhenApplyingPlacement_IsFalse()
    {
        RECT approved = Rect(0, 0, 1920, 30);
        RECT moved = Rect(0, 30, 1920, 60);
        Assert.False(AppBarPlacement.ShouldNotifyShellWindowPosChanged(
            isApplyingPlacement: true,
            haveApproved: true,
            moved,
            approved));
    }

    [Fact]
    public void ShouldNotifyShellWindowPosChanged_WhenMovedOffApproved_IsTrue()
    {
        RECT approved = Rect(0, 0, 1920, 30);
        RECT moved = Rect(0, 30, 1920, 60);
        Assert.True(AppBarPlacement.ShouldNotifyShellWindowPosChanged(
            isApplyingPlacement: false,
            haveApproved: true,
            moved,
            approved));
    }

    [Fact]
    public void GetDockSlideClampRange_WhenParkedHidden_LocksToHiddenY()
    {
        AppBarPlacement.GetDockSlideClampRange(
            autoHideEnabled: true,
            allowSlide: false,
            parkedHidden: true,
            visibleY: 1034,
            hiddenY: 1080,
            out int minY,
            out int maxY);
        Assert.Equal(1080, minY);
        Assert.Equal(1080, maxY);
    }

    [Fact]
    public void GetDockSlideClampRange_WhenAnimating_AllowsVisibleToHidden()
    {
        AppBarPlacement.GetDockSlideClampRange(
            autoHideEnabled: true,
            allowSlide: true,
            parkedHidden: false,
            visibleY: 1034,
            hiddenY: 1080,
            out int minY,
            out int maxY);
        Assert.Equal(1034, minY);
        Assert.Equal(1080, maxY);
    }

    [Fact]
    public void IsIntervalDue_WhenElapsedBelowInterval_IsFalse()
    {
        Assert.False(AppBarPlacement.IsIntervalDue(nowMs: 900, lastMs: 0, intervalMs: 1000));
    }

    [Fact]
    public void IsIntervalDue_WhenNeverRun_IsTrue()
    {
        Assert.True(AppBarPlacement.IsIntervalDue(nowMs: 0, lastMs: long.MinValue, intervalMs: 1000));
    }

    [Fact]
    public void KeepHiddenWorkAreaPoll_IsAtLeastOneSecond()
    {
        Assert.True(AppBarPlacement.KeepHiddenWorkAreaPollMs >= 1000);
        Assert.True(AppBarPlacement.KeepHiddenTrayPollMs >= 250);
        Assert.True(AppBarPlacement.KeepHiddenWorkAreaPollMs >= AppBarPlacement.KeepHiddenTrayPollMs);
    }
}
