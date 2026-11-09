using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockWindowPickerTests
{
    private static readonly DockMonitorBounds MonitorA = new(0, 0, 1920, 1080);
    private static readonly DockMonitorBounds MonitorB = new(1920, 0, 3840, 1080);

    private const uint ExplorerPid = 700;
    private const uint OtherPid = 900;

    [Fact]
    public void Collect_KeepsEveryWindowOfTheListedProcesses()
    {
        List<AppWindowInfo> table =
        [
            Window(1, ExplorerPid, title: "Users", minimized: false),
            Window(2, ExplorerPid, title: "Windows", minimized: false),
        ];

        List<AppWindowInfo> picked = DockWindowPicker.Collect(
            table,
            [ExplorerPid],
            MonitorA,
            Rect(100, 100, 900, 700));

        Assert.Equal([(IntPtr)1, (IntPtr)2], picked.Select(static w => w.Handle));
    }

    [Fact]
    public void Collect_DropsWindowsOfOtherProcesses()
    {
        List<AppWindowInfo> table =
        [
            Window(1, ExplorerPid, title: "Users", minimized: false),
            Window(2, OtherPid, title: "Notepad", minimized: false),
        ];

        List<AppWindowInfo> picked = DockWindowPicker.Collect(
            table,
            [ExplorerPid],
            MonitorA,
            Rect(100, 100, 900, 700));

        Assert.Single(picked);
        Assert.Equal((IntPtr)1, picked[0].Handle);
    }

    [Fact]
    public void Collect_DropsWindowsOnTheOtherMonitor()
    {
        List<AppWindowInfo> onA = [Window(1, ExplorerPid, title: "Here", minimized: false)];
        List<AppWindowInfo> onB = [Window(2, ExplorerPid, title: "There", minimized: false)];

        Assert.Single(DockWindowPicker.Collect(onA, [ExplorerPid], MonitorA, Rect(100, 100, 900, 700)));
        Assert.Single(DockWindowPicker.Collect(onB, [ExplorerPid], MonitorB, Rect(2100, 100, 2900, 700)));
        Assert.Empty(DockWindowPicker.Collect(onB, [ExplorerPid], MonitorA, Rect(2100, 100, 2900, 700)));
    }

    [Fact]
    public void Collect_MinimizedWindowIsPlacedByTheRectTheCallerSupplies()
    {
        List<AppWindowInfo> table = [Window(1, ExplorerPid, title: "Users", minimized: true)];

        // The caller places an iconic window from its restored rectangle; the picker only
        // tests the center against the monitor.
        List<AppWindowInfo> picked = DockWindowPicker.Collect(
            table,
            [ExplorerPid],
            MonitorA,
            Rect(100, 100, 900, 700));

        Assert.Single(picked);
        Assert.True(picked[0].IsMinimized);
    }

    [Fact]
    public void Collect_OpenWindowsComeBeforeMinimized()
    {
        List<AppWindowInfo> table =
        [
            Window(1, ExplorerPid, title: "first", minimized: true),
            Window(2, ExplorerPid, title: "second", minimized: false),
            Window(3, ExplorerPid, title: "third", minimized: true),
            Window(4, ExplorerPid, title: "fourth", minimized: false),
        ];

        List<AppWindowInfo> picked = DockWindowPicker.Collect(
            table,
            [ExplorerPid],
            MonitorA,
            Rect(100, 100, 900, 700));

        Assert.Equal(
            [(IntPtr)2, (IntPtr)4, (IntPtr)1, (IntPtr)3],
            picked.Select(static w => w.Handle));
    }

    [Fact]
    public void Collect_CapsTheListAtMaxWindows_PreferringOpenWindows()
    {
        List<AppWindowInfo> table = [];
        for (int i = 1; i <= DockWindowPicker.MaxWindows; i++)
        {
            table.Add(Window(i, ExplorerPid, title: "open " + i, minimized: false));
        }

        for (int i = 100; i < 106; i++)
        {
            table.Add(Window(i, ExplorerPid, title: "min " + i, minimized: true));
        }

        List<AppWindowInfo> picked = DockWindowPicker.Collect(
            table,
            [ExplorerPid],
            MonitorA,
            Rect(100, 100, 900, 700));

        Assert.Equal(DockWindowPicker.MaxWindows, picked.Count);
        Assert.All(picked, static w => Assert.False(w.IsMinimized));
    }

    [Fact]
    public void Collect_DedupesByHandle()
    {
        List<AppWindowInfo> table =
        [
            Window(1, ExplorerPid, title: "Users", minimized: false),
            Window(1, ExplorerPid, title: "Users again", minimized: false),
        ];

        Assert.Single(DockWindowPicker.Collect(table, [ExplorerPid], MonitorA, Rect(100, 100, 900, 700)));
    }

    [Fact]
    public void Collect_SkipsTheExcludedHandle()
    {
        List<AppWindowInfo> table =
        [
            Window(1, ExplorerPid, title: "tile", minimized: true),
            Window(2, ExplorerPid, title: "other", minimized: false),
        ];

        List<AppWindowInfo> picked = DockWindowPicker.Collect(
            table,
            [ExplorerPid],
            MonitorA,
            Rect(100, 100, 900, 700),
            exclude: (IntPtr)1);

        Assert.Single(picked);
        Assert.Equal((IntPtr)2, picked[0].Handle);
    }

    [Fact]
    public void Collect_KeepsAWindowThatCannotBeMeasured()
    {
        List<AppWindowInfo> table = [Window(1, ExplorerPid, title: "unknown", minimized: false)];

        List<AppWindowInfo> picked = DockWindowPicker.Collect(
            table,
            [ExplorerPid],
            MonitorA,
            static _ => null);

        Assert.Single(picked);
    }

    [Fact]
    public void Collect_EmptyInputsAreEmpty()
    {
        Assert.Empty(DockWindowPicker.Collect([], [ExplorerPid], MonitorA, Rect(100, 100, 900, 700)));
        Assert.Empty(DockWindowPicker.Collect(
            [Window(1, ExplorerPid, title: "Users", minimized: false)],
            [],
            MonitorA,
            Rect(100, 100, 900, 700)));
    }

    private static AppWindowInfo Window(IntPtr handle, uint pid, string title, bool minimized) =>
        new(handle, title, pid, minimized);

    private static Func<AppWindowInfo, RECT?> Rect(int left, int top, int right, int bottom) =>
        _ => new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
}
