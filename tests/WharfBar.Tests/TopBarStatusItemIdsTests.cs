using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class TopBarStatusItemIdsTests
{
    [Fact]
    public void DefaultOrder_OmitsHudItems_CatalogKeepsThem()
    {
        Assert.DoesNotContain(TopBarStatusItemIds.Volume, TopBarStatusItemIds.DefaultOrder);
        Assert.DoesNotContain(TopBarStatusItemIds.Brightness, TopBarStatusItemIds.DefaultOrder);
        Assert.DoesNotContain(TopBarStatusItemIds.Media, TopBarStatusItemIds.DefaultOrder);

        var catalog = TopBarStatusItemIds.CatalogOrder.ToList();
        int brightness = catalog.IndexOf(TopBarStatusItemIds.Brightness);
        Assert.Equal(TopBarStatusItemIds.Volume, catalog[brightness - 1]);
        Assert.Equal(TopBarStatusItemIds.Media, catalog[brightness + 1]);

        foreach (string id in TopBarStatusItemIds.DefaultOrder)
        {
            Assert.Contains(id, catalog);
        }
    }

    [Fact]
    public void Normalize_KeepsSavedVolumeBrightnessMedia()
    {
        var saved = new List<string>
        {
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.Volume,
            TopBarStatusItemIds.Brightness,
            TopBarStatusItemIds.Media,
            TopBarStatusItemIds.Battery,
        };
        Assert.Equal(saved, TopBarStatusItemIds.Normalize(saved));
    }

    [Fact]
    public void InsertInCatalogOrder_ReenablesVolumeBetweenNeighbors()
    {
        List<string> placed = TopBarStatusItemIds.InsertInCatalogOrder(
            TopBarStatusItemIds.DefaultOrder.ToList(),
            TopBarStatusItemIds.Volume);
        int volume = placed.IndexOf(TopBarStatusItemIds.Volume);
        Assert.True(volume > placed.IndexOf(TopBarStatusItemIds.ControlCenter));
        Assert.True(volume < placed.IndexOf(TopBarStatusItemIds.Network));
    }

    [Fact]
    public void Normalize_KeepsMedia_WhenPresent()
    {
        var items = new List<string> { TopBarStatusItemIds.Media, TopBarStatusItemIds.Battery };
        var normalized = TopBarStatusItemIds.Normalize(items);
        Assert.Contains(TopBarStatusItemIds.Media, normalized);
    }

    [Fact]
    public void EnsureMedia_InsertsAfterBrightness()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.Volume,
            TopBarStatusItemIds.Brightness,
            TopBarStatusItemIds.Network,
        };
        TopBarStatusItemIds.EnsureMedia(items);
        Assert.Equal(
            [TopBarStatusItemIds.Volume, TopBarStatusItemIds.Brightness, TopBarStatusItemIds.Media, TopBarStatusItemIds.Network],
            items);
    }

    [Fact]
    public void EnsureMedia_InsertsAfterVolume_WhenBrightnessMissing()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.Volume,
            TopBarStatusItemIds.Network,
        };
        TopBarStatusItemIds.EnsureMedia(items);
        Assert.Equal([TopBarStatusItemIds.Volume, TopBarStatusItemIds.Media, TopBarStatusItemIds.Network], items);
    }

    [Fact]
    public void EnsureMedia_Appends_WhenNeitherPresent()
    {
        var items = new List<string> { TopBarStatusItemIds.DateTime };
        TopBarStatusItemIds.EnsureMedia(items);
        Assert.Equal([TopBarStatusItemIds.DateTime, TopBarStatusItemIds.Media], items);
    }

    [Fact]
    public void EnsureMedia_NoOp_WhenAlreadyPresent()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.Network,
            TopBarStatusItemIds.Media,
            TopBarStatusItemIds.Battery,
        };
        TopBarStatusItemIds.EnsureMedia(items);
        Assert.Equal([TopBarStatusItemIds.Network, TopBarStatusItemIds.Media, TopBarStatusItemIds.Battery], items);
    }

    [Fact]
    public void DefaultOrder_ContainsTrayMirror_BeforeTrayOverflow()
    {
        var order = TopBarStatusItemIds.DefaultOrder.ToList();
        int trayOverflowIndex = order.IndexOf(TopBarStatusItemIds.TrayOverflow);
        Assert.True(trayOverflowIndex > 0);
        Assert.Equal(TopBarStatusItemIds.TrayMirror, order[trayOverflowIndex - 1]);
    }

    [Fact]
    public void EnsureTrayMirror_InsertsBeforeTrayOverflow()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.TrayOverflow,
            TopBarStatusItemIds.Battery,
        };
        TopBarStatusItemIds.EnsureTrayMirror(items);
        Assert.Equal(
            [TopBarStatusItemIds.DateTime, TopBarStatusItemIds.TrayMirror, TopBarStatusItemIds.TrayOverflow, TopBarStatusItemIds.Battery],
            items);
    }

    [Fact]
    public void EnsureTrayMirror_Appends_WhenTrayOverflowMissing()
    {
        var items = new List<string> { TopBarStatusItemIds.DateTime };
        TopBarStatusItemIds.EnsureTrayMirror(items);
        Assert.Equal([TopBarStatusItemIds.DateTime, TopBarStatusItemIds.TrayMirror], items);
    }

    [Fact]
    public void EnsureTrayMirror_NoOp_WhenAlreadyPresent()
    {
        var items = new List<string> { TopBarStatusItemIds.TrayMirror, TopBarStatusItemIds.TrayOverflow };
        TopBarStatusItemIds.EnsureTrayMirror(items);
        Assert.Equal([TopBarStatusItemIds.TrayMirror, TopBarStatusItemIds.TrayOverflow], items);
    }

    [Fact]
    public void Move_OntoTheNextItem_SwapsWithIt()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.Notifications,
            TopBarStatusItemIds.Network,
        };

        Assert.True(TopBarStatusItemIds.Move(
            items,
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.Notifications));
        Assert.Equal(
            [
                TopBarStatusItemIds.Notifications,
                TopBarStatusItemIds.DateTime,
                TopBarStatusItemIds.Network,
            ],
            items);
    }

    [Fact]
    public void Move_OntoThePreviousItem_SwapsWithIt()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.Notifications,
            TopBarStatusItemIds.Network,
        };

        Assert.True(TopBarStatusItemIds.Move(
            items,
            TopBarStatusItemIds.Notifications,
            TopBarStatusItemIds.DateTime));
        Assert.Equal(
            [
                TopBarStatusItemIds.Notifications,
                TopBarStatusItemIds.DateTime,
                TopBarStatusItemIds.Network,
            ],
            items);
    }

    [Fact]
    public void Move_OntoTheLastItem_BecomesLast()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.Notifications,
            TopBarStatusItemIds.Network,
            TopBarStatusItemIds.Battery,
        };

        Assert.True(TopBarStatusItemIds.Move(
            items,
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.Battery));
        Assert.Equal(
            [
                TopBarStatusItemIds.Notifications,
                TopBarStatusItemIds.Network,
                TopBarStatusItemIds.Battery,
                TopBarStatusItemIds.DateTime,
            ],
            items);
    }

    [Fact]
    public void Move_WhenTargetIsTheDraggedItem_LeavesOrder()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.Network,
        };
        var before = items.ToList();

        Assert.False(TopBarStatusItemIds.Move(
            items,
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.DateTime));
        Assert.Equal(before, items);
    }

    [Fact]
    public void Move_WhenIdIsMissing_LeavesOrder()
    {
        var items = new List<string>
        {
            TopBarStatusItemIds.DateTime,
            TopBarStatusItemIds.Network,
        };
        var before = items.ToList();

        Assert.False(TopBarStatusItemIds.Move(items, TopBarStatusItemIds.Battery, TopBarStatusItemIds.Network));
        Assert.False(TopBarStatusItemIds.Move(items, TopBarStatusItemIds.DateTime, TopBarStatusItemIds.Battery));
        Assert.Equal(before, items);
    }
}
