using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class TopBarStatusItemIdsTests
{
    [Fact]
    public void DefaultOrder_ContainsMedia_AfterBrightness()
    {
        var order = TopBarStatusItemIds.DefaultOrder.ToList();
        int brightnessIndex = order.IndexOf(TopBarStatusItemIds.Brightness);
        Assert.True(brightnessIndex >= 0);
        Assert.Equal(TopBarStatusItemIds.Media, order[brightnessIndex + 1]);
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
}
