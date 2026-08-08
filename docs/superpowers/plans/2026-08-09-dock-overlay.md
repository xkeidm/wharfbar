# Faz 4 Dock Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a primary-monitor overlay dock (Mica, rounded corners) with a hardcoded demo pin list, hover scale, process launch, fullscreen hide via `FullscreenState`, and a disabled auto-hide/hit-test skeleton — no config persistence.

**Architecture:** Thin `Models` / `Native` / `Services` layers feed a single `Views/DockWindow`. Dock is not an AppBar; placement uses primary monitor **work area** bottom (above the Windows taskbar). `TopBarWindow` publishes fullscreen and work-area signals; dock subscribes. Appearance reuses `WindowAppearance` with `DwmCornerPreference.Round`.

**Tech Stack:** .NET 8 WPF, WPF-UI 4.3.0, Win32 P/Invoke (`SHGetFileInfo`, `SetWindowPos`, `GetMonitorInfo.rcWork`), no new NuGet packages.

**Spec:** [`docs/superpowers/specs/2026-08-09-dock-overlay-design.md`](../specs/2026-08-09-dock-overlay-design.md)

## Global Constraints

- Target: `net8.0-windows10.0.19041.0`; no `global.json`
- No new NuGet packages (do not add xUnit / test projects in this plan)
- Verification is `dotnet build WharfBar.sln` (0 warnings / 0 errors) + manual checklist from the spec
- Dock must **not** call `SHAppBarMessage`; never skip existing `ABM_REMOVE` paths on the top bar
- `AllowsTransparency=true` is forbidden; Mica via WPF-UI + `WindowAppearance` only
- v1: primary monitor only; classic `.exe` paths only (no UWP icons)
- Do not hide or alter the Windows taskbar
- UI folder: `Views/`; raw P/Invoke stays `internal` in `Native/`
- Human docs Turkish; code identifiers English
- Commit only when the user asks (skip commit steps unless user explicitly requests commits during execution)

---

## File structure

| File | Responsibility |
|------|----------------|
| `src/WharfBar/Models/PinnedApp.cs` | Immutable pin record |
| `src/WharfBar/Models/DemoPinnedApps.cs` | Hardcoded Explorer / Notepad / Calculator / cmd |
| `src/WharfBar/Native/NativeMethods.cs` | Add `SHGetFileInfo`, `SHFILEINFO`, `DestroyIcon`, flags |
| `src/WharfBar/Native/ShellIcon.cs` | Load / destroy `HICON` for a path |
| `src/WharfBar/Native/Display.cs` | Add `GetPrimaryMonitorWorkArea` |
| `src/WharfBar/Services/AppIconService.cs` | Path → cached `ImageSource` |
| `src/WharfBar/Services/ProcessLauncherService.cs` | `Process.Start` with shell execute |
| `src/WharfBar/Services/FullscreenState.cs` | Process-wide fullscreen flag + event |
| `src/WharfBar/Services/WorkAreaChanged.cs` | Static notify after top-bar placement |
| `src/WharfBar/Services/DockAutoHideController.cs` | Stub; `Enabled=false` |
| `src/WharfBar/Views/DockWindow.xaml(.cs)` | Overlay UI, placement, hover, launch, hide |
| `src/WharfBar/Views/TopBarWindow.xaml.cs` | Publish fullscreen + work-area signals |
| `src/WharfBar/App.xaml.cs` | Create / show / dispose dock |
| Docs: roadmap, project, architecture, development-order | Mark Faz 4 done |

---

### Task 1: `PinnedApp` + `DemoPinnedApps`

**Files:**
- Create: `src/WharfBar/Models/PinnedApp.cs`
- Create: `src/WharfBar/Models/DemoPinnedApps.cs`

**Interfaces:**
- Consumes: none
- Produces: `PinnedApp(string Path, string Args, string Label)`; `DemoPinnedApps.All` → `IReadOnlyList<PinnedApp>` with exactly four entries

- [ ] **Step 1: Add `PinnedApp`**

```csharp
namespace WharfBar.Models;

internal sealed record PinnedApp(string Path, string Args, string Label);
```

- [ ] **Step 2: Add `DemoPinnedApps`**

```csharp
namespace WharfBar.Models;

internal static class DemoPinnedApps
{
    internal static IReadOnlyList<PinnedApp> All { get; } =
    [
        new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "", "Explorer"),
        new(System.IO.Path.Combine(Environment.SystemDirectory, "notepad.exe"), "", "Notepad"),
        new(System.IO.Path.Combine(Environment.SystemDirectory, "calc.exe"), "", "Calculator"),
        new(System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"), "", "Command Prompt"),
    ];
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`
Expected: 0 warnings, 0 errors

---

### Task 2: Work area helper + `ShellIcon` P/Invoke

**Files:**
- Modify: `src/WharfBar/Native/Display.cs`
- Modify: `src/WharfBar/Native/NativeMethods.cs`
- Create: `src/WharfBar/Native/ShellIcon.cs`

**Interfaces:**
- Consumes: existing `GetMonitorInfo` / `MONITORINFO`
- Produces: `Display.GetPrimaryMonitorWorkArea(IntPtr hwnd) → RECT` (physical pixels, `rcWork`); `ShellIcon.TryExtract(string path, out IntPtr hIcon)`; `ShellIcon.Destroy(IntPtr hIcon)`

- [ ] **Step 1: Add work-area accessor to `Display`**

Append to `Display.cs` (same pattern as `GetPrimaryMonitorBounds`, but return `info.rcWork`):

```csharp
/// <summary>
/// Work area of the primary monitor (excludes AppBars / taskbar). Physical pixels.
/// </summary>
internal static RECT GetPrimaryMonitorWorkArea(IntPtr hwnd)
{
    IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
    var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };

    if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
    {
        return info.rcWork;
    }

    return new RECT { Left = 0, Top = 0, Right = 0, Bottom = 0 };
}
```

- [ ] **Step 2: Add SHGetFileInfo surface to `NativeMethods`**

Add structs/constants/imports (keep `internal`):

```csharp
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEINFO
{
    public IntPtr hIcon;
    public int iIcon;
    public uint dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string szTypeName;
}

internal const uint SHGFI_ICON = 0x000000100;
internal const uint SHGFI_LARGEICON = 0x000000000;

[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
internal static extern IntPtr SHGetFileInfo(
    string pszPath,
    uint dwFileAttributes,
    ref SHFILEINFO psfi,
    uint cbFileInfo,
    uint uFlags);

[DllImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static extern bool DestroyIcon(IntPtr hIcon);
```

- [ ] **Step 3: Add `ShellIcon` wrapper**

```csharp
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Extracts a shell icon for a classic filesystem path. Caller must <see cref="Destroy"/>.
/// </summary>
internal static class ShellIcon
{
    internal static bool TryExtract(string path, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var info = new SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return false;
        }

        hIcon = info.hIcon;
        return true;
    }

    internal static void Destroy(IntPtr hIcon)
    {
        if (hIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`
Expected: 0 warnings, 0 errors

---

### Task 3: `AppIconService` + `ProcessLauncherService`

**Files:**
- Create: `src/WharfBar/Services/AppIconService.cs`
- Create: `src/WharfBar/Services/ProcessLauncherService.cs`

**Interfaces:**
- Consumes: `ShellIcon.TryExtract` / `Destroy`
- Produces: `AppIconService.GetIcon(string path) → ImageSource`; `ProcessLauncherService.Launch(string path, string args)` (swallows exceptions)

- [ ] **Step 1: Implement `AppIconService`**

```csharp
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed class AppIconService
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private ImageSource? _fallback;

    internal ImageSource GetIcon(string path)
    {
        string key = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        return _cache.GetOrAdd(key, Load);
    }

    private ImageSource Load(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)
            && ShellIcon.TryExtract(path, out IntPtr hIcon))
        {
            try
            {
                ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                ShellIcon.Destroy(hIcon);
            }
        }

        return Fallback();
    }

    private ImageSource Fallback()
    {
        if (_fallback is not null)
        {
            return _fallback;
        }

        // Simple 32×32 gray tile — enough to show a missing pin without crashing.
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), null, new Rect(0, 0, 32, 32));
        }

        bmp.Render(dv);
        bmp.Freeze();
        _fallback = bmp;
        return _fallback;
    }
}
```

- [ ] **Step 2: Implement `ProcessLauncherService`**

```csharp
using System.Diagnostics;

namespace WharfBar.Services;

internal sealed class ProcessLauncherService
{
    internal void Launch(string path, string args)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = args ?? string.Empty,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launch failed: {path} — {ex.Message}");
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`
Expected: 0 warnings, 0 errors

---

### Task 4: `FullscreenState`, `WorkAreaChanged`, wire `TopBarWindow`

**Files:**
- Create: `src/WharfBar/Services/FullscreenState.cs`
- Create: `src/WharfBar/Services/WorkAreaChanged.cs`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` (fullscreen + after `ApplyPlacement`)

**Interfaces:**
- Consumes: existing `SetFullScreenAppActive` / `ApplyPlacement`
- Produces: `FullscreenState.Instance` with `bool IsFullscreen`, `event EventHandler? Changed`, `void Set(bool)`; `WorkAreaChanged.Notify()` + `event EventHandler? Raised`

- [ ] **Step 1: Add `FullscreenState`**

```csharp
namespace WharfBar.Services;

/// <summary>
/// Process-wide fullscreen signal. Written by the AppBar-owning top bar; read by the dock.
/// </summary>
internal sealed class FullscreenState
{
    internal static FullscreenState Instance { get; } = new();

    private bool _isFullscreen;

    internal bool IsFullscreen => _isFullscreen;

    internal event EventHandler? Changed;

    internal void Set(bool isFullscreen)
    {
        if (_isFullscreen == isFullscreen)
        {
            return;
        }

        _isFullscreen = isFullscreen;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 2: Add `WorkAreaChanged`**

```csharp
namespace WharfBar.Services;

/// <summary>
/// Fired after the top bar finishes an AppBar placement that may have resized the work area.
/// </summary>
internal static class WorkAreaChanged
{
    internal static event EventHandler? Raised;

    internal static void Notify() => Raised?.Invoke(null, EventArgs.Empty);
}
```

- [ ] **Step 3: Publish from `TopBarWindow`**

In `SetFullScreenAppActive`, after updating `_isFullScreenAppActive` / `SetWindowPos`, call:

```csharp
FullscreenState.Instance.Set(isActive);
```

At the end of successful `ApplyPlacement()` (after `SetWindowPos` of the bar), call:

```csharp
WorkAreaChanged.Notify();
```

Keep existing top-bar z-order behavior unchanged.

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`
Expected: 0 warnings, 0 errors

---

### Task 5: Auto-hide + hit-test stubs

**Files:**
- Create: `src/WharfBar/Services/DockAutoHideController.cs`
- Create: `src/WharfBar/Native/DockHitTest.cs` (stub helpers only)

**Interfaces:**
- Consumes: none
- Produces: `DockAutoHideController` with `bool Enabled { get; }` always false; `Show()` / `Hide()` / `OnPointerNear()` no-ops; `DockHitTest` with commented stub for future `WS_EX_TRANSPARENT` — **must not** clear hit-testing in Faz 4

- [ ] **Step 1: `DockAutoHideController`**

```csharp
namespace WharfBar.Services;

/// <summary>
/// Auto-hide skeleton for Faz 4. Always disabled; real proximity behavior comes later.
/// </summary>
internal sealed class DockAutoHideController
{
    internal bool Enabled => false;

    internal void Show() { }

    internal void Hide() { }

    internal void OnPointerNear() { }
}
```

- [ ] **Step 2: `DockHitTest` stub**

```csharp
namespace WharfBar.Native;

/// <summary>
/// Placeholder for future edge-band / WS_EX_TRANSPARENT hit-testing used by auto-hide.
/// Faz 4 keeps the dock fully interactive — do not call SetWindowLong to strip hit-testing.
/// </summary>
internal static class DockHitTest
{
    /// <summary>Reserved. No-op until auto-hide is enabled.</summary>
    internal static void ApplyInteractive(IntPtr hwnd)
    {
        _ = hwnd;
        // Future: ensure WS_EX_TRANSPARENT is cleared so clicks reach the dock.
    }

    /// <summary>Reserved. No-op until auto-hide is enabled.</summary>
    internal static void ApplyPassthrough(IntPtr hwnd)
    {
        _ = hwnd;
        // Future: set WS_EX_TRANSPARENT while the dock is hidden/collapsed for click-through.
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`
Expected: 0 warnings, 0 errors

---

### Task 6: `DockWindow` UI — shell, appearance, placement

**Files:**
- Create: `src/WharfBar/Views/DockWindow.xaml`
- Create: `src/WharfBar/Views/DockWindow.xaml.cs`

**Interfaces:**
- Consumes: `Display.GetPrimaryMonitorWorkArea`, `Display.DipToPixels` / `GetDpi`, `WindowAppearance`, `DwmCornerPreference.Round`, `WorkAreaChanged`, `FullscreenState`, `DockHitTest.ApplyInteractive`
- Produces: borderless Topmost overlay window, 72 DIP tall, horizontally centered on work-area bottom; Mica + round corners

Constants (match spec):

| Name | Value |
|------|--------|
| `DockHeightDip` | `72` |
| `IconSizeDip` | `48` |
| `HoverScale` | `1.2` |
| `HoverDurationMs` | `180` |
| Backdrop | `WindowBackdropType.Mica` |
| Corners | `DwmCornerPreference.Round` |

- [ ] **Step 1: Create `DockWindow.xaml`**

```xml
<Window x:Class="WharfBar.Views.DockWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="WharfBar Dock"
        WindowStyle="None"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Topmost="True"
        WindowStartupLocation="Manual"
        SizeToContent="Manual"
        Background="{DynamicResource ApplicationBackgroundBrush}"
        Foreground="{DynamicResource TextFillColorPrimaryBrush}"
        UseLayoutRounding="True"
        AllowsTransparency="False">

    <Border x:Name="Surface"
            Padding="12,8"
            Background="Transparent"
            CornerRadius="12">
        <ItemsControl x:Name="Pins">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
        </ItemsControl>
    </Border>
</Window>
```

Width is set in code from content + padding via measure, then `SetWindowPos`. Do not bind `Left`/`Top` in DIP.

- [ ] **Step 2: Create `DockWindow.xaml.cs` skeleton**

Implement at minimum:

1. Fields: `_source`, `_appearance`, `_icons`, `_launcher`, `_autoHide`, `_isFullscreenHidden`
2. `OnSourceInitialized`: hook `WndProc`; `WindowAppearance(this, hwnd, Mica, Round).Apply()`; `DockHitTest.ApplyInteractive(hwnd)`; subscribe `FullscreenState.Instance.Changed` + `WorkAreaChanged.Raised`; build pin visuals; `ApplyPlacement()`
3. `ApplyPlacement()`:
   - `RECT work = Display.GetPrimaryMonitorWorkArea(hwnd)`
   - `heightPx = Display.DipToPixels(72, dpi)`
   - Measure desired width from `Pins` (or fixed width from icon count × (48+spacing) + padding); clamp to `work.Width`
   - `x = work.Left + (work.Width - widthPx) / 2`
   - `y = work.Bottom - heightPx`
   - `SetWindowPos(hwnd, HWND_TOPMOST, x, y, widthPx, heightPx, SWP_NOACTIVATE)` — skip TOPMOST if currently fullscreen-hidden
4. `WndProc`: handle `WM_DPICHANGED`, `WM_DISPLAYCHANGE` → `BeginInvoke(ApplyPlacement)`; theme messages → `_appearance.Refresh()` (same set as top bar: `WM_THEMECHANGED`, `WM_SYSCOLORCHANGE`, `WM_DWMCOLORIZATIONCOLORCHANGED`, `WM_SETTINGCHANGE`+`ImmersiveColorSet`)
5. Fullscreen handler: if `IsFullscreen` → `Visibility = Collapsed` and set `_isFullscreenHidden`; else `Visibility = Visible` and re-apply Topmost + placement
6. `OnClosed`: unsubscribe events; remove hook

Pin item visuals and hover are Task 7; this task may leave `Pins.Items` empty so placement still runs with a minimum width (~200 px) for smoke testing.

- [ ] **Step 3: Wire `App.OnStartup` to show dock**

```csharp
// After topBar.Show():
_dock = new DockWindow();
_dock.Show();
```

Keep `MainWindow = topBar`. Hold `_dock` on `App`; on `OnExit`, `_dock?.Close()`.

- [ ] **Step 4: Build + smoke**

Run: `dotnet build WharfBar.sln`  
Expected: 0 warnings, 0 errors  

Manual (optional this task): `dotnet run --project src/WharfBar` — empty/rounded dock strip above taskbar, work-area bottom; close with Alt+F4 on top bar. **Do not** force-kill the process.

---

### Task 7: Pins UI — icons, hover scale, click-to-launch

**Files:**
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs`

**Interfaces:**
- Consumes: `DemoPinnedApps.All`, `AppIconService.GetIcon`, `ProcessLauncherService.Launch`
- Produces: four interactive icon buttons with hover scale ~1.2 / 180 ms

- [ ] **Step 1: Build pin visuals in code**

For each `PinnedApp` in `DemoPinnedApps.All`, create an `Image` (or `Button` with `Image` content):

- `Width`/`Height` = 48
- `Margin` = `0,0,8,0` (last item 0)
- `Stretch` = Uniform
- `Source` = `_icons.GetIcon(app.Path)`
- `ToolTip` = `app.Label`
- `RenderTransformOrigin` = `0.5,0.5`
- `RenderTransform` = `new ScaleTransform(1,1)`
- `Cursor` = Hand
- `MouseEnter` → animate scale to 1.2
- `MouseLeave` → animate scale to 1.0
- `MouseLeftButtonUp` → `_launcher.Launch(app.Path, app.Args)`

Add each to `Pins.Items`. After population, call `ApplyPlacement()` again so width fits content.

Hover animation (no resource dictionary required):

```csharp
private static void AnimateScale(ScaleTransform transform, double to)
{
    var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(180))
    {
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
    };
    transform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
    transform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`
Expected: 0 warnings, 0 errors

- [ ] **Step 3: Manual verification**

Run: `dotnet run --project src/WharfBar` (close via Alt+F4, never force-kill)

Checklist:
- [ ] Four icons visible; hover grows; click opens app (or debug log if path missing)
- [ ] Dock centered on work-area bottom, above Windows taskbar (not overlapping it)
- [ ] Mica + rounded corners (or opaque fallback)
- [ ] Fullscreen app (e.g. video maximized true-fullscreen) hides dock; exit restores it
- [ ] Top-bar AppBar still reserves 30 px; exit restores work area

---

### Task 8: Docs sync

**Files:**
- Modify: `.agent/context/roadmap.md`
- Modify: `.agent/project.md`
- Modify: `docs/01-architecture.md`
- Modify: `docs/03-development-order.md`

**Interfaces:** none (documentation only)

- [ ] **Step 1: Mark Faz 4 complete in roadmap / development-order**

- Roadmap “Şimdi”: add **Faz 4 — Dock ✓** with one-line summary (overlay, demo pins, fullscreen bridge, auto-hide stub)
- Roadmap “Sonraki”: start at 5 Config
- `docs/03-development-order.md` Faz 4 checklist → all `[x]` matching shipped scope; note config still Faz 5
- Faz 1 `DockWindow` deferred checkbox → `[x]` with note “Faz 4 ile geldi”

- [ ] **Step 2: Update architecture + project brief**

- `docs/01-architecture.md`: move dock files from “Sonraki” into “Mevcut”; document `FullscreenState` / `WorkAreaChanged`; lifecycle step “dock Show”
- `.agent/project.md`: “Henüz yok” → config / autostart / status; dock listed as done

- [ ] **Step 3: Build once more**

Run: `dotnet build WharfBar.sln`
Expected: 0 warnings, 0 errors

---

## Spec coverage (self-review)

| Spec requirement | Task |
|------------------|------|
| Hardcoded demo pins (4 apps) | 1, 7 |
| Always visible + auto-hide stub | 5, 6 |
| Fullscreen hide via AppBar bridge | 4, 6 |
| Hit-test stub, not passthrough | 5 |
| Mica + Round corners | 6 |
| Work-area bottom, centered, above taskbar | 2, 6 |
| Hover scale + click launch | 3, 7 |
| No AppBar on dock / ABM_REMOVE intact | 6, Global Constraints |
| DPI / display / theme refresh | 6 |
| Docs update with implementation | 8 |
| No config JSON | Global Constraints / omitted |

## Placeholder / consistency check

- Names locked: `FullscreenState.Instance.Set`, `WorkAreaChanged.Notify`, `DemoPinnedApps.All`, `ShellIcon.TryExtract`/`Destroy`, height 72 DIP, scale 1.2 / 180 ms
- No TBD / “similar to Task N” gaps remaining after write
- Commits listed as optional per user rule (ask before committing)
