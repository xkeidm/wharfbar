using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockBuiltInTilesTests
{
    [Fact]
    public void Append_PutsDownloadsAfterPinsAndTrashAtTheEnd()
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

        Assert.Equal(5, strip.Count);
        Assert.Equal(DockBuiltInTiles.LaunchpadId, strip[0].Id);
        Assert.Equal(DockTileKind.Launchpad, strip[0].Kind);
        Assert.Equal("notepad", strip[1].Label);
        Assert.Equal(DockBuiltInTiles.DownloadsId, strip[2].Id);
        Assert.Equal(DockTileKind.Downloads, strip[2].Kind);
        Assert.False(strip[2].IsPinned);
        Assert.Equal(@"D:\Downloads", strip[2].Path);
        Assert.Equal("Calc", strip[3].Label);
        Assert.Equal(DockBuiltInTiles.TrashId, strip[4].Id);
        Assert.Equal(DockTileKind.Trash, strip[4].Kind);
        Assert.False(strip[4].IsPinned);
        Assert.Equal("id:" + DockBuiltInTiles.LaunchpadId, DockPresence.KeyFor(strip[0]));
        Assert.Equal("id:" + DockBuiltInTiles.DownloadsId, DockPresence.KeyFor(strip[2]));
        Assert.Equal("id:" + DockBuiltInTiles.TrashId, DockPresence.KeyFor(strip[4]));
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
