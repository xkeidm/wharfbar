# Üst Bar Tray Aynası (ToolbarWindow32) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Üçüncü taraf tepsi ikonlarını üst bar durum şeridinde salt-okunur aynalayıp tıklamayı gerçek toolbar'a iletmek (ADR-009).

**Architecture:** `TrayToolbar` (Native) belgelenmiş comctl32 `TB_*` mesajlarıyla gerçek tray toolbar'ı (`Shell_TrayWnd → TrayNotifyWnd → SysPager → ToolbarWindow32`) okur; `TrayMirrorService` 1.5 sn arka plan timer ile poll eder, sistem ikonlarını filtreler ve `TrayMirrorItem` snapshot'ı yayınlar; `TopBarWindow` bunu durum şeridindeki yeni `trayMirror` öğesine işler. Tıklama, `TB_GETITEMRECT` merkezine `SendMessageTimeout` mouse mesajlarıyla forward edilir — `ReadProcessMemory`, enjeksiyon veya undocumented API yok.

**Tech Stack:** .NET 8 WPF, P/Invoke (user32.dll, comctl32.dll), xUnit.

**Spec:** [`../specs/2026-08-19-tray-mirror-design.md`](../specs/2026-08-19-tray-mirror-design.md)
**ADR:** [`../../decisions/009-tray-mirror-toolbar-enumeration.md`](../../decisions/009-tray-mirror-toolbar-enumeration.md)

## Global Constraints

- Hedef: `net8.0-windows10.0.19041.0`; **yeni NuGet paketi yok**; `AllowUnsafeBlocks` **yok**.
- `InternalsVisibleTo("WharfBar.Tests")` var — yeni türler `internal` kalır.
- UI, `NativeMethods`'a doğrudan bağlanmaz; ham P/Invoke `Native/` içinde.
- Kullanıcı yüzü metinleri Türkçe (yeni başlık: "Tepsi simgeleri").
- Poll **arka plan thread** üzerinde çalışır; UI'a `Dispatcher.BeginInvoke` ile marshal edilir.
- RPM / enjeksiyon / undocumented API **yasak** (ADR-009); yalnızca belgelenmiş comctl32 toolbar mesajları.
- Shell durumu asla değişmez — salt-okunur; çıkışta geri yüklenecek bir şey yok.
- Pencere boyutları DIP, mesaj koordinatları fiziksel piksel; `Display.DipToPixels` / `Display.GetDpi` mevcut.

---

### Task 1: Katalog v5 — `trayMirror` öğesi + migration

**Files:**
- Modify: `src/WharfBar/Models/TopBarStatusItemIds.cs`
- Modify: `src/WharfBar/Models/AppConfig.cs:85`
- Modify: `src/WharfBar/Services/ConfigService.cs:196-199`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs:977-988`
- Modify: `src/WharfBar/Views/Pages/TopBarPage.xaml.cs:194-218`
- Test: `tests/WharfBar.Tests/TopBarStatusItemIdsTests.cs`

**Interfaces:**
- Consumes: `TopBarStatusItemIds.DefaultOrder` (mevcut), `ConfigService.Normalize` (mevcut).
- Produces: `TopBarStatusItemIds.TrayMirror` (`"trayMirror"`), `TopBarStatusItemIds.EnsureTrayMirror(List<string>)`, katalog v5.

- [ ] **Step 1: Katalog id + order + migration helper ekle**

`src/WharfBar/Models/TopBarStatusItemIds.cs` — sabitleri ve `DefaultOrder` başına ekle:

```csharp
internal const string TrayMirror = "trayMirror";
```

`DefaultOrder`'ı aşağıdaki gibi yap (TrayMirror en solda, TrayOverflow'un hemen solunda):

```csharp
internal static readonly IReadOnlyList<string> DefaultOrder =
[
    TrayMirror,
    TrayOverflow,
    DateTime,
    Notifications,
    ControlCenter,
    Volume,
    Brightness,
    Media,
    Network,
    Battery,
];
```

Dosyanın sonuna `EnsureMedia` bloğundan sonra ekle:

```csharp
/// <summary>
/// Soft migration helper: eksik <see cref="TrayMirror"/>'ı <see cref="TrayOverflow"/> öncesine ekler
/// (yoksa sona).
/// </summary>
internal static void EnsureTrayMirror(List<string> items)
{
    ArgumentNullException.ThrowIfNull(items);

    if (items.Contains(TrayMirror, StringComparer.Ordinal))
    {
        return;
    }

    int index = items.FindIndex(static x =>
        string.Equals(x, TrayOverflow, StringComparison.Ordinal));
    if (index >= 0)
    {
        items.Insert(index, TrayMirror);
    }
    else
    {
        items.Add(TrayMirror);
    }
}
```

- [ ] **Step 2: Katalog sürümünü 5 yap + migration çağrısı**

`src/WharfBar/Models/AppConfig.cs:85` — `CurrentStatusCatalogVersion`'ı `5` yap.

`src/WharfBar/Services/ConfigService.cs` — `< 4` bloğundan sonra ekle:

```csharp
if (config.TopBar.StatusCatalogVersion < 5)
{
    TopBarStatusItemIds.EnsureTrayMirror(config.TopBar.StatusItems);
}
```

- [ ] **Step 3: Başlık + sembol haritaları**

`src/WharfBar/Views/TopBarWindow.xaml.cs` `StatusItemHeader` switch'ine ekle:

```csharp
TopBarStatusItemIds.TrayMirror => "Tepsi simgeleri",
```

`src/WharfBar/Views/Pages/TopBarPage.xaml.cs` — `StatusItemSymbol`'a:

```csharp
TopBarStatusItemIds.TrayMirror => SymbolRegular.Apps24,
```

`StatusItemName`'e:

```csharp
TopBarStatusItemIds.TrayMirror => "Tepsi simgeleri",
```

- [ ] **Step 4: Başarısız testleri yaz**

`tests/WharfBar.Tests/TopBarStatusItemIdsTests.cs` sonuna ekle:

```csharp
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
```

- [ ] **Step 5: Testleri koş — mevcut testler yeşil, yenileri fail** (EnsureTrayMirror tanımsız → derleme hatası)

Run: `dotnet test tests/WharfBar.Tests`
Expected: derleme hatası (EnsureTrayMirror bulunamıyor) veya yeni testler FAIL.

- [ ] **Step 6: Step 1-3 uygulandıktan sonra testleri koş**

Run: `dotnet test tests/WharfBar.Tests`
Expected: tüm testler PASS.

- [ ] **Step 7: Commit**

```bash
git add src/WharfBar/Models/TopBarStatusItemIds.cs src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs src/WharfBar/Views/TopBarWindow.xaml.cs src/WharfBar/Views/Pages/TopBarPage.xaml.cs tests/WharfBar.Tests/TopBarStatusItemIdsTests.cs
git commit -m "feat(tray-mirror): status catalog v5 — trayMirror item + migration"
```

---

### Task 2: Native `TrayToolbar` + `TrayClickPlan` (TB_* okuma + tıklama forward)

**Files:**
- Modify: `src/WharfBar/Native/NativeMethods.cs` (sabitler + DllImport)
- Create: `src/WharfBar/Native/TrayToolbar.cs`
- Test: `tests/WharfBar.Tests/TrayClickPlanTests.cs`

**Interfaces:**
- Consumes: `TaskbarNative.FindTrayHwnd()`, `NativeMethods.FindWindowEx/SendMessageTimeout/GetKeyState/ImageList_GetIcon/DestroyIcon`, `NativeMethods.RECT`.
- Produces: `TrayToolbar.FindToolbarHwnd() → IntPtr`, `ReadButtons(IntPtr) → IReadOnlyList<TrayButtonInfo>`, `GetImageList(IntPtr) → IntPtr`, `TryGetIcon(IntPtr,int,out IntPtr)`, `TryGetItemRect(IntPtr,int,out RECT)`, `ClickItem(IntPtr,int,TrayClick)`; `TrayButtonInfo(int CommandId, int ImageIndex, string Tooltip)`; `TrayClick { Left, Right, DoubleLeft }`; `TrayClickPlan.Build(TrayClick,int,int) → IReadOnlyList<(uint Message, IntPtr WParam, IntPtr LParam)>`.

- [ ] **Step 1: NativeMethods sabitleri + importları**

`src/WharfBar/Native/NativeMethods.cs` — WM_* bloklarının yanına ekle:

```csharp
internal const int WM_USER = 0x0400;
internal const uint WM_LBUTTONDBLCLK = 0x0203;
internal const uint WM_RBUTTONDOWN = 0x0204;
internal const uint MK_RBUTTON = 0x0002;
internal const uint MK_SHIFT = 0x0004;
internal const uint MK_CONTROL = 0x0008;

internal const uint TB_BUTTONCOUNT = (uint)WM_USER + 24;
internal const uint TB_GETBUTTON = (uint)WM_USER + 23;
internal const uint TB_GETITEMRECT = (uint)WM_USER + 29;
internal const uint TB_GETIMAGELIST = (uint)WM_USER + 49;
internal const uint TB_GETBUTTONTEXT = (uint)WM_USER + 75;
internal const uint ILD_TRANSPARENT = 0x0001;
internal const uint SMTO_ABORTIFHUNG = 0x0002;
internal const ushort VK_SHIFT = 0x10;
internal const ushort VK_CONTROL = 0x11;
```

DllImport bloklarına ekle:

```csharp
[DllImport("user32.dll")]
internal static extern short GetKeyState(int nVirtKey);

[DllImport("user32.dll", SetLastError = true)]
internal static extern IntPtr SendMessageTimeout(
    IntPtr hWnd,
    uint Msg,
    IntPtr wParam,
    IntPtr lParam,
    uint fuFlags,
    uint uTimeout,
    out IntPtr lpdwResult);

[DllImport("comctl32.dll")]
internal static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, uint flags);
```

- [ ] **Step 2: Başarısız testi yaz**

`tests/WharfBar.Tests/TrayClickPlanTests.cs` (yeni):

```csharp
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class TrayClickPlanTests
{
    [Fact]
    public void PackLParam_CombinesXAndY()
    {
        IntPtr packed = TrayClickPlan.PackLParam(0x1234, 0x5678);
        int value = packed.ToInt32();
        Assert.Equal(0x1234, value & 0xFFFF);
        Assert.Equal(0x5678, (value >> 16) & 0xFFFF);
    }

    [Fact]
    public void Left_BuildsDownUpSequence()
    {
        var plan = TrayClickPlan.Build(TrayClick.Left, 10, 20);
        Assert.Equal(2, plan.Count);
        Assert.Equal(NativeMethods.WM_LBUTTONDOWN, plan[0].Message);
        Assert.Equal((uint)NativeMethods.WM_LBUTTONUP, plan[1].Message);
        Assert.Equal(plan[0].LParam, plan[1].LParam);
    }

    [Fact]
    public void DoubleLeft_BuildsDownUpDblclkUp()
    {
        var plan = TrayClickPlan.Build(TrayClick.DoubleLeft, 10, 20);
        Assert.Equal(4, plan.Count);
        Assert.Equal(NativeMethods.WM_LBUTTONDBLCLK, plan[2].Message);
    }

    [Fact]
    public void Right_BuildsDownUpSequence()
    {
        var plan = TrayClickPlan.Build(TrayClick.Right, 10, 20);
        Assert.Equal(2, plan.Count);
        Assert.Equal(NativeMethods.WM_RBUTTONDOWN, plan[0].Message);
        Assert.Equal((uint)NativeMethods.WM_RBUTTONUP, plan[1].Message);
    }
}
```

- [ ] **Step 3: Testi koş — derleme hatası beklenir** (TrayClickPlan yok)

Run: `dotnet test tests/WharfBar.Tests`
Expected: derleme hatası (TrayClickPlan bulunamıyor).

- [ ] **Step 4: `TrayToolbar` + `TrayClickPlan` uygula**

`src/WharfBar/Native/TrayToolbar.cs` (yeni) — tam içerik:

```csharp
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>How a mirrored tray icon click is replayed onto the real toolbar.</summary>
internal enum TrayClick
{
    Left,
    Right,
    DoubleLeft,
}

/// <summary>Metadata for one button in the tray toolbar.</summary>
internal sealed record TrayButtonInfo(int CommandId, int ImageIndex, string Tooltip);

/// <summary>
/// Read-only access to the real Windows tray toolbar (ADR-009). Uses documented comctl32
/// toolbar messages (TB_*) and forwards clicks by sending mouse messages at the button rect —
/// never ReadProcessMemory or undocumented APIs.
/// </summary>
internal static class TrayToolbar
{
    private const uint TimeoutMs = 500;
    private const int MaxTooltipChars = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct TBBUTTON
    {
        public int iBitmap;
        public int idCommand;
        public byte fsState;
        public byte fsStyle;
        public byte bReserved0;
        public byte bReserved1;
        public IntPtr dwData;
        public IntPtr iString;
    }

    internal static IntPtr FindToolbarHwnd()
    {
        IntPtr tray = TaskbarNative.FindTrayHwnd();
        if (tray == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr notify = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr pager = NativeMethods.FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
        IntPtr parent = pager != IntPtr.Zero ? pager : notify;

        IntPtr toolbar = IntPtr.Zero;
        while (true)
        {
            toolbar = NativeMethods.FindWindowEx(parent, toolbar, "ToolbarWindow32", null);
            if (toolbar == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (SendTimeout(toolbar, NativeMethods.TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64() > 0)
            {
                return toolbar;
            }
        }
    }

    internal static IReadOnlyList<TrayButtonInfo> ReadButtons(IntPtr toolbar)
    {
        long count = SendTimeout(toolbar, NativeMethods.TB_BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt64();
        if (count <= 0 || count > 256)
        {
            return [];
        }

        var result = new List<TrayButtonInfo>((int)count);
        for (int i = 0; i < count; i++)
        {
            if (TryReadButton(toolbar, i, out TrayButtonInfo info))
            {
                result.Add(info);
            }
        }

        return result;
    }

    internal static IntPtr GetImageList(IntPtr toolbar) =>
        SendTimeout(toolbar, NativeMethods.TB_GETIMAGELIST, IntPtr.Zero, IntPtr.Zero);

    internal static bool TryGetIcon(IntPtr toolbar, int imageIndex, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        if (imageIndex < 0)
        {
            return false;
        }

        IntPtr himl = GetImageList(toolbar);
        if (himl == IntPtr.Zero)
        {
            return false;
        }

        hIcon = NativeMethods.ImageList_GetIcon(himl, imageIndex, NativeMethods.ILD_TRANSPARENT);
        return hIcon != IntPtr.Zero;
    }

    internal static bool TryGetItemRect(IntPtr toolbar, int index, out RECT rect)
    {
        rect = default;
        if (index < 0)
        {
            return false;
        }

        IntPtr rectPtr = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        try
        {
            Marshal.StructureToPtr(rect, rectPtr, false);
            IntPtr ret = SendTimeout(toolbar, NativeMethods.TB_GETITEMRECT, (IntPtr)index, rectPtr);
            if (ret.ToInt32() == 0)
            {
                return false;
            }

            rect = Marshal.PtrToStructure<RECT>(rectPtr);
            return rect.Width > 0 && rect.Height > 0;
        }
        finally
        {
            Marshal.FreeHGlobal(rectPtr);
        }
    }

    internal static void ClickItem(IntPtr toolbar, int index, TrayClick kind)
    {
        if (!TryGetItemRect(toolbar, index, out RECT rect))
        {
            return;
        }

        int x = (rect.Left + rect.Right) / 2;
        int y = (rect.Top + rect.Bottom) / 2;
        foreach ((uint msg, IntPtr wParam, IntPtr lParam) in TrayClickPlan.Build(kind, x, y))
        {
            _ = SendTimeout(toolbar, msg, wParam, lParam);
        }
    }

    private static bool TryReadButton(IntPtr toolbar, int index, out TrayButtonInfo info)
    {
        info = new TrayButtonInfo(0, -1, string.Empty);

        IntPtr rawPtr = Marshal.AllocHGlobal(Marshal.SizeOf<TBBUTTON>());
        try
        {
            var raw = new TBBUTTON();
            Marshal.StructureToPtr(raw, rawPtr, false);
            IntPtr ret = SendTimeout(toolbar, NativeMethods.TB_GETBUTTON, (IntPtr)index, rawPtr);
            if (ret.ToInt32() == 0)
            {
                return false;
            }

            raw = Marshal.PtrToStructure<TBBUTTON>(rawPtr);
            string tooltip = ReadTooltip(toolbar, index);
            info = new TrayButtonInfo(raw.idCommand, raw.iBitmap, tooltip);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(rawPtr);
        }
    }

    private static string ReadTooltip(IntPtr toolbar, int index)
    {
        IntPtr textPtr = Marshal.AllocCoTaskMem(MaxTooltipChars * sizeof(char));
        try
        {
            IntPtr len = SendTimeout(toolbar, NativeMethods.TB_GETBUTTONTEXT, (IntPtr)index, textPtr);
            int n = len.ToInt32();
            if (n <= 0)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUni(textPtr, Math.Min(n, MaxTooltipChars)) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeCoTaskMem(textPtr);
        }
    }

    private static IntPtr SendTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        NativeMethods.SendMessageTimeout(
            hwnd,
            msg,
            wParam,
            lParam,
            NativeMethods.SMTO_ABORTIFHUNG,
            TimeoutMs,
            out _);
}

/// <summary>Pure builder for the mouse-message sequence replayed onto a tray button.</summary>
internal static class TrayClickPlan
{
    internal static IntPtr PackLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    internal static IReadOnlyList<(uint Message, IntPtr WParam, IntPtr LParam)> Build(TrayClick kind, int x, int y)
    {
        uint mods = GetModifiers();
        IntPtr lParam = PackLParam(x, y);
        return kind switch
        {
            TrayClick.Right => new (uint, IntPtr, IntPtr)[]
            {
                (NativeMethods.WM_RBUTTONDOWN, (IntPtr)(NativeMethods.MK_RBUTTON | mods), lParam),
                ((uint)NativeMethods.WM_RBUTTONUP, (IntPtr)mods, lParam),
            },
            TrayClick.DoubleLeft => new (uint, IntPtr, IntPtr)[]
            {
                (NativeMethods.WM_LBUTTONDOWN, (IntPtr)(NativeMethods.MK_LBUTTON | mods), lParam),
                ((uint)NativeMethods.WM_LBUTTONUP, (IntPtr)mods, lParam),
                (NativeMethods.WM_LBUTTONDBLCLK, (IntPtr)(NativeMethods.MK_LBUTTON | mods), lParam),
                ((uint)NativeMethods.WM_LBUTTONUP, (IntPtr)mods, lParam),
            },
            _ => new (uint, IntPtr, IntPtr)[]
            {
                (NativeMethods.WM_LBUTTONDOWN, (IntPtr)(NativeMethods.MK_LBUTTON | mods), lParam),
                ((uint)NativeMethods.WM_LBUTTONUP, (IntPtr)mods, lParam),
            },
        };
    }

    private static uint GetModifiers()
    {
        uint mods = 0;
        if ((NativeMethods.GetKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0)
        {
            mods |= NativeMethods.MK_SHIFT;
        }

        if ((NativeMethods.GetKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0)
        {
            mods |= NativeMethods.MK_CONTROL;
        }

        return mods;
    }
}
```

- [ ] **Step 5: Testleri koş — PASS beklenir**

Run: `dotnet test tests/WharfBar.Tests`
Expected: TrayClickPlanTests 4/4 PASS; mevcut testler de PASS.

- [ ] **Step 6: Commit**

```bash
git add src/WharfBar/Native/NativeMethods.cs src/WharfBar/Native/TrayToolbar.cs tests/WharfBar.Tests/TrayClickPlanTests.cs
git commit -m "feat(tray-mirror): native tray toolbar TB_* reader + click forward plan"
```

---

### Task 3: Modeller + `TraySystemIconFilter` + `TrayMirrorDiff`

**Files:**
- Create: `src/WharfBar/Models/TrayMirrorItem.cs`
- Create: `src/WharfBar/Services/TraySystemIconFilter.cs`
- Create: `src/WharfBar/Services/TrayMirrorDiff.cs`
- Test: `tests/WharfBar.Tests/TraySystemIconFilterTests.cs`, `tests/WharfBar.Tests/TrayMirrorDiffTests.cs`

**Interfaces:**
- Consumes: `TrayButtonInfo` (Task 2).
- Produces: `TrayMirrorItem(string Key, int CommandId, int Index, string Tooltip, ImageSource? Icon)`; `TraySystemIconFilter.Normalize(string?) → string`, `IsSystemIcon(string?) → bool`, `IsWharfBarIcon(string?) → bool`; `TrayMirrorDiff.Compute(IReadOnlySet<string>, IReadOnlyList<TrayMirrorItem>) → (Removed, Added)`.

- [ ] **Step 1: Başarısız testleri yaz**

`tests/WharfBar.Tests/TraySystemIconFilterTests.cs` (yeni):

```csharp
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TraySystemIconFilterTests
{
    [Theory]
    [InlineData("Ses")]
    [InlineData("Ağ durumu")]
    [InlineData("Volume")]
    [InlineData("Battery 75%")]
    [InlineData("Görev çubuğu")]
    [InlineData("Show hidden icons")]
    public void IsSystemIcon_True_ForKnownSystemNames(string tooltip) =>
        Assert.True(TraySystemIconFilter.IsSystemIcon(tooltip));

    [Theory]
    [InlineData("Spotify")]
    [InlineData("Update available")]
    [InlineData("Discord")]
    [InlineData("")]
    public void IsSystemIcon_False_ForOthers(string tooltip) =>
        Assert.False(TraySystemIconFilter.IsSystemIcon(tooltip));

    [Fact]
    public void IsSystemIcon_Null_IsFalse() =>
        Assert.False(TraySystemIconFilter.IsSystemIcon(null));

    [Fact]
    public void IsWharfBarIcon_MatchesOurTray()
    {
        Assert.True(TraySystemIconFilter.IsWharfBarIcon("WharfBar"));
        Assert.True(TraySystemIconFilter.IsWharfBarIcon("wharfbar menüsü"));
        Assert.False(TraySystemIconFilter.IsWharfBarIcon("Spotify"));
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceAndCase() =>
        Assert.Equal("ağ durumu", TraySystemIconFilter.Normalize("  Ağ   DURUMU "));
}
```

`tests/WharfBar.Tests/TrayMirrorDiffTests.cs` (yeni):

```csharp
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
```

- [ ] **Step 2: Testleri koş — derleme hatası beklenir**

Run: `dotnet test tests/WharfBar.Tests`
Expected: derleme hatası (yeni türler yok).

- [ ] **Step 3: Model + filtre + diff uygula**

`src/WharfBar/Models/TrayMirrorItem.cs` (yeni):

```csharp
using System.Windows.Media;

namespace WharfBar.Models;

/// <summary>One visible tray icon shown in the top bar mirror.</summary>
internal sealed record TrayMirrorItem(string Key, int CommandId, int Index, string Tooltip, ImageSource? Icon);
```

`src/WharfBar/Services/TraySystemIconFilter.cs` (yeni):

```csharp
namespace WharfBar.Services;

/// <summary>
/// Decides whether a tray toolbar button is a system icon (already simulated in the status
/// strip) or WharfBar's own tray icon and should be skipped by the mirror.
/// Only a fixed, known set is filtered — unknown tooltips always pass through.
/// </summary>
internal static class TraySystemIconFilter
{
    private static readonly HashSet<string> SystemTokens = new(StringComparer.Ordinal)
    {
        // tr-TR
        "ses", "ağ", "internet", "pil", "saat", "tarih", "güvenlik",
        "gizli simgeleri göster", "görev çubuğu", "giriş dili", "bildirim",
        // en-US
        "volume", "network", "internet", "battery", "clock", "date", "security",
        "show hidden icons", "taskbar", "input", "notification",
    };

    internal static string Normalize(string? tooltip)
    {
        if (string.IsNullOrWhiteSpace(tooltip))
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            tooltip.Trim().ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    internal static bool IsSystemIcon(string? tooltip)
    {
        string normalized = Normalize(tooltip);
        if (normalized.Length == 0)
        {
            return false;
        }

        if (SystemTokens.Contains(normalized))
        {
            return true;
        }

        string[] words = normalized.Split(' ');
        foreach (string token in SystemTokens)
        {
            if (token.Contains(' '))
            {
                if (normalized.Contains(token, StringComparison.Ordinal))
                {
                    return true;
                }

                continue;
            }

            if (words.Contains(token, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsWharfBarIcon(string? tooltip) =>
        Normalize(tooltip).StartsWith("wharfbar", StringComparison.Ordinal);
}
```

`src/WharfBar/Services/TrayMirrorDiff.cs` (yeni):

```csharp
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>Pure key diff for mirror snapshots (unit-tested; no window access).</summary>
internal static class TrayMirrorDiff
{
    internal static (IReadOnlyList<string> Removed, IReadOnlyList<string> Added) Compute(
        IReadOnlySet<string> previousKeys,
        IReadOnlyList<TrayMirrorItem> next)
    {
        var nextKeys = new HashSet<string>(next.Select(static i => i.Key), StringComparer.Ordinal);
        var removed = previousKeys.Where(k => !nextKeys.Contains(k)).ToList();
        var added = next.Select(static i => i.Key).Where(k => !previousKeys.Contains(k)).ToList();
        return (removed, added);
    }
}
```

- [ ] **Step 4: Testleri koş — PASS beklenir**

Run: `dotnet test tests/WharfBar.Tests`
Expected: TraySystemIconFilterTests + TrayMirrorDiffTests PASS; mevcut testler PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Models/TrayMirrorItem.cs src/WharfBar/Services/TraySystemIconFilter.cs src/WharfBar/Services/TrayMirrorDiff.cs tests/WharfBar.Tests/TraySystemIconFilterTests.cs tests/WharfBar.Tests/TrayMirrorDiffTests.cs
git commit -m "feat(tray-mirror): tray system-icon filter + snapshot diff models"
```

---

### Task 4: `TrayMirrorService` — poll + ikon çıkarma + snapshot

**Files:**
- Create: `src/WharfBar/Services/TrayMirrorService.cs`

**Interfaces:**
- Consumes: `TrayToolbar` (Task 2), `TraySystemIconFilter` (Task 3), `TrayMirrorItem` (Task 3), `Display`/`NativeMethods.DestroyIcon` (mevcut).
- Produces: `TrayMirrorService(Dispatcher)`, `event EventHandler? Changed`, `IReadOnlyList<TrayMirrorItem> Items`, `void Reenumerate()`, `IDisposable`.

- [ ] **Step 1: Servisi yaz**

`src/WharfBar/Services/TrayMirrorService.cs` (yeni) — tam içerik:

```csharp
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Polls the real tray toolbar (ADR-009) and publishes a filtered snapshot of third-party
/// icons for the top bar mirror. Read-only: never modifies shell state; nothing to restore.
/// </summary>
internal sealed class TrayMirrorService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly Dispatcher _dispatcher;
    private readonly System.Threading.Timer _timer;
    private ImageSource? _fallback;
    private bool _disposed;

    internal TrayMirrorService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timer = new System.Threading.Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    internal event EventHandler? Changed;

    internal IReadOnlyList<TrayMirrorItem> Items { get; private set; } = [];

    internal void Reenumerate() => _ = Task.Run(Poll);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            IntPtr toolbar = TrayToolbar.FindToolbarHwnd();
            IReadOnlyList<TrayButtonInfo> buttons = toolbar == IntPtr.Zero
                ? []
                : TrayToolbar.ReadButtons(toolbar);

            var items = new List<TrayMirrorItem>(buttons.Count);
            for (int i = 0; i < buttons.Count; i++)
            {
                TrayButtonInfo b = buttons[i];
                if (TraySystemIconFilter.IsSystemIcon(b.Tooltip)
                    || TraySystemIconFilter.IsWharfBarIcon(b.Tooltip))
                {
                    continue;
                }

                items.Add(BuildItem(toolbar, b, i));
            }

            Publish(items);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: tray mirror poll failed — {ex.Message}");
        }
    }

    private TrayMirrorItem BuildItem(IntPtr toolbar, TrayButtonInfo button, int index)
    {
        ImageSource icon = Fallback;
        if (TrayToolbar.TryGetIcon(toolbar, button.ImageIndex, out IntPtr hIcon))
        {
            try
            {
                BitmapSource bmp = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                icon = bmp;
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        string key = $"{button.CommandId}\0{button.Tooltip}";
        return new TrayMirrorItem(key, button.CommandId, index, button.Tooltip, icon);
    }

    private ImageSource Fallback => _fallback ??= BuildFallback();

    private static ImageSource BuildFallback()
    {
        var bmp = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                null,
                new Point(10, 10),
                6,
                6);
        }

        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private void Publish(List<TrayMirrorItem> items)
    {
        if (_dispatcher.CheckAccess())
        {
            Items = items;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            Items = items;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
```

- [ ] **Step 2: Derleme + mevcut testler**

Run: `dotnet build WharfBar.sln`
Expected: derleme başarılı.

Run: `dotnet test tests/WharfBar.Tests`
Expected: tüm testler PASS.

- [ ] **Step 3: Commit**

```bash
git add src/WharfBar/Services/TrayMirrorService.cs
git commit -m "feat(tray-mirror): TrayMirrorService poll + icon extraction + snapshot"
```

---

### Task 5: TopBar UI entegrasyonu — `trayMirror` öğesi

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml` (StatusStrip içine, TrayOverflowButton öncesi)
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `TrayMirrorService` (Task 4), `TrayMirrorItem` (Task 3), `TrayToolbar`/`TrayClick` (Task 2), `TopBarStatusItemIds.TrayMirror` (Task 1), `SystemVersion.TextFontFamily` (mevcut).
- Produces: UI öğesi `TrayMirrorHost` + `_statusElements` girişi + `_trayMirrorActive` görünürlük + tıklama forward'ları.

- [ ] **Step 1: XAML — `trayMirror` host**

`src/WharfBar/Views/TopBarWindow.xaml` — `<Border x:Name="TrayOverflowButton"` satırından (119) hemen önce ekle:

```xml
<Border x:Name="TrayMirrorHost"
        Style="{StaticResource StatusHit}"
        Padding="2,2"
        Margin="0,0,2,0"
        Visibility="Collapsed">
    <ItemsControl x:Name="TrayMirrorItems">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <StackPanel Orientation="Horizontal" />
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
    </ItemsControl>
</Border>
```

- [ ] **Step 2: Alanlar + kayıt + olaylar (code-behind)**

`src/WharfBar/Views/TopBarWindow.xaml.cs`:

(2a) Alan ekle (mevcut `_mediaActive` satırlarının yanına):

```csharp
private TrayMirrorService? _trayMirror;
private bool _trayMirrorActive;
private readonly Dictionary<string, Image> _trayMirrorImages = new(StringComparer.Ordinal);
```

(2b) Ctor `_statusElements` sözlüğüne ekle:

```csharp
[TopBarStatusItemIds.TrayMirror] = TrayMirrorHost,
```

(2c) `OnSourceInitialized` içinde `_media` kurulumundan sonra ekle:

```csharp
_trayMirror = new TrayMirrorService(Dispatcher);
_trayMirror.Changed += (_, _) => ApplyTrayMirror(_trayMirror.Items);
_trayMirror.Reenumerate();
```

(2d) `WndProc`'un `_taskbarCreatedMessage` dalına, `QueueApplyPlacement(reRegister: true)`'dan sonra ekle:

```csharp
_trayMirror?.Reenumerate();
```

(2e) `ApplyStatusItemLayout` görünürlük switch'ine ekle:

```csharp
TopBarStatusItemIds.TrayMirror => _trayMirrorActive,
```

(2f) `DisposeOwnedServices`'e ekle:

```csharp
_trayMirror?.Dispose();
_trayMirror = null;
```

- [ ] **Step 3: Uygulama + tıklama forward yöntemleri (code-behind)**

`src/WharfBar/Views/TopBarWindow.xaml.cs` sonuna ekle:

```csharp
private void ApplyTrayMirror(IReadOnlyList<TrayMirrorItem> items)
{
    _trayMirrorActive = items.Count > 0;

    var nextKeys = new HashSet<string>(items.Select(static i => i.Key), StringComparer.Ordinal);
    var stale = _trayMirrorImages.Keys.Where(k => !nextKeys.Contains(k)).ToList();
    foreach (string key in stale)
    {
        TrayMirrorItems.Items.Remove(_trayMirrorImages[key]);
        _trayMirrorImages.Remove(key);
    }

    foreach (TrayMirrorItem item in items)
    {
        if (_trayMirrorImages.TryGetValue(item.Key, out Image? existing))
        {
            // Icons can change in place (badges) — refresh Source on every poll.
            existing.Source = item.Icon;
            existing.Tag = item;
            SetPlainToolTip(existing, item.Tooltip);
        }
        else
        {
            var image = new Image
            {
                Source = item.Icon,
                Tag = item,
                Width = 20,
                Height = 20,
                Stretch = Stretch.None,
                Cursor = Cursors.Hand,
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            image.MouseLeftButtonDown += OnTrayMirrorItemMouseDown;
            image.MouseRightButtonDown += OnTrayMirrorItemMouseDown;
            image.MouseRightButtonUp += OnTrayMirrorItemRightUp;
            image.MouseLeftButtonDoubleClick += OnTrayMirrorItemDoubleClick;
            SetPlainToolTip(image, item.Tooltip);
            _trayMirrorImages[item.Key] = image;
            TrayMirrorItems.Items.Add(image);
        }
    }

    ApplyStatusItemLayout();
}

private void OnTrayMirrorItemMouseDown(object sender, MouseButtonEventArgs e)
{
    if (sender is not Image { Tag: TrayMirrorItem item })
    {
        return;
    }

    e.Handled = true;
    ForwardTrayClick(item, e.ChangedButton == MouseButton.Right ? TrayClick.Right : TrayClick.Left);
}

private void OnTrayMirrorItemRightUp(object sender, MouseButtonEventArgs e)
{
    // Blocks the status-strip context menu when the click landed on a tray icon.
    if (sender is Image { Tag: TrayMirrorItem })
    {
        e.Handled = true;
    }
}

private void OnTrayMirrorItemDoubleClick(object sender, MouseButtonEventArgs e)
{
    if (sender is not Image { Tag: TrayMirrorItem item })
    {
        return;
    }

    e.Handled = true;
    ForwardTrayClick(item, TrayClick.DoubleLeft);
}

private void ForwardTrayClick(TrayMirrorItem item, TrayClick kind)
{
    IntPtr toolbar = TrayToolbar.FindToolbarHwnd();
    if (toolbar == IntPtr.Zero)
    {
        return;
    }

    TrayToolbar.ClickItem(toolbar, item.Index, kind);
}
```

> Not: `SetPlainToolTip` mevcut static yardımcı (`element.ToolTip = null` boşsa). Tray item'ları `ToolTipService.SetToolTip` yerine bu deseni kullanır — diğer durum öğeleriyle tutarlı.

- [ ] **Step 4: Derleme**

Run: `dotnet build WharfBar.sln`
Expected: derleme başarılı.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Views/TopBarWindow.xaml src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat(tray-mirror): top bar trayMirror UI + click forwarding"
```

---

### Task 6: Doküman + boundaries + roadmap güncellemesi (ADR-009 disiplini)

**Files:**
- Modify: `.agent/boundaries.md`
- Modify: `.agent/project.md`
- Modify: `.agent/context/roadmap.md`
- Modify: `docs/00-overview.md`
- Modify: `docs/01-architecture.md`
- Modify: `docs/02-win32-appbar-dwm.md`
- Modify: `docs/03-development-order.md`
- Modify: `docs/04-gotchas.md`

- [ ] **Step 1: `.agent/boundaries.md`**

"Önce sor" listesindeki şu satırı **kaldır**:

```
- Yabancı tepsi ikonlarını WharfBar içinde çizme / click-forward (ADR-005 reddetti; yeniden açmak için onay)
```

"Yap" listesindeki şu satırı güncelle (ADR-009):

```markdown
- Üst barda tek "tray overflow" düğmesi (`Win+B` / chevron) + **tray aynası (ADR-009):** üçüncü
  taraf tepsi ikonlarını **belgelenmiş comctl32 toolbar mesajlarıyla** (`TB_BUTTONCOUNT`,
  `TB_GETBUTTONINFO`, `TB_GETIMAGELIST` + `ImageList_GetIcon`, `TB_GETITEMRECT`,
  `TB_GETBUTTONTEXT`) salt-okunur okuyup üst barda çizmek + `SendMessage` ile tıklama forward.
  Gizli/overflow ikonlar kapsam dışı (yalnızca açık popup'ta var) → `Win+B` düğmesi korunur.
```

"Asla" listesindeki ilk tray maddesini **daralt**:

```markdown
- Sistem / üçüncü taraf tepsi ikonlarını **`ReadProcessMemory`, enjeksiyon/subclass veya
  belgelenmemiş API ile** "çözmek" — `Shell_NotifyIcon` yalnızca WharfBar'ın kendi ikonu için.
  (Belgelenmiş comctl32 `TB_*` mesajlarıyla salt-okunur ayna ADR-009 ile serbesttir.)
```

- [ ] **Step 2: `.agent/project.md`**

"Ürün" bölümündeki top bar satırını güncelle — `trayOverflow` açıklamasına `trayMirror` ekle:

```markdown
Sağda durum şeridi (`trayMirror` → `trayOverflow` → `dateTime` → … ; özelleştirilebilir)
```

"Kritik teknik gerçekler" maddesine ekle:

```markdown
9. **Tray aynası (ADR-009):** yabancı tepsi ikonları belgelenmiş comctl32 `TB_*` mesajlarıyla
   salt-okunur okunur + `SendMessage` click-forward; RPM/enjeksiyon/undocumented API **yasak**;
   gizli ikonlar kapsam dışı (`Win+B` kalır); sistem ikonları tooltip filtreyle düşülür.
```

"Mevcut repo durumu" bölümüne (medya paneli paragrafından sonra) kısa paragraf ekle:

```markdown
**Tray aynası (ADR-009):** üst bar `trayMirror` öğesi (katalog **v5**) — gerçek tray
toolbar'ından (TrayNotifyWnd/SysPager/ToolbarWindow32) üçüncü taraf ikonları okur,
sistem ikonlarını filtreler, 1.5 sn poll, tıklama forward. Spec:
[`../docs/superpowers/specs/2026-08-19-tray-mirror-design.md`](../docs/superpowers/specs/2026-08-19-tray-mirror-design.md);
ADR: [`../docs/decisions/009-tray-mirror-toolbar-enumeration.md`](../docs/decisions/009-tray-mirror-toolbar-enumeration.md).
```

- [ ] **Step 3: `.agent/context/roadmap.md`**

"Şimdi (tamamlanan)" bölümüne (medya paneli satırından sonra) ekle:

```markdown
- **Tray aynası (ADR-009) ✓** — üst bar `trayMirror` (belgelenmiş comctl32 `TB_*` mesajlarıyla
  salt-okunur ayna + `SendMessage` click-forward; sistem ikonu filtresi; 1.5 sn poll; gizli
  ikonlar `Win+B`'de kalır). Spec: [`../../docs/superpowers/specs/2026-08-19-tray-mirror-design.md`](../../docs/superpowers/specs/2026-08-19-tray-mirror-design.md); plan: [`../../docs/superpowers/plans/2026-08-19-tray-mirror.md`](../../docs/superpowers/plans/2026-08-19-tray-mirror.md); karar: [`../../docs/decisions/009-tray-mirror-toolbar-enumeration.md`](../../docs/decisions/009-tray-mirror-toolbar-enumeration.md)
```

"Bilinçli ertelenenler / yasaklar" listesinin ilk maddesini güncelle:

```markdown
- Yabancı / sistem tepsi ikonlarını `ReadProcessMemory` / enjeksiyon / undocumented API ile
  okuma, çizme, click-forward (ADR-009 ile yalnızca belgelenmiş `TB_*` mesaj aynası serbest;
  gizli/overflow ikonlar hâlâ yasak — `Win+B` kullanılır)
```

- [ ] **Step 4: `docs/00-overview.md`**

Non-goals listesindeki tepsi maddesini güncelle:

```markdown
- Sistem / üçüncü taraf tepsi ikonlarını `ReadProcessMemory` / enjeksiyon / belgelenmemiş
  NotifyIcon enumeration ile okuma. (ADR-009: belgelenmiş comctl32 `TB_*` mesajlarıyla
  salt-okunur ayna + tıklama forward üst barda **hedeftir**; gizli/overflow ikonlar kapsam dışı.)
```

"Başarı kriterleri" listesindeki şu satırı güncelle:

```markdown
- Üst bar: üçüncü taraf tepsi ikonları aynalanır (tıklama forward); gizli ikonlar için
  `Win+B`/chevron düğmesi native overflow'u açar.
```

- [ ] **Step 5: `docs/01-architecture.md`**

`Native/` listesine ekle:

```markdown
    TrayToolbar.cs               # gerçek tray toolbar TB_* okuyucu + click forward (ADR-009)
```

`Services/` tablosuna satır ekle (ör. `TrayOverflowInvoker` satırından sonra):

```markdown
| `TrayMirrorService` | Tray toolbar'ı poll'lar, sistem ikonlarını filtreler, `TrayMirrorItem` snapshot yayınlar (ADR-009) |
```

`TrayMirrorService`'i servis açıklama listesine ekle (mevcut 7. `TrayIconService` civarına).

- [ ] **Step 6: `docs/02-win32-appbar-dwm.md`**

"c) Sistem tepsisi / durum göstergeleri" bölümündeki tepsi hibrit satırını güncelle:

```markdown
- **Tepsi hibrit + ayna (ADR-005 + ADR-009):** üçüncü taraf ikonlar **belgelenmiş comctl32
  toolbar mesajlarıyla** (`TB_BUTTONCOUNT`, `TB_GETBUTTONINFO`, `TB_GETIMAGELIST` +
  `ImageList_GetIcon`, `TB_GETITEMRECT`, `TB_GETBUTTONTEXT`) üst barda aynalanır; tıklama
  `SendMessageTimeout` mouse mesajlarıyla forward edilir. `ReadProcessMemory` / enjeksiyon /
  belgelenmemiş enumeration yok. Gizli/overflow ikonlar aynalanamaz (yalnızca açık popup'ta var);
  `TrayOverflowInvoker` (`Win+B` / chevron) kalır. Poll 1.5 sn; `SendMessageTimeout` (`SMTO_ABORTIFHUNG`,
  500 ms) asılı explorer'da UI'ı kilitlemez.
```

- [ ] **Step 7: `docs/03-development-order.md`**

Faz 9 satırlarından sonra yeni faz notu ekle:

```markdown
### Tray aynası (ADR-009) ✓ (kod)

Üçüncü taraf tepsi ikonlarını belgelenmiş comctl32 `TB_*` mesajlarıyla üst barda aynalar
(`trayMirror` katalog v5); sistem ikonu filtresi; tıklama forward; gizli ikonlar `Win+B`.
Spec: [`superpowers/specs/2026-08-19-tray-mirror-design.md`](superpowers/specs/2026-08-19-tray-mirror-design.md).
```

- [ ] **Step 8: `docs/04-gotchas.md`**

Tepsi ikonu satırını güncelle:

```markdown
| Sistem / yabancı tepsi ikonlarını okumaya çalışma | ADR-009: yalnızca belgelenmiş comctl32 `TB_*` mesajlarıyla salt-okunur ayna + `SendMessage` click-forward; `ToolbarWindow32`/RPM/enjeksiyon yok. `SendMessageTimeout` (`SMTO_ABORTIFHUNG`, 500 ms) asılı explorer'da UI kilitlemesini önler. Gizli/overflow ikonlar aynalanamaz (yalnızca açık popup'ta var) → `Win+B`. |
```

Gotcha tablosuna satır ekle:

```markdown
| Tray ikonu tıklaması yanlış uygulamaya gidiyor | Snapshot poll arasında buton sırası kayarsa (ikon ekle/kaldır) index eski kalabilir; sonraki 1.5 sn poll düzeltir. Çift tık: WPF ilk tıklamada `MouseLeftButtonDown`'ı da yükseltir → forward edilen sıra `down/up, down/up, dblclk/up` olur (native `down,up,dblclk,up`); uygulamalar fazladan down/up'ı yok sayar. |
```

- [ ] **Step 9: Self-review — dokümanlarda "tam ayna yok / ADR-005 reddi" ile çelişen eski ifade kalmadığını tara**

```bash
rg -n "tam ayna yok|çizilmez|click-forward yok|ADR-005 reddetti|tep siyi" docs .agent
```

Expected: yalnızca kasıtlı tarihsel referanslar kalır (ör. roadmap'in 62. satırı "ADR-005 tepsi aynası yok" — bu satırı da Step 3'te güncelledik; kontrol et). Uygunsuz kalan varsa düzelt.

- [ ] **Step 10: Commit**

```bash
git add .agent docs
git commit -m "docs: tray mirror ADR-009 — boundaries, roadmap, win32/gotchas/architecture"
```

---

## Self-Review

**Spec kapsamı:**
- Enumerasyon kaynağı + TB_* mesajları → Task 2 ✓
- Sistem ikon filtresi (tr/en, sabit set) → Task 3 ✓
- WharfBar kendi ikonu düşme → Task 3 (`IsWharfBarIcon`) ✓
- Gizli/overflow kapsam dışı (`Win+B` kalır) → Task 1 (katalog `trayMirror` + `trayOverflow` yan yana), Task 6 doküman ✓
- 1.5 sn poll + snapshot diff → Task 4 (`PollInterval`), Task 3 (`TrayMirrorDiff`) ✓
- Tıklama forward (sol/sağ/çift, modifier) → Task 2 (`TrayClickPlan`) ✓
- Tooltip → Task 5 (`SetPlainToolTip`) ✓
- Katalog v5 migration ("hide must stick") → Task 1 ✓
- Explorer restart (`TaskbarCreated` → `Reenumerate`) → Task 5 (2d) ✓
- HICON sızıntısı önleme (`DestroyIcon` her `TryGetIcon` sonrası) → Task 4 ✓
- Sessiz degrade (hata → debug log, boş liste → Collapsed) → Task 4 (`Poll` catch), Task 5 (`_trayMirrorActive`) ✓

**Placeholder taraması:** Tüm adımlar somut kod içerir; "TBD/implement later" yok. Her görev `dotnet test`/`dotnet build` komutuyla kapanır.

**Tip tutarlılığı:** `TrayButtonInfo(CommandId, ImageIndex, Tooltip)` Task 2'de tanımlanıp Task 4'te aynı sırayla kullanılıyor. `TrayMirrorItem(Key, CommandId, Index, Tooltip, Icon)` Task 3'te tanımlanıp Task 4-5'te tutarlı. `TrayClick` Task 2'de tanımlanıp Task 5'te kullanılıyor. `TrayMirrorDiff.Compute` imzası Task 3'te tanımlı; Task 5'te doğrudan kullanılmıyor (UI, kendi key set diff'iyle add/remove yapıyor) — bu bilinçli: ikonlar her poll'da yenilenir, diff yalnızca add/remove için gerektiğinden Task 3'ün diff'i bağımsız test ediliyor ve servis tüketicisi tarafından kullanılabilir. Çelişki yok.

## Execution Handoff

Plan kaydedildi: `docs/superpowers/plans/2026-08-19-tray-mirror.md`.