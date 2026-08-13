# System Stats Detail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CPU / GPU / RAM rozetlerinde her alan için `Gizli` / `Yalnızca tooltip` / `Üst bar` seçimi; varsayılan barda `%`, tooltip’te GHz ve GB.

**Architecture:** `SystemMonitorService` ham snapshot üretir (config okumaz). `SystemStatsFormatter` yerleşim + snapshot’tan bar/tooltip/görünürlük üretir. CPU GHz `CallNtPowerInformation`; VRAM DXGI `QueryVideoMemoryInfo`. Ayarlar `GeneralPage` ComboBox. Yeni NuGet yok.

**Tech Stack:** .NET 8 WPF, mevcut Win32/`Native/` COM deseni (`AudioEndpointVolume` gibi); `powrprof.dll` + `dxgi.dll`.

**Spec:** [`docs/superpowers/specs/2026-08-13-system-stats-detail-design.md`](../specs/2026-08-13-system-stats-detail-design.md)

## Global Constraints

- Yeni NuGet / LibreHardwareMonitor / NVAPI / ADL yok
- `AllowsTransparency=true` + Mica yasak
- Çıkışta `ABM_REMOVE` + görev çubuğu restore
- Ham P/Invoke yalnızca `Native/` `internal`
- `global.json` yok
- `schemaVersion` bump yok (`1` kalır)
- `AllowUnsafeBlocks` yok
- Poll 1.5 sn; GPU Engine sayaç yolunu daha pahalı yapma
- Commit yalnızca kullanıcı isterse
- Derleme: `dotnet build WharfBar.sln` (`dotnet run` AppBar riski — manuel doğrulama kullanıcıda)
- İnsan metinleri Türkçe; API adları İngilizce

---

## File map

| File | Role |
|------|------|
| `src/WharfBar/Models/StatPlacement.cs` | `bar` / `tooltip` / `hidden` + Normalize + ComboBox index |
| `src/WharfBar/Models/AppConfig.cs` | Altı yerleşim string’i |
| `src/WharfBar/Services/ConfigService.cs` | Normalize yerleşimler |
| `src/WharfBar/Services/SystemStatsFormatter.cs` | Snapshot + config → bar/tooltip/visibility |
| `src/WharfBar/Services/SystemMonitorService.cs` | Ham snapshot; GHz + VRAM doldur; önceden biçimli metin yok |
| `src/WharfBar/Native/SystemInfoNative.cs` | `CallNtPowerInformation`, `GetSystemInfo`, `PROCESSOR_POWER_INFORMATION` |
| `src/WharfBar/Native/DxgiAdapterMemory.cs` | DXGI dedicated VRAM |
| `src/WharfBar/Views/TopBarWindow.xaml.cs` | `ApplySystemStats` formatter kullanır |
| `src/WharfBar/Views/Pages/GeneralPage.xaml` | ComboBox satırları |
| `src/WharfBar/Views/Pages/GeneralPage.xaml.cs` | Yerleşim kaydet / uygula |
| `docs/01-architecture.md` | Servis + config alanları |
| `docs/02-win32-appbar-dwm.md` | GHz + DXGI imzaları |
| `.agent/project.md` | Ürün notu |
| Spec | Durum → plan linki |

---

### Task 1: Yerleşim modeli + config

**Files:**
- Create: `src/WharfBar/Models/StatPlacement.cs`
- Modify: `src/WharfBar/Models/AppConfig.cs` (`TopBarConfig`, `ShowRamStats` sonrası)
- Modify: `src/WharfBar/Services/ConfigService.cs` (`Normalize`)

**Interfaces:**
- Produces: `StatPlacement.Bar = "bar"`, `Tooltip = "tooltip"`, `Hidden = "hidden"`
- Produces: `string Normalize(string? value, string fallback)` — yalnızca `bar`/`tooltip`/`hidden` (case-insensitive); aksi `fallback` (fallback da geçersizse `Bar`)
- Produces: `int ToComboIndex(string? value, string fallback)` → 0 hidden, 1 tooltip, 2 bar
- Produces: `string FromComboIndex(int index)` → 0 hidden, 1 tooltip, else bar
- Produces: `TopBarConfig.CpuPercentPlacement` default `bar`, `CpuGhzPlacement` default `tooltip`, `RamPercentPlacement` default `bar`, `RamUsedTotalPlacement` default `tooltip`, `GpuPercentPlacement` default `bar`, `GpuVramPlacement` default `tooltip`

- [ ] **Step 1: Add `StatPlacement.cs`**

```csharp
namespace WharfBar.Models;

internal static class StatPlacement
{
    internal const string Bar = "bar";
    internal const string Tooltip = "tooltip";
    internal const string Hidden = "hidden";

    internal static string Normalize(string? value, string fallback)
    {
        if (string.Equals(value, Bar, StringComparison.OrdinalIgnoreCase))
        {
            return Bar;
        }

        if (string.Equals(value, Tooltip, StringComparison.OrdinalIgnoreCase))
        {
            return Tooltip;
        }

        if (string.Equals(value, Hidden, StringComparison.OrdinalIgnoreCase))
        {
            return Hidden;
        }

        if (string.Equals(fallback, Tooltip, StringComparison.OrdinalIgnoreCase))
        {
            return Tooltip;
        }

        if (string.Equals(fallback, Hidden, StringComparison.OrdinalIgnoreCase))
        {
            return Hidden;
        }

        return Bar;
    }

    internal static int ToComboIndex(string? value, string fallback) =>
        Normalize(value, fallback) switch
        {
            Hidden => 0,
            Tooltip => 1,
            _ => 2,
        };

    internal static string FromComboIndex(int index) => index switch
    {
        0 => Hidden,
        1 => Tooltip,
        _ => Bar,
    };

    internal static bool IsBar(string? value, string fallback) =>
        Normalize(value, fallback) == Bar;

    internal static bool IsOnTooltip(string? value, string fallback)
    {
        string p = Normalize(value, fallback);
        return p is Bar or Tooltip;
    }
}
```

- [ ] **Step 2: Extend `TopBarConfig` in `AppConfig.cs`**

After `ShowRamStats`:

```csharp
    public string CpuPercentPlacement { get; set; } = StatPlacement.Bar;
    public string CpuGhzPlacement { get; set; } = StatPlacement.Tooltip;
    public string RamPercentPlacement { get; set; } = StatPlacement.Bar;
    public string RamUsedTotalPlacement { get; set; } = StatPlacement.Tooltip;
    public string GpuPercentPlacement { get; set; } = StatPlacement.Bar;
    public string GpuVramPlacement { get; set; } = StatPlacement.Tooltip;
```

- [ ] **Step 3: Normalize in `ConfigService.Normalize`**

After `config.TopBar.TintOpacity = ...` line:

```csharp
        config.TopBar.CpuPercentPlacement = StatPlacement.Normalize(
            config.TopBar.CpuPercentPlacement, StatPlacement.Bar);
        config.TopBar.CpuGhzPlacement = StatPlacement.Normalize(
            config.TopBar.CpuGhzPlacement, StatPlacement.Tooltip);
        config.TopBar.RamPercentPlacement = StatPlacement.Normalize(
            config.TopBar.RamPercentPlacement, StatPlacement.Bar);
        config.TopBar.RamUsedTotalPlacement = StatPlacement.Normalize(
            config.TopBar.RamUsedTotalPlacement, StatPlacement.Tooltip);
        config.TopBar.GpuPercentPlacement = StatPlacement.Normalize(
            config.TopBar.GpuPercentPlacement, StatPlacement.Bar);
        config.TopBar.GpuVramPlacement = StatPlacement.Normalize(
            config.TopBar.GpuVramPlacement, StatPlacement.Tooltip);
```

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`

Expected: `Build succeeded` (0 errors).

- [ ] **Step 5: Commit (yalnızca kullanıcı istediyse)**

```bash
git add src/WharfBar/Models/StatPlacement.cs src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs
git commit -m "feat: add CPU/GPU/RAM stat placement config"
```

---

### Task 2: Ham snapshot + formatter + üst bar bağlama

**Files:**
- Create: `src/WharfBar/Services/SystemStatsFormatter.cs`
- Modify: `src/WharfBar/Services/SystemMonitorService.cs` (record + `BuildSnapshot` / `BuildEmptySnapshot`; GHz ve VRAM şimdilik null)
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` (`ApplySystemStats`)

**Interfaces:**
- Consumes: `StatPlacement.*`, `TopBarConfig` yerleşim + `Show*Stats`
- Produces: `SystemStatsSnapshot` ham alanlar (aşağıdaki record)
- Produces: `SystemStatsDisplay Format(SystemStatsSnapshot snapshot, TopBarConfig config)`
- Produces: `SystemStatsFormatter.FormatGbPair(double used, double total)`
- GHz/VRAM henüz doldurulmaz (`null` / `false`) — varsayılan bar hâlâ yalnızca `%`

- [ ] **Step 1: Replace `SystemStatsSnapshot` in `SystemMonitorService.cs`**

Replace the record and keep the class. New record:

```csharp
internal sealed record SystemStatsSnapshot(
    int CpuPercent,
    double? CpuGhz,
    int RamPercent,
    double UsedRamGb,
    double TotalRamGb,
    bool IsGpuUsageAvailable,
    int GpuPercent,
    bool IsGpuVramAvailable,
    double? GpuUsedVramGb,
    double? GpuTotalVramGb);
```

Replace `BuildSnapshot` body so it no longer builds `*Text` / `*Tooltip`. After existing CPU/RAM/GPU reads:

```csharp
        int cpuPercent = ReadCpuUsage();
        (int ramPercent, double usedGb, double totalGb) = ReadRamUsage();
        int gpuPercent = ReadGpuUsage();
        bool isGpuUsageAvailable = gpuPercent >= 0;

        return new SystemStatsSnapshot(
            cpuPercent,
            CpuGhz: null,
            ramPercent,
            usedGb,
            totalGb,
            isGpuUsageAvailable,
            isGpuUsageAvailable ? gpuPercent : 0,
            IsGpuVramAvailable: false,
            GpuUsedVramGb: null,
            GpuTotalVramGb: null);
```

Replace `BuildEmptySnapshot`:

```csharp
    private static SystemStatsSnapshot BuildEmptySnapshot() => new(
        0,
        null,
        0,
        0,
        0,
        false,
        0,
        false,
        null,
        null);
```

Delete unused `using System.Globalization` from this file if nothing else needs it.

- [ ] **Step 2: Add `SystemStatsFormatter.cs`**

```csharp
using System.Globalization;
using System.Text;
using WharfBar.Models;

namespace WharfBar.Services;

internal sealed record SystemStatsDisplay(
    bool CpuVisible,
    string CpuBarText,
    string CpuTooltip,
    bool GpuVisible,
    string GpuBarText,
    string GpuTooltip,
    bool RamVisible,
    string RamBarText,
    string RamTooltip);

internal static class SystemStatsFormatter
{
    private const string Separator = " · ";

    internal static SystemStatsDisplay Format(SystemStatsSnapshot snapshot, TopBarConfig config)
    {
        if (!config.ShowSystemStats)
        {
            return new SystemStatsDisplay(
                false, string.Empty, string.Empty,
                false, string.Empty, string.Empty,
                false, string.Empty, string.Empty);
        }

        bool cpuPctOk = true;
        bool cpuGhzOk = snapshot.CpuGhz is > 0;
        bool ramOk = true;
        bool gpuPctOk = snapshot.IsGpuUsageAvailable;
        bool gpuVramOk = snapshot.IsGpuVramAvailable
            && snapshot.GpuUsedVramGb is not null
            && snapshot.GpuTotalVramGb is > 0;

        (bool cpuVisible, string cpuBar, string cpuTip) = FormatBadge(
            config.ShowCpuStats,
            Field(config.CpuPercentPlacement, StatPlacement.Bar, cpuPctOk, $"{snapshot.CpuPercent}%",
                $"CPU kullanımı: {snapshot.CpuPercent}%"),
            Field(config.CpuGhzPlacement, StatPlacement.Tooltip, cpuGhzOk,
                string.Format(CultureInfo.CurrentCulture, "{0:F1} GHz", snapshot.CpuGhz ?? 0),
                string.Format(CultureInfo.CurrentCulture, "CPU hızı: {0:F1} GHz", snapshot.CpuGhz ?? 0)));

        (bool ramVisible, string ramBar, string ramTip) = FormatBadge(
            config.ShowRamStats,
            Field(config.RamPercentPlacement, StatPlacement.Bar, ramOk, $"{snapshot.RamPercent}%",
                $"RAM kullanımı: {snapshot.RamPercent}%"),
            Field(config.RamUsedTotalPlacement, StatPlacement.Tooltip, ramOk,
                FormatGbPair(snapshot.UsedRamGb, snapshot.TotalRamGb),
                $"RAM: {FormatGbPair(snapshot.UsedRamGb, snapshot.TotalRamGb)}"));

        (bool gpuVisible, string gpuBar, string gpuTip) = FormatBadge(
            config.ShowGpuStats,
            Field(config.GpuPercentPlacement, StatPlacement.Bar, gpuPctOk, $"{snapshot.GpuPercent}%",
                $"GPU kullanımı: {snapshot.GpuPercent}%"),
            Field(config.GpuVramPlacement, StatPlacement.Tooltip, gpuVramOk,
                FormatGbPair(snapshot.GpuUsedVramGb ?? 0, snapshot.GpuTotalVramGb ?? 0),
                $"GPU VRAM: {FormatGbPair(snapshot.GpuUsedVramGb ?? 0, snapshot.GpuTotalVramGb ?? 0)}"));

        return new SystemStatsDisplay(
            cpuVisible, cpuBar, cpuTip,
            gpuVisible, gpuBar, gpuTip,
            ramVisible, ramBar, ramTip);
    }

    internal static string FormatGbPair(double used, double total)
    {
        string usedText = used.ToString("F1", CultureInfo.CurrentCulture);
        string totalText = Math.Abs(total - Math.Round(total)) < 0.15
            ? Math.Round(total).ToString("F0", CultureInfo.CurrentCulture)
            : total.ToString("F1", CultureInfo.CurrentCulture);
        return $"{usedText} / {totalText} GB";
    }

    private readonly record struct FieldSpec(
        string Placement,
        string Fallback,
        bool Available,
        string BarPiece,
        string TooltipLine);

    private static FieldSpec Field(
        string placement, string fallback, bool available, string barPiece, string tooltipLine) =>
        new(placement, fallback, available, barPiece, tooltipLine);

    private static (bool Visible, string BarText, string Tooltip) FormatBadge(
        bool showBadge, FieldSpec first, FieldSpec second)
    {
        if (!showBadge)
        {
            return (false, string.Empty, string.Empty);
        }

        List<string> barParts = [];
        List<string> tipLines = [];
        bool any = false;

        Append(first, barParts, tipLines, ref any);
        Append(second, barParts, tipLines, ref any);

        if (!any)
        {
            return (false, string.Empty, string.Empty);
        }

        StringBuilder tip = new();
        for (int i = 0; i < tipLines.Count; i++)
        {
            if (i > 0)
            {
                tip.AppendLine();
            }

            tip.Append(tipLines[i]);
        }

        return (true, string.Join(Separator, barParts), tip.ToString());
    }

    private static void Append(
        FieldSpec field, List<string> barParts, List<string> tipLines, ref bool any)
    {
        if (!field.Available)
        {
            return;
        }

        if (StatPlacement.IsBar(field.Placement, field.Fallback))
        {
            barParts.Add(field.BarPiece);
            tipLines.Add(field.TooltipLine);
            any = true;
            return;
        }

        if (StatPlacement.IsOnTooltip(field.Placement, field.Fallback))
        {
            tipLines.Add(field.TooltipLine);
            any = true;
        }
    }
}
```

- [ ] **Step 3: Wire `ApplySystemStats` in `TopBarWindow.xaml.cs`**

Replace the method:

```csharp
    private void ApplySystemStats(SystemStatsSnapshot snapshot)
    {
        TopBarConfig topBarConfig = _config.Current.TopBar ?? new TopBarConfig();
        SystemStatsDisplay display = SystemStatsFormatter.Format(snapshot, topBarConfig);

        bool any = display.CpuVisible || display.GpuVisible || display.RamVisible;
        SystemStatsPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (!any)
        {
            return;
        }

        CpuStatsHit.Visibility = display.CpuVisible ? Visibility.Visible : Visibility.Collapsed;
        CpuText.Text = display.CpuBarText;
        CpuToolTip.Content = display.CpuTooltip;

        GpuStatsHit.Visibility = display.GpuVisible ? Visibility.Visible : Visibility.Collapsed;
        GpuText.Text = display.GpuBarText;
        GpuToolTip.Content = display.GpuTooltip;

        RamStatsHit.Visibility = display.RamVisible ? Visibility.Visible : Visibility.Collapsed;
        RamText.Text = display.RamBarText;
        RamToolTip.Content = display.RamTooltip;
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`

Expected: `Build succeeded`. `IsGpuAvailable` / `CpuText` referansları kalmamalı.

- [ ] **Step 5: Commit (yalnızca kullanıcı istediyse)**

```bash
git add src/WharfBar/Services/SystemStatsFormatter.cs src/WharfBar/Services/SystemMonitorService.cs src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat: format system stats from placement instead of fixed percent text"
```

---

### Task 3: CPU GHz (`CallNtPowerInformation`)

**Files:**
- Modify: `src/WharfBar/Native/SystemInfoNative.cs`
- Modify: `src/WharfBar/Services/SystemMonitorService.cs` (`ReadCpuGhz`, `BuildSnapshot`)

**Interfaces:**
- Consumes: `PROCESSOR_POWER_INFORMATION`, `CallNtPowerInformation`, `GetSystemInfo`
- Produces: `double? SystemInfoNative.TryReadCpuGhz()` — çekirdek `CurrentMhz` ortalaması / 1000; 0 veya hata → `null`
- Produces: snapshot `CpuGhz` doldurulur

- [ ] **Step 1: Extend `SystemInfoNative.cs`**

Mevcut `FILETIME`, `MEMORYSTATUSEX`, `GetSystemTimes`, `GlobalMemoryStatusEx` **silinmez**. `MEMORYSTATUSEX` bloğundan sonra şu struct’ları ekle; mevcut `SystemInfoNative` class **içine** (ikinci class yok) `ProcessorInformation` sabiti, `GetSystemInfo`, `CallNtPowerInformation`, `TryReadCpuGhz` ekle.

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_INFO
{
    public ushort wProcessorArchitecture;
    public ushort wReserved;
    public uint dwPageSize;
    public IntPtr lpMinimumApplicationAddress;
    public IntPtr lpMaximumApplicationAddress;
    public IntPtr dwActiveProcessorMask;
    public uint dwNumberOfProcessors;
    public uint dwProcessorType;
    public uint dwAllocationGranularity;
    public ushort wProcessorLevel;
    public ushort wProcessorRevision;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESSOR_POWER_INFORMATION
{
    public uint Number;
    public uint MaxMhz;
    public uint CurrentMhz;
    public uint MhzLimit;
    public uint MaxIdleState;
    public uint CurrentIdleState;
}
```

Inside existing `SystemInfoNative`:

```csharp
    private const int ProcessorInformation = 11;

    [DllImport("kernel32.dll")]
    internal static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

    [DllImport("powrprof.dll")]
    internal static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        IntPtr outputBuffer,
        uint outputBufferLength);

    internal static double? TryReadCpuGhz()
    {
        GetSystemInfo(out SYSTEM_INFO sys);
        int count = (int)sys.dwNumberOfProcessors;
        if (count <= 0)
        {
            return null;
        }

        int size = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
        IntPtr buffer = Marshal.AllocHGlobal(checked(size * count));
        try
        {
            uint status = CallNtPowerInformation(
                ProcessorInformation,
                IntPtr.Zero,
                0,
                buffer,
                (uint)(size * count));
            if (status != 0)
            {
                return null;
            }

            double sum = 0;
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                PROCESSOR_POWER_INFORMATION info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(
                    buffer + (i * size));
                if (info.CurrentMhz > 0)
                {
                    sum += info.CurrentMhz;
                    n++;
                }
            }

            if (n == 0)
            {
                return null;
            }

            return (sum / n) / 1000.0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
```

- [ ] **Step 2: Call from `BuildSnapshot`**

```csharp
        double? cpuGhz = SystemInfoNative.TryReadCpuGhz();
        return new SystemStatsSnapshot(
            cpuPercent,
            cpuGhz,
            ramPercent,
            usedGb,
            totalGb,
            isGpuUsageAvailable,
            isGpuUsageAvailable ? gpuPercent : 0,
            IsGpuVramAvailable: false,
            GpuUsedVramGb: null,
            GpuTotalVramGb: null);
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`

Expected: `Build succeeded`.

Manuel (kullanıcı çalıştırır): varsayılan tooltip’te `CPU hızı: X.X GHz` görünür; bar hâlâ `N%`.

- [ ] **Step 4: Commit (yalnızca kullanıcı istediyse)**

```bash
git add src/WharfBar/Native/SystemInfoNative.cs src/WharfBar/Services/SystemMonitorService.cs
git commit -m "feat: read current CPU frequency via CallNtPowerInformation"
```

---

### Task 4: GPU VRAM (DXGI)

**Files:**
- Create: `src/WharfBar/Native/DxgiAdapterMemory.cs`
- Modify: `src/WharfBar/Services/SystemMonitorService.cs` (`BuildSnapshot`)

**Interfaces:**
- Consumes: `dxgi.dll` `CreateDXGIFactory1`, `IDXGIAdapter3.QueryVideoMemoryInfo`
- Produces: `bool DxgiAdapterMemory.TryReadDedicated(out double usedGb, out double totalGb)`
- Produces: snapshot `IsGpuVramAvailable`, `GpuUsedVramGb`, `GpuTotalVramGb`
- Skip: `DXGI_ADAPTER_FLAG_SOFTWARE` (2) ve açıklamasında `Microsoft Basic Render Driver` geçen adaptörler
- Çoklu GPU: en büyük `DedicatedVideoMemory`; used = `CurrentUsage`; total = dedicated (0 ise `Budget`)
- `CreateDXGIFactory1` `0x800401F0` (`CO_E_NOTINITIALIZED`) → o thread’de `CoInitializeEx(COINIT_MULTITHREADED)` sonra tekrar dene; yalnızca o durumda `CoUninitialize`

- [ ] **Step 1: Add `DxgiAdapterMemory.cs`**

COM vtable `Native/AudioEndpointVolume.cs` gibi stub + çağrılan slotlar. Tam dosya:

```csharp
using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class DxgiAdapterMemory
{
    private static readonly Guid Factory1Iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid Adapter3Iid = new("645967a4-1392-4310-a798-8053ce3e93fd");

    private const int DxgiAdapterFlagSoftware = 2;
    private const int DxgiMemorySegmentGroupLocal = 0;
    private const uint CoInitMultithreaded = 0;
    private const int CoENotInitialized = unchecked((int)0x800401F0);
    private const double BytesPerGb = 1024.0 * 1024.0 * 1024.0;

    internal static bool TryReadDedicated(out double usedGb, out double totalGb)
    {
        usedGb = 0;
        totalGb = 0;
        bool didInit = false;
        int hr = CreateDXGIFactory1(in Factory1Iid, out IDXGIFactory1? factory);
        if (hr == CoENotInitialized)
        {
            _ = CoInitializeEx(IntPtr.Zero, CoInitMultithreaded);
            didInit = true;
            hr = CreateDXGIFactory1(in Factory1Iid, out factory);
        }

        try
        {
            if (hr != NativeMethods.S_OK || factory is null)
            {
                return false;
            }

            nuint bestDedicated = 0;
            double bestUsed = 0;
            double bestTotal = 0;
            bool found = false;

            for (uint i = 0; ; i++)
            {
                int enumHr = factory.EnumAdapters1(i, out IDXGIAdapter1? adapter1);
                if (enumHr != NativeMethods.S_OK || adapter1 is null)
                {
                    break;
                }

                try
                {
                    if (adapter1.GetDesc1(out DXGI_ADAPTER_DESC1 desc) != NativeMethods.S_OK)
                    {
                        continue;
                    }

                    if ((desc.Flags & DxgiAdapterFlagSoftware) != 0
                        || desc.Description.Contains("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    IntPtr unk = Marshal.GetIUnknownForObject(adapter1);
                    Guid iid = Adapter3Iid;
                    int qi = Marshal.QueryInterface(unk, ref iid, out IntPtr adapter3Ptr);
                    Marshal.Release(unk);
                    if (qi != NativeMethods.S_OK || adapter3Ptr == IntPtr.Zero)
                    {
                        continue;
                    }

                    IDXGIAdapter3 adapter3 = (IDXGIAdapter3)Marshal.GetObjectForIUnknown(adapter3Ptr);
                    Marshal.Release(adapter3Ptr);
                    try
                    {
                        if (adapter3.QueryVideoMemoryInfo(0, DxgiMemorySegmentGroupLocal, out DXGI_QUERY_VIDEO_MEMORY_INFO mem)
                            != NativeMethods.S_OK)
                        {
                            continue;
                        }

                        nuint dedicated = desc.DedicatedVideoMemory;
                        if (dedicated >= bestDedicated)
                        {
                            bestDedicated = dedicated;
                            double dedicatedGb = dedicated / BytesPerGb;
                            double budgetGb = mem.Budget / BytesPerGb;
                            bestUsed = mem.CurrentUsage / BytesPerGb;
                            bestTotal = dedicatedGb > 0 ? dedicatedGb : budgetGb;
                            found = bestTotal > 0;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter3);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter1);
                }
            }

            if (!found)
            {
                return false;
            }

            usedGb = bestUsed;
            totalGb = bestTotal;
            return true;
        }
        finally
        {
            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }

            if (didInit)
            {
                CoUninitialize();
            }
        }
    }

    [DllImport("dxgi.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int CreateDXGIFactory1(
        in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IDXGIFactory1? factory);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [StructLayout(LayoutKind.Sequential)]
    private struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong AvailableForReservation;
        public ulong CurrentUsage;
        public ulong CurrentReservation;
        public ulong Budget;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC2
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
        public int GraphicsPreemptionGranularity;
        public int ComputePreemptionGranularity;
    }

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int EnumAdapters(uint adapter, out IntPtr ppAdapter);
        int MakeWindowAssociation(IntPtr windowHandle, uint flags);
        int GetWindowAssociation(out IntPtr pWindowHandle);
        int CreateSwapChain(IntPtr pDevice, IntPtr pDesc, out IntPtr ppSwapChain);
        int CreateSoftwareAdapter(IntPtr module, out IntPtr ppAdapter);

        [PreserveSig]
        int EnumAdapters1(uint adapter, out IDXGIAdapter1 ppAdapter);

        [PreserveSig]
        int IsCurrent();
    }

    [ComImport]
    [Guid("29038f61-3839-4626-91fd-086879011a05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int EnumOutputs(uint output, out IntPtr ppOutput);
        int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        int CheckInterfaceSupport(ref Guid interfaceName, out long pUmdVersion);

        [PreserveSig]
        int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
    }

    [ComImport]
    [Guid("645967a4-1392-4310-a798-8053ce3e93fd")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter3
    {
        int SetPrivateData(ref Guid name, uint dataSize, IntPtr pData);
        int SetPrivateDataInterface(ref Guid name, IntPtr pUnknown);
        int GetPrivateData(ref Guid name, ref uint pDataSize, IntPtr pData);
        int GetParent(ref Guid riid, out IntPtr ppParent);
        int EnumOutputs(uint output, out IntPtr ppOutput);
        int GetDesc(out DXGI_ADAPTER_DESC pDesc);
        int CheckInterfaceSupport(ref Guid interfaceName, out long pUmdVersion);
        int GetDesc1(out DXGI_ADAPTER_DESC1 pDesc);
        int GetDesc2(out DXGI_ADAPTER_DESC2 pDesc);
        int RegisterHardwareContentProtectionTeardownStatusEvent(IntPtr hEvent, out uint pdwCookie);
        int UnregisterHardwareContentProtectionTeardownStatusEvent(uint dwCookie);

        [PreserveSig]
        int QueryVideoMemoryInfo(
            uint nodeIndex,
            int memorySegmentGroup,
            out DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);
    }
}
```

- [ ] **Step 2: Fill VRAM in `BuildSnapshot`**

```csharp
        bool vramOk = DxgiAdapterMemory.TryReadDedicated(out double vramUsed, out double vramTotal);
        return new SystemStatsSnapshot(
            cpuPercent,
            cpuGhz,
            ramPercent,
            usedGb,
            totalGb,
            isGpuUsageAvailable,
            isGpuUsageAvailable ? gpuPercent : 0,
            vramOk,
            vramOk ? vramUsed : null,
            vramOk ? vramTotal : null);
```

Add `using WharfBar.Native;` if not already present (`SystemInfoNative` already requires it).

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`

Expected: `Build succeeded`.

Manuel: GPU tooltip’te `GPU VRAM: X.X / Y GB` (Basic Render / DXGI yoksa satır yok; GPU `%` durur).

- [ ] **Step 4: Commit (yalnızca kullanıcı istediyse)**

```bash
git add src/WharfBar/Native/DxgiAdapterMemory.cs src/WharfBar/Services/SystemMonitorService.cs
git commit -m "feat: read dedicated GPU memory via DXGI"
```

---

### Task 5: Genel ayarlar ComboBox’ları

**Files:**
- Modify: `src/WharfBar/Views/Pages/GeneralPage.xaml` (CPU/GPU/RAM kartları)
- Modify: `src/WharfBar/Views/Pages/GeneralPage.xaml.cs`

**Interfaces:**
- Consumes: `StatPlacement.ToComboIndex` / `FromComboIndex`, `TopBarConfig` yerleşimleri
- Produces: ana rozet kapalı → kart `Collapsed`; rozet toggle kapalı → ComboBox satırları `Collapsed` (değer silinmez)
- ComboBox item sırası: 0 `Gizli`, 1 `Yalnızca tooltip`, 2 `Üst bar`
- `SelectionChanged` XAML’de değil; ctor’da `LoadSettings` sonrası bağlanır (`_isLoading` koruması)

- [ ] **Step 1: Replace CPU/GPU/RAM cards in `GeneralPage.xaml`**

Replace the three `CpuCard` / `GpuCard` / `RamCard` borders with:

```xml
            <Border x:Name="CpuCard" Style="{StaticResource PowerToysSubCard}">
                <StackPanel>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <ui:SymbolIcon Grid.Column="0" Symbol="DeveloperBoard24" FontSize="16"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                       VerticalAlignment="Center" Margin="0,0,12,0" />
                        <TextBlock Grid.Column="1" Text="CPU Kullanımı Rozeti" FontSize="13" VerticalAlignment="Center" />
                        <ui:ToggleSwitch x:Name="CpuToggle"
                                        Grid.Column="2"
                                        HorizontalAlignment="Right"
                                        VerticalAlignment="Center" />
                    </Grid>
                    <StackPanel x:Name="CpuPlacementPanel" Margin="0,10,0,0">
                        <Grid Margin="0,0,0,8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Kullanım yüzdesi" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                            <ComboBox x:Name="CpuPercentCombo" Grid.Column="1" Width="180"
                                      HorizontalAlignment="Right">
                                <ComboBoxItem Content="Gizli" />
                                <ComboBoxItem Content="Yalnızca tooltip" />
                                <ComboBoxItem Content="Üst bar" />
                            </ComboBox>
                        </Grid>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Anlık hız (GHz)" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                            <ComboBox x:Name="CpuGhzCombo" Grid.Column="1" Width="180"
                                      HorizontalAlignment="Right">
                                <ComboBoxItem Content="Gizli" />
                                <ComboBoxItem Content="Yalnızca tooltip" />
                                <ComboBoxItem Content="Üst bar" />
                            </ComboBox>
                        </Grid>
                    </StackPanel>
                </StackPanel>
            </Border>

            <Border x:Name="GpuCard" Style="{StaticResource PowerToysSubCard}">
                <StackPanel>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <ui:SymbolIcon Grid.Column="0" Symbol="Desktop24" FontSize="16"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                       VerticalAlignment="Center" Margin="0,0,12,0" />
                        <TextBlock Grid.Column="1" Text="GPU Kullanımı Rozeti" FontSize="13" VerticalAlignment="Center" />
                        <ui:ToggleSwitch x:Name="GpuToggle"
                                        Grid.Column="2"
                                        HorizontalAlignment="Right"
                                        VerticalAlignment="Center" />
                    </Grid>
                    <StackPanel x:Name="GpuPlacementPanel" Margin="0,10,0,0">
                        <Grid Margin="0,0,0,8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Kullanım yüzdesi" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                            <ComboBox x:Name="GpuPercentCombo" Grid.Column="1" Width="180"
                                      HorizontalAlignment="Right">
                                <ComboBoxItem Content="Gizli" />
                                <ComboBoxItem Content="Yalnızca tooltip" />
                                <ComboBoxItem Content="Üst bar" />
                            </ComboBox>
                        </Grid>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="VRAM (kullanılan / toplam)" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                            <ComboBox x:Name="GpuVramCombo" Grid.Column="1" Width="180"
                                      HorizontalAlignment="Right">
                                <ComboBoxItem Content="Gizli" />
                                <ComboBoxItem Content="Yalnızca tooltip" />
                                <ComboBoxItem Content="Üst bar" />
                            </ComboBox>
                        </Grid>
                    </StackPanel>
                </StackPanel>
            </Border>

            <Border x:Name="RamCard" Style="{StaticResource PowerToysSubCard}">
                <StackPanel>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <ui:SymbolIcon Grid.Column="0" Symbol="Server24" FontSize="16"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                       VerticalAlignment="Center" Margin="0,0,12,0" />
                        <TextBlock Grid.Column="1" Text="RAM Kullanımı Rozeti" FontSize="13" VerticalAlignment="Center" />
                        <ui:ToggleSwitch x:Name="RamToggle"
                                        Grid.Column="2"
                                        HorizontalAlignment="Right"
                                        VerticalAlignment="Center" />
                    </Grid>
                    <StackPanel x:Name="RamPlacementPanel" Margin="0,10,0,0">
                        <Grid Margin="0,0,0,8">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Kullanım yüzdesi" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                            <ComboBox x:Name="RamPercentCombo" Grid.Column="1" Width="180"
                                      HorizontalAlignment="Right">
                                <ComboBoxItem Content="Gizli" />
                                <ComboBoxItem Content="Yalnızca tooltip" />
                                <ComboBoxItem Content="Üst bar" />
                            </ComboBox>
                        </Grid>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="Kullanılan / toplam" FontSize="12" VerticalAlignment="Center"
                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                            <ComboBox x:Name="RamUsedTotalCombo" Grid.Column="1" Width="180"
                                      HorizontalAlignment="Right">
                                <ComboBoxItem Content="Gizli" />
                                <ComboBoxItem Content="Yalnızca tooltip" />
                                <ComboBoxItem Content="Üst bar" />
                            </ComboBox>
                        </Grid>
                    </StackPanel>
                </StackPanel>
            </Border>
```

- [ ] **Step 2: Update `GeneralPage.xaml.cs`**

Replace the class body handlers so `LoadSettings` fills combos, `OnStatsDetailChanged` still saves toggles, new `OnPlacementChanged` saves placements, `UpdateSubToggleVisibility` also toggles placement panels:

```csharp
    public GeneralPage()
    {
        InitializeComponent();
        LoadSettings();

        AutostartToggle.Checked += OnAutostartChanged;
        AutostartToggle.Unchecked += OnAutostartChanged;
        SystemStatsToggle.Checked += OnSystemStatsChanged;
        SystemStatsToggle.Unchecked += OnSystemStatsChanged;
        CpuToggle.Checked += OnStatsDetailChanged;
        CpuToggle.Unchecked += OnStatsDetailChanged;
        GpuToggle.Checked += OnStatsDetailChanged;
        GpuToggle.Unchecked += OnStatsDetailChanged;
        RamToggle.Checked += OnStatsDetailChanged;
        RamToggle.Unchecked += OnStatsDetailChanged;
        CpuPercentCombo.SelectionChanged += OnPlacementChanged;
        CpuGhzCombo.SelectionChanged += OnPlacementChanged;
        GpuPercentCombo.SelectionChanged += OnPlacementChanged;
        GpuVramCombo.SelectionChanged += OnPlacementChanged;
        RamPercentCombo.SelectionChanged += OnPlacementChanged;
        RamUsedTotalCombo.SelectionChanged += OnPlacementChanged;

        _isLoading = false;
    }

    private void LoadSettings()
    {
        AutostartToggle.IsChecked = Cfg.Autostart;
        SystemStatsToggle.IsChecked = Cfg.TopBar.ShowSystemStats;
        CpuToggle.IsChecked = Cfg.TopBar.ShowCpuStats;
        GpuToggle.IsChecked = Cfg.TopBar.ShowGpuStats;
        RamToggle.IsChecked = Cfg.TopBar.ShowRamStats;
        CpuPercentCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.CpuPercentPlacement, StatPlacement.Bar);
        CpuGhzCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.CpuGhzPlacement, StatPlacement.Tooltip);
        GpuPercentCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.GpuPercentPlacement, StatPlacement.Bar);
        GpuVramCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.GpuVramPlacement, StatPlacement.Tooltip);
        RamPercentCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.RamPercentPlacement, StatPlacement.Bar);
        RamUsedTotalCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.RamUsedTotalPlacement, StatPlacement.Tooltip);
        UpdateSubToggleVisibility();
    }

    private void OnPlacementChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.TopBar.CpuPercentPlacement = StatPlacement.FromComboIndex(CpuPercentCombo.SelectedIndex);
        Cfg.TopBar.CpuGhzPlacement = StatPlacement.FromComboIndex(CpuGhzCombo.SelectedIndex);
        Cfg.TopBar.GpuPercentPlacement = StatPlacement.FromComboIndex(GpuPercentCombo.SelectedIndex);
        Cfg.TopBar.GpuVramPlacement = StatPlacement.FromComboIndex(GpuVramCombo.SelectedIndex);
        Cfg.TopBar.RamPercentPlacement = StatPlacement.FromComboIndex(RamPercentCombo.SelectedIndex);
        Cfg.TopBar.RamUsedTotalPlacement = StatPlacement.FromComboIndex(RamUsedTotalCombo.SelectedIndex);
        SaveAndApply();
    }

    private void UpdateSubToggleVisibility()
    {
        bool show = SystemStatsToggle.IsChecked == true;
        CpuCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        GpuCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RamCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CpuPlacementPanel.Visibility = CpuToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GpuPlacementPanel.Visibility = GpuToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RamPlacementPanel.Visibility = RamToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
```

Keep existing `OnAutostartChanged`, `OnSystemStatsChanged`, `OnStatsDetailChanged`, `SaveAndApply`. In `OnStatsDetailChanged` after assigning toggles, call `UpdateSubToggleVisibility()` before `SaveAndApply()`.

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`

Expected: `Build succeeded`.

Manuel checklist (spec):

- Varsayılan: bar `%`; tooltip `%` + GHz / GB / VRAM
- CPU GHz → Üst bar: `CPU  32% · 4.2 GHz`
- RAM `%` → Gizli, GB → Üst bar: `RAM  8.1 / 16 GB`
- CPU her iki alan Gizli: CPU rozeti yok
- CPU `%` Gizli, GHz Yalnızca tooltip: rozet `CPU`, hover’da GHz
- Ayar değişince üst bar anında; `config.json` yazılır
- Çıkışta AppBar + görev çubuğu restore

- [ ] **Step 4: Commit (yalnızca kullanıcı istediyse)**

```bash
git add src/WharfBar/Views/Pages/GeneralPage.xaml src/WharfBar/Views/Pages/GeneralPage.xaml.cs
git commit -m "feat: add per-field bar/tooltip placement for CPU GPU RAM"
```

---

### Task 6: Doküman

**Files:**
- Modify: `docs/01-architecture.md`
- Modify: `docs/02-win32-appbar-dwm.md`
- Modify: `.agent/project.md`
- Modify: `docs/superpowers/specs/2026-08-13-system-stats-detail-design.md`

**Interfaces:**
- Consumes: uygulanan API adları (`StatPlacement`, `SystemStatsFormatter`, `DxgiAdapterMemory`, `CallNtPowerInformation`)
- Produces: spec durum satırı plan linkine güncellenir

- [ ] **Step 1: `docs/01-architecture.md`**

In Native/ list after `PowerStatus.cs`:

```
    SystemInfoNative.cs         # GetSystemTimes, GlobalMemoryStatusEx, CallNtPowerInformation
    DxgiAdapterMemory.cs        # DXGI dedicated VRAM (IDXGIAdapter3)
```

In Services/ list after `StatusService.cs`:

```
    SystemMonitorService.cs     # CPU/RAM/GPU ham snapshot (poll)
    SystemStatsFormatter.cs     # yerleşim → bar/tooltip metni
```

In sorumluluklar table after `StatusService`:

```
| `SystemMonitorService` | CPU `%`/`GHz`, RAM, GPU `%`/VRAM ham snapshot; 1.5 sn poll |
| `SystemStatsFormatter` | `StatPlacement` + snapshot → rozet metni |
| `DxgiAdapterMemory` | DXGI local segment VRAM |
```

In config alan table after `topBar.statusItems`:

```
| `topBar.showSystemStats` / `showCpuStats` / `showGpuStats` / `showRamStats` | Rozet ana anahtarları |
| `topBar.cpuPercentPlacement` (ve `cpuGhz`, `ramPercent`, `ramUsedTotal`, `gpuPercent`, `gpuVram`) | `bar` \| `tooltip` \| `hidden`; bilinmeyen → alan varsayılanı (`%`=`bar`, ekstra=`tooltip`) |
```

- [ ] **Step 2: `docs/02-win32-appbar-dwm.md` bölüm (c)**

After the “Top bar göstergeleri için” bullets add:

```
Donanım rozetleri (sol küme, `SystemMonitorService`):

- CPU `%`: `GetSystemTimes`
- CPU GHz: `powrprof!CallNtPowerInformation` (`ProcessorInformation` = 11), çekirdek `CurrentMhz` ortalaması
- RAM: `GlobalMemoryStatusEx`
- GPU `%`: `GPU Engine` performans sayaçları (`engtype_3D`)
- GPU VRAM: `CreateDXGIFactory1` + `IDXGIAdapter3.QueryVideoMemoryInfo` (local); Basic Render atlanır
- Yerleşim: `topBar.*Placement` (`bar` / `tooltip` / `hidden`); biçim `SystemStatsFormatter`
```

- [ ] **Step 3: `.agent/project.md`**

In “Kritik teknik gerçekler” item 8 (Durum satırı), append:

```
Üst bar sol küme: CPU/GPU/RAM (`SystemMonitorService`); alan yerleşimi `bar`/`tooltip`/`hidden`
(GHz: `CallNtPowerInformation`, VRAM: DXGI).
```

In “Mevcut repo durumu” paragraph, after saat/ağ/pil mention, add that system stats badges support GHz/GB placement.

- [ ] **Step 4: Spec header**

Replace:

`**Durum:** Tasarım onaylandı (kullanıcı 2026-08-13) — uygulama planı henüz yok.`

with:

```
**Durum:** Tasarım onaylandı (kullanıcı 2026-08-13) — uygulama planı:
  [`../plans/2026-08-13-system-stats-detail.md`](../plans/2026-08-13-system-stats-detail.md).
```

- [ ] **Step 5: Build (docs-only; still compile once)**

Run: `dotnet build WharfBar.sln`

Expected: `Build succeeded`.

- [ ] **Step 6: Commit (yalnızca kullanıcı istediyse)**

```bash
git add docs/01-architecture.md docs/02-win32-appbar-dwm.md .agent/project.md docs/superpowers/specs/2026-08-13-system-stats-detail-design.md
git commit -m "docs: document CPU GPU RAM placement, GHz, and DXGI VRAM"
```
