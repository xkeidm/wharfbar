using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockMonitorWindowFilterTests
{
    private static readonly DockMonitorBounds MonitorA = new(0, 0, 1920, 1080);
    private static readonly DockMonitorBounds MonitorB = new(1920, 0, 3840, 1080);

    [Fact]
    public void CenterOnB_DoesNotIncrementA()
    {
        var onA = new DockPlacedWindow((IntPtr)1, 7, false, 100, 100, 500, 400);
        var onB = new DockPlacedWindow((IntPtr)2, 7, false, 2000, 100, 2600, 500);
        DockAppItem pin = RunningPin("notepad", (IntPtr)2, 7, windowCount: 2);

        DockAppItem a = Assert.Single(DockMonitorWindowFilter.Apply([pin], [onA, onB], MonitorA));
        Assert.Equal(1, a.WindowCount);
        Assert.Equal(DockLocalPresence.Open, a.LocalPresence);
        Assert.Equal((IntPtr)1, a.MainWindowHandle);
        Assert.True(a.IsPinned);
        Assert.True(a.IsRunning);

        DockAppItem b = Assert.Single(DockMonitorWindowFilter.Apply([pin], [onA, onB], MonitorB));
        Assert.Equal(1, b.WindowCount);
        Assert.Equal((IntPtr)2, b.MainWindowHandle);
    }

    [Fact]
    public void SharedEdge_BelongsToTheMonitorThatContainsTheCenter()
    {
        var onEdge = new DockPlacedWindow((IntPtr)3, 7, false, 1860, 100, 1980, 400);
        Assert.Equal(1920, 1860 + ((1980 - 1860) / 2));
        Assert.False(DockMonitorWindowFilter.CenterLiesOn(onEdge, MonitorA));
        Assert.True(DockMonitorWindowFilter.CenterLiesOn(onEdge, MonitorB));
    }

    [Fact]
    public void PinnedAppWithNoWindowHere_StaysAndDropsTheLine()
    {
        var onB = new DockPlacedWindow((IntPtr)2, 7, true, 2000, 100, 2400, 400);
        DockAppItem pin = RunningPin("notepad", (IntPtr)2, 7, windowCount: 1);
        DockAppItem a = Assert.Single(DockMonitorWindowFilter.Apply([pin], [onB], MonitorA));
        Assert.Equal(DockLocalPresence.Absent, a.LocalPresence);
        Assert.Equal(0, a.WindowCount);
        Assert.False(a.IsOpenOnDesktop);
        Assert.True(a.IsRunning);
        Assert.Equal((IntPtr)2, a.MainWindowHandle);
    }

    [Fact]
    public void UnpinnedAppOnTheOtherMonitor_IsNotASecondTileHere()
    {
        var onB = new DockPlacedWindow((IntPtr)2, 9, false, 2100, 40, 2500, 400);
        var unpinned = new DockAppItem(
            "calc",
            @"C:\Windows\System32\calc.exe",
            string.Empty,
            "Calc",
            IsPinned: false,
            IsRunning: true,
            (IntPtr)2,
            DockSection.Running,
            string.Empty,
            WindowCount: 1,
            ProcessIds: [9]);

        Assert.Empty(DockMonitorWindowFilter.Apply([unpinned], [onB], MonitorA));
        DockAppItem kept = Assert.Single(DockMonitorWindowFilter.Apply([unpinned], [onB], MonitorB));
        Assert.Equal(DockLocalPresence.Open, kept.LocalPresence);
    }

    [Fact]
    public void MinimizedIconicPoint_UsesTheRestoredMonitor()
    {
        var iconic = new RECT { Left = -32000, Top = -32000, Right = -31840, Bottom = -31800 };
        var restored = new RECT { Left = 2000, Top = 40, Right = 2600, Bottom = 400 };
        RECT used = DockMonitorWindowFilter.RectForCenter(minimized: true, iconic, restored, hasRestored: true);
        var unpinned = new DockAppItem(
            "calc",
            @"C:\Windows\System32\calc.exe",
            string.Empty,
            "Calc",
            IsPinned: false,
            IsRunning: true,
            (IntPtr)2,
            DockSection.Running,
            string.Empty,
            WindowCount: 1,
            ProcessIds: [9]);
        var placed = new DockPlacedWindow((IntPtr)2, 9, true, used.Left, used.Top, used.Right, used.Bottom);

        Assert.Empty(DockMonitorWindowFilter.Apply([unpinned], [placed], MonitorA));
        DockAppItem kept = Assert.Single(DockMonitorWindowFilter.Apply([unpinned], [placed], MonitorB));
        Assert.Equal(DockLocalPresence.Minimized, kept.LocalPresence);
        Assert.True(kept.IsRunning);
    }

    [Fact]
    public void AllMinimizedHere_UsesTheShortLine()
    {
        var minimized = new DockPlacedWindow((IntPtr)4, 7, true, 10, 10, 80, 80);
        DockAppItem pin = RunningPin("notepad", (IntPtr)4, 7, windowCount: 3);
        DockAppItem scoped = Assert.Single(DockMonitorWindowFilter.Apply([pin], [minimized], MonitorA));
        Assert.Equal(DockLocalPresence.Minimized, scoped.LocalPresence);
        Assert.False(scoped.IsOpenOnDesktop);
        Assert.Equal(1, scoped.WindowCount);
    }

    [Fact]
    public void EmptyWindowTable_LeavesTheStripUnscoped()
    {
        DockAppItem pin = RunningPin("notepad", (IntPtr)1, 7, windowCount: 2);
        IReadOnlyList<DockAppItem> same = DockMonitorWindowFilter.ApplyMeasured([pin], [], MonitorA);
        Assert.Same(pin, Assert.Single(same));
        Assert.Equal(DockLocalPresence.Unscoped, same[0].LocalPresence);
    }

    [Fact]
    public void BuiltInTiles_StayOnEveryMonitor()
    {
        var trash = new DockAppItem(
            DockBuiltInTiles.TrashId,
            "shell:RecycleBinFolder",
            string.Empty,
            "Geri Dönüşüm Kutusu",
            false,
            false,
            IntPtr.Zero,
            DockSection.Running,
            string.Empty,
            Kind: DockTileKind.Trash);
        var taskView = new DockAppItem(
            DockBuiltInTiles.TaskViewId,
            string.Empty,
            string.Empty,
            "Görev Görünümü",
            false,
            false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty,
            Kind: DockTileKind.TaskView);
        Assert.Equal([trash, taskView], DockMonitorWindowFilter.Apply([trash, taskView], [], MonitorA));
    }

    private static DockAppItem RunningPin(string name, IntPtr hwnd, uint pid, int windowCount) =>
        new(
            name,
            $@"C:\Windows\System32\{name}.exe",
            string.Empty,
            name,
            IsPinned: true,
            IsRunning: true,
            hwnd,
            DockSection.Pinned,
            string.Empty,
            WindowCount: windowCount,
            ProcessIds: [pid]);
}
