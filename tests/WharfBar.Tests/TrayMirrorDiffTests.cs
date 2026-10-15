using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayMirrorDiffTests
{
    [Fact]
    public void Compute_DetectsAddedAndRemoved()
    {
        IReadOnlySet<string> prev = new HashSet<string>(new[] { "a", "b" }, StringComparer.Ordinal);
        var next = new List<TrayMirrorItem>
        {
            new("b", 1, 0, "B", null),
            new("c", 2, 1, "C", null),
        };

        var (removed, added) = TrayMirrorDiff.Compute(prev, next);

        Assert.Equal(["a"], removed);
        Assert.Equal(["c"], added);
    }

    [Fact]
    public void Compute_EmptyNext_RemovesAll()
    {
        IReadOnlySet<string> prev = new HashSet<string>(new[] { "x" }, StringComparer.Ordinal);
        var (removed, added) = TrayMirrorDiff.Compute(prev, []);
        Assert.Equal(["x"], removed);
        Assert.Empty(added);
    }

    [Fact]
    public void Compute_NoChanges_EmptyBoth()
    {
        IReadOnlySet<string> prev = new HashSet<string>(new[] { "a" }, StringComparer.Ordinal);
        var next = new List<TrayMirrorItem> { new("a", 1, 0, "A", null) };
        var (removed, added) = TrayMirrorDiff.Compute(prev, next);
        Assert.Empty(removed);
        Assert.Empty(added);
    }

    private static TrayMirrorItem Item(string key, int index, object? tag = null) =>
        new(key, index, index, key, null, tag);

    [Fact]
    public void IsSameSnapshot_SameKeysAndIcons_IgnoresTag()
    {
        var a = new List<TrayMirrorItem> { Item("Discord", 0, tag: new object()), Item("Steam", 1) };
        var b = new List<TrayMirrorItem> { Item("Discord", 0, tag: new object()), Item("Steam", 1) };

        Assert.True(TrayMirrorDiff.IsSameSnapshot(a, b));
        Assert.True(TrayMirrorDiff.IsSameSnapshot([], []));
    }

    [Fact]
    public void IsSameSnapshot_DifferentIconInstance_IsAChange()
    {
        // The icon cache hands back the cached instance for unchanged pixels, so a new instance
        // means new pixels (Discord mute → unmute) and must reach the bar.
        var icon1 = new System.Windows.Media.DrawingImage();
        var icon2 = new System.Windows.Media.DrawingImage();
        var a = new List<TrayMirrorItem> { new("Discord", 0, 0, "Discord", icon1) };
        var same = new List<TrayMirrorItem> { new("Discord", 0, 0, "Discord", icon1) };
        var changed = new List<TrayMirrorItem> { new("Discord", 0, 0, "Discord", icon2) };

        Assert.True(TrayMirrorDiff.IsSameSnapshot(a, same));
        Assert.False(TrayMirrorDiff.IsSameSnapshot(a, changed));
    }

    [Fact]
    public void IsSameSnapshot_OrderTooltipOrCountChange_IsAChange()
    {
        var a = new List<TrayMirrorItem> { Item("A", 0), Item("B", 1) };

        Assert.False(TrayMirrorDiff.IsSameSnapshot(a, [Item("B", 0), Item("A", 1)]));
        Assert.False(TrayMirrorDiff.IsSameSnapshot(a, [Item("A", 0)]));
        Assert.False(TrayMirrorDiff.IsSameSnapshot(a, [Item("A", 0), new("B", 1, 1, "B: 83%", null)]));
        Assert.False(TrayMirrorDiff.IsSameSnapshot(a, [Item("A", 0), Item("B", 2)]));
    }
}