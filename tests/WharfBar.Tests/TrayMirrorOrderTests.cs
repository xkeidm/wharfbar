using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayMirrorOrderTests
{
    [Fact]
    public void UniqueKeys_KeepsEveryButton_WithUniqueKeysInOrder()
    {
        IReadOnlyList<string> keys = TrayMirrorOrder.UniqueKeys(
            ["Discord", "", "Discord", "EarTrumpet: 42%", null]);

        Assert.Equal(5, keys.Count);
        Assert.Equal("Discord", keys[0]);
        Assert.Equal("\0#1", keys[1]);
        Assert.NotEqual(keys[0], keys[2]);
        Assert.Contains("#2", keys[2], StringComparison.Ordinal);
        Assert.Equal("EarTrumpet: 42%", keys[3]);
        Assert.Equal("\0#4", keys[4]);
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void UniqueKeys_NoSuffixWhenNamesAreAlreadyUnique()
    {
        Assert.Equal(["A", "B", "C"], TrayMirrorOrder.UniqueKeys(["A", "B", "C"]));
    }

    [Fact]
    public void UniqueKeys_TrimsNamesSoPersistedTooltipKeysStillMatch()
    {
        Assert.Equal(["Steam"], TrayMirrorOrder.UniqueKeys([" Steam "]));
    }

    [Fact]
    public void UniqueByKey_KeepsFirstAndDropsDuplicates()
    {
        var items = new List<TrayMirrorItem>
        {
            new("a", 1, 0, "A", null),
            new("b", 2, 1, "B", null),
            new("a", 3, 2, "A2", null),
            new("c", 4, 3, "C", null),
        };

        IReadOnlyList<TrayMirrorItem> unique = TrayMirrorOrder.UniqueByKey(items);

        Assert.Equal(["a", "b", "c"], unique.Select(static i => i.Key).ToList());
        Assert.Equal("A", unique[0].Tooltip);
    }

    [Fact]
    public void SyncOrder_MovingEarlierItemToEnd_DoesNotThrow()
    {
        // Repro of the crash shape: RemoveAt(0) then Insert(target) with target == old Count-1.
        var items = new List<object> { "A", "B", "C" };

        TrayMirrorOrder.SyncOrder(items, ["B", "C", "A"]);

        Assert.Equal(["B", "C", "A"], items);
    }

    [Fact]
    public void SyncOrder_WhenDesiredLongerThanItems_ClampsInsert()
    {
        // Duplicate keys left one host in Items while desired indexes past Count.
        var host = new object();
        var items = new List<object> { host };

        TrayMirrorOrder.SyncOrder(items, [host, host]);

        Assert.Single(items);
        Assert.Same(host, items[0]);
    }

    [Fact]
    public void SyncOrder_ReordersWithoutClear()
    {
        var a = new object();
        var b = new object();
        var c = new object();
        var items = new List<object> { a, b, c };

        TrayMirrorOrder.SyncOrder(items, [c, a, b]);

        Assert.Equal([c, a, b], items);
    }
}
