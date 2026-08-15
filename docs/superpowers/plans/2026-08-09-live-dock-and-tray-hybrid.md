# Faz 9 — Canlı Dock, Tepsi Hibrit ve Görev Çubuğu Yaşam Döngüsü Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WharfBar açıkken Windows görev çubuğunu gizleyip her çıkış yolunda geri yüklemek; dock’u canlı TaskBar pinleri ve çalışan uygulamalarla doldurmak; üst bara yabancı ikon çizmeden native tray overflow düğmesi eklemek.

**Architecture:** `TaskbarVisibilityService` başlatmada baseline yakalar (`ABM_GETSTATE` + `Shell_TrayWnd` görünürlüğü), `ShowWindow(SW_HIDE)` ile gizler, tüm çıkış/crash yollarında idempotent `Restore` çalıştırır (`ABM_REMOVE` ile aynı set; sıra: önce taskbar restore, sonra AppBar remove). Dock: `TaskbarPinnedAppsSource` (`.lnk`) + `RunningAppsSource` (`EnumWindows` + AUMID) → `DockAppsComposer`; `config.pinnedApps` yalnızca seed/fallback. Üst bar: `TrayOverflowInvoker` → `SendInput` `Win+B`.

**Tech Stack:** .NET 8 WPF, WPF-UI 4.x, Win32 P/Invoke (`ABM_GETSTATE`/`SETSTATE`, `FindWindow`/`ShowWindow`, `EnumWindows`, AUMID `GetProp`, `SendInput`), WScript.Shell COM (`.lnk`). Yeni NuGet yok.

**Spec:** [`docs/superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md`](../specs/2026-08-09-live-dock-and-tray-hybrid-design.md)  
**ADR:** [`docs/decisions/005-shell-replace-live-dock-tray-hybrid.md`](../../decisions/005-shell-replace-live-dock-tray-hybrid.md)

## Global Constraints

- Target: `net8.0-windows10.0.19041.0`; `global.json` ekleme
- Yeni NuGet / test projesi ekleme; doğrulama = `dotnet build WharfBar.sln` (0 warnings / 0 errors) + manuel checklist
- Ham P/Invoke `Native/` içinde `internal`; UI `NativeMethods`’a doğrudan bağlanmaz
- `AllowsTransparency=true` + Mica yasak
- Yabancı tepsi ikonu okuma / çizme / click-forward / `ReadProcessMemory` / belgelenmemiş NotifyIcon enumeration **yasak**
- `Shell_NotifyIcon` yalnızca WharfBar’ın kendi Quit ikonu (`TrayIconService`)
- `schemaVersion` 1 kalır; `pinnedApps` şemadan silinmez (seed/fallback)
- v1: birincil monitör; UWP ikon yolu yok (placeholder veya atla; kırık Appx yolu sunma)
- Görev çubuğunu WharfBar kapalıyken kalıcı bozma yok
- Faz 8 (dock auto-hide polish) bu planın **dışında** — 9a/9b sonrası ayrı iş; burada yalnızca “sonra gelir” notu
- Commit yalnızca kullanıcı istediğinde (görev sonundaki commit adımlarını kullanıcı istemedikçe atla)

### Kilitlenen teknik seçimler (spec’te açık bırakılanlar)

| Konu | Seçim |
|------|--------|
| Görev çubuğu gizleme | `FindWindow("Shell_TrayWnd")` + `ShowWindow(SW_HIDE)`; baseline = `ABM_GETSTATE` + `IsWindowVisible` |
| Restore | `ShowWindow(SW_SHOWNA)` (yakalanan görünürlüğe göre) + `ABM_SETSTATE(baseline)`; `Interlocked` idempotent |
| Çıkış sırası | Önce `TaskbarVisibilityService.Restore()`, sonra `AppBar.Remove()` |
| Pin yenileme | `FileSystemWatcher` (debounce 750 ms) + yedek `DispatcherTimer` 15 s poll |
| Overflow | Birincil: `SendInput` ile `Win+B`; başarısızsa `Debug.WriteLine` + no-op (chevron yedek bu planda yok) |
| Çalışan gösterge | İkon altında 6×6 DIP accent `Ellipse` |
| Config `replaceTaskbar` | Eklenmez (v1 her zaman gizle) |
| Ses / volume | Bu planda **yok** (P2 / 9c sonrası aday; StatusService genişletmesi) |

---

## File structure

| File | Responsibility |
|------|----------------|
| `src/WharfBar/Native/NativeMethods.cs` | `ABM_GETSTATE`/`SETSTATE`, `ABS_*`, `FindWindow`, `ShowWindow`, `IsWindowVisible`, `EnumWindows`, `GetWindowText`, `IsWindowVisible`, `GetWindowThreadProcessId`, `SendInput` / `INPUT`, `GetProp` / property-store P/Invoke ihtiyacı |
| `src/WharfBar/Native/TaskbarNative.cs` | Tray hwnd bulma, GETSTATE/SETSTATE, ShowWindow wrapper |
| `src/WharfBar/Native/WindowEnumeration.cs` | `EnumWindows` callback + top-level filtre yardımcıları |
| `src/WharfBar/Native/AppUserModelId.cs` | Pencere → AUMID (veya boş) |
| `src/WharfBar/Native/SendInputKeys.cs` | `Win+B` sentetik basış |
| `src/WharfBar/Services/TaskbarVisibilityService.cs` | Capture → Hide → Restore; çıkış hook’ları |
| `src/WharfBar/Services/ShortcutResolver.cs` | `.lnk` → target path / args / display name (WScript.Shell) |
| `src/WharfBar/Services/TaskbarPinnedAppsSource.cs` | TaskBar klasörü `.lnk` listesi + watcher/poll |
| `src/WharfBar/Services/RunningAppsSource.cs` | EnumWindows + gruplama snapshot |
| `src/WharfBar/Services/DockAppsComposer.cs` | Pin + running birleşimi; config fallback |
| `src/WharfBar/Services/WindowActivationService.cs` | Çalışan pencereyi öne getir / restore |
| `src/WharfBar/Services/TrayOverflowInvoker.cs` | Native overflow tetikleyici |
| `src/WharfBar/Models/DockAppItem.cs` | Birleşik dock satırı (path, label, running, hwnd, pinned) |
| `src/WharfBar/Models/PinnedApp.cs` | Mevcut; değişmez (config seed) |
| `src/WharfBar/Views/DockWindow.xaml(.cs)` | Composer’a bağlan; gösterge + launch/focus |
| `src/WharfBar/Views/TopBarWindow.xaml(.cs)` | Overflow düğmesi; shutdown’ta Restore+Remove sırası; `TaskbarCreated`’da yeniden gizle |
| `src/WharfBar/App.xaml.cs` | Startup’ta CaptureAndHide (pencerelerden önce) |
| Docs | roadmap / 03 / 04 / architecture / DemoPinnedApps yorumu |

```
App.OnStartup
  ├─ TaskbarVisibilityService.CaptureAndHide()   ← Task 2–3
  ├─ TopBarWindow (AppBar + Status + TrayOverflowButton)  ← Task 10–11
  ├─ DockWindow ← DockAppsComposer
  │     ├─ TaskbarPinnedAppsSource
  │     └─ RunningAppsSource
  └─ TrayIconService (Quit; değişmez)

Exit paths
  ├─ TaskbarVisibilityService.Restore()
  └─ AppBar.Remove()
```

---

## Faz 9a — Görev çubuğu yaşam döngüsü

### Task 1: Taskbar native P/Invoke

**Files:**
- Modify: `src/WharfBar/Native/NativeMethods.cs`
- Create: `src/WharfBar/Native/TaskbarNative.cs`

**Interfaces:**
- Consumes: mevcut `SHAppBarMessage`, `APPBARDATA`
- Produces:
  - `NativeMethods.ABM_GETSTATE` (= `0x00000004`), `ABM_SETSTATE` (= `0x0000000A`)
  - `NativeMethods.ABS_AUTOHIDE` (= `0x00000001`), `ABS_ALWAYSONTOP` (= `0x00000002`)
  - `NativeMethods.SW_HIDE` (= `0`), `SW_SHOWNA` (= `8`)
  - `TaskbarNative.FindTrayHwnd() → IntPtr`
  - `TaskbarNative.GetState() → uint`
  - `TaskbarNative.SetState(uint state)`
  - `TaskbarNative.IsTrayVisible() → bool`
  - `TaskbarNative.SetTrayVisible(bool visible)`

- [ ] **Step 1: `NativeMethods` sabitleri ve DllImport’lar**

`NativeMethods.cs` içine (mevcut `ABM_*` bloğunun yanına) ekle:

```csharp
internal const uint ABM_GETSTATE = 0x00000004;
internal const uint ABM_SETSTATE = 0x0000000A;

internal const int ABS_AUTOHIDE = 0x00000001;
internal const int ABS_ALWAYSONTOP = 0x00000002;

internal const int SW_HIDE = 0;
internal const int SW_SHOWNA = 8;

[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

[DllImport("user32.dll", SetLastError = true)]
internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

[DllImport("user32.dll", SetLastError = true)]
internal static extern bool IsWindowVisible(IntPtr hWnd);
```

- [ ] **Step 2: `TaskbarNative` wrapper**

```csharp
namespace WharfBar.Native;

/// <summary>
/// Thin wrappers for primary-monitor taskbar hwnd + AppBar state.
/// UI must not call NativeMethods for tray hide/restore directly.
/// </summary>
internal static class TaskbarNative
{
    private const string TrayClass = "Shell_TrayWnd";

    internal static IntPtr FindTrayHwnd() =>
        NativeMethods.FindWindow(TrayClass, null);

    internal static uint GetState()
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        };
        return (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
    }

    internal static void SetState(uint state)
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            lParam = (IntPtr)(int)state,
        };
        _ = NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
    }

    internal static bool IsTrayVisible()
    {
        IntPtr hwnd = FindTrayHwnd();
        return hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);
    }

    internal static void SetTrayVisible(bool visible)
    {
        IntPtr hwnd = FindTrayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: Shell_TrayWnd not found");
            return;
        }

        _ = NativeMethods.ShowWindow(
            hwnd,
            visible ? NativeMethods.SW_SHOWNA : NativeMethods.SW_HIDE);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Native/NativeMethods.cs src/WharfBar/Native/TaskbarNative.cs
git commit -m "feat(native): add taskbar state and Shell_TrayWnd helpers"
```

---

### Task 2: `TaskbarVisibilityService`

**Files:**
- Create: `src/WharfBar/Services/TaskbarVisibilityService.cs`

**Interfaces:**
- Consumes: `TaskbarNative.*`
- Produces:
  - `TaskbarVisibilityService` (sealed, process-wide usage via `App`)
  - `void CaptureAndHide()` — baseline sakla, gizle, shutdown hook’larını bağla
  - `void Restore()` — idempotent; baseline’a dön
  - `void ReapplyHide()` — `TaskbarCreated` sonrası tekrar gizle (baseline değiştirmez)
  - `bool IsHidden { get; }`

- [ ] **Step 1: Servisi yaz**

```csharp
using System.Diagnostics;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Captures the Windows taskbar baseline on startup, hides it while WharfBar runs,
/// and restores on every exit/crash path (same discipline as AppBar.Remove).
/// </summary>
internal sealed class TaskbarVisibilityService
{
    private uint _baselineState;
    private bool _baselineVisible;
    private int _captured; // 0 = not captured
    private int _restored; // 0 = not restored
    private int _hooksAttached;
    private bool _hidden;

    internal bool IsHidden => _hidden;

    internal void CaptureAndHide()
    {
        if (Interlocked.Exchange(ref _captured, 1) != 0)
        {
            ReapplyHide();
            return;
        }

        _baselineState = TaskbarNative.GetState();
        _baselineVisible = TaskbarNative.IsTrayVisible();
        Interlocked.Exchange(ref _restored, 0);

        AttachShutdownHooks();
        ApplyHide();
    }

    internal void ReapplyHide()
    {
        if (Volatile.Read(ref _captured) == 0 || Volatile.Read(ref _restored) != 0)
        {
            return;
        }

        ApplyHide();
    }

    internal void Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
        {
            return;
        }

        if (Volatile.Read(ref _captured) == 0)
        {
            return;
        }

        try
        {
            TaskbarNative.SetTrayVisible(_baselineVisible);
            TaskbarNative.SetState(_baselineState);
            _hidden = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar taskbar restore failed: {ex.Message}");
        }
    }

    private void ApplyHide()
    {
        try
        {
            TaskbarNative.SetTrayVisible(false);
            _hidden = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar taskbar hide failed: {ex.Message}");
        }
    }

    private void AttachShutdownHooks()
    {
        if (Interlocked.Exchange(ref _hooksAttached, 1) != 0)
        {
            return;
        }

        if (System.Windows.Application.Current is { } app)
        {
            app.Exit += (_, _) => Restore();
            app.SessionEnding += (_, _) => Restore();
            app.DispatcherUnhandledException += (_, _) => Restore();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Restore();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Services/TaskbarVisibilityService.cs
git commit -m "feat: add TaskbarVisibilityService capture/hide/restore"
```

---

### Task 3: Startup + çıkış yollarına bağla

**Files:**
- Modify: `src/WharfBar/App.xaml.cs`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `TaskbarVisibilityService`
- Produces: App alanı `_taskbar`; TopBar `ReleaseShellState()` = Restore → Remove; `TaskbarCreated` → ReRegister + ReapplyHide + ApplyPlacement

- [ ] **Step 1: `App.OnStartup` — pencerelerden önce CaptureAndHide**

`App.xaml.cs` alan ekle ve startup sırasını güncelle:

```csharp
private TaskbarVisibilityService? _taskbar;

// OnStartup, Autostart/tema sonrası, TopBarWindow oluşturmadan hemen önce:
_taskbar = new TaskbarVisibilityService();
_taskbar.CaptureAndHide();

var topBar = new TopBarWindow(_taskbar);
MainWindow = topBar;
topBar.Show();
// ... dock / tray aynı
```

`OnExit` başında (tray dispose’tan önce veya sonra fark etmez; Restore idempotent):

```csharp
_taskbar?.Restore();
_taskbar = null;
```

- [ ] **Step 2: `TopBarWindow` — ctor + Release sırası + TaskbarCreated**

Ctor’a servis al:

```csharp
private readonly TaskbarVisibilityService _taskbar;

public TopBarWindow(TaskbarVisibilityService taskbar)
{
    _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
    InitializeComponent();
}
```

`ReleaseAppBar` yerine:

```csharp
private void ReleaseShellState()
{
    // Taskbar first so work area can expand before we drop our AppBar reservation.
    _taskbar.Restore();
    _appBar?.Remove();
}
```

Tüm `ReleaseAppBar()` çağrılarını `ReleaseShellState()` yap.  
`TaskbarCreated` handler:

```csharp
_appBar?.ReRegister();
_taskbar.ReapplyHide();
ApplyPlacement();
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Manuel doğrulama (9a)**

Run: `dotnet run --project src/WharfBar` — **asla `Stop-Process -Force` kullanma**

Checklist:
- [ ] Başlatınca Windows görev çubuğu kaybolur
- [ ] Tepsi **Quit WharfBar** / Alt+F4 → görev çubuğu geri gelir
- [ ] Üst bar AppBar hâlâ 30 px rezerve; çıkışta work area baseline
- [ ] (İsteğe bağlı) Görev Yöneticisi’nden “görevi sonlandır” → görev çubuğu asılı kalabilir; `explorer.exe` restart düzeltir (beklenen gotcha)

- [ ] **Step 5: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/App.xaml.cs src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat: hide taskbar on start and restore on all exit paths"
```

---

## Faz 9b — Canlı dock

### Task 4: `DockAppItem` + `ShortcutResolver`

**Files:**
- Create: `src/WharfBar/Models/DockAppItem.cs`
- Create: `src/WharfBar/Services/ShortcutResolver.cs`
- Modify: `src/WharfBar/Models/DemoPinnedApps.cs` (yorum: seed/fallback)

**Interfaces:**
- Consumes: WScript.Shell COM (`WScript.Shell`)
- Produces:
  - `DockAppItem(string Id, string Path, string Args, string Label, bool IsPinned, bool IsRunning, IntPtr MainWindowHandle)`
  - `ShortcutResolver.TryResolve(string lnkPath, out string targetPath, out string args, out string label) → bool`

- [ ] **Step 1: Model**

```csharp
namespace WharfBar.Models;

/// <summary>Unified dock strip entry (pinned and/or running).</summary>
internal sealed record DockAppItem(
    string Id,
    string Path,
    string Args,
    string Label,
    bool IsPinned,
    bool IsRunning,
    IntPtr MainWindowHandle);
```

`Id` kuralı: mümkünse AUMID; yoksa `Path` (OrdinalIgnoreCase); pinsiz UWP için `"hwnd:{handle}"`.

- [ ] **Step 2: ShortcutResolver**

```csharp
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WharfBar.Services;

internal static class ShortcutResolver
{
    internal static bool TryResolve(string lnkPath, out string targetPath, out string args, out string label)
    {
        targetPath = string.Empty;
        args = string.Empty;
        label = Path.GetFileNameWithoutExtension(lnkPath) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(lnkPath) || !File.Exists(lnkPath))
        {
            return false;
        }

        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            object shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("WScript.Shell create failed");
            try
            {
                object shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shell,
                    [lnkPath])!;

                try
                {
                    targetPath = ((string?)shortcut.GetType().InvokeMember(
                        "TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null))
                        ?.Trim() ?? string.Empty;
                    args = ((string?)shortcut.GetType().InvokeMember(
                        "Arguments", System.Reflection.BindingFlags.GetProperty, null, shortcut, null))
                        ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = Path.GetFileNameWithoutExtension(targetPath) ?? "App";
                    }

                    return !string.IsNullOrWhiteSpace(targetPath);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar shortcut resolve failed: {lnkPath} — {ex.Message}");
            return false;
        }
    }
}
```

- [ ] **Step 3: `DemoPinnedApps` yorumunu güncelle**

```csharp
/// Default pin list used to seed config.json on first run and as dock fallback
/// when TaskBar User Pinned folder is empty/unreadable (ADR-005). Not the live source.
```

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 5: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Models/DockAppItem.cs src/WharfBar/Services/ShortcutResolver.cs src/WharfBar/Models/DemoPinnedApps.cs
git commit -m "feat: add DockAppItem and .lnk ShortcutResolver"
```

---

### Task 5: `TaskbarPinnedAppsSource`

**Files:**
- Create: `src/WharfBar/Services/TaskbarPinnedAppsSource.cs`

**Interfaces:**
- Consumes: `ShortcutResolver`, `PinnedApp`
- Produces:
  - `TaskbarPinnedAppsSource : IDisposable`
  - `IReadOnlyList<PinnedApp> Pins { get; }` — başarılı tarama; boş olabilir
  - `bool HasLivePins { get; }` — `Pins.Count > 0`
  - `event EventHandler? Changed`
  - `void Refresh()`
  - Klasör: `%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar`

- [ ] **Step 1: Kaynak sınıfı**

```csharp
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using WharfBar.Models;

namespace WharfBar.Services;

internal sealed class TaskbarPinnedAppsSource : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly Dispatcher _dispatcher;
    private readonly List<PinnedApp> _pins = [];
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _poll;
    private bool _disposed;

    internal TaskbarPinnedAppsSource(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Refresh();
        StartWatcher();
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => Refresh();
        _poll.Start();
    }

    internal IReadOnlyList<PinnedApp> Pins => _pins;
    internal bool HasLivePins => _pins.Count > 0;
    internal event EventHandler? Changed;

    internal void Refresh()
    {
        string dir = GetTaskBarPinnedDirectory();
        var next = new List<PinnedApp>();

        try
        {
            if (Directory.Exists(dir))
            {
                foreach (string lnk in Directory.EnumerateFiles(dir, "*.lnk")
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    if (ShortcutResolver.TryResolve(lnk, out string path, out string args, out string label))
                    {
                        next.Add(new PinnedApp(path, args, label));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar pin scan failed: {ex.Message}");
        }

        if (PinsEqual(_pins, next))
        {
            return;
        }

        _pins.Clear();
        _pins.AddRange(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _poll?.Stop();
        _poll = null;
        _debounce?.Stop();
        _debounce = null;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void StartWatcher()
    {
        string dir = GetTaskBarPinnedDirectory();
        if (!Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, "*.lnk")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
        };
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Changed += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _debounce ??= new DispatcherTimer { Interval = Debounce };
            _debounce.Stop();
            _debounce.Tick -= OnDebounce;
            _debounce.Tick += OnDebounce;
            _debounce.Start();
        });
    }

    private void OnDebounce(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        Refresh();
    }

    private static string GetTaskBarPinnedDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    private static bool PinsEqual(List<PinnedApp> a, List<PinnedApp> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Path, b[i].Path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Args, b[i].Args, StringComparison.Ordinal)
                || !string.Equals(a[i].Label, b[i].Label, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Services/TaskbarPinnedAppsSource.cs
git commit -m "feat: scan TaskBar User Pinned .lnk files for dock pins"
```

---

### Task 6: Running pencereler — native + `RunningAppsSource`

**Files:**
- Create: `src/WharfBar/Native/WindowEnumeration.cs`
- Create: `src/WharfBar/Native/AppUserModelId.cs`
- Create: `src/WharfBar/Services/RunningAppsSource.cs`
- Modify: `src/WharfBar/Native/NativeMethods.cs` (EnumWindows / GetWindow / GetWindowText / GetWindowThreadProcessId)

**Interfaces:**
- Consumes: user32 enumeration APIs
- Produces:
  - `WindowEnumeration.GetTopLevelCandidates() → IReadOnlyList<(IntPtr Hwnd, string Title, uint Pid)>`
  - `AppUserModelId.TryGet(IntPtr hwnd, out string aumid) → bool`
  - `RunningAppInfo(string Id, string Path, string Label, IntPtr MainWindowHandle)`
  - `RunningAppsSource` — 2 s poll; `IReadOnlyList<RunningAppInfo> Apps`; `event Changed`

**Filtre (v1):**
- Görünür top-level (`IsWindowVisible`)
- Owner yok (`GW_OWNER == 0`)
- Tool window değil (`WS_EX_TOOLWINDOW` yok; `WS_EX_APPWINDOW` veya başlık uzunluğu > 0)
- WharfBar kendi process PID’si hariç
- `Shell_TrayWnd` / `Progman` / `WorkerW` sınıf adlarını atla

**Gruplama:** AUMID varsa onunla; yoksa `Process.MainModule.FileName` (erişim hatasında hwnd id). Aynı Id için ilk hwnd ana pencere.

- [ ] **Step 1: NativeMethods ekleri**

```csharp
internal const uint GW_OWNER = 4;
internal const int GWL_EXSTYLE = -20;
internal const int WS_EX_APPWINDOW = 0x00040000;
internal const int WS_EX_TOOLWINDOW = 0x00000080;

internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

[DllImport("user32.dll")]
internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

[DllImport("user32.dll", SetLastError = true)]
internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

[DllImport("user32.dll", SetLastError = true)]
internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
internal static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
internal static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

[DllImport("user32.dll", SetLastError = true)]
internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

[DllImport("user32.dll")]
internal static extern bool IsIconic(IntPtr hWnd);

[DllImport("user32.dll")]
internal static extern bool SetForegroundWindow(IntPtr hWnd);

internal const int SW_RESTORE = 9;
```

Not: `ShowWindow` DllImport Task 1’de zaten eklendi — **tekrar ekleme**; yalnızca `SW_RESTORE` sabitini ekle.

- [ ] **Step 2: `WindowEnumeration` + `AppUserModelId`**

`AppUserModelId` için pratik v1 yolu (COM property store ağırysa): `GetProp(hwnd, "ApplicationUserModelID")` dene; boşsa `false` dön. Tam `SHGetPropertyStoreForWindow` isteğe bağlı iyileştirme — ilk sürümde GetProp yeterli; AUMID yoksa path ile grupla.

```csharp
// Native/AppUserModelId.cs — GetProp yolu
[DllImport("user32.dll", CharSet = CharSet.Unicode)]
private static extern IntPtr GetProp(IntPtr hWnd, string lpString);

internal static bool TryGet(IntPtr hwnd, out string aumid)
{
    aumid = string.Empty;
    IntPtr p = GetProp(hwnd, "ApplicationUserModelID");
    if (p == IntPtr.Zero) return false;
    aumid = Marshal.PtrToStringUni(p) ?? string.Empty;
    return !string.IsNullOrWhiteSpace(aumid);
}
```

`WindowEnumeration.GetTopLevelCandidates` — yukarıdaki filtreleri uygula; sınıf adı `Shell_TrayWnd`/`Progman`/`WorkerW` atla.

- [ ] **Step 3: `RunningAppsSource`**

```csharp
internal sealed record RunningAppInfo(string Id, string Path, string Label, IntPtr MainWindowHandle);

internal sealed class RunningAppsSource : IDisposable
{
    // DispatcherTimer 2s; Refresh builds Dictionary by Id; raises Changed on diff
    // Path: try Process.GetProcessById(pid).MainModule?.FileName; catch → ""
    // Label: window title or Path file name; empty path + empty title → skip entry
    // UWP (path empty, aumid present): keep with Path="" so composer can placeholder icon
}
```

Tam gövdeyi implement ederken Task 5’teki timer/`Changed` desenini kopyala; process path erişimi `try/catch` ile yutulur (`Debug.WriteLine`).

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 5: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Native/NativeMethods.cs src/WharfBar/Native/WindowEnumeration.cs src/WharfBar/Native/AppUserModelId.cs src/WharfBar/Services/RunningAppsSource.cs
git commit -m "feat: enumerate running top-level apps for dock"
```

---

### Task 7: `DockAppsComposer` + `WindowActivationService`

**Files:**
- Create: `src/WharfBar/Services/DockAppsComposer.cs`
- Create: `src/WharfBar/Services/WindowActivationService.cs`

**Interfaces:**
- Consumes: `TaskbarPinnedAppsSource`, `RunningAppsSource`, `IReadOnlyList<PinnedApp> configFallback`
- Produces:
  - `DockAppsComposer.Items → IReadOnlyList<DockAppItem>`
  - `event EventHandler? Changed`
  - `WindowActivationService.TryActivate(IntPtr hwnd) → bool`
  - `WindowActivationService.LaunchOrActivate(DockAppItem item, ProcessLauncherService launcher)`

**Birleşim kuralları (spec):**
1. Pin kaynağı: `HasLivePins` ise canlı pinler; değilse `configFallback`
2. Pin sırası korunur; çalışan eşleşme → `IsRunning=true` + hwnd
3. Eşleşme: Path OrdinalIgnoreCase; args yok sayılabilir v1’de
4. Pinlenmemiş çalışanlar pinlerden **sonra** eklenir
5. Path boş (UWP) → yine listeye girer; ikon placeholder (`AppIconService` zaten fallback)

- [ ] **Step 1: Composer**

```csharp
internal sealed class DockAppsComposer : IDisposable
{
    private readonly TaskbarPinnedAppsSource _pins;
    private readonly RunningAppsSource _running;
    private readonly IReadOnlyList<PinnedApp> _fallback;
    private List<DockAppItem> _items = [];

    internal DockAppsComposer(
        TaskbarPinnedAppsSource pins,
        RunningAppsSource running,
        IReadOnlyList<PinnedApp> configFallback)
    {
        _pins = pins;
        _running = running;
        _fallback = configFallback;
        _pins.Changed += OnSourceChanged;
        _running.Changed += OnSourceChanged;
        Rebuild();
    }

    internal IReadOnlyList<DockAppItem> Items => _items;
    internal event EventHandler? Changed;

    private void OnSourceChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        IReadOnlyList<PinnedApp> pinList = _pins.HasLivePins ? _pins.Pins : _fallback;
        // ... merge into _items; if equal skip; else Changed
    }

    public void Dispose()
    {
        _pins.Changed -= OnSourceChanged;
        _running.Changed -= OnSourceChanged;
    }
}
```

Eşleşme yardımcısı:

```csharp
private static bool PathsMatch(string a, string b) =>
    !string.IsNullOrWhiteSpace(a)
    && !string.IsNullOrWhiteSpace(b)
    && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 2: Activation**

```csharp
internal sealed class WindowActivationService
{
    internal bool TryActivate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        if (NativeMethods.IsIconic(hwnd))
            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        return NativeMethods.SetForegroundWindow(hwnd);
    }

    internal void LaunchOrActivate(DockAppItem item, ProcessLauncherService launcher)
    {
        if (item.IsRunning && item.MainWindowHandle != IntPtr.Zero)
        {
            if (TryActivate(item.MainWindowHandle)) return;
        }

        if (!string.IsNullOrWhiteSpace(item.Path))
            launcher.Launch(item.Path, item.Args);
    }
}
```

`DockWindow` / activation, `ShowWindow`/`SetForegroundWindow` için `NativeMethods`’ı doğrudan çağırmasın — yalnızca bu servis.

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Services/DockAppsComposer.cs src/WharfBar/Services/WindowActivationService.cs
git commit -m "feat: compose pinned and running dock items"
```

---

### Task 8: `DockWindow` canlı strip UI

**Files:**
- Modify: `src/WharfBar/Views/DockWindow.xaml`
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs`

**Interfaces:**
- Consumes: `DockAppsComposer`, `WindowActivationService`, `AppIconService`, `ProcessLauncherService`
- Produces: birleşik ikon şeridi; çalışan gösterge; tıklama launch/focus; dispose kaynaklar `OnClosed`

- [ ] **Step 1: XAML — ItemsControl aynı; görseller code-behind’da (mevcut desen)**

İsteğe bağlı separator: pinlenmemiş çalışanlar başlamadan önce ince `Rectangle` (Width=1, Opacity=0.3, Margin 8,4) — yalnızca pinsiz running varsa.

- [ ] **Step 2: `BuildPinVisuals` → `RebuildStrip`**

`OnSourceInitialized` içinde:

```csharp
_pinSource = new TaskbarPinnedAppsSource(Dispatcher);
_runningSource = new RunningAppsSource(Dispatcher);
_composer = new DockAppsComposer(_pinSource, _runningSource, _config.PinnedApps);
_activation = new WindowActivationService();
_composer.Changed += (_, _) => Dispatcher.BeginInvoke(RebuildStrip);
RebuildStrip();
```

`RebuildStrip`:

```csharp
private void RebuildStrip()
{
    Pins.Items.Clear();
    IReadOnlyList<DockAppItem> items = _composer!.Items;
    bool sawUnpinned = false;

    for (int i = 0; i < items.Count; i++)
    {
        DockAppItem item = items[i];
        if (!item.IsPinned && !sawUnpinned && Pins.Items.Count > 0)
        {
            sawUnpinned = true;
            Pins.Items.Add(CreateSeparator());
        }

        Pins.Items.Add(CreateIconVisual(item, i < items.Count - 1));
    }

    ApplyPlacement();
}

private FrameworkElement CreateIconVisual(DockAppItem item, bool trailingMargin)
{
    var transform = new ScaleTransform(1, 1);
    var image = new System.Windows.Controls.Image
    {
        Width = _iconSizeDip,
        Height = _iconSizeDip,
        Stretch = Stretch.Uniform,
        Source = string.IsNullOrWhiteSpace(item.Path)
            ? _icons.GetIcon(string.Empty) // placeholder
            : _icons.GetIcon(item.Path),
        ToolTip = item.Label,
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = transform,
        Cursor = Cursors.Hand,
    };

    image.MouseEnter += (_, _) => AnimateScale(transform, HoverScale);
    image.MouseLeave += (_, _) => AnimateScale(transform, 1.0);
    image.MouseLeftButtonUp += (_, _) => _activation!.LaunchOrActivate(item, _launcher);

    var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
    if (trailingMargin) stack.Margin = new Thickness(0, 0, 8, 0);
    stack.Children.Add(image);

    var dot = new System.Windows.Shapes.Ellipse
    {
        Width = 6,
        Height = 6,
        Margin = new Thickness(0, 4, 0, 0),
        Fill = (System.Windows.Media.Brush)FindResource("TextFillColorPrimaryBrush"),
        Visibility = item.IsRunning ? Visibility.Visible : Visibility.Hidden,
    };
    stack.Children.Add(dot);
    return stack;
}
```

`OnClosed`: `_composer` / `_pinSource` / `_runningSource` dispose.

Eski `BuildPinVisuals` + yalnızca `_config.PinnedApps` döngüsü silinir.

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Manuel doğrulama (9b)**

- [ ] Dock, Windows TaskBar pinlerini gösterir (config demo değil — pin klasöründe ≥1 `.lnk` varsa)
- [ ] Pin klasörü boşken config/demo pinler görünür (fallback)
- [ ] Pinli uygulama çalışınca tek ikon + alt nokta
- [ ] Pinsiz çalışan sağda / ayırıcıdan sonra
- [ ] Tık: çalışıyorsa öne gelir; değilse launch
- [ ] TaskBar’a pin ekle/çıkar → ≤15 s (veya watcher debounce) içinde güncellenir
- [ ] UWP penceresi çökme yok (placeholder veya listede zarif)

- [ ] **Step 5: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Views/DockWindow.xaml src/WharfBar/Views/DockWindow.xaml.cs
git commit -m "feat: bind dock UI to live pins and running apps"
```

---

## Faz 9c — Tepsi hibrit düğmesi

### Task 9: `TrayOverflowInvoker` + native SendInput

**Files:**
- Create: `src/WharfBar/Native/SendInputKeys.cs`
- Create: `src/WharfBar/Services/TrayOverflowInvoker.cs`
- Modify: `src/WharfBar/Native/NativeMethods.cs` (`SendInput`, `INPUT`, `KEYBDINPUT`, `VK_LWIN`, `VK_B`, flags)

**Interfaces:**
- Consumes: `SendInput`
- Produces: `TrayOverflowInvoker.TryOpen() → bool` (başarı; false → debug log, UI no-op)

- [ ] **Step 1: Native SendInput iskeleti**

```csharp
internal const ushort VK_LWIN = 0x5B;
internal const ushort VK_B = 0x42;
internal const uint INPUT_KEYBOARD = 1;
internal const uint KEYEVENTF_KEYUP = 0x0002;

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[DllImport("user32.dll", SetLastError = true)]
internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
```

- [ ] **Step 2: `SendInputKeys.SendWinB`**

Sıra: LWIN down → B down → B up → LWIN up. `SendInput` dönüşü `4` değilse false.

- [ ] **Step 3: `TrayOverflowInvoker`**

```csharp
internal sealed class TrayOverflowInvoker
{
    internal bool TryOpen()
    {
        try
        {
            if (!SendInputKeys.TrySendWinB())
            {
                System.Diagnostics.Debug.WriteLine("WharfBar: Win+B SendInput failed");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: tray overflow invoke failed — {ex.Message}");
            return false;
        }
    }
}
```

**Yasak kontrolü:** Bu görevde `NOTIFYICONDATA` okuma, explorer bellek, yabancı ikon listesi **yok**.

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 5: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Native/NativeMethods.cs src/WharfBar/Native/SendInputKeys.cs src/WharfBar/Services/TrayOverflowInvoker.cs
git commit -m "feat: invoke native tray overflow via Win+B"
```

---

### Task 10: Üst bar overflow düğmesi

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `TrayOverflowInvoker`
- Produces: StatusStrip’te Network’ten **önce** tıklanabilir `SymbolIcon` (ör. `ChevronUp24` veya `MoreHorizontal24`); ToolTip Türkçe/İngilizce kısa: `Hidden icons` / `Gizli simgeler`

- [ ] **Step 1: XAML**

`StatusStrip` içinde, `NetworkIcon`’dan önce:

```xml
<ui:SymbolIcon x:Name="TrayOverflowIcon"
               Symbol="ChevronUp24"
               Cursor="Hand"
               ToolTip="Hidden icons" />
```

- [ ] **Step 2: Code-behind**

Alan: `private readonly TrayOverflowInvoker _trayOverflow = new();`

`OnSourceInitialized` sonunda:

```csharp
TrayOverflowIcon.MouseLeftButtonUp += (_, _) => _trayOverflow.TryOpen();
```

StatusService / ApplyStatus değişmez. Ses göstergesi **eklenmez**.

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 4: Manuel doğrulama (9c)**

- [ ] Düğme tıklanınca Windows tepsi / overflow odağı (`Win+B` davranışı)
- [ ] WharfBar üst barında üçüncü taraf uygulama ikonu **yok**
- [ ] Mevcut saat / ağ / pil aynı; kendi tepsi Quit ikonu duruyor

- [ ] **Step 5: Commit (yalnızca kullanıcı isterse)**

```bash
git add src/WharfBar/Views/TopBarWindow.xaml src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat: add top-bar button to open native tray overflow"
```

---

### Task 11: Doküman senkronu

**Files:**
- Modify: `.agent/context/roadmap.md` — Faz 9 plan yolu; uygulama bitince checklist
- Modify: `docs/03-development-order.md` — Faz 9 maddeleri; plan linki
- Modify: `docs/01-architecture.md` — yeni servisler / yaşam döngüsü sırası
- Modify: `docs/02-win32-appbar-dwm.md` — `ABM_GETSTATE`/`SETSTATE`, `Shell_TrayWnd` hide, `SendInput` Win+B, EnumWindows notları (kısa)
- Modify: `docs/04-gotchas.md` — zaten ADR-005 satırları var; planın seçtiği hide yolunu bir cümleyle netleştir
- Modify: `.agent/project.md` — canlı dock / taskbar lifecycle durumu
- Modify: `docs/superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md` — durum: “uygulama planı hazır” (kod bitmeden “tamamlandı” deme)

**Interfaces:** none

- [ ] **Step 1: Roadmap / development-order**

- P0 maddesine plan linki: `docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md`
- Faz 8 notunu koru: 9a/9b sonrası
- Uygulama tamamlanınca (ayrı oturum) checklist `[x]` — bu Task yalnızca plan + mimari metnini günceller; kod bitmeden Faz 9’u “✓” yapma

- [ ] **Step 2: Architecture + win32 + gotchas**

`docs/01-architecture.md` diyagramına `TaskbarVisibilityService`, pin/running kaynakları, `TrayOverflowInvoker` ekle.  
Çıkış sırası: Restore taskbar → `ABM_REMOVE`.

- [ ] **Step 3: Spec durum satırı**

Spec başlığında: `Durum: Onaylandı; uygulama planı: docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md`

- [ ] **Step 4: Build (doküman-only olsa da çözüm derlemesi bozulmamalı)**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors

- [ ] **Step 5: Commit (yalnızca kullanıcı isterse)**

```bash
git add .agent/context/roadmap.md .agent/project.md docs/
git commit -m "docs: link Faz 9 implementation plan and architecture notes"
```

---

## Faz 8 notu (kapsam dışı)

Dock auto-hide / hit-test polish (`DockAutoHideController`, `DockHitTest`) **bu plandan sonra** gelir. Görev çubuğu gizlenince alt kenar geometrisi değiştiği için 9a/9b sonrası daha anlamlı (`docs/03-development-order.md` Faz 8). Bu planda auto-hide davranışı değiştirilmez (stub kalır).

---

## Uçtan uca manuel checklist (9a+9b+9c)

1. Başlat → görev çubuğu gizli; WharfBar dock altta; üst bar AppBar yerinde  
2. Quit / Alt+F4 / tepsi Quit → görev çubuğu + work area baseline  
3. Unhandled path (bilinçli) → restore + `ABM_REMOVE`  
4. Zorla kill → asılı kalabilir; explorer restart (gotcha)  
5. Pin ekle/çıkar → dock güncellenir  
6. Pinli çalışan: tek ikon + nokta; pinsiz çalışan ayrı  
7. Tray düğmesi → native overflow; yabancı ikon yok  
8. `TaskbarCreated` (explorer restart) → AppBar yeniden + taskbar tekrar gizli  
9. UWP: çökme yok  

---

## Spec coverage (self-review)

| Spec requirement | Task |
|------------------|------|
| Capture → hide → restore; AppBar ile aynı çıkış seti; idempotent | 2, 3 |
| Restore sırası / TaskbarCreated re-hide | 3 |
| TaskBar `.lnk` pin birincil kaynak | 5, 7, 8 |
| EnumWindows + AUMID running | 6, 7 |
| `pinnedApps` seed/fallback; schema 1 | 4, 7, Global |
| Birleşik UI + çalışan gösterge + launch/focus | 7, 8 |
| UWP placeholder; kırık ikon yolu yok | 6, 8 |
| Pin refresh watcher/poll | 5 |
| Tray overflow düğmesi; Win+B | 9, 10 |
| Yabancı ikon / RPM yasak | Global, 9 |
| Status saat/ağ/pil devam; ses bu planda yok | 10, Global |
| `replaceTaskbar` config yok (her zaman gizle) | Global |
| Docs / gotchas | 11 |
| Faz 8 ertelenmiş | Faz 8 notu |

## Placeholder / consistency check

- İsimler kilitli: `TaskbarVisibilityService`, `CaptureAndHide` / `Restore` / `ReapplyHide`, `TaskbarPinnedAppsSource`, `RunningAppsSource`, `DockAppsComposer`, `DockAppItem`, `TrayOverflowInvoker`, `WindowActivationService`
- Hide yolu: `Shell_TrayWnd` + `ShowWindow`; baseline `ABM_GETSTATE` + visible flag
- Overflow: yalnızca `Win+B` (`SendInput`); chevron yedek yok
- Commit adımları kullanıcı kuralına bağlı (varsayılan: atla)
- TBD / “similar to Task N” yok; ses göstergesi bilinçli olarak kapsam dışı

---

## Execution handoff

Plan kaydedildi: `docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md`.

**İki uygulama seçeneği:**

1. **Subagent-Driven (önerilen)** — görev başına taze subagent; görevler arası review (`superpowers:subagent-driven-development`)
2. **Inline Execution** — bu oturumda `superpowers:executing-plans` ile batch + checkpoint

Kod yazmaya başlamadan önce kullanıcının planı gözden geçirip onaylaması gerekir.
