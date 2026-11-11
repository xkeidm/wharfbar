using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockBuiltInTilesTests
{
    [Fact]
    public void Append_PutsLaunchpadAndTaskViewBeforeThePins()
    {
        var pin = new DockAppItem(
            @"C:\Windows\System32\notepad.exe",
            @"C:\Windows\System32\notepad.exe",
            string.Empty,
            "notepad",
            true,
            false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty);
        var running = new DockAppItem(
            "calc",
            @"C:\Windows\System32\calc.exe",
            string.Empty,
            "Calc",
            false,
            true,
            (IntPtr)4,
            DockSection.Running,
            string.Empty,
            WindowCount: 1,
            ProcessIds: [4]);

        IReadOnlyList<DockAppItem> strip = DockBuiltInTiles.Append([pin, running], @"D:\Downloads");

        Assert.Equal(6, strip.Count);
        Assert.Equal(
            [
                DockTileKind.Launchpad,
                DockTileKind.TaskView,
                DockTileKind.App,
                DockTileKind.Downloads,
                DockTileKind.App,
                DockTileKind.Trash,
            ],
            strip.Select(static item => item.Kind).ToArray());
        Assert.Equal(DockBuiltInTiles.LaunchpadId, strip[0].Id);
        Assert.Equal(DockBuiltInTiles.TaskViewId, strip[1].Id);
        Assert.Equal("notepad", strip[2].Label);
        Assert.Equal(DockBuiltInTiles.DownloadsId, strip[3].Id);
        Assert.Equal(DockTileKind.Downloads, strip[3].Kind);
        Assert.False(strip[3].IsPinned);
        Assert.Equal(@"D:\Downloads", strip[3].Path);
        Assert.Equal("Calc", strip[4].Label);
        Assert.Equal(DockBuiltInTiles.TrashId, strip[5].Id);
        Assert.Equal(DockTileKind.Trash, strip[5].Kind);
        Assert.False(strip[5].IsPinned);
        Assert.Equal("id:" + DockBuiltInTiles.LaunchpadId, DockPresence.KeyFor(strip[0]));
        Assert.Equal("id:" + DockBuiltInTiles.TaskViewId, DockPresence.KeyFor(strip[1]));
        Assert.Equal("id:" + DockBuiltInTiles.DownloadsId, DockPresence.KeyFor(strip[3]));
        Assert.Equal("id:" + DockBuiltInTiles.TrashId, DockPresence.KeyFor(strip[5]));
    }

    /// <summary>
    /// The tile is a handler, not a store row: no path means it can never be written to
    /// <c>dock.items</c> or the User Pinned folder, and the pick/pin/reorder paths skip it.
    /// </summary>
    [Fact]
    public void Append_TaskViewHasNoStoreOrPinPath()
    {
        IReadOnlyList<DockAppItem> strip = DockBuiltInTiles.Append([], downloadsPath: null);
        DockAppItem tile = Assert.Single(strip, item => item.Kind == DockTileKind.TaskView);

        Assert.Equal("builtin:taskview", tile.Id);
        Assert.Equal(DockBuiltInTiles.TaskViewId, tile.Id);
        Assert.Equal(Strings.Dock_TaskView, tile.Label);
        Assert.Equal(string.Empty, tile.Path);
        Assert.Equal(string.Empty, tile.Args);
        Assert.False(tile.IsPinned);
        Assert.False(tile.IsRunning);
        Assert.Equal(IntPtr.Zero, tile.MainWindowHandle);
        Assert.Equal(DockSection.Pinned, tile.Section);
    }

    [Fact]
    public void MarkFolders_UsesThePredicateAndLeavesOtherKinds()
    {
        var folder = new DockAppItem(
            @"C:\Work",
            @"C:\Work",
            string.Empty,
            "Work",
            true,
            false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty);
        var exe = new DockAppItem(
            @"C:\Windows\System32\notepad.exe",
            @"C:\Windows\System32\notepad.exe",
            string.Empty,
            "notepad",
            true,
            false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty);

        IReadOnlyList<DockAppItem> marked = DockBuiltInTiles.MarkFolders(
            [folder, exe],
            path => path.EndsWith("Work", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(DockTileKind.Folder, marked[0].Kind);
        Assert.Equal(@"C:\Work", marked[0].Path);
        Assert.Equal(DockTileKind.App, marked[1].Kind);
    }
}
