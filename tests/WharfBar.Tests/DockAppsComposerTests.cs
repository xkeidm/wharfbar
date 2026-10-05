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

    private static RunningAppInfo Running(string id, string path, string label, IntPtr hwnd = default) =>
        new(id, path, label, hwnd);

    private static AppWindowInfo Minimized(IntPtr hwnd, uint pid, string title) =>
        new(hwnd, title, pid, IsMinimized: true);

    private static IReadOnlyList<DockAppItem> Compose(
        IReadOnlyList<PinnedApp>? pins = null,
        IReadOnlyList<RunningAppInfo>? running = null,
        IReadOnlyList<AppWindowInfo>? minimized = null,
        bool showRunning = true) =>
        DockAppsComposer.Compose(pins ?? [], running ?? [], minimized ?? [], showRunning);

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
        Assert.Equal((IntPtr)7, item.MainWindowHandle);
        Assert.Equal("Microsoft.Notepad", item.Id);
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
    public void Compose_CreatesOneTilePerMinimizedWindow()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [Running("np", Notepad, "Not Defteri")],
            [Minimized((IntPtr)11, 100, "Bir"), Minimized((IntPtr)12, 100, "İki")]);

        DockAppItem[] minimized = [.. items.Where(i => i.Section == DockSection.Minimized)];
        Assert.Equal(2, minimized.Length);
        Assert.Equal(["Bir", "İki"], minimized.Select(i => i.WindowTitle));
        Assert.Equal("hwnd:11", minimized[0].Id);
        Assert.Equal("hwnd:12", minimized[1].Id);
    }

    [Fact]
    public void Compose_MapsMinimizedWindowToAppPathForIconAndLabel()
    {
        // The composer resolves a minimized window's owner through the running app's main
        // window handle, so this needs a real hwnd — a fake one resolves no pid.
        using var window = new DummyWindow();
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [Running("np", Notepad, "Not Defteri", window.Handle)],
            [Minimized((IntPtr)11, window.Pid, "Belge")]);

        DockAppItem minimized = Assert.Single(items, i => i.Section == DockSection.Minimized);
        Assert.Equal(Notepad, minimized.Path);
        Assert.Equal("Not Defteri", minimized.Label);
        Assert.Equal("Belge", minimized.WindowTitle);
    }

    [Fact]
    public void Compose_UnmappedMinimizedWindowFallsBackToWindowTitle()
    {
        IReadOnlyList<DockAppItem> items = Compose([], [], [Minimized((IntPtr)13, 999, "Yalnız")]);
        DockAppItem minimized = Assert.Single(items);
        Assert.Equal(string.Empty, minimized.Path);
        Assert.Equal("Yalnız", minimized.Label);
    }

    [Fact]
    public void Compose_OrdersMinimizedWindowsByTitleThenHandle()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [],
            [Minimized((IntPtr)21, 100, "Z"), Minimized((IntPtr)22, 200, "M"), Minimized((IntPtr)23, 100, "A")]);

        string[] titles = [.. items.Where(i => i.Section == DockSection.Minimized).Select(i => i.WindowTitle)];
        Assert.Equal(["A", "M", "Z"], titles);
    }

    [Fact]
    public void Compose_WhenMinimizedTitlesMatch_OrdersByHandle()
    {
        // Same-titled windows are the common case (two Explorer windows, two untitled
        // documents). Scan order is not handle order, so without the handle tiebreak
        // OrderBy's stability would hand back the fresh scan's order — and the scan is
        // re-enumerated every 800 ms, so the tiles would swap on every poll.
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [],
            [Minimized((IntPtr)31, 100, "Belge"), Minimized((IntPtr)30, 200, "Belge"), Minimized((IntPtr)32, 100, "Belge")]);

        string[] ids = [.. items.Where(i => i.Section == DockSection.Minimized).Select(i => i.Id)];
        Assert.Equal(["hwnd:30", "hwnd:31", "hwnd:32"], ids);
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
