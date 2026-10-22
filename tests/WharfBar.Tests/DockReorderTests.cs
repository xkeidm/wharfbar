using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockReorderTests
{
    [Fact]
    public void StoreIndexBeforeSlot_SkipsLaunchpadAndBuiltIns()
    {
        List<PinnedApp> store = Pins("A", "B", "C");
        DockAppItem[] strip =
        [
            Tile("builtin:launchpad", DockTileKind.Launchpad, pinned: false),
            Pin("A"),
            Pin("B"),
            Pin("C"),
            Tile("builtin:downloads", DockTileKind.Downloads, pinned: false),
            Running("Calc"),
            Tile("builtin:trash", DockTileKind.Trash, pinned: false),
        ];

        Assert.Equal(0, Index(strip, 0, store));
        Assert.Equal(0, Index(strip, 1, store));
        Assert.Equal(1, Index(strip, 2, store));
        Assert.Equal(2, Index(strip, 3, store));
        Assert.Equal(3, Index(strip, 4, store));
        Assert.Equal(3, Index(strip, 6, store));
    }

    [Fact]
    public void StoreIndexBeforeSlot_IgnoresTaskbarPinsThatAreNotInTheStore()
    {
        List<PinnedApp> store = Pins("A", "B");
        DockAppItem[] strip =
        [
            Tile("builtin:launchpad", DockTileKind.Launchpad, pinned: false),
            Pin("TaskbarOnly"),
            Pin("A"),
            Pin("B"),
            Tile("builtin:downloads", DockTileKind.Downloads, pinned: false),
        ];

        Assert.Equal(0, Index(strip, 2, store));
        Assert.Equal(1, Index(strip, 3, store));
        Assert.Equal(2, Index(strip, 4, store));
    }

    [Fact]
    public void MoveCore_UsesTheStoreIndex_NotTheVisualSlot()
    {
        var items = Pins("A", "B", "C");
        DockAppItem[] strip =
        [
            Tile("builtin:launchpad", DockTileKind.Launchpad, pinned: false),
            Pin("A"),
            Pin("B"),
            Pin("C"),
        ];

        int visualSlotOfB = 2;
        int storeIndex = Index(strip, visualSlotOfB, items);
        Assert.Equal(1, storeIndex);

        Assert.True(DockItemStore.MoveCore(items, @"C:\apps\A.exe", storeIndex));
        Assert.Equal(["B", "A", "C"], items.Select(static item => item.Label).ToArray());
    }

    [Fact]
    public void OrderAfterDrop_MovesTheDraggedKeyBeforeTheTarget()
    {
        IReadOnlyList<string> order = DockReorder.OrderAfterDrop(
            [@"C:\a.exe", @"C:\b.exe", @"C:\c.exe"],
            @"C:\c.exe",
            insertAt: 0);

        Assert.Equal([@"C:\c.exe", @"C:\a.exe", @"C:\b.exe"], order);
    }

    [Fact]
    public void OrderAfterDrop_ClampsInsertPastTheEnd()
    {
        IReadOnlyList<string> order = DockReorder.OrderAfterDrop(
            [@"C:\a.exe", @"C:\b.exe", @"C:\c.exe"],
            @"C:\a.exe",
            insertAt: 99);

        Assert.Equal([@"C:\b.exe", @"C:\c.exe", @"C:\a.exe"], order);
    }

    [Fact]
    public void OrderAfterDrop_UnknownDraggedKey_LeavesSequenceAlone()
    {
        IReadOnlyList<string> keys = [@"C:\a.exe", @"C:\b.exe"];
        IReadOnlyList<string> order = DockReorder.OrderAfterDrop(keys, @"C:\x.exe", insertAt: 0);
        Assert.Same(keys, order);
    }

    [Fact]
    public void IsStoredPin_AcceptsFolderPinsAndRejectsBuiltIns()
    {
        List<PinnedApp> store =
        [
            new(@"C:\Work", string.Empty, "Work"),
            new(@"C:\apps\A.exe", string.Empty, "A"),
        ];

        Assert.True(DockReorder.IsStoredPin(Folder(@"C:\Work"), store));
        Assert.True(DockReorder.IsStoredPin(Pin("A"), store));
        Assert.False(DockReorder.IsStoredPin(Pin("Missing"), store));
        Assert.False(DockReorder.IsStoredPin(
            Tile("builtin:launchpad", DockTileKind.Launchpad, pinned: false),
            store));
        Assert.False(DockReorder.IsStoredPin(Running("Calc"), store));
    }

    private static int Index(IReadOnlyList<DockAppItem> strip, int slot, IReadOnlyList<PinnedApp> store) =>
        DockReorder.StoreIndexBeforeSlot(strip, slot, item => DockReorder.IsStoredPin(item, store));

    private static List<PinnedApp> Pins(params string[] labels) =>
        labels.Select(label => new PinnedApp($@"C:\apps\{label}.exe", string.Empty, label)).ToList();

    private static DockAppItem Pin(string label) =>
        new(
            $@"C:\apps\{label}.exe",
            $@"C:\apps\{label}.exe",
            string.Empty,
            label,
            true,
            false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty);

    private static DockAppItem Folder(string path) =>
        new(
            path,
            path,
            string.Empty,
            "Work",
            true,
            false,
            IntPtr.Zero,
            DockSection.Pinned,
            string.Empty,
            Kind: DockTileKind.Folder);

    private static DockAppItem Running(string label) =>
        new(
            label,
            $@"C:\apps\{label}.exe",
            string.Empty,
            label,
            false,
            true,
            (IntPtr)4,
            DockSection.Running,
            string.Empty);

    private static DockAppItem Tile(string id, DockTileKind kind, bool pinned) =>
        new(
            id,
            string.Empty,
            string.Empty,
            id,
            pinned,
            false,
            IntPtr.Zero,
            kind == DockTileKind.Trash ? DockSection.Running : DockSection.Pinned,
            string.Empty,
            Kind: kind);
}
