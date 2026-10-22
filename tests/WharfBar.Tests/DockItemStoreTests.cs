using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockItemStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-dock-item-store-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private DockItemStore NewStore(out ConfigService config)
    {
        config = new ConfigService(_root);
        config.LoadOrCreate();
        return new DockItemStore(config);
    }

    /// <summary>
    /// A second service over the same temp root: it parses the <c>config.json</c> the first
    /// service wrote, so assertions against its store cross the real serialization boundary
    /// instead of reading back the in-memory list the first store handed over.
    /// </summary>
    private DockItemStore NewStoreFromDisk(out ConfigService config)
    {
        config = new ConfigService(_root);
        config.LoadOrCreate();
        return new DockItemStore(config);
    }

    private static List<PinnedApp> Three() =>
    [
        new PinnedApp(@"C:\apps\one.exe", string.Empty, "One"),
        new PinnedApp(@"C:\apps\two.exe", string.Empty, "Two"),
        new PinnedApp(@"C:\apps\three.exe", string.Empty, "Three"),
    ];

    [Fact]
    public void Normalize_WhenPathIsAbsolute_TrimsAndKeepsFullPath()
    {
        Assert.Equal(@"C:\apps\one.exe", DockItemStore.Normalize(@"  C:\apps\one.exe  "));
    }

    [Fact]
    public void Normalize_StripsTheExtendedPathPrefix()
    {
        Assert.Equal(
            @"C:\Windows\System32\notepad.exe",
            DockItemStore.Normalize(@"\\?\C:\Windows\System32\notepad.exe"));
        Assert.Equal(
            @"C:\Windows\System32\notepad.exe",
            DockItemStore.Normalize(@"\??\C:\Windows\System32\notepad.exe"));
    }

    [Fact]
    public void Normalize_WhenPathIsWhitespaceOnly_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DockItemStore.Normalize("   "));
    }

    /// <summary>
    /// Reaches the <c>GetFullPath</c> catch for real: past the Windows ~32 767-char limit
    /// <see cref="Path.GetFullPath(string)"/> throws <see cref="PathTooLongException"/>. The
    /// old "malformed" test never got there — whitespace short-circuits before the try and a
    /// relative path resolves fine against the CWD.
    /// </summary>
    [Fact]
    public void Normalize_WhenPathExceedsWindowsLengthLimit_FallsBackToTrimmedInput()
    {
        string tooLong = @"C:\" + new string('a', 40_000);

        Assert.Equal(tooLong, DockItemStore.Normalize(tooLong));
    }

    [Fact]
    public void Normalize_WhenPathIsRelative_ReturnsAbsolutePath()
    {
        string normalized = DockItemStore.Normalize("relative.exe");

        Assert.True(Path.IsPathFullyQualified(normalized), $"expected a rooted path, got '{normalized}'");
        Assert.EndsWith("relative.exe", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_WhenPathContainsNullCharacter_FallsBackToTrimmedInput()
    {
        // The other catch entry point: on .NET 8 an embedded NUL is the illegal character that
        // GetFullPath rejects outright (PowerShell 5.1's runtime also throws for "////", but
        // .NET 8 normalizes that one to a bare root instead).
        string illegal = "C:\\a\0b";

        Assert.Equal(illegal, DockItemStore.Normalize(illegal));
    }

    [Fact]
    public void IdFor_UsesNormalizedPath()
    {
        Assert.Equal(
            DockItemStore.Normalize(@"C:\apps\one.exe"),
            DockItemStore.IdFor(new PinnedApp(@"  C:\apps\one.exe", string.Empty, "One")));
    }

    [Fact]
    public void ContainsById_IgnoresCaseAndSurroundingSpace()
    {
        List<PinnedApp> items = Three();
        Assert.True(DockItemStore.ContainsById(items, "  c:\\APPS\\ONE.EXE "));
        Assert.False(DockItemStore.ContainsById(items, @"C:\apps\four.exe"));
    }

    [Fact]
    public void TryAddCore_WhenNew_AppendsWithNormalizedPath()
    {
        List<PinnedApp> items = [];
        Assert.True(DockItemStore.TryAddCore(items, @"  C:\apps\one.exe ", string.Empty, "One"));
        Assert.Equal(@"C:\apps\one.exe", Assert.Single(items).Path);
    }

    [Fact]
    public void TryAddCore_WhenDuplicatePath_IsRejected()
    {
        List<PinnedApp> items = Three();
        Assert.False(DockItemStore.TryAddCore(items, @"C:\apps\two.exe", string.Empty, "Two"));
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void TryAddCore_WhenDuplicateDiffersOnlyByCase_IsRejected()
    {
        List<PinnedApp> items = Three();
        Assert.False(DockItemStore.TryAddCore(items, @"C:\APPS\TWO.EXE", string.Empty, "Two"));
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void TryAddCore_WhenLabelBlank_DerivesLabelFromFileName()
    {
        List<PinnedApp> items = [];
        DockItemStore.TryAddCore(items, @"C:\tools\thing.exe", string.Empty, "   ");
        Assert.Equal("thing", Assert.Single(items).Label);
    }

    [Fact]
    public void TryAddCore_WhenLabelBlankAndPathEndsWithSeparator_UsesSeparatorTrimmedPath()
    {
        List<PinnedApp> items = [];
        Assert.True(DockItemStore.TryAddCore(items, @"C:\tools\", string.Empty, string.Empty));
        Assert.Equal(@"C:\tools", Assert.Single(items).Label);
    }

    [Fact]
    public void TryAddCore_WhenLabelBlankAndPathIsDriveRoot_UsesSeparatorTrimmedPath()
    {
        List<PinnedApp> items = [];
        Assert.True(DockItemStore.TryAddCore(items, @"C:\", string.Empty, string.Empty));
        Assert.Equal("C:", Assert.Single(items).Label);
    }

    [Fact]
    public void TryAddCore_WhenLabelBlankAndPathIsUncRoot_UsesSeparatorTrimmedPath()
    {
        List<PinnedApp> items = [];
        Assert.True(DockItemStore.TryAddCore(items, @"\\server\share\", string.Empty, string.Empty));
        Assert.Equal(@"\\server\share", Assert.Single(items).Label);
    }

    [Fact]
    public void TryAddCore_WhenLabelBlankAndPathIsAllSeparators_UsesStoredPath()
    {
        // GetFullPath collapses this to a bare root ("\\\" on .NET 8), so the name and the
        // separator-trimmed form are both empty — the raw stored path is the final floor.
        List<PinnedApp> items = [];
        Assert.True(DockItemStore.TryAddCore(items, "////", string.Empty, string.Empty));
        PinnedApp stored = Assert.Single(items);
        Assert.False(string.IsNullOrWhiteSpace(stored.Label));
        Assert.Equal(stored.Path, stored.Label);
    }

    [Fact]
    public void TryAddCore_WhenPathBlank_IsRejected()
    {
        List<PinnedApp> items = [];
        Assert.False(DockItemStore.TryAddCore(items, "   ", string.Empty, "Yok"));
        Assert.Empty(items);
    }

    [Fact]
    public void RemoveCore_WhenPresent_Removes()
    {
        List<PinnedApp> items = Three();
        Assert.True(DockItemStore.RemoveCore(items, DockItemStore.IdFor(items[1])));
        Assert.Equal(["One", "Three"], items.Select(i => i.Label));
    }

    [Fact]
    public void RemoveCore_WhenAbsent_ReturnsFalse()
    {
        List<PinnedApp> items = Three();
        Assert.False(DockItemStore.RemoveCore(items, @"C:\apps\nope.exe"));
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void MoveCore_MovesForwardPastTheEnd_AppendsLast()
    {
        List<PinnedApp> items = Three();
        Assert.True(DockItemStore.MoveCore(items, DockItemStore.IdFor(items[0]), 99));
        Assert.Equal(["Two", "Three", "One"], items.Select(i => i.Label));
    }

    [Fact]
    public void MoveCore_MovesBackwardToStart()
    {
        List<PinnedApp> items = Three();
        Assert.True(DockItemStore.MoveCore(items, DockItemStore.IdFor(items[2]), 0));
        Assert.Equal(["Three", "One", "Two"], items.Select(i => i.Label));
    }

    [Fact]
    public void MoveCore_WhenTargetIndexNegative_PutsFirst()
    {
        List<PinnedApp> items = Three();
        DockItemStore.MoveCore(items, DockItemStore.IdFor(items[1]), -5);
        Assert.Equal("Two", items[0].Label);
    }

    [Fact]
    public void MoveCore_WhenIdAbsent_ReturnsFalseAndKeepsOrder()
    {
        List<PinnedApp> items = Three();
        Assert.False(DockItemStore.MoveCore(items, @"C:\apps\nope.exe", 0));
        Assert.Equal(["One", "Two", "Three"], items.Select(i => i.Label));
    }

    [Fact]
    public void TryAdd_PersistsAndRaisesChanged()
    {
        DockItemStore store = NewStore(out ConfigService config);
        int changes = 0;
        store.Changed += (_, _) => changes++;
        int before = store.Items.Count;

        Assert.True(store.TryAdd(@"C:\apps\added.exe", "--x", "Added"));

        Assert.Equal(before + 1, store.Items.Count);
        Assert.Equal(1, changes);
        Assert.Equal(before + 1, new DockItemStore(config).Items.Count);
    }

    [Fact]
    public void TryAdd_WhenDuplicate_DoesNotRaiseChanged()
    {
        DockItemStore store = NewStore(out _);
        string existing = store.Items[0].Path;
        int changes = 0;
        store.Changed += (_, _) => changes++;

        Assert.False(store.TryAdd(existing, string.Empty, "Yine"));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Remove_PersistsRemoval()
    {
        DockItemStore store = NewStore(out ConfigService config);
        string target = store.Items[1].Path;
        int before = store.Items.Count;

        Assert.True(store.Remove(DockItemStore.IdFor(store.Items[1])));

        Assert.Equal(before - 1, store.Items.Count);
        Assert.DoesNotContain(new DockItemStore(config).Items, i => i.Path == target);
    }

    [Fact]
    public void Move_PersistsNewOrder()
    {
        DockItemStore store = NewStore(out ConfigService config);
        string lastPath = store.Items[^1].Path;

        Assert.True(store.Move(DockItemStore.IdFor(store.Items[^1]), 0));

        Assert.Equal(lastPath, store.Items[0].Path);
        Assert.Equal(lastPath, new DockItemStore(config).Items[0].Path);
    }

    [Fact]
    public void Reload_PicksUpExternalChange()
    {
        DockItemStore store = NewStore(out ConfigService config);
        config.Current.Dock.Items = [];
        config.Save(config.Current);

        Assert.NotEmpty(store.Items);
        store.Reload();
        Assert.Empty(store.Items);
    }

    [Fact]
    public void Reload_RaisesChanged()
    {
        DockItemStore store = NewStore(out _);
        int raised = 0;
        store.Changed += (_, _) => raised++;

        store.Reload();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void TryAdd_AndIsReadBackFromDisk()
    {
        DockItemStore store = NewStore(out _);
        int before = store.Items.Count;

        Assert.True(store.TryAdd(@"C:\apps\diskadded.exe", "--x", "DiskAdded"));

        IReadOnlyList<PinnedApp> reloaded = NewStoreFromDisk(out _).Items;
        Assert.Equal(before + 1, reloaded.Count);
        Assert.Equal(@"C:\apps\diskadded.exe", reloaded[^1].Path);
        Assert.Equal("--x", reloaded[^1].Args);
        Assert.Equal("DiskAdded", reloaded[^1].Label);
    }

    [Fact]
    public void Remove_AndIsReadBackFromDisk()
    {
        DockItemStore store = NewStore(out _);
        string target = store.Items[1].Path;
        string[] expected = store.Items.Where(i => i.Path != target).Select(i => i.Path).ToArray();

        Assert.True(store.Remove(DockItemStore.IdFor(store.Items[1])));

        IReadOnlyList<PinnedApp> reloaded = NewStoreFromDisk(out _).Items;
        Assert.Equal(expected, reloaded.Select(i => i.Path).ToArray());
        Assert.DoesNotContain(reloaded, i => i.Path == target);
    }

    [Fact]
    public void Move_AndIsReadBackFromDisk()
    {
        DockItemStore store = NewStore(out _);
        string[] before = store.Items.Select(i => i.Path).ToArray();
        string lastPath = before[^1];
        string[] expected = [lastPath, .. before[..^1]];

        Assert.True(store.Move(DockItemStore.IdFor(store.Items[^1]), 0));

        // Full order, not just the head or the count: a reordered entry lost on the way
        // through the serializer would still pass a count assertion.
        IReadOnlyList<PinnedApp> reloaded = NewStoreFromDisk(out _).Items;
        Assert.Equal(expected, reloaded.Select(i => i.Path).ToArray());
    }

    [Fact]
    public void SetOrder_PersistsAndCanonicalizesAcrossDisk()
    {
        DockItemStore store = NewStore(out _);
        store.SetOrder([@"C:\pin\a.lnk", @"  C:\pin\a.lnk  ", @"C:\pin\b.lnk", ""]);

        IReadOnlyList<string> reloaded = NewStoreFromDisk(out _).ItemOrder;
        Assert.Equal([@"C:\pin\a.lnk", @"C:\pin\b.lnk"], reloaded);
    }

    [Fact]
    public void ReorderOnStrip_MovesStoredPinAndFreezesSequence()
    {
        DockItemStore store = NewStore(out _);
        string[] before = store.Items.Select(static pin => pin.Path).ToArray();
        string[] frozen = [before[^1], .. before[..^1]];

        store.ReorderOnStrip(DockItemStore.IdFor(store.Items[^1]), frozen, storeIndex: 0);

        DockItemStore reloaded = NewStoreFromDisk(out _);
        Assert.Equal(frozen, reloaded.Items.Select(static pin => pin.Path));
        Assert.Equal(frozen, reloaded.ItemOrder);
    }

    [Fact]
    public void ReorderOnStrip_TaskbarOnlyKey_FreezesWithoutTouchingItems()
    {
        DockItemStore store = NewStore(out _);
        string[] before = store.Items.Select(static pin => pin.Path).ToArray();
        string[] frozen = [@"C:\lnks\taskbar-pin.lnk", .. before];

        store.ReorderOnStrip(@"C:\lnks\taskbar-pin.lnk", frozen, storeIndex: 0);

        DockItemStore reloaded = NewStoreFromDisk(out _);
        Assert.Equal(before, reloaded.Items.Select(static pin => pin.Path));
        Assert.Equal(frozen, reloaded.ItemOrder);
    }

    [Fact]
    public void Move_UnfreezesTheMovedKey()
    {
        DockItemStore store = NewStore(out _);
        string secondId = DockItemStore.IdFor(store.Items[1]);
        string firstId = DockItemStore.IdFor(store.Items[0]);
        store.SetOrder([secondId, firstId]);
        Assert.Equal(2, store.ItemOrder.Count);

        Assert.True(store.Move(secondId, 0));

        DockItemStore reloaded = NewStoreFromDisk(out _);
        Assert.DoesNotContain(reloaded.ItemOrder, key => key == secondId);
        Assert.Equal([firstId], reloaded.ItemOrder);
    }
}
