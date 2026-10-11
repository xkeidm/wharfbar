using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockAppsComposerTests
{
    private const string Notepad = @"C:\Windows\System32\notepad.exe";
    private const string Calc = @"C:\Windows\System32\calc.exe";

    private static List<PinnedApp> Pins(params string[] paths) =>
        [.. paths.Select(p => new PinnedApp(p, string.Empty, System.IO.Path.GetFileNameWithoutExtension(p)))];

    private static RunningAppInfo Running(string id, string path, string label, IntPtr hwnd = default, uint pid = 0) =>
        new(id, path, label, hwnd, pid);

    private static AppWindowInfo Minimized(IntPtr hwnd, uint pid, string title) =>
        new(hwnd, title, pid, IsMinimized: true);

    private static IReadOnlyList<DockAppItem> Compose(
        IReadOnlyList<PinnedApp>? pins = null,
        IReadOnlyList<RunningAppInfo>? running = null,
        IReadOnlyList<AppWindowInfo>? minimized = null,
        bool showRunning = true,
        Func<string, PinMatchKey?>? resolveShortcut = null,
        IReadOnlyList<PinnedApp>? taskbarPins = null) =>
        DockAppsComposer.Compose(
            pins ?? [], running ?? [], minimized ?? [], showRunning, resolveShortcut, taskbarPins);

    [Fact]
    public void Compose_KeepsConfiguredPinOrder()
    {
        IReadOnlyList<DockAppItem> items = Compose(Pins(Notepad, Calc));
        Assert.Equal(["notepad", "calc"], items.Select(i => i.Label));
        Assert.All(items, i => Assert.Equal(DockSection.Pinned, i.Section));
    }

    [Fact]
    public void Compose_MarksPinnedItemRunningWhenPathMatches()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad), [Running("Microsoft.Notepad", Notepad, "Not Defteri", (IntPtr)7)]);
        DockAppItem item = Assert.Single(items);
        Assert.True(item.IsRunning);
        Assert.Equal(1, item.WindowCount);
        Assert.Equal((IntPtr)7, item.MainWindowHandle);
        Assert.Equal("Microsoft.Notepad", item.Id);
    }

    [Fact]
    public void ApplyOpenMarks_RestoredWindow_KeepsTheColoredMark()
    {
        var hwnd = (IntPtr)7;
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad), [Running("Microsoft.Notepad", Notepad, "Not Defteri", hwnd)]);
        AppWindowInfo[] windows = [new(hwnd, "Not Defteri", 10, IsMinimized: false)];

        DockAppItem marked = Assert.Single(DockAppsComposer.ApplyOpenMarks(items, windows));
        Assert.True(marked.IsRunning);
        Assert.True(marked.IsOpenOnDesktop);
    }

    [Fact]
    public void ApplyOpenMarks_MinimizedWindow_DropsTheColoredMark()
    {
        var hwnd = (IntPtr)7;
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad), [Running("Microsoft.Notepad", Notepad, "Not Defteri", hwnd)]);
        AppWindowInfo[] windows = [new(hwnd, "Not Defteri", 10, IsMinimized: true)];

        DockAppItem marked = Assert.Single(DockAppsComposer.ApplyOpenMarks(items, windows));
        Assert.True(marked.IsRunning);
        Assert.False(marked.IsOpenOnDesktop);
    }

    [Fact]
    public void ApplyOpenMarks_RestoredSiblingOfTheSameProcess_KeepsTheColoredMark()
    {
        var iconic = (IntPtr)7;
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad), [Running("Microsoft.Notepad", Notepad, "Not Defteri", iconic)]);
        AppWindowInfo[] windows =
        [
            new(iconic, "Not Defteri", 10, IsMinimized: true),
            new((IntPtr)8, "Not Defteri", 10, IsMinimized: false),
        ];

        DockAppItem marked = Assert.Single(DockAppsComposer.ApplyOpenMarks(items, windows));
        Assert.True(marked.IsOpenOnDesktop);
    }

    [Fact]
    public void Compose_MatchesRunningAppToAtMostOnePin()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad, Notepad.ToUpperInvariant()),
            [Running("A", Notepad, "Not Defteri", (IntPtr)7)]);

        Assert.Equal(2, items.Count);
        Assert.Single(items, i => i.IsRunning);
        Assert.Equal(2, items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Compose_UsesPinPathAsIdWhenNothingIsRunning()
    {
        DockAppItem item = Assert.Single(Compose(Pins(Notepad)));
        Assert.Equal(Notepad, item.Id);
        Assert.False(item.IsRunning);
        Assert.Equal(0, item.WindowCount);
        Assert.Equal(IntPtr.Zero, item.MainWindowHandle);
    }

    [Fact]
    public void Compose_AppendsUnpinnedRunningAppsSortedByLabel()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [Running("c", Calc, "Zebra"), Running("b", @"C:\x.exe", "Alpha"), Running("a", Notepad, "Not Defteri")]);

        // The pinned tile keeps its own label; only the unbound running apps are re-sorted.
        Assert.Equal(["notepad", "Alpha", "Zebra"], items.Select(i => i.Label));
        Assert.Equal(DockSection.Running, items[1].Section);
        Assert.False(items[1].IsPinned);
    }

    [Fact]
    public void Compose_OrdersUnpinnedRunningAppsCaseInsensitively()
    {
        // "a" before "B" is alphabetical but not ordinal: OrdinalIgnoreCase puts "a" first
        // because it compares letters, Ordinal puts "B" (0x42) before "a" (0x61).
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [Running("u", @"C:\B.exe", "B"), Running("l", @"C:\a.exe", "a")]);

        Assert.Equal(["a", "B"], items.Select(i => i.Label));
    }

    [Fact]
    public void Compose_WhenShowRunningAppsFalse_OmitsRunningAndMinimizedSections()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [Running("c", Calc, "Calc")],
            [Minimized((IntPtr)9, 42, "Belge")],
            showRunning: false);

        Assert.Single(items);
        Assert.Equal(DockSection.Pinned, Assert.Single(items).Section);
    }

    [Fact]
    public void Compose_PinnedRunningSameApp_IsOneTileEvenWhenAWindowIsMinimized()
    {
        // The running app's main window has to be real: the composer learns the process id
        // from that hwnd, and only then can it tell the minimized window belongs to the pin.
        using var window = new DummyWindow();
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [Running("np", Notepad, "Not Defteri", window.Handle)],
            [Minimized((IntPtr)11, window.Pid, "Belge"), Minimized((IntPtr)12, window.Pid, "İkinci")]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Pinned, item.Section);
        Assert.True(item.IsPinned);
        Assert.True(item.IsRunning);
        Assert.Equal(Notepad, item.Path);
    }

    [Fact]
    public void Compose_MinimizedWindowOfUnpinnedRunningApp_DoesNotAddASecondTile()
    {
        using var window = new DummyWindow();
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [Running("np", Notepad, "Not Defteri", window.Handle)],
            [Minimized((IntPtr)11, window.Pid, "Belge")]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Running, item.Section);
        Assert.Equal(Notepad, item.Path);
        Assert.Equal("Not Defteri", item.Label);
        Assert.False(item.IsPinned);
    }

    [Fact]
    public void Compose_TwoMinimizedWindowsOfUnpinnedApp_AreOneTile()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [],
            [Minimized((IntPtr)12, 100, "İki"), Minimized((IntPtr)11, 100, "Bir")]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Running, item.Section);
        Assert.False(item.IsPinned);
        Assert.True(item.IsRunning);
        Assert.Equal(2, item.WindowCount);
        Assert.Equal("Bir", item.Label);
        Assert.Equal((IntPtr)11, item.MainWindowHandle);
    }

    [Fact]
    public void Compose_MinimizedOnlyApps_AreOneTilePerProcessAfterPins()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [],
            [Minimized((IntPtr)21, 100, "Z"), Minimized((IntPtr)22, 200, "M"), Minimized((IntPtr)23, 100, "A")]);

        Assert.Equal(["notepad", "A", "M"], items.Select(i => i.Label));
        Assert.Equal(DockSection.Pinned, items[0].Section);
        Assert.All(items.Skip(1), i => Assert.Equal(DockSection.Running, i.Section));
        Assert.Equal((IntPtr)23, items[1].MainWindowHandle);
    }

    [Fact]
    public void Compose_WhenMinimizedOnlyLabelsMatch_OrdersTilesByHandle()
    {
        // Same-titled windows are the common case. Without a handle tiebreak the two
        // process groups would follow scan order, and the 800 ms rescan would swap them.
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [],
            [Minimized((IntPtr)31, 100, "Belge"), Minimized((IntPtr)30, 200, "Belge"), Minimized((IntPtr)32, 100, "Belge")]);

        Assert.Equal(2, items.Count);
        Assert.Equal([(IntPtr)30, (IntPtr)31], items.Select(i => i.MainWindowHandle));
    }

    [Fact]
    public void Compose_UnmappedMinimizedWindowFallsBackToWindowTitle()
    {
        IReadOnlyList<DockAppItem> items = Compose([], [], [Minimized((IntPtr)13, 999, "Yalnız")]);
        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Running, item.Section);
        Assert.Equal(string.Empty, item.Path);
        Assert.Equal("Yalnız", item.Label);
    }

    [Fact]
    public void Compose_MatchesPinToRunningAppByAppUserModelId()
    {
        const string aumid = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";
        IReadOnlyList<DockAppItem> items = Compose(
            [new PinnedApp(aumid, string.Empty, "Not Defteri")],
            [Running(aumid, @"C:\Windows\SystemApps\Microsoft.WindowsNotepad\Notepad.exe", "Not Defteri", (IntPtr)7)]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Pinned, item.Section);
        Assert.True(item.IsRunning);
        Assert.Equal(aumid, item.Id);
        Assert.Equal(aumid, item.Path);
    }

    [Fact]
    public void Compose_MatchesShortcutPinToResolvedTarget()
    {
        const string lnk = @"C:\Users\me\Desktop\Notepad.lnk";
        IReadOnlyList<DockAppItem> items = Compose(
            [new PinnedApp(lnk, string.Empty, "Not Defteri")],
            [Running("np", Notepad, "Not Defteri", (IntPtr)7)],
            resolveShortcut: path => string.Equals(path, lnk, StringComparison.OrdinalIgnoreCase)
                ? new PinMatchKey(Notepad, null, IsDirectory: false)
                : null);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Pinned, item.Section);
        Assert.True(item.IsRunning);
        Assert.Equal(lnk, item.Path);
    }

    [Fact]
    public void Compose_MatchesShortcutPinByAppUserModelIdWhenTargetPathDiffers()
    {
        const string lnk = @"C:\Users\me\Desktop\Notepad.lnk";
        const string aumid = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";
        IReadOnlyList<DockAppItem> items = Compose(
            [new PinnedApp(lnk, string.Empty, "Not Defteri")],
            [Running(aumid, @"C:\Windows\SystemApps\Microsoft.WindowsNotepad\Notepad.exe", "Not Defteri", (IntPtr)7)],
            resolveShortcut: _ => new PinMatchKey(string.Empty, aumid, IsDirectory: false));

        DockAppItem item = Assert.Single(items);
        Assert.True(item.IsRunning);
        Assert.Equal(DockSection.Pinned, item.Section);
    }

    [Fact]
    public void Compose_DoesNotMatchFolderPinToExplorer()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [new PinnedApp(@"C:\Users\me\Documents", string.Empty, "Belgeler")],
            [Running("Microsoft.Windows.Explorer", @"C:\Windows\explorer.exe", "Dosya Gezgini", (IntPtr)4)]);

        Assert.Equal(2, items.Count);
        Assert.Equal(DockSection.Pinned, items[0].Section);
        Assert.False(items[0].IsRunning);
        Assert.Equal(@"C:\Users\me\Documents", items[0].Path);
        Assert.Equal(DockSection.Running, items[1].Section);
        Assert.Equal(@"C:\Windows\explorer.exe", items[1].Path);
    }

    [Fact]
    public void Compose_DoesNotMatchFolderShortcutToExplorerByAppUserModelId()
    {
        const string aumid = "Microsoft.Windows.Explorer";
        IReadOnlyList<DockAppItem> items = Compose(
            [new PinnedApp(@"C:\Users\me\Desktop\Belgeler.lnk", string.Empty, "Belgeler")],
            [Running(aumid, @"C:\Windows\explorer.exe", "Dosya Gezgini", (IntPtr)4)],
            resolveShortcut: _ => new PinMatchKey(@"C:\Users\me\Documents", aumid, IsDirectory: true));

        Assert.Equal(2, items.Count);
        Assert.False(items[0].IsRunning);
        Assert.Equal(DockSection.Pinned, items[0].Section);
        Assert.Equal(DockSection.Running, items[1].Section);
    }

    [Fact]
    public void Compose_DoesNotMatchDifferentExecutablesWithTheSameFileName()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(@"C:\A\tool.exe"),
            [Running("t", @"C:\B\tool.exe", "tool", (IntPtr)3)]);

        Assert.Equal(2, items.Count);
        Assert.False(items[0].IsRunning);
        Assert.Equal(DockSection.Running, items[1].Section);
    }

    [Fact]
    public void Compose_TaskbarPins_ShowClosedAppsOnTheLeftAndBindRunningOnes()
    {
        const string steam = @"C:\Program Files\Steam\steam.exe";
        IReadOnlyList<DockAppItem> items = Compose(
            pins: [],
            running: [Running("calc", Calc, "Hesap Makinesi", (IntPtr)4), Running("steam", steam, "Steam", (IntPtr)8)],
            taskbarPins: [new PinnedApp(Notepad, string.Empty, "Not Defteri"), new PinnedApp(Calc, string.Empty, "Hesap")]);

        Assert.Equal(["Not Defteri", "Hesap", "Steam"], items.Select(i => i.Label));
        Assert.Equal(DockSection.Pinned, items[0].Section);
        Assert.False(items[0].IsRunning);
        Assert.Equal(DockSection.Pinned, items[1].Section);
        Assert.True(items[1].IsRunning);
        Assert.Equal(DockSection.Running, items[2].Section);
        Assert.False(items[2].IsPinned);
    }

    [Fact]
    public void Compose_TaskbarOrderWins_AndDockOnlyPinsFollowOnce()
    {
        const string steam = @"C:\Program Files\Steam\steam.exe";
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad, Calc),
            taskbarPins: [new PinnedApp(steam, string.Empty, "Steam"), new PinnedApp(Notepad, string.Empty, "Not Defteri")]);

        Assert.Equal(["Steam", "Not Defteri", "calc"], items.Select(i => i.Label));
        Assert.All(items, i => Assert.Equal(DockSection.Pinned, i.Section));
        Assert.All(items, i => Assert.False(i.IsRunning));
        Assert.Equal(Notepad, items[1].Path);
    }

    [Fact]
    public void Compose_DedupesDockShortcutAgainstTheTaskbarTarget()
    {
        const string lnk = @"C:\Users\me\Desktop\Notepad.lnk";
        IReadOnlyList<DockAppItem> items = Compose(
            [new PinnedApp(lnk, string.Empty, "Kısayol")],
            taskbarPins: Pins(Notepad),
            resolveShortcut: path => string.Equals(path, lnk, StringComparison.OrdinalIgnoreCase)
                ? new PinMatchKey(Notepad, null, IsDirectory: false)
                : null);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(Notepad, item.Path);
        Assert.Equal("notepad", item.Label);
        Assert.False(item.IsRunning);
    }

    [Fact]
    public void Compose_FolderTaskbarPinDoesNotSwallowExplorer()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            pins: [],
            running: [Running("Microsoft.Windows.Explorer", @"C:\Windows\explorer.exe", "Dosya Gezgini", (IntPtr)4)],
            taskbarPins: [new PinnedApp(@"C:\Windows", string.Empty, "Windows")]);

        Assert.Equal(2, items.Count);
        Assert.Equal(DockSection.Pinned, items[0].Section);
        Assert.False(items[0].IsRunning);
        Assert.Equal(@"C:\Windows", items[0].Path);
        Assert.Equal(DockSection.Running, items[1].Section);
    }

    [Fact]
    public void Compose_WhenShowRunningAppsFalse_StillShowsClosedTaskbarPins()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            pins: [],
            running: [Running("c", Calc, "Calc")],
            showRunning: false,
            taskbarPins: Pins(Notepad));

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Pinned, item.Section);
        Assert.False(item.IsRunning);
    }

    [Fact]
    public void Compose_WhenAllInputsEmpty_YieldsNoItems()
    {
        Assert.Empty(Compose());
    }

    [Fact]
    public void ItemsEqual_WhenSectionDiffers_IsFalse()
    {
        List<DockAppItem> a = [new("x", "p", string.Empty, "L", true, true, IntPtr.Zero, DockSection.Pinned, string.Empty)];
        List<DockAppItem> b = [a[0] with { Section = DockSection.Running }];
        Assert.False(DockAppsComposer.ItemsEqual(a, b));
    }

    [Fact]
    public void ItemsEqual_WhenWindowTitleDiffers_IsFalse()
    {
        List<DockAppItem> a = [new("x", "p", string.Empty, "L", true, true, IntPtr.Zero, DockSection.Minimized, "A")];
        List<DockAppItem> b = [a[0] with { WindowTitle = "B" }];
        Assert.False(DockAppsComposer.ItemsEqual(a, b));
    }

    [Fact]
    public void ItemsEqual_WhenEverythingMatches_IsTrue()
    {
        List<DockAppItem> a = [new("x", "p", string.Empty, "L", true, true, IntPtr.Zero, DockSection.Pinned, string.Empty)];
        List<DockAppItem> b = [new("X", "P", string.Empty, "L", true, true, IntPtr.Zero, DockSection.Pinned, string.Empty)];
        Assert.True(DockAppsComposer.ItemsEqual(a, b));
    }

    [Fact]
    public void Tooltip_ForMinimizedTile_NamesAppAndWindow()
    {
        DockAppItem named = new("x", "p", string.Empty, "Not Defteri", false, true, (IntPtr)1, DockSection.Minimized, "Belge");
        Assert.Equal("Not Defteri — Belge", named.Tooltip);

        DockAppItem same = named with { WindowTitle = "Not Defteri" };
        Assert.Equal("Not Defteri", same.Tooltip);
    }

    [Fact]
    public void Compose_PinnedAumidWindowAndPathWindow_IsOneRunningPin()
    {
        // Foreground window reports an AppUserModelId; a second top-level window of the same
        // exe (typical once the app is in the background) does not. Both used to become tiles.
        const string aumid = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [
                Running(aumid, Notepad, "Not Defteri", (IntPtr)7, pid: 10),
                Running(Notepad, Notepad, "Not Defteri", (IntPtr)8, pid: 11),
            ],
            [Minimized((IntPtr)8, 11, "Arka plan")]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Pinned, item.Section);
        Assert.True(item.IsPinned);
        Assert.True(item.IsRunning);
        Assert.Equal(Notepad, item.Path);
        Assert.Equal(2, item.WindowCount);
        Assert.NotNull(item.ProcessIds);
        Assert.Contains(10u, item.ProcessIds);
        Assert.Contains(11u, item.ProcessIds);
    }

    [Fact]
    public void Compose_BackgroundWindowOfTheSameProcess_StaysOnThePin()
    {
        const string aumid = "Contoso.App";
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [
                Running(aumid, Notepad, "Not Defteri", (IntPtr)7, pid: 10),
                Running("hwnd:8", string.Empty, "Helper", (IntPtr)8, pid: 10),
            ],
            [new AppWindowInfo((IntPtr)8, "Helper", 10, IsMinimized: true)]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Pinned, item.Section);
        Assert.True(item.IsRunning);
        Assert.Equal(2, item.WindowCount);
    }

    [Fact]
    public void Compose_UnpinnedSameExeTwoProcesses_IsOneTileOnTheRight()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [
                Running(Notepad, Notepad, "Not Defteri", (IntPtr)7, pid: 10),
                Running(Notepad, Notepad, "Not Defteri", (IntPtr)8, pid: 11),
            ],
            [Minimized((IntPtr)8, 11, "Arka plan")]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Running, item.Section);
        Assert.False(item.IsPinned);
        Assert.True(item.IsRunning);
        Assert.Equal(2, item.WindowCount);
        Assert.NotNull(item.ProcessIds);
        Assert.Contains(10u, item.ProcessIds);
        Assert.Contains(11u, item.ProcessIds);
    }

    [Fact]
    public void Compose_UnpinnedMinimized_IsOneTileOnTheRight()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [Running(Notepad, Notepad, "Not Defteri", (IntPtr)7, pid: 10)],
            [Minimized((IntPtr)7, 10, "Not Defteri")]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Running, item.Section);
        Assert.False(item.IsPinned);
        Assert.True(item.IsRunning);
        Assert.Equal(1, item.WindowCount);
    }

    [Fact]
    public void Compose_ExtendedProcessPath_BindsToTheWin32Pin()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [Running("np", @"\\?\C:\Windows\System32\notepad.exe", "Not Defteri", (IntPtr)7)]);

        DockAppItem item = Assert.Single(items);
        Assert.Equal(DockSection.Pinned, item.Section);
        Assert.True(item.IsRunning);
        Assert.Equal(Notepad, item.Path);
    }

    [Fact]
    public void Compose_DifferentExecutableNames_StayDistinctUnlessTheyShareAnAppUserModelId()
    {
        IReadOnlyList<DockAppItem> distinct = Compose(
            [],
            [
                Running("a", @"C:\App\app.exe", "App", (IntPtr)1, pid: 1),
                Running("b", @"C:\App\app.background.exe", "App Bg", (IntPtr)2, pid: 2),
            ]);
        Assert.Equal(2, distinct.Count);

        const string aumid = "Contoso.App";
        IReadOnlyList<DockAppItem> linked = Compose(
            [new PinnedApp(aumid, string.Empty, "App")],
            [
                Running(aumid, @"C:\App\app.exe", "App", (IntPtr)1, pid: 1),
                Running(aumid, @"C:\Windows\System32\ApplicationFrameHost.exe", "App", (IntPtr)2, pid: 2),
            ]);

        DockAppItem item = Assert.Single(linked);
        Assert.Equal(DockSection.Pinned, item.Section);
        Assert.True(item.IsRunning);
        Assert.Equal(2, item.WindowCount);
    }

    [Fact]
    public void Compose_System32AndSysWow64_StayDistinct()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(@"C:\Windows\System32\notepad.exe"),
            [Running("wow", @"C:\Windows\SysWOW64\notepad.exe", "notepad", (IntPtr)3, pid: 9)]);

        Assert.Equal(2, items.Count);
        Assert.False(items[0].IsRunning);
        Assert.Equal(DockSection.Running, items[1].Section);
    }

    [Fact]
    public void ApplyOpenMarks_RestoredSiblingProcess_KeepsTheColoredMark()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [
                Running(Notepad, Notepad, "Not Defteri", (IntPtr)7, pid: 10),
                Running(Notepad, Notepad, "Not Defteri", (IntPtr)8, pid: 11),
            ]);
        AppWindowInfo[] windows =
        [
            new((IntPtr)7, "A", 10, IsMinimized: true),
            new((IntPtr)8, "B", 11, IsMinimized: false),
        ];

        DockAppItem marked = Assert.Single(DockAppsComposer.ApplyOpenMarks(items, windows));
        Assert.True(marked.IsRunning);
        Assert.True(marked.IsOpenOnDesktop);
        Assert.Equal(2, marked.WindowCount);
    }

    [Fact]
    public void ApplyOpenMarks_EveryProcessMinimized_DropsTheColoredMark()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [
                Running(Notepad, Notepad, "Not Defteri", (IntPtr)7, pid: 10),
                Running(Notepad, Notepad, "Not Defteri", (IntPtr)8, pid: 11),
            ]);
        AppWindowInfo[] windows =
        [
            new((IntPtr)7, "A", 10, IsMinimized: true),
            new((IntPtr)8, "B", 11, IsMinimized: true),
        ];

        DockAppItem marked = Assert.Single(DockAppsComposer.ApplyOpenMarks(items, windows));
        Assert.True(marked.IsRunning);
        Assert.False(marked.IsOpenOnDesktop);
        Assert.Equal(DockSection.Pinned, marked.Section);
    }

    [Fact]
    public void Tooltip_WhenSeveralWindows_IncludesTheCount()
    {
        DockAppItem item = new(
            "x", Notepad, string.Empty, "Not Defteri", true, true, (IntPtr)1,
            DockSection.Pinned, string.Empty, WindowCount: 3);
        Assert.Equal("Not Defteri (3)", item.Tooltip);
    }

    [Fact]
    public void Tooltip_ForPinnedOrRunningTile_IsTheAppLabel()
    {
        DockAppItem pinned = new("x", "p", string.Empty, "Not Defteri", true, true, (IntPtr)1, DockSection.Pinned, string.Empty);
        Assert.Equal("Not Defteri", pinned.Tooltip);

        DockAppItem running = pinned with { Section = DockSection.Running, WindowTitle = "Belge" };
        Assert.Equal("Not Defteri", running.Tooltip);
    }

    private static IReadOnlyList<DockAppItem> ThreeSections() =>
    [
        new("a", "p1", string.Empty, "P1", true, true, (IntPtr)1, DockSection.Pinned, string.Empty),
        new("b", "p2", string.Empty, "P2", true, false, IntPtr.Zero, DockSection.Pinned, string.Empty),
        new("c", "r1", string.Empty, "R1", false, true, (IntPtr)3, DockSection.Running, string.Empty),
        new("d", "m1", string.Empty, "M1", false, true, (IntPtr)4, DockSection.Minimized, "W1"),
    ];

    [Fact]
    public void Build_RowCountIsItemsWithNoSeparatorSlots()
    {
        IReadOnlyList<DockAppItem> items = ThreeSections();
        IReadOnlyList<DockStripRow> rows = DockStripBuilder.Build(items);

        // One continuous strip: a section change no longer costs a rendered row.
        Assert.Equal(items.Count, rows.Count);
        Assert.All(rows, row => Assert.NotNull(row.Item));
    }

    [Fact]
    public void Build_KeepsSectionOrderWithoutDividerRows()
    {
        IReadOnlyList<DockStripRow> rows = DockStripBuilder.Build(ThreeSections());

        Assert.Equal(["P1", "P2", "R1", "M1"], rows.Select(r => r.Item.Label));
        Assert.Equal(
            [DockSection.Pinned, DockSection.Pinned, DockSection.Running, DockSection.Minimized],
            rows.Select(r => r.Item.Section));
    }

    [Fact]
    public void Build_ForSingleSection_YieldsOneRowPerItem()
    {
        IReadOnlyList<DockAppItem> items = ThreeSections().Where(i => i.Section == DockSection.Pinned).ToList();
        IReadOnlyList<DockStripRow> rows = DockStripBuilder.Build(items);

        Assert.Equal(items.Count, rows.Count);
        Assert.All(rows, row => Assert.Equal(DockSection.Pinned, row.Item.Section));
    }

    [Fact]
    public void Build_ForEmptyList_YieldsNoRows()
    {
        Assert.Empty(DockStripBuilder.Build([]));
    }

    /// <summary>
    /// A real, never-shown window owned by the test process, so a pid lookup through
    /// <c>GetWindowThreadProcessId</c> resolves the way it does in production.
    /// </summary>
    private sealed class DummyWindow : IDisposable
    {
        private static readonly WndProcDelegate WndProc = new(DefWindowProc);

        internal DummyWindow()
        {
            const string className = "WharfBarTestsComposerDummy";

            var wndClass = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProc),
                lpszClassName = className,
            };

            // A duplicate registration fails with ERROR_CLASS_ALREADY_EXISTS; the class is
            // registered either way, which is all CreateWindowEx needs.
            _ = RegisterClassEx(ref wndClass);

            Handle = CreateWindowEx(
                0,
                className,
                "WharfBar composer dummy",
                0,
                0,
                0,
                0,
                0,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            uint pid;
            _ = NativeMethods.GetWindowThreadProcessId(Handle, out pid);
            Pid = pid;
        }

        internal IntPtr Handle { get; }

        internal uint Pid { get; }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                _ = DestroyWindow(Handle);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX wndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
