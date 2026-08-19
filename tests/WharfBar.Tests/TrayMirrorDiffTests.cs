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
}