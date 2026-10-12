using System.IO;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class LaunchpadBoxTests
{
    [Fact]
    public void GroupByLetter_PutsEachNameUnderItsFirstLetter()
    {
        IReadOnlyList<LaunchpadLetterGroup> groups = LaunchpadCatalog.GroupByLetter(
        [
            new LaunchpadApp("Edge", "e"),
            new LaunchpadApp("access", "a"),
            new LaunchpadApp("Affinity", "f"),
            new LaunchpadApp("3D Viewer", "n"),
        ]);

        Assert.Equal(["#", "A", "E"], groups.Select(group => group.Letter).ToArray());
        Assert.Equal(["3D Viewer"], groups[0].Apps.Select(app => app.Name).ToArray());
        Assert.Equal(["access", "Affinity"], groups[1].Apps.Select(app => app.Name).ToArray());
        Assert.Equal(["Edge"], groups[2].Apps.Select(app => app.Name).ToArray());
    }

    [Fact]
    public void Anchor_SitsAboveABottomDock()
    {
        var monitor = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var dock = new RECT { Left = 700, Top = 1000, Right = 1200, Bottom = 1060 };
        (int x, int y) = LaunchpadBoxPlacement.Anchor(monitor, dock, 680, 500, 10, dockOnLeft: false);

        Assert.Equal(610, x);
        Assert.Equal(490, y);
    }

    [Fact]
    public void Anchor_SitsToTheRightOfALeftDock()
    {
        var monitor = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        var dock = new RECT { Left = 0, Top = 200, Right = 68, Bottom = 880 };
        (int x, int y) = LaunchpadBoxPlacement.Anchor(monitor, dock, 680, 500, 10, dockOnLeft: true);

        Assert.Equal(78, x);
        Assert.Equal(380, y);
    }

    [Fact]
    public void Anchor_ClampsOntoTheMonitor()
    {
        var monitor = new RECT { Left = 0, Top = 0, Right = 800, Bottom = 600 };
        var dock = new RECT { Left = 0, Top = 540, Right = 800, Bottom = 600 };
        (int x, int y) = LaunchpadBoxPlacement.Anchor(monitor, dock, 900, 700, 10, dockOnLeft: false);

        Assert.Equal(10, x);
        Assert.Equal(10, y);
    }

    [Fact]
    public void FormatAge_UsesTurkishBuckets()
    {
        var now = new DateTime(2026, 10, 12, 13, 0, 0);

        Assert.Equal("Az önce", LaunchpadRecent.FormatAge(now.AddSeconds(-20), now));
        Assert.Equal("5 dakika önce", LaunchpadRecent.FormatAge(now.AddMinutes(-5), now));
        Assert.Equal("2 saat önce", LaunchpadRecent.FormatAge(now.AddHours(-2), now));
        Assert.Equal("Dün saat 13:00", LaunchpadRecent.FormatAge(now.AddDays(-1), now));
    }

    [Fact]
    public void Recent_ListsNewestShortcuts()
    {
        string root = Path.Combine(Path.GetTempPath(), "wharfbar-recent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string older = Path.Combine(root, "eski.lnk");
            string newer = Path.Combine(root, "yeni.lnk");
            File.WriteAllText(older, "a");
            File.WriteAllText(newer, "b");
            File.SetLastWriteTime(older, DateTime.Now.AddHours(-3));
            File.SetLastWriteTime(newer, DateTime.Now.AddMinutes(-5));

            IReadOnlyList<LaunchpadRecentItem> list = LaunchpadRecent.List(root, 10);

            Assert.Equal(["yeni", "eski"], list.Select(item => item.Name).ToArray());
            Assert.Empty(LaunchpadRecent.List(root, 0));
            Assert.Empty(LaunchpadRecent.List(Path.Combine(root, "yok"), 5));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Pins_SkipBlankPaths()
    {
        var dock = new DockConfig
        {
            Items =
            [
                new PinnedApp(string.Empty, string.Empty, "Boş"),
                new PinnedApp(@"C:\apps\edge.lnk", string.Empty, "Edge"),
                new PinnedApp(@"C:\apps\bare.lnk", string.Empty, string.Empty),
            ],
        };

        IReadOnlyList<LaunchpadApp> pins = LaunchpadPins.FromDock(dock);

        Assert.Equal(["Edge", "bare"], pins.Select(pin => pin.Name).ToArray());
        Assert.Empty(LaunchpadPins.FromDock(null));
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("", false, null)]
    [InlineData("  ", true, "Telefon")]
    [InlineData("Pixel", false, "Pixel")]
    public void Phone_ShowsOnlyWhenLinked(string? name, bool running, string? expected)
    {
        LaunchpadPhone? phone = LaunchpadPhoneLink.Resolve(name, running);

        Assert.Equal(expected, phone?.Name);
    }
}
