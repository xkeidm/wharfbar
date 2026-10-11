using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockPresenceTests
{
    [Fact]
    public void KeyFor_PinnedAppKeepsItsPathWhenItStartsRunning()
    {
        const string path = @"C:\Apps\Note.exe";
        var closed = new DockAppItem(
            path, path, string.Empty, "Note",
            IsPinned: true, IsRunning: false, IntPtr.Zero, DockSection.Pinned, string.Empty);
        var running = new DockAppItem(
            "Microsoft.Note_app", path, string.Empty, "Note",
            IsPinned: true, IsRunning: true, new IntPtr(12), DockSection.Pinned, string.Empty,
            WindowCount: 1);

        Assert.Equal(DockPresence.KeyFor(closed), DockPresence.KeyFor(running));
    }

    [Fact]
    public void KeyFor_EmptyPathFallsBackToId()
    {
        var item = new DockAppItem(
            "pid:42", string.Empty, string.Empty, "App",
            IsPinned: false, IsRunning: true, new IntPtr(7), DockSection.Running, string.Empty);

        Assert.Equal("id:pid:42", DockPresence.KeyFor(item));
    }

    [Fact]
    public void Diff_InsertAndRemove_KeepNeighboursAndMarkTheChange()
    {
        Assert.Equal(
            new[] { Slot("a", DockSlotKind.Enter) },
            DockPresence.Diff([], ["a"]));

        Assert.Equal(
            new[] { Slot("a", DockSlotKind.Exit) },
            DockPresence.Diff(["a"], []));

        Assert.Equal(
            new[] { Slot("a", DockSlotKind.Stay), Slot("b", DockSlotKind.Enter), Slot("c", DockSlotKind.Stay) },
            DockPresence.Diff(["a", "c"], ["a", "b", "c"]));

        Assert.Equal(
            new[] { Slot("a", DockSlotKind.Stay), Slot("b", DockSlotKind.Exit), Slot("c", DockSlotKind.Stay) },
            DockPresence.Diff(["a", "b", "c"], ["a", "c"]));
    }

    [Fact]
    public void Diff_Reorder_FollowsTheNewOrderWithoutExits()
    {
        Assert.Equal(
            new[] { Slot("b", DockSlotKind.Stay), Slot("a", DockSlotKind.Stay) },
            DockPresence.Diff(["a", "b"], ["b", "a"]));
    }

    [Fact]
    public void Diff_ExitBeforeTheFirstSurvivor_StaysInFront()
    {
        Assert.Equal(
            new[] { Slot("b", DockSlotKind.Exit), Slot("a", DockSlotKind.Stay) },
            DockPresence.Diff(["b", "a"], ["a"]));
    }

    private static DockSlotChange Slot(string key, DockSlotKind kind) => new(key, kind);
}
