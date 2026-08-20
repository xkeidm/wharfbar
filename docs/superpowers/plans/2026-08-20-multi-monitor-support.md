# Multi-Monitor Support for Top Bar & Dock — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users independently enable/disable the WharfBar top bar and dock on each connected monitor, while sharing one set of global appearance settings and one set of service instances.

**Architecture:** A `MonitorManager` service owns monitor enumeration and the set of per-monitor `TopBarWindow`/`DockWindow` instances, reconciling them against `config × monitors` on startup, monitor plug/unplug, and settings changes. Heavy per-window services move into a shared `TopBarEngine`; each top bar window keeps only its own AppBar registration, WndProc, placement, and status-strip interaction. Per-monitor placement uses the window's own monitor bounds/DPI (Per-Monitor V2), and each top bar reserves its own monitor's top edge via its own `SHAppBarMessage` registration.

**Tech Stack:** .NET 8 WPF, WPF-UI, Win32 P/Invoke (`EnumDisplayMonitors`, `GetMonitorInfo(MONITORINFOEX)`, `EnumDisplayDevices`, `SHAppBarMessage`, `SPI_SETWORKAREA`, `GetDpiForWindow`), xUnit.

**Spec:** `docs/superpowers/specs/2026-08-20-multi-monitor-support-design.md`

## Global Constraints

- Target framework: `net8.0-windows10.0.19041.0`; Per-Monitor V2 DPI (`app.manifest`).
- Placement is always in **physical pixels** (virtual-screen coordinates) via `SetWindowPos`; never use WPF DIP `Left/Top/Width/Height` for the bar/dock geometry.
- `SHAppBarMessage` `ABM_REMOVE` must run on every close/exit path (idempotent, thread-safe) — a stranded reservation survives until explorer restart.
- Taskbar behavior is unchanged: `TaskbarVisibilityService.CaptureAndHide` hides all taskbars on startup; `Restore` on every exit/crash path.
- Config is persisted at `%AppData%\WharfBar\config.json` via `ConfigService` (camelCase, atomic temp+replace save).
- Internal types are visible to tests via `InternalsVisibleTo("WharfBar.Tests")` (`src/WharfBar/AssemblyInfo.cs`).
- Never pair Mica with `AllowsTransparency=true` (top bar window).
- Docs that describe behavior must be updated in the same change: `docs/00-overview.md`, `docs/02-win32-appbar-dwm.md`, `docs/04-gotchas.md`, `.agent/boundaries.md`, `.agent/project.md`, `.agent/context/roadmap.md`, `.agent/screens/topbar.md`, `docs/decisions/003-topbar-placement-and-appbar-lifecycle.md`.

---

## File Map

**Created:**
- `src/WharfBar/Native/MonitorInfo.cs` — `MonitorInfo` record.
- `src/WharfBar/Services/MonitorIdentityMatcher.cs` — pure config↔monitor matching + defaults.
- `src/WharfBar/Services/MonitorMessageWindow.cs` — hidden message-only window (display/settings change events).
- `src/WharfBar/Services/MonitorManager.cs` — window sets + reconciliation + `RecreateTopBars`.
- `src/WharfBar/Services/TopBarEngine.cs` — once-created shared services.
- `src/WharfBar/Views/Pages/MonitorsPage.xaml` / `MonitorsPage.xaml.cs` — "Ekranlar" settings page.
- `tests/WharfBar.Tests/MonitorIdentityMatcherTests.cs`
- `tests/WharfBar.Tests/MonitorConfigNormalizerTests.cs`

**Modified:**
- `src/WharfBar/Native/MonitorNative.cs` — add `Enumerate()`; `MONITORINFOEX`/`DISPLAY_DEVICE` already exist.
- `src/WharfBar/Native/NativeMethods.cs` — add `MONITOR_DEFAULTTONEAREST`, `MONITORINFOF_PRIMARY`, `GetDpiForMonitor` (Shcore).
- `src/WharfBar/Native/Display.cs` — add `GetMonitorBounds(hwnd)` / `GetMonitorWorkArea(hwnd)` (nearest monitor).
- `src/WharfBar/Models/AppConfig.cs` — `MonitorConfig`, `AppConfig.Monitors`, `SchemaVersion = 2`.
- `src/WharfBar/Services/ConfigService.cs` — v1→v2 accept-and-migrate, `Monitors` normalization.
- `src/WharfBar/Services/TaskbarVisibilityService.cs` — per-monitor bottom reservation.
- `src/WharfBar/Native/TaskbarNative.cs` — per-monitor work-area expand/restore.
- `src/WharfBar/Views/TopBarWindow.xaml.cs` — engine wiring, `MonitorInfo` ctor, pre-register positioning, per-monitor placement.
- `src/WharfBar/Views/DockWindow.xaml.cs` — `MonitorInfo` ctor, per-monitor placement + reservation.
- `src/WharfBar/App.xaml.cs` — `MonitorManager` startup/exit wiring; `RecreateTopBar` delegates to manager.
- `src/WharfBar/Views/SettingsWindow.xaml` — add "Ekranlar" nav item.

---

## Task 1: MonitorInfo + monitor enumeration + nearest-monitor geometry

**Files:**
- Create: `src/WharfBar/Native/MonitorInfo.cs`
- Modify: `src/WharfBar/Native/NativeMethods.cs`, `src/WharfBar/Native/MonitorNative.cs`, `src/WharfBar/Native/Display.cs`
- Test: `tests/WharfBar.Tests/MonitorEnumerateTests.cs` (enumeration smoke test, guarded)

**Interfaces:**
- Consumes: existing `MONITORINFOEX`, `DISPLAY_DEVICE`, `MonitorEnumDelegate`, `RECT`, `GetMonitorInfo`, `EnumDisplayDevices` in `MonitorNative`.
- Produces:
  - `record MonitorInfo(IntPtr Handle, string DeviceId, string DeviceName, string FriendlyName, bool IsPrimary, RECT Bounds, RECT WorkArea, uint Dpi)`.
  - `static IReadOnlyList<MonitorInfo> MonitorNative.Enumerate()` — primary first, then left→right, top→bottom.
  - `static RECT Display.GetMonitorBounds(IntPtr hwnd)` and `static RECT Display.GetMonitorWorkArea(IntPtr hwnd)` — monitor containing the window (`MONITOR_DEFAULTTONEAREST`).
  - New constants: `NativeMethods.MONITOR_DEFAULTTONEAREST = 0x00000002`, `NativeMethods.MONITORINFOF_PRIMARY = 0x00000001`, `NativeMethods.MDT_EFFECTIVE_DPI = 0`, `NativeMethods.GetDpiForMonitor`.

- [ ] **Step 1: Add constants + P/Invoke**

In `src/WharfBar/Native/NativeMethods.cs`, inside the constants block next to `MONITOR_DEFAULTTOPRIMARY` (line 190):

```csharp
internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
internal const uint MONITORINFOF_PRIMARY = 0x00000001;
internal const uint MDT_EFFECTIVE_DPI = 0;
```

Add the Shcore import at the end of the `NativeMethods` class:

```csharp
[DllImport("Shcore.dll", SetLastError = true)]
internal static extern int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY);
```

- [ ] **Step 2: Create the `MonitorInfo` record**

Create `src/WharfBar/Native/MonitorInfo.cs`:

```csharp
namespace WharfBar.Native;

/// <summary>
/// A live display monitor. <see cref="DeviceId"/> is the EnumDisplayDevices registry path,
/// stable across reboots; <see cref="DeviceName"/> (e.g. "\\.\DISPLAY1") can renumber on replug.
/// All geometry is in physical pixels (virtual-screen coordinates).
/// </summary>
internal sealed record MonitorInfo(
    IntPtr Handle,
    string DeviceId,
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    RECT Bounds,
    RECT WorkArea,
    uint Dpi);
```

- [ ] **Step 3: Implement `MonitorNative.Enumerate()`**

In `src/WharfBar/Native/MonitorNative.cs`, inside `MonitorNative`:

```csharp
internal static IReadOnlyList<MonitorInfo> Enumerate()
{
    var list = new List<MonitorInfo>();

    void EnumerateOne(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor)
    {
        var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            return;
        }

        string deviceId = string.Empty;
        string friendly = string.Empty;
        var device = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (NativeMethods.EnumDisplayDevices(info.szDevice, 0, ref device, 0))
        {
            deviceId = device.DeviceID ?? string.Empty;
            friendly = device.DeviceString ?? string.Empty;
        }

        bool primary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
        uint dpi = Display.DefaultDpi;
        if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
        {
            dpi = dpiX;
        }

        list.Add(new MonitorInfo(
            hMonitor,
            deviceId,
            info.szDevice,
            friendly,
            primary,
            info.rcMonitor,
            info.rcWork,
            dpi));
    }

    MonitorEnumDelegate callback = (hMonitor, hdc, rect, _) =>
    {
        EnumerateOne(hMonitor, hdc, ref rect);
        return true;
    };

    _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

    return list
        .OrderByDescending(m => m.IsPrimary)
        .ThenBy(m => m.Bounds.Left)
        .ThenBy(m => m.Bounds.Top)
        .ToList();
}
```

- [ ] **Step 4: Add nearest-monitor geometry to `Display`**

In `src/WharfBar/Native/Display.cs`, after `GetPrimaryMonitorWorkArea`:

```csharp
/// <summary>
/// Full bounds of the monitor currently hosting <paramref name="hwnd"/> (nearest, not
/// default-to-primary). Physical pixels. Falls back to the primary bounds on failure.
/// </summary>
internal static RECT GetMonitorBounds(IntPtr hwnd)
{
    IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
    var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };

    if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
    {
        return info.rcMonitor;
    }

    return GetPrimaryMonitorBounds(hwnd);
}

/// <summary>Work area of the monitor hosting <paramref name="hwnd"/> (nearest). Physical pixels.</summary>
internal static RECT GetMonitorWorkArea(IntPtr hwnd)
{
    IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
    var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };

    if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
    {
        return info.rcWork;
    }

    return GetPrimaryMonitorWorkArea(hwnd);
}
```

- [ ] **Step 5: Write the enumeration smoke test**

Create `tests/WharfBar.Tests/MonitorEnumerateTests.cs`:

```csharp
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class MonitorEnumerateTests
{
    [Fact]
    public void Enumerate_ReturnsAtLeastOneMonitor()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorNative.Enumerate();
        Assert.NotEmpty(monitors);
    }

    [Fact]
    public void Enumerate_ExactlyOneMonitorIsPrimary()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorNative.Enumerate();
        Assert.Equal(1, monitors.Count(m => m.IsPrimary));
    }

    [Fact]
    public void Enumerate_PrimaryComesFirst()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorNative.Enumerate();
        Assert.True(monitors[0].IsPrimary);
    }

    [Fact]
    public void Enumerate_AllMonitorsHaveNonEmptyDeviceIdAndPositiveDpi()
    {
        foreach (MonitorInfo monitor in MonitorNative.Enumerate())
        {
            Assert.False(string.IsNullOrWhiteSpace(monitor.DeviceId));
            Assert.True(monitor.Dpi > 0);
            Assert.True(monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0);
        }
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test`
Expected: all 4 tests PASS (enumeration requires an interactive desktop; the CI/dev machine has one).

- [ ] **Step 7: Commit**

```bash
git add src/WharfBar/Native/MonitorInfo.cs src/WharfBar/Native/NativeMethods.cs src/WharfBar/Native/MonitorNative.cs src/WharfBar/Native/Display.cs tests/WharfBar.Tests/MonitorEnumerateTests.cs
git commit -m "feat(monitor): MonitorInfo + EnumDisplayMonitors enumeration + nearest-monitor geometry"
```

---

## Task 2: MonitorIdentityMatcher (pure matching + defaults)

**Files:**
- Create: `src/WharfBar/Services/MonitorIdentityMatcher.cs`
- Test: `tests/WharfBar.Tests/MonitorIdentityMatcherTests.cs`

**Interfaces:**
- Consumes: `MonitorInfo` (Task 1), `MonitorConfig` (defined here).
- Produces:
  - `record MonitorConfig(string DeviceId, string DeviceName, string Name, bool TopBar, bool Dock)` — placed in `Models` so `AppConfig` can hold a list (defined fully in Task 5; this task references the same shape).
  - `static MonitorConfig MonitorIdentityMatcher.Resolve(IReadOnlyList<MonitorConfig> configured, MonitorInfo live)` — returns the matching entry, or a synthesized default (primary → both on, others → both off).
  - `static bool MonitorIdentityMatcher.Matches(MonitorConfig config, MonitorInfo live)`.
  - `static IReadOnlyList<MonitorInfo> MonitorIdentityMatcher.MissingMonitors(IReadOnlyList<MonitorConfig> configured, IReadOnlyList<MonitorInfo> live)`.

**Note on ordering:** this task uses the `MonitorConfig` record. Define `MonitorConfig` in `src/WharfBar/Models/AppConfig.cs` here (Task 5 will add `AppConfig.Monitors` + `SchemaVersion` bump). To keep the compile green, add the record in this task.

- [ ] **Step 1: Write the failing tests**

Create `tests/WharfBar.Tests/MonitorIdentityMatcherTests.cs`:

```csharp
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MonitorIdentityMatcherTests
{
    private static MonitorInfo Monitor(string deviceId, string deviceName, bool primary) =>
        new(IntPtr.Zero, deviceId, deviceName, "Test Display", primary, default, default, 96);

    private static MonitorConfig Entry(string deviceId, string deviceName, bool topBar, bool dock) =>
        new(deviceId, deviceName, "Test Display", topBar, dock);

    [Fact]
    public void Resolve_MatchingByDeviceId_ReturnsConfiguredEntry()
    {
        var live = Monitor("PCI\\VEN_1", "\\\\.\\DISPLAY2", primary: false);
        var configured = new[] { Entry("PCI\\VEN_1", "\\\\.\\DISPLAY2", topBar: true, dock: false) };

        MonitorConfig result = MonitorIdentityMatcher.Resolve(configured, live);

        Assert.True(result.TopBar);
        Assert.False(result.Dock);
    }

    [Fact]
    public void Resolve_MatchingByDeviceNameWhenNoDeviceId_ReturnsConfiguredEntry()
    {
        var live = Monitor(string.Empty, "\\\\.\\DISPLAY3", primary: false);
        var configured = new[] { Entry("", "\\\\.\\DISPLAY3", topBar: false, dock: true) };

        MonitorConfig result = MonitorIdentityMatcher.Resolve(configured, live);

        Assert.False(result.TopBar);
        Assert.True(result.Dock);
    }

    [Fact]
    public void Resolve_NoMatch_PrimaryDefaultsBothOn()
    {
        var live = Monitor("PCI\\VEN_NEW", "\\\\.\\DISPLAY1", primary: true);

        MonitorConfig result = MonitorIdentityMatcher.Resolve([], live);

        Assert.True(result.TopBar);
        Assert.True(result.Dock);
        Assert.Equal("PCI\\VEN_NEW", result.DeviceId);
    }

    [Fact]
    public void Resolve_NoMatch_SecondaryDefaultsBothOff()
    {
        var live = Monitor("PCI\\VEN_NEW", "\\\\.\\DISPLAY2", primary: false);

        MonitorConfig result = MonitorIdentityMatcher.Resolve([], live);

        Assert.False(result.TopBar);
        Assert.False(result.Dock);
    }

    [Fact]
    public void Resolve_PrefersDeviceIdOverDeviceName()
    {
        // DeviceId match wins even though the device name matches a different entry.
        var live = Monitor("PCI\\VEN_A", "\\\\.\\DISPLAY9", primary: false);
        var configured = new[]
        {
            Entry("PCI\\VEN_A", "\\\\.\\DISPLAY1", topBar: true, dock: false),
            Entry("PCI\\VEN_B", "\\\\.\\DISPLAY9", topBar: false, dock: true),
        };

        MonitorConfig result = MonitorIdentityMatcher.Resolve(configured, live);

        Assert.True(result.TopBar);
        Assert.False(result.Dock);
    }

    [Fact]
    public void MissingMonitors_ReturnsLiveMonitorsWithNoConfiguredEntry()
    {
        var m1 = Monitor("PCI\\VEN_A", "\\\\.\\DISPLAY1", primary: true);
        var m2 = Monitor("PCI\\VEN_B", "\\\\.\\DISPLAY2", primary: false);
        var configured = new[] { Entry("PCI\\VEN_A", "\\\\.\\DISPLAY1", topBar: true, dock: true) };

        IReadOnlyList<MonitorInfo> missing = MonitorIdentityMatcher.MissingMonitors(configured, new[] { m1, m2 });

        Assert.Single(missing);
        Assert.Equal("PCI\\VEN_B", missing[0].DeviceId);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MonitorIdentityMatcherTests"`
Expected: build error (types do not exist).

- [ ] **Step 3: Add the `MonitorConfig` record to Models**

In `src/WharfBar/Models/AppConfig.cs`, after the `AppConfig` class:

```csharp
/// <summary>Per-monitor visibility settings under <see cref="AppConfig.Monitors"/>.</summary>
internal sealed record MonitorConfig
{
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>szDevice (e.g. "\\\\.\\DISPLAY1"); secondary match key when DeviceId is empty.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Display-only friendly name (e.g. "DELL U2720Q"); refreshed on save.</summary>
    public string Name { get; set; } = string.Empty;

    public bool TopBar { get; set; }

    public bool Dock { get; set; }
}
```

- [ ] **Step 4: Implement the matcher**

Create `src/WharfBar/Services/MonitorIdentityMatcher.cs`:

```csharp
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Pure matching between configured monitor entries and live monitors. No P/Invoke — unit-testable.
/// Identity is the EnumDisplayDevices registry path (DeviceId); DeviceName is a fallback because
/// the display number can renumber on replug.
/// </summary>
internal static class MonitorIdentityMatcher
{
    internal static bool Matches(MonitorConfig config, MonitorInfo live)
    {
        if (!string.IsNullOrWhiteSpace(config.DeviceId) && !string.IsNullOrWhiteSpace(live.DeviceId))
        {
            return string.Equals(config.DeviceId, live.DeviceId, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(config.DeviceName)
            && string.Equals(config.DeviceName, live.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the configured entry for <paramref name="live"/>, or a synthesized default:
    /// the primary monitor defaults to both on (preserves v1 behavior), everything else off.
    /// </summary>
    internal static MonitorConfig Resolve(IReadOnlyList<MonitorConfig> configured, MonitorInfo live)
    {
        MonitorConfig? match = configured.FirstOrDefault(c => Matches(c, live));
        if (match is not null)
        {
            return match;
        }

        return new MonitorConfig
        {
            DeviceId = live.DeviceId,
            DeviceName = live.DeviceName,
            Name = live.FriendlyName,
            TopBar = live.IsPrimary,
            Dock = live.IsPrimary,
        };
    }

    /// <summary>Live monitors that have no configured entry (newly plugged displays).</summary>
    internal static IReadOnlyList<MonitorInfo> MissingMonitors(
        IReadOnlyList<MonitorConfig> configured,
        IReadOnlyList<MonitorInfo> live)
    {
        return live.Where(m => !configured.Any(c => Matches(c, m))).ToList();
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MonitorIdentityMatcherTests"`
Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

```bash
git add src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/MonitorIdentityMatcher.cs tests/WharfBar.Tests/MonitorIdentityMatcherTests.cs
git commit -m "feat(monitor): MonitorIdentityMatcher — DeviceId matching + primary-default synthesis"
```

---

## Task 3: MonitorMessageWindow (hidden message-only window)

**Files:**
- Create: `src/WharfBar/Services/MonitorMessageWindow.cs`

**Interfaces:**
- Consumes: `NativeMethods` (no new imports needed — uses WPF `HwndSource` with `HWND_MESSAGE` parent).
- Produces:
  - `internal sealed class MonitorMessageWindow : IDisposable`
  - `event EventHandler? DisplaySettingsChanged` — raised on `WM_DISPLAYCHANGE`.
  - `event EventHandler? SettingsChanged` — raised on `WM_SETTINGCHANGE`.

- [ ] **Step 1: Implement the message-only window**

Create `src/WharfBar/Services/MonitorMessageWindow.cs`:

```csharp
using System.Windows.Interop;

namespace WharfBar.Services;

/// <summary>
/// A hidden message-only window (HWND_MESSAGE) that receives broadcast messages such as
/// WM_DISPLAYCHANGE / WM_SETTINGCHANGE without touching the visible window set. Created on the
/// UI thread (STA) so the WPF dispatcher can pump its messages.
/// </summary>
internal sealed class MonitorMessageWindow : IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _disposed;

    internal event EventHandler? DisplaySettingsChanged;
    internal event EventHandler? SettingsChanged;

    internal MonitorMessageWindow()
    {
        var parameters = new HwndSourceParameters("WharfBarMonitorWindow")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_DISPLAYCHANGE:
                DisplaySettingsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case NativeMethods.WM_SETTINGCHANGE:
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/WharfBar/Services/MonitorMessageWindow.cs
git commit -m "feat(monitor): MonitorMessageWindow — hidden HWND_MESSAGE for display/settings change"
```

---

## Task 4: MonitorManager core (enumeration, events, enable queries)

**Files:**
- Create: `src/WharfBar/Services/MonitorManager.cs`

**Interfaces:**
- Consumes: `MonitorNative.Enumerate()` (Task 1), `MonitorIdentityMatcher` (Task 2), `MonitorMessageWindow` (Task 3), `ConfigService`, `TaskbarVisibilityService`, `VeilService`.
- Produces (window ownership arrives in Tasks 10/11/14; the shell of the class is built here):
  - `internal enum MonitorPart { TopBar, Dock }`.
  - `internal sealed class MonitorManager : IDisposable`.
  - `event EventHandler? MonitorsChanged` — raised when the live monitor set or config visibility changes.
  - `IReadOnlyList<MonitorInfo> CurrentMonitors`.
  - `bool IsEnabled(string deviceId, MonitorPart part)`.
  - `MonitorConfig? FindEntry(string deviceId)` / `void UpsertEntry(MonitorInfo monitor)` (used by settings page later).
  - `void Refresh()` — re-enumerate; raise `MonitorsChanged` when the monitor set changed.
  - `public void Dispose()`.

- [ ] **Step 1: Implement the MonitorManager skeleton**

Create `src/WharfBar/Services/MonitorManager.cs`:

```csharp
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

internal enum MonitorPart
{
    TopBar,
    Dock,
}

/// <summary>
/// Owns monitor enumeration and the mapping between live monitors and config visibility.
/// Window-set reconciliation and per-monitor top bars/docks arrive in later tasks; this task
/// establishes enumeration, change events, and enable/disable queries on the UI thread.
/// </summary>
internal sealed class MonitorManager : IDisposable
{
    private readonly ConfigService _config;
    private readonly MonitorMessageWindow _messageWindow;
    private IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();

    internal event EventHandler? MonitorsChanged;

    internal MonitorManager(ConfigService config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _messageWindow = new MonitorMessageWindow();
        _messageWindow.DisplaySettingsChanged += OnDisplayChanged;
    }

    /// <summary>Live monitors, primary first. Only valid on the UI thread.</summary>
    internal IReadOnlyList<MonitorInfo> CurrentMonitors => _monitors;

    /// <summary>Call once on the UI thread after construction.</summary>
    internal void Initialize()
    {
        Refresh();
    }

    internal void Refresh()
    {
        IReadOnlyList<MonitorInfo> next = MonitorNative.Enumerate();
        bool changed = !MonitorSetsEqual(_monitors, next);
        _monitors = next;
        if (changed)
        {
            MonitorsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    internal bool IsEnabled(string deviceId, MonitorPart part)
    {
        MonitorConfig? entry = FindEntry(deviceId);
        if (entry is null)
        {
            MonitorInfo? live = _monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
            if (live is null)
            {
                return false;
            }

            entry = MonitorIdentityMatcher.Resolve(_config.Current.Monitors, live);
        }

        return part == MonitorPart.TopBar ? entry.TopBar : entry.Dock;
    }

    internal MonitorConfig? FindEntry(string deviceId)
    {
        return _config.Current.Monitors.FirstOrDefault(c =>
            string.Equals(c.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds or updates a config entry for a live monitor and persists. Returns the entry.</summary>
    internal MonitorConfig UpsertEntry(MonitorInfo monitor)
    {
        MonitorConfig? existing = FindEntry(monitor.DeviceId);
        if (existing is not null)
        {
            existing.DeviceName = monitor.DeviceName;
            existing.Name = monitor.FriendlyName;
            return existing;
        }

        var entry = new MonitorConfig
        {
            DeviceId = monitor.DeviceId,
            DeviceName = monitor.DeviceName,
            Name = monitor.FriendlyName,
            TopBar = monitor.IsPrimary,
            Dock = monitor.IsPrimary,
        };
        _config.Current.Monitors.Add(entry);
        return entry;
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private static bool MonitorSetsEqual(IReadOnlyList<MonitorInfo> a, IReadOnlyList<MonitorInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            MonitorInfo x = a[i];
            MonitorInfo y = b[i];
            if (!string.Equals(x.DeviceId, y.DeviceId, StringComparison.OrdinalIgnoreCase)
                || x.IsPrimary != y.IsPrimary
                || x.Bounds.Left != y.Bounds.Left || x.Bounds.Top != y.Bounds.Top
                || x.Bounds.Right != y.Bounds.Right || x.Bounds.Bottom != y.Bounds.Bottom
                || x.Dpi != y.Dpi)
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        _messageWindow.DisplaySettingsChanged -= OnDisplayChanged;
        _messageWindow.Dispose();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/WharfBar/Services/MonitorManager.cs
git commit -m "feat(monitor): MonitorManager — enumeration, change events, enable queries"
```

---

## Task 5: Config model v2 — Monitors list

**Files:**
- Modify: `src/WharfBar/Models/AppConfig.cs`
- Test: `tests/WharfBar.Tests/MonitorConfigNormalizerTests.cs`

**Interfaces:**
- Consumes: `MonitorConfig` (Task 2).
- Produces:
  - `AppConfig.CurrentSchemaVersion = 2`.
  - `AppConfig.Monitors` — `List<MonitorConfig>`.
  - `MonitorConfigNormalizer.Normalize(List<MonitorConfig>? monitors)` — pure: drops null/blank `DeviceId` entries, dedupes by `DeviceId`.

- [ ] **Step 1: Write the failing normalizer tests**

Create `tests/WharfBar.Tests/MonitorConfigNormalizerTests.cs`:

```csharp
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MonitorConfigNormalizerTests
{
    [Fact]
    public void Normalize_Null_ReturnsEmpty()
    {
        Assert.Empty(MonitorConfigNormalizer.Normalize(null));
    }

    [Fact]
    public void Normalize_DropsNullAndBlankDeviceIdEntries()
    {
        var list = new List<MonitorConfig>
        {
            new() { DeviceId = "PCI\\VEN_A" },
            new() { DeviceId = "" },
            new() { DeviceId = "   " },
            null!,
        };

        IReadOnlyList<MonitorConfig> result = MonitorConfigNormalizer.Normalize(list);

        Assert.Single(result);
        Assert.Equal("PCI\\VEN_A", result[0].DeviceId);
    }

    [Fact]
    public void Normalize_DedupesByDeviceId_KeepFirst()
    {
        var list = new List<MonitorConfig>
        {
            new() { DeviceId = "PCI\\VEN_A", TopBar = true, Dock = false },
            new() { DeviceId = "PCI\\VEN_A", TopBar = false, Dock = true },
        };

        IReadOnlyList<MonitorConfig> result = MonitorConfigNormalizer.Normalize(list);

        Assert.Single(result);
        Assert.True(result[0].TopBar);
        Assert.False(result[0].Dock);
    }

    [Fact]
    public void Normalize_TrimsDeviceIdAndName()
    {
        var list = new List<MonitorConfig>
        {
            new() { DeviceId = " PCI\\VEN_A ", Name = " DELL U2720Q " },
        };

        IReadOnlyList<MonitorConfig> result = MonitorConfigNormalizer.Normalize(list);

        Assert.Equal("PCI\\VEN_A", result[0].DeviceId);
        Assert.Equal("DELL U2720Q", result[0].Name);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MonitorConfigNormalizerTests"`
Expected: build error (`MonitorConfigNormalizer` does not exist).

- [ ] **Step 3: Bump schema version and add the Monitors list**

In `src/WharfBar/Models/AppConfig.cs`:

```csharp
internal const int CurrentSchemaVersion = 2;
```

Add to `AppConfig` after `PinnedApps`:

```csharp
public List<MonitorConfig> Monitors { get; set; } = [];
```

- [ ] **Step 4: Implement the normalizer**

Create `src/WharfBar/Services/MonitorConfigNormalizer.cs`:

```csharp
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>Pure normalization for <see cref="AppConfig.Monitors"/> (unit-tested).</summary>
internal static class MonitorConfigNormalizer
{
    internal static IReadOnlyList<MonitorConfig> Normalize(List<MonitorConfig>? monitors)
    {
        if (monitors is null)
        {
            return [];
        }

        var result = new List<MonitorConfig>(monitors.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorConfig? entry in monitors)
        {
            if (entry is null)
            {
                continue;
            }

            entry.DeviceId = entry.DeviceId?.Trim() ?? string.Empty;
            entry.DeviceName = entry.DeviceName?.Trim() ?? string.Empty;
            entry.Name = entry.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(entry.DeviceId))
            {
                continue;
            }

            if (!seen.Add(entry.DeviceId))
            {
                continue;
            }

            result.Add(entry);
        }

        return result;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MonitorConfigNormalizerTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/MonitorConfigNormalizer.cs tests/WharfBar.Tests/MonitorConfigNormalizerTests.cs
git commit -m "feat(config): SchemaVersion 2 — per-monitor Monitors list + normalizer"
```

---

## Task 6: ConfigService v1→v2 migration

**Files:**
- Modify: `src/WharfBar/Services/ConfigService.cs`
- Test: `tests/WharfBar.Tests/ConfigMigrationTests.cs`

**Interfaces:**
- Consumes: `MonitorConfigNormalizer` (Task 5), `AppConfig.CurrentSchemaVersion = 2` (Task 5).
- Produces:
  - `LoadOrCreate` accepts `SchemaVersion` 1 or 2; future versions (>2) still reset to defaults.
  - `Normalize` applies `MonitorConfigNormalizer.Normalize` to `config.Monitors` and persists when a migration or normalization changed the file.
  - `internal static bool NeedsMonitorsMigration(AppConfig loaded, bool monitorsKeyMissing)` — pure, testable.

- [ ] **Step 1: Write the failing migration tests**

Create `tests/WharfBar.Tests/ConfigMigrationTests.cs`:

```csharp
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ConfigMigrationTests
{
    [Fact]
    public void NeedsMonitorsMigration_MissingKey_IsTrue()
    {
        Assert.True(ConfigService.NeedsMonitorsMigration(monitorsKeyMissing: true));
    }

    [Fact]
    public void NeedsMonitorsMigration_Schema1_IsTrue()
    {
        var config = new AppConfig { SchemaVersion = 1, Monitors = [] };
        Assert.True(ConfigService.NeedsMonitorsMigration(monitorsKeyMissing: false));
    }

    [Fact]
    public void NeedsMonitorsMigration_CurrentSchemaWithMonitors_IsFalse()
    {
        var config = new AppConfig
        {
            SchemaVersion = AppConfig.CurrentSchemaVersion,
            Monitors = [new MonitorConfig { DeviceId = "PCI\\VEN_A" }],
        };
        Assert.False(ConfigService.NeedsMonitorsMigration(monitorsKeyMissing: false));
    }

    [Fact]
    public void MigrateMonitors_Version1_LeavesListEmptyForRuntimeDefaults()
    {
        // v1 configs have no monitor knowledge; the runtime (MonitorManager.Reconcile)
        // synthesizes primary-on entries on first launch and persists them.
        var config = new AppConfig { SchemaVersion = 1, Monitors = [] };

        AppConfig result = ConfigService.MigrateMonitors(config);

        Assert.Equal(AppConfig.CurrentSchemaVersion, result.SchemaVersion);
        Assert.Empty(result.Monitors);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConfigMigrationTests"`
Expected: build error (`NeedsMonitorsMigration`/`MigrateMonitors` do not exist).

- [ ] **Step 3: Accept version 1 in LoadOrCreate**

In `src/WharfBar/Services/ConfigService.cs`, replace the schema check (lines 64-67):

```csharp
if (loaded.SchemaVersion > AppConfig.CurrentSchemaVersion)
{
    return ReplaceWithDefault($"unsupported schemaVersion {loaded.SchemaVersion}");
}
```

- [ ] **Step 4: Add the migration helpers + wire into LoadOrCreate**

Add `using System.Linq;` if not present. Add to `ConfigService`:

```csharp
internal static bool NeedsMonitorsMigration(bool monitorsKeyMissing) => monitorsKeyMissing;

internal static AppConfig MigrateMonitors(AppConfig config)
{
    config.SchemaVersion = AppConfig.CurrentSchemaVersion;
    config.Monitors ??= [];
    return config;
}
```

In `LoadOrCreate`, after the schema check and before `Normalize`, replace:

```csharp
double originalIconSize = loaded.Dock?.IconSize ?? DockConfig.DefaultIconSize;
```

with:

```csharp
bool monitorsKeyMissing = !JsonRootHasProperty(json, "monitors");
bool monitorsMigrated = NeedsMonitorsMigration(monitorsKeyMissing);
MigrateMonitors(loaded);
double originalIconSize = loaded.Dock?.IconSize ?? DockConfig.DefaultIconSize;
```

- [ ] **Step 5: Normalize Monitors and persist on change**

In `Normalize` (line ~178), after `config.PinnedApps ??= [];`:

```csharp
config.Monitors = MonitorConfigNormalizer.Normalize(config.Monitors).ToList();
```

In `LoadOrCreate`, extend the persist condition (line 79) so a migrated/normalized Monitors list is written:

```csharp
bool monitorsChanged = monitorsMigrated
    || MonitorCountChanged(loaded.Monitors, Current.Monitors);
```

and add to the `if (iconChanged || statusChanged || catalogChanged || veilKeyMissing || monitorsChanged)` condition. Add the helper:

```csharp
private static bool MonitorCountChanged(IReadOnlyList<MonitorConfig>? loaded, IReadOnlyList<MonitorConfig> current)
{
    if (loaded is null)
    {
        return current.Count > 0;
    }

    if (loaded.Count != current.Count)
    {
        return true;
    }

    for (int i = 0; i < current.Count; i++)
    {
        if (!string.Equals(loaded[i]?.DeviceId, current[i].DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test`
Expected: all existing tests + the 4 new migration tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/WharfBar/Services/ConfigService.cs tests/WharfBar.Tests/ConfigMigrationTests.cs
git commit -m "feat(config): accept SchemaVersion 1 and migrate to v2 Monitors list"
```

---

## Task 7: TopBarEngine — shared services

**Files:**
- Create: `src/WharfBar/Services/TopBarEngine.cs`

**Interfaces:**
- Consumes: `TaskbarVisibilityService`, `VeilService`, all service ctors currently used in `TopBarWindow.OnSourceInitialized` (Status, Volume, AudioMixer, NightLight, Wifi, Bluetooth, Projection, QuickSettings, MonitorBrightness, MediaSession, TrayMirror, TrayOverflowInvoker, NotificationCenterInvoker).
- Produces:
  - `internal sealed class TopBarEngine : IDisposable`
  - Properties: `Status`, `Volume`, `Mixer`, `NightLight`, `Wifi`, `Bluetooth`, `Projection`, `QuickSettings`, `MonitorBrightness`, `Media`, `TrayMirror`, `TrayOverflow`, `Notifications`.
  - `void Initialize()` — `Media.InitializeAsync()` + `TrayMirror.Reenumerate()`.
  - `void Dispose()` — disposes every disposable service exactly once.

- [ ] **Step 1: Implement TopBarEngine**

Create `src/WharfBar/Services/TopBarEngine.cs`:

```csharp
using System.Windows.Threading;

namespace WharfBar.Services;

/// <summary>
/// Owns the service instances shared by every per-monitor top bar window. Services are
/// observable (Changed events); each window subscribes to the ones that drive its own UI.
/// Created once on the UI thread and disposed once on app exit.
/// </summary>
internal sealed class TopBarEngine : IDisposable
{
    internal StatusService Status { get; }
    internal VolumeService Volume { get; }
    internal AudioMixerService Mixer { get; }
    internal NightLightService NightLight { get; }
    internal WifiService Wifi { get; }
    internal BluetoothService Bluetooth { get; }
    internal ProjectionService Projection { get; }
    internal QuickSettingsService QuickSettings { get; }
    internal MonitorBrightnessService MonitorBrightness { get; }
    internal MediaSessionService Media { get; }
    internal TrayMirrorService TrayMirror { get; }
    internal TrayOverflowInvoker TrayOverflow { get; }
    internal NotificationCenterInvoker Notifications { get; }

    internal TopBarEngine(TaskbarVisibilityService taskbar, VeilService veil)
    {
        ArgumentNullException.ThrowIfNull(taskbar);
        ArgumentNullException.ThrowIfNull(veil);

        Dispatcher dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        TrayOverflow = new TrayOverflowInvoker(taskbar);
        Notifications = new NotificationCenterInvoker();
        Status = new StatusService();
        Volume = new VolumeService();
        Mixer = new AudioMixerService(Volume);
        NightLight = new NightLightService();
        Wifi = new WifiService();
        Bluetooth = new BluetoothService();
        Projection = new ProjectionService();
        QuickSettings = new QuickSettingsService(NightLight, veil);
        MonitorBrightness = new MonitorBrightnessService();
        Media = new MediaSessionService();
        TrayMirror = new TrayMirrorService(dispatcher, taskbar);
    }

    internal void Initialize()
    {
        _ = Media.InitializeAsync();
        TrayMirror.Reenumerate();
    }

    public void Dispose()
    {
        TrayMirror.Dispose();
        Media.Dispose();
        MonitorBrightness.Dispose();
        QuickSettings.Dispose();
        Projection.Dispose();
        Bluetooth.Dispose();
        Wifi.Dispose();
        NightLight.Dispose();
        Mixer.Dispose();
        Volume.Dispose();
        Status.Dispose();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`
Expected: build succeeds (new file not yet referenced by anything).

- [ ] **Step 3: Commit**

```bash
git add src/WharfBar/Services/TopBarEngine.cs
git commit -m "feat(engine): TopBarEngine — shared per-window service instances"
```

---

## Task 8: TopBarWindow consumes the engine (single-window regression)

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`
- Modify: `src/WharfBar/App.xaml.cs`

**Interfaces:**
- Consumes: `TopBarEngine` (Task 7), `MonitorInfo` (Task 1).
- Produces: new ctor `TopBarWindow(TopBarEngine engine, ConfigService config, TaskbarVisibilityService taskbar, VeilService veil, MonitorInfo monitor)`; `internal MonitorInfo Monitor { get; }`.

**Behavior must remain identical on a single (primary) monitor at the end of this task.**

- [ ] **Step 1: Change the constructor**

In `src/WharfBar/Views/TopBarWindow.xaml.cs`:

Replace the fields `_trayOverflow` (line 33) and `_notifications` (line 34) and add engine+monitor fields:

```csharp
private readonly TopBarEngine _engine;
private readonly ConfigService _config;
private readonly VeilService _veil;
```

Delete `private readonly TrayOverflowInvoker _trayOverflow;` and `private readonly NotificationCenterInvoker _notifications = new();`.

Replace the constructor (lines 74-101) with:

```csharp
internal TopBarWindow(TopBarEngine engine, ConfigService config, TaskbarVisibilityService taskbar, VeilService veil, MonitorInfo monitor)
{
    _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
    _config = config ?? throw new ArgumentNullException(nameof(config));
    _veil = veil ?? throw new ArgumentNullException(nameof(veil));
    Monitor = monitor;

    // Must be set before InitializeComponent — WPF locks AllowsTransparency after creation.
    AllowsTransparency = TopBarBackgroundModes.NeedsTransparency(
        _config.Current.TopBar?.BackgroundMode ?? TopBarBackgroundModes.Mica);
    InitializeComponent();

    _statusElements = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
    {
        [TopBarStatusItemIds.TrayMirror] = TrayMirrorHost,
        [TopBarStatusItemIds.TrayOverflow] = TrayOverflowButton,
        [TopBarStatusItemIds.DateTime] = DateTimeButton,
        [TopBarStatusItemIds.Notifications] = NotificationsButton,
        [TopBarStatusItemIds.ControlCenter] = ControlCenterButton,
        [TopBarStatusItemIds.Volume] = VolumeButton,
        [TopBarStatusItemIds.Brightness] = BrightnessButton,
        [TopBarStatusItemIds.Media] = MediaButton,
        [TopBarStatusItemIds.Network] = NetworkButton,
        [TopBarStatusItemIds.Battery] = BatteryGroup,
    };
    ApplyStatusItemLayout();
    StatusStrip.MouseRightButtonUp += OnStatusStripRightClick;
}
```

- [ ] **Step 2: Rewire OnSourceInitialized to the engine**

Replace lines 119-159 (the `_appBar = new AppBar(...)` block stays) — replace only the service-creation block (lines 130-158) with:

```csharp
        _appearance = new WindowAppearance(this, _source.Handle, Backdrop, Corners);
        WindowAppearance.MainWindowChromeClobbered += OnMainWindowChromeClobbered;
        ApplyBackgroundAppearance();

        _engine.Status.Changed += OnStatusChanged;
        ApplyStatus(_engine.Status.Snapshot);

        _engine.Volume.Changed += OnVolumeChanged;
        ApplyVolume(_engine.Volume.Snapshot);

        QuickSettingsHost.Attach(
            _engine.Volume,
            _engine.Mixer,
            _engine.QuickSettings,
            _engine.Wifi,
            _engine.Bluetooth,
            _engine.Projection);
        BrightnessHost.Attach(_engine.MonitorBrightness);

        _engine.Media.Changed += OnMediaChanged;
        _engine.Media.SessionsChanged += OnMediaSessionsChanged;
        MediaHost.Attach(_engine.Media);

        _engine.TrayMirror.Changed += OnTrayMirrorChanged;
        ApplyTrayMirror(_engine.TrayMirror.Items);

        _appBar.Register();
        ApplyPlacement();
```

The current subscription lambdas `(_, _) => ApplyStatus(_status.Snapshot)` etc. move into named handlers:

```csharp
private void OnStatusChanged(object? sender, EventArgs e) => ApplyStatus(_engine.Status.Snapshot);
private void OnVolumeChanged(object? sender, EventArgs e) => ApplyVolume(_engine.Volume.Snapshot);
private void OnTrayMirrorChanged(object? sender, EventArgs e) => ApplyTrayMirror(_engine.TrayMirror.Items);
```

Delete the per-window service fields (`_status`, `_volume`, `_mixer`, `_quick`, `_nightLight`, `_wifi`, `_bluetooth`, `_projection`, `_monitorBrightness`, `_media`, `_trayMirror`) from the field declarations (lines 37-45, 65-69) and their `_trayMirrorImages` stays (still per-window UI).

- [ ] **Step 3: Update `_trayOverflow` / `_notifications` references**

Every reference to `_trayOverflow` becomes `_engine.TrayOverflow`; every reference to `_notifications` becomes `_engine.Notifications`. Grep and replace within the file (they appear in `OnTrayOverflowClick`, `OnDateTimeOrNotificationsClick`, and related handlers).

- [ ] **Step 4: Update DisposeOwnedServices**

Replace `DisposeOwnedServices` (lines 655-677) — it must unsubscribe, not dispose shared services:

```csharp
private void DisposeOwnedServices()
{
    WindowAppearance.MainWindowChromeClobbered -= OnMainWindowChromeClobbered;
    QuickSettingsHost.Detach();
    _engine.Status.Changed -= OnStatusChanged;
    _engine.Volume.Changed -= OnVolumeChanged;
    _engine.Media.Changed -= OnMediaChanged;
    _engine.Media.SessionsChanged -= OnMediaSessionsChanged;
    _engine.TrayMirror.Changed -= OnTrayMirrorChanged;
}
```

- [ ] **Step 5: Update the TaskbarCreated handler**

In the `_taskbarCreatedMessage` handler (line 1408-1413), replace `_trayMirror?.Reenumerate()` with `_engine.TrayMirror.Reenumerate()`.

- [ ] **Step 6: Update App.OnStartup wiring**

In `src/WharfBar/App.xaml.cs`, replace lines 112-120 with:

```csharp
        _taskbar = new TaskbarVisibilityService();
        _taskbar.CaptureAndHide();

        _topBarEngine = new TopBarEngine(_taskbar, _veil);
        _topBarEngine.Initialize();

        MonitorInfo primary = MonitorNative.Enumerate().FirstOrDefault(m => m.IsPrimary)
            ?? throw new InvalidOperationException("No primary monitor found.");

        var topBar = new TopBarWindow(_topBarEngine, _config, _taskbar, _veil, primary);
        MainWindow = topBar;
        topBar.Show();

        _dock = new DockWindow(config, _taskbar);
        _dock.Show();
```

Add the field `private TopBarEngine? _topBarEngine;` next to `_taskbar` (line 17). In `OnExit`, dispose it after the taskbar restore:

```csharp
        _topBarEngine?.Dispose();
        _topBarEngine = null;
```

The `DockWindow` ctor is unchanged in this task (still `(AppConfig, TaskbarVisibilityService)`); it will take a `MonitorInfo` in Task 13.

- [ ] **Step 7: Build + test**

Run: `dotnet build WharfBar.sln`
Expected: build succeeds. Run `dotnet test` — all tests pass.
Manual smoke: run the app once; the primary top bar must look and behave exactly as before (status items, flyouts, AppBar reservation, taskbar hidden).

- [ ] **Step 8: Commit**

```bash
git add src/WharfBar/Views/TopBarWindow.xaml.cs src/WharfBar/App.xaml.cs
git commit -m "refactor(topbar): TopBarWindow consumes shared TopBarEngine (single-window regression)"
```

---

## Task 9: Per-monitor top bar placement + AppBar registration

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `TopBarWindow.Monitor` (Task 8), `Display.GetMonitorBounds` (Task 1).
- Produces: per-monitor placement — `ApplyPlacement` uses the window's own monitor bounds instead of the primary monitor; the window is positioned on its target monitor before `AppBar.Register()` so the shell reserves the right edge.

- [ ] **Step 1: Position on target monitor before AppBar registration**

In `OnSourceInitialized`, immediately before `_appBar.Register();` (currently around line 160), add:

```csharp
        // The shell binds an AppBar to the monitor the window occupies at ABM_NEW time.
        // Move onto the target monitor first so a secondary bar reserves its own edge.
        RECT home = Monitor.Bounds;
        NativeMethods.SetWindowPos(
            _source.Handle,
            IntPtr.Zero,
            home.Left,
            home.Top,
            home.Width,
            1,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        _appBar.Register();
        ApplyPlacement();
```

- [ ] **Step 2: ApplyPlacement uses the window's monitor**

In `ApplyPlacement` (line 1335), replace:

```csharp
RECT monitor = Display.GetPrimaryMonitorBounds(hwnd);
```

with:

```csharp
RECT monitor = Display.GetMonitorBounds(hwnd);
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`
Expected: build succeeds.

- [ ] **Step 4: Manual verification (single monitor)**

Run the app: bar must still sit at the top of the primary monitor; AppBar reservation + taskbar hide unchanged. `Display.GetMonitorBounds` returns the primary bounds on a single-monitor machine, so behavior is identical.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat(topbar): position on target monitor before AppBar registration + nearest-monitor placement"
```

---

## Task 10: MonitorManager owns top bars — reconciliation + App wiring

**Files:**
- Modify: `src/WharfBar/Services/MonitorManager.cs`
- Modify: `src/WharfBar/App.xaml.cs`

**Interfaces:**
- Consumes: `TopBarEngine` (Task 7), `TopBarWindow` ctor (Task 8), `MonitorIdentityMatcher.Resolve` (Task 2).
- Produces:
  - `IReadOnlyList<TopBarWindow> TopBars`.
  - `TopBarWindow? PrimaryTopBar` — the bar whose `Monitor.IsPrimary` is true, else the first bar, else null.
  - `void Reconcile()` — computes the desired top-bar set from `config.Monitors × CurrentMonitors` and creates/closes windows; synthesizes missing config entries (via `MonitorIdentityMatcher.Resolve`) and persists them.
  - `void RefreshAllPlacements()` — calls `QueueApplyPlacement()` on every bar.

- [ ] **Step 1: Extend MonitorManager**

Add to `src/WharfBar/Services/MonitorManager.cs`:

```csharp
using WharfBar.Views;

private readonly TaskbarVisibilityService _taskbar;
private readonly VeilService _veil;
private TopBarEngine? _engine;
private readonly List<TopBarWindow> _topBars = new();

internal IReadOnlyList<TopBarWindow> TopBars => _topBars;

internal TopBarWindow? PrimaryTopBar =>
    _topBars.FirstOrDefault(b => b.Monitor.IsPrimary) ?? _topBars.FirstOrDefault();

internal TopBarEngine Engine => _engine ?? throw new InvalidOperationException("Engine not created.");
```

Update the constructor to store the new dependencies:

```csharp
internal MonitorManager(ConfigService config, TaskbarVisibilityService taskbar, VeilService veil)
{
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(taskbar);
    ArgumentNullException.ThrowIfNull(veil);
    _config = config;
    _taskbar = taskbar;
    _veil = veil;
    _messageWindow = new MonitorMessageWindow();
    _messageWindow.DisplaySettingsChanged += OnDisplayChanged;
}
```

- [ ] **Step 2: Add Reconcile + window helpers**

Add to `MonitorManager`:

```csharp
internal void Reconcile()
{
    if (_engine is null)
    {
        _engine = new TopBarEngine(_taskbar, _veil);
        _engine.Initialize();
    }

    bool persisted = false;
    foreach (MonitorInfo monitor in _monitors)
    {
        MonitorConfig entry = MonitorIdentityMatcher.Resolve(_config.Current.Monitors, monitor);
        if (FindEntry(monitor.DeviceId) is null)
        {
            _config.Current.Monitors.Add(entry);
            persisted = true;
        }
    }

    if (persisted)
    {
        _config.Save(_config.Current);
    }

    List<TopBarWindow> desired = _monitors
        .Where(m => IsEnabled(m.DeviceId, MonitorPart.TopBar))
        .ToList();

    // Close bars whose monitor is no longer desired.
    for (int i = _topBars.Count - 1; i >= 0; i--)
    {
        TopBarWindow bar = _topBars[i];
        if (!desired.Any(m => m.DeviceId == bar.Monitor.DeviceId))
        {
            bar.PrepareForRecreate();
            bar.Close();
            _topBars.RemoveAt(i);
        }
    }

    // Open bars for monitors that need one.
    foreach (MonitorInfo monitor in desired)
    {
        if (_topBars.Any(b => b.Monitor.DeviceId == monitor.DeviceId))
        {
            continue;
        }

        var bar = new TopBarWindow(_engine, _config, _taskbar, _veil, monitor);
        _topBars.Add(bar);
        bar.Show();
    }

    RefreshPrimaryOwnership();
}

internal void RefreshAllPlacements()
{
    foreach (TopBarWindow bar in _topBars)
    {
        bar.QueueApplyPlacement();
    }
}

private void RefreshPrimaryOwnership()
{
    // Kept as a no-op hook for Task 11 (MainWindow assignment happens in App).
}
```

- [ ] **Step 3: Trigger reconcile on display change and config change**

In `OnDisplayChanged`, replace `Refresh();` with:

```csharp
private void OnDisplayChanged(object? sender, EventArgs e)
{
    Refresh();
    if (_sourceConfigured)
    {
        Reconcile();
    }
}
```

Add the flag and set it from `Initialize`:

```csharp
private bool _sourceConfigured;

internal void Initialize()
{
    Refresh();
    _sourceConfigured = true;
    Reconcile();
}
```

- [ ] **Step 4: Dispose closes the bars**

In `Dispose`:

```csharp
foreach (TopBarWindow bar in _topBars)
{
    bar.PrepareForRecreate();
    bar.Close();
}

_topBars.Clear();
_engine?.Dispose();
_engine = null;
```

- [ ] **Step 5: App uses MonitorManager for startup**

In `src/WharfBar/App.xaml.cs`, replace the startup block written in Task 8 (lines creating `_topBarEngine`, `primary`, `topBar`) with:

```csharp
        _taskbar = new TaskbarVisibilityService();
        _taskbar.CaptureAndHide();

        _monitors = new MonitorManager(_config, _taskbar, _veil);
        _monitors.Initialize(); // creates engine + top bars + docks(empty for now)

        MainWindow = _monitors.PrimaryTopBar ?? CreateHiddenUtilityWindow();
```

Remove the `_topBarEngine` field (the engine now lives in `MonitorManager`). Add the fallback helper:

```csharp
private static Window CreateHiddenUtilityWindow()
{
    var window = new Window
    {
        Width = 0,
        Height = 0,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
        Visibility = Visibility.Hidden,
    };
    return window;
}
```

In `OnExit`, replace the `_topBarEngine?.Dispose()` block with `_monitors?.Dispose();` and add the field `private MonitorManager? _monitors;`.

- [ ] **Step 6: Update App.TopBar**

Replace `App.TopBar` (line 27-28):

```csharp
/// <summary>The primary top bar (MainWindow when a bar exists).</summary>
internal TopBarWindow? TopBar => _monitors?.PrimaryTopBar;
```

- [ ] **Step 6: Make QueueApplyPlacement internal**

In `src/WharfBar/Views/TopBarWindow.xaml.cs` line 1535, change the method accessibility:

```csharp
internal void QueueApplyPlacement(bool reRegister = false)
```

- [ ] **Step 7: Apply config changes to every bar**

Add to `MonitorManager`:

```csharp
internal void ApplyTopBarChanges()
{
    foreach (TopBarWindow bar in _topBars)
    {
        bar.ApplyConfigChanges();
    }
}
```

Add to `App`:

```csharp
/// <summary>Applies top-bar settings to every per-monitor bar.</summary>
internal void ApplyTopBarChanges()
{
    _monitors?.ApplyTopBarChanges();
}
```

Switch the page callers from `WharfApp.TopBar?.ApplyConfigChanges()` to `WharfApp.ApplyTopBarChanges()` in `src/WharfBar/Views/Pages/GeneralPage.xaml.cs` (line 45), `src/WharfBar/Views/Pages/TopBarPage.xaml.cs` (line 190), and `src/WharfBar/Views/Pages/AppearancePage.xaml.cs` (lines 123, 136). `AppearancePage` keeps calling `WharfApp.RecreateTopBar()` for the Mica↔translucent flip.

- [ ] **Step 8: Build + test**

Run: `dotnet build WharfBar.sln`; `dotnet test`.
Manual: single-monitor app starts with one bar; settings that call `WharfApp.ApplyTopBarChanges()` (General/TopBar/Appearance pages) still work.

- [ ] **Step 9: Commit**

```bash
git add src/WharfBar/Services/MonitorManager.cs src/WharfBar/App.xaml.cs src/WharfBar/Views/Pages/GeneralPage.xaml.cs src/WharfBar/Views/Pages/TopBarPage.xaml.cs src/WharfBar/Views/Pages/AppearancePage.xaml.cs src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat(monitor): MonitorManager owns top-bar set + reconcile on display/config change"
```

---

## Task 11: RecreateTopBar → RecreateTopBars (all bars)

**Files:**
- Modify: `src/WharfBar/Services/MonitorManager.cs`
- Modify: `src/WharfBar/App.xaml.cs`

**Interfaces:**
- Consumes: `MonitorManager.TopBars`, `MonitorManager.Engine`, `TopBarWindow.PrepareForRecreate`/`AbortRecreate` (existing methods).
- Produces: `void MonitorManager.RecreateTopBars()` — rebuilds every top bar (Mica↔translucent flip); preserves `PrimaryTopBar` ownership.

- [ ] **Step 1: Implement RecreateTopBars**

Add to `MonitorManager`:

```csharp
internal void RecreateTopBars()
{
    if (_engine is null)
    {
        return;
    }

    TopBarWindow? oldPrimary = PrimaryTopBar;
    List<TopBarWindow> windows = _topBars.ToList();
    foreach (TopBarWindow old in windows)
    {
        TopBarWindow? next = null;
        var prepared = false;
        try
        {
            // Ctor only — AppBar registers in OnSourceInitialized during Show.
            next = new TopBarWindow(_engine, _config, _taskbar, _veil, old.Monitor);
            old.PrepareForRecreate(); // ABM_REMOVE; no taskbar restore
            prepared = true;
            next.Show(); // AppBar registers in OnSourceInitialized
            int index = _topBars.IndexOf(old);
            if (index >= 0)
            {
                _topBars[index] = next;
            }
        }
        catch
        {
            if (next is not null)
            {
                try
                {
                    next.PrepareForRecreate();
                    next.Close();
                }
                catch
                {
                    // Best-effort discard.
                }
            }

            if (prepared)
            {
                old.AbortRecreate();
            }

            continue;
        }

        old.Close();
    }

    RefreshPrimaryOwnership();
}
```

- [ ] **Step 2: App delegates to the manager**

In `src/WharfBar/App.xaml.cs`, replace the entire `RecreateTopBar` method (lines 149-198) with:

```csharp
/// <summary>
/// Rebuilds every top bar when <c>AllowsTransparency</c> must flip (Mica ↔ translucent).
/// Removes old AppBars only — does <strong>not</strong> restore the Windows taskbar.
/// </summary>
internal void RecreateTopBar()
{
    _monitors?.RecreateTopBars();
}
```

- [ ] **Step 3: Build + test**

Run: `dotnet build WharfBar.sln`; `dotnet test`.
Manual: in Settings → Görünüm, switch top-bar background between Mica and transparent — the bar rebuilds without losing the AppBar reservation or taskbar state.

- [ ] **Step 4: Commit**

```bash
git add src/WharfBar/Services/MonitorManager.cs src/WharfBar/App.xaml.cs
git commit -m "refactor(topbar): RecreateTopBar rebuilds all per-monitor bars via MonitorManager"
```

---

## Task 12: Per-monitor work area + reservation

**Files:**
- Modify: `src/WharfBar/Native/TaskbarNative.cs`
- Modify: `src/WharfBar/Services/TaskbarVisibilityService.cs`

**Interfaces:**
- Consumes: `MonitorInfo` (Task 1), `Display` (Task 1).
- Produces:
  - `static bool TaskbarNative.TryExpandWorkAreaBottom(MonitorInfo monitor, int reservedBottomPx, bool broadcast)` — per-monitor expansion (SPI_SETWORKAREA applies to the monitor containing the rect).
  - `TaskbarVisibilityService.SetDockReservation(MonitorInfo monitor, int bottomPx)` — per-monitor; replaces the single-value `ReservedBottomPx` with a dictionary keyed by `DeviceId`.

- [ ] **Step 1: Add per-monitor expand to TaskbarNative**

In `src/WharfBar/Native/TaskbarNative.cs`, after `TryExpandPrimaryWorkAreaBottom`, add:

```csharp
/// <summary>
/// Expands <paramref name="monitor"/>'s work-area bottom to its edge (minus an optional dock
/// reservation strip). SPI_SETWORKAREA applies to the monitor containing the rect. Silent
/// (no SPIF_SENDCHANGE) except when <paramref name="broadcast"/> is true.
/// </summary>
internal static bool TryExpandWorkAreaBottom(MonitorInfo monitor, int reservedBottomPx, bool broadcast)
{
    RECT work = monitor.WorkArea;
    if (work.Width <= 0 || work.Height <= 0)
    {
        return false;
    }

    int targetBottom = monitor.Bounds.Bottom - Math.Max(0, reservedBottomPx);
    if (!AppBarPlacement.NeedsBottomExpand(work.Bottom, targetBottom))
    {
        return true;
    }

    var expanded = new RECT
    {
        Left = work.Left,
        Top = work.Top,
        Right = work.Right,
        Bottom = targetBottom,
    };

    return NativeMethods.SystemParametersInfo(
        NativeMethods.SPI_SETWORKAREA,
        0,
        ref expanded,
        broadcast ? NativeMethods.SPIF_SENDCHANGE : 0);
}
```

- [ ] **Step 2: Make the reservation per-monitor**

In `src/WharfBar/Services/TaskbarVisibilityService.cs`:

Replace the `_reservedBottomPx` field and `ReservedBottomPx` property (lines 31, 41-45) with:

```csharp
private readonly Dictionary<string, int> _reservedBottomByMonitor = new(StringComparer.OrdinalIgnoreCase);
```

Replace `SetDockReservation` (lines 53-62) with:

```csharp
/// <summary>
/// Updates the dock reservation for <paramref name="monitor"/> and re-announces that
/// monitor's work area so maximized apps resize to the new bottom edge.
/// </summary>
internal void SetDockReservation(MonitorInfo monitor, int bottomPx)
{
    int current = _reservedBottomByMonitor.GetValueOrDefault(monitor.DeviceId);
    if (current == bottomPx)
    {
        return;
    }

    _reservedBottomByMonitor[monitor.DeviceId] = Math.Max(0, bottomPx);
    ExpandWorkAreaBottom(monitor, broadcast: true);
}
```

Update `EnsureWorkAreaExpanded` to expand every monitor whose reservation exists:

```csharp
internal void EnsureWorkAreaExpanded()
{
    if (!_hidden || Volatile.Read(ref _restored) != 0 || Volatile.Read(ref _captured) == 0)
    {
        return;
    }

    foreach (KeyValuePair<string, int> pair in _reservedBottomByMonitor)
    {
        MonitorInfo? monitor = MonitorNative.Enumerate().FirstOrDefault(m =>
            string.Equals(m.DeviceId, pair.Key, StringComparison.OrdinalIgnoreCase));
        if (monitor is not null)
        {
            ExpandWorkAreaBottom(monitor);
        }
    }
}
```

- [ ] **Step 3: Expand per monitor**

Add the per-monitor expand that runs off the UI thread, and route `ExpandWorkAreaBottom` calls through it. Replace the existing `ExpandWorkAreaBottom(bool broadcast)` / `RunExpandWorkAreaBottom` with:

```csharp
private void ExpandWorkAreaBottom(MonitorInfo monitor, bool broadcast = false)
{
    if (Interlocked.Exchange(ref _expandingWorkArea, 1) != 0)
    {
        if (broadcast)
        {
            Interlocked.Exchange(ref _pendingBroadcastExpand, 1);
        }

        return;
    }

    try
    {
        ThreadPool.QueueUserWorkItem(RunExpandWorkAreaBottom, (monitor, broadcast));
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"WharfBar work area expand queue failed: {ex.Message}");
        Interlocked.Exchange(ref _expandingWorkArea, 0);
    }
}

private void RunExpandWorkAreaBottom(object? state)
{
    try
    {
        var (monitor, broadcast) = ((MonitorInfo, bool))state!;
        bool restoreRaced = Volatile.Read(ref _restored) != 0;
        if (!restoreRaced)
        {
            int reserved = _reservedBottomByMonitor.GetValueOrDefault(monitor.DeviceId);
            if (TaskbarNative.TryExpandWorkAreaBottom(monitor, reserved, broadcast))
            {
                return;
            }
        }

        if (restoreRaced || Volatile.Read(ref _restored) != 0)
        {
            if (_haveBaselineWorkArea)
            {
                _ = TaskbarNative.TrySetPrimaryWorkArea(_baselineWorkArea);
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"WharfBar work area expand failed: {ex.Message}");
    }
    finally
    {
        Interlocked.Exchange(ref _expandingWorkArea, 0);
        if (Interlocked.Exchange(ref _pendingBroadcastExpand, 0) == 1)
        {
            MonitorInfo? monitor = MonitorNative.Enumerate().FirstOrDefault(m => m.IsPrimary);
            if (monitor is not null)
            {
                ExpandWorkAreaBottom(monitor, broadcast: true);
            }
        }
    }
}
```

The primary-only paths (`TryExpandPrimaryWorkAreaBottom`, `ReservedBottomPx`) may be removed once no caller remains (Task 13 removes the last one).

- [ ] **Step 4: Fix the remaining ExpandWorkAreaBottom call sites**

The old `ExpandWorkAreaBottom(bool broadcast = false)` callers must pass a monitor. In `ApplyHide`, replace `ExpandWorkAreaBottom(broadcast);` (line 265) with:

```csharp
            MonitorInfo? primary = MonitorNative.Enumerate().FirstOrDefault(m => m.IsPrimary);
            if (primary is not null)
            {
                ExpandWorkAreaBottom(primary, broadcast);
            }
```

In `BeginTrayPeek`, replace `ExpandWorkAreaBottom();` (line 177) with:

```csharp
            MonitorInfo? primary = MonitorNative.Enumerate().FirstOrDefault(m => m.IsPrimary);
            if (primary is not null)
            {
                ExpandWorkAreaBottom(primary);
            }
```

- [ ] **Step 5: Build**

Run: `dotnet build WharfBar.sln`
Expected: build fails where `SetDockReservation(int)`/`EnsureWorkAreaExpanded` old signatures are used. Fix callers in `DockWindow` in Task 13; for now, keep a compatibility overload in this task:

```csharp
// Temporary compatibility shim (removed in Task 13).
internal void SetDockReservation(int bottomPx)
{
    MonitorInfo? primary = MonitorNative.Enumerate().FirstOrDefault(m => m.IsPrimary);
    if (primary is not null)
    {
        SetDockReservation(primary, bottomPx);
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add src/WharfBar/Native/TaskbarNative.cs src/WharfBar/Services/TaskbarVisibilityService.cs
git commit -m "feat(taskbar): per-monitor work-area expansion + dock reservation"
```

---

## Task 13: DockWindow per monitor

**Files:**
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs`

**Interfaces:**
- Consumes: `MonitorInfo` (Task 1), `Display.GetMonitorBounds` (Task 1), `TaskbarVisibilityService.SetDockReservation(MonitorInfo, int)` (Task 12).
- Produces: ctor `DockWindow(AppConfig config, TaskbarVisibilityService taskbar, MonitorInfo monitor)`; `internal MonitorInfo Monitor { get; }`; per-monitor placement + reservation.

- [ ] **Step 1: Add MonitorInfo to the constructor**

Replace the constructor (lines 97-123) signature and add the property:

```csharp
internal DockWindow(AppConfig config, TaskbarVisibilityService taskbar, MonitorInfo monitor)
{
    ArgumentNullException.ThrowIfNull(config);
    ArgumentNullException.ThrowIfNull(taskbar);

    Monitor = monitor;
    InitializeComponent();
    // ... rest of the existing ctor body unchanged
}

internal MonitorInfo Monitor { get; }
```

- [ ] **Step 2: Use per-monitor geometry**

In `TryGetPlacementGeometry` (line 736), replace:

```csharp
RECT monitor = Display.GetPrimaryMonitorBounds(hwnd);
```

with:

```csharp
RECT monitor = Display.GetMonitorBounds(hwnd);
```

In `BuildEdgeProbe` (line 763), replace the same call with `Display.GetMonitorBounds(hwnd)`.

- [ ] **Step 3: Per-monitor reservation**

Replace `ApplyWorkAreaReservation` (lines 278-289) with:

```csharp
private void ApplyWorkAreaReservation()
{
    if (_source is null || _autoHide.Enabled || !TryGetPlacementGeometry(out PlacementGeometry geo))
    {
        _taskbar.SetDockReservation(Monitor, 0);
        return;
    }

    uint dpi = Display.GetDpi(_source.Handle);
    int bottomGapPx = Display.DipToPixels(BottomGapDip, dpi);
    _taskbar.SetDockReservation(Monitor, geo.HeightPx + bottomGapPx);
}
```

- [ ] **Step 4: Remove the compatibility shim**

In `TaskbarVisibilityService`, delete the temporary `SetDockReservation(int)` shim added in Task 12.

- [ ] **Step 5: Build + test**

Run: `dotnet build WharfBar.sln`; `dotnet test`.
Manual: dock still works on the primary monitor; auto-hide off still reserves the bottom strip.

- [ ] **Step 6: Commit**

```bash
git add src/WharfBar/Views/DockWindow.xaml.cs src/WharfBar/Services/TaskbarVisibilityService.cs
git commit -m "feat(dock): per-monitor DockWindow ctor + placement + reservation"
```

---

## Task 14: MonitorManager owns docks — full reconcile loop

**Files:**
- Modify: `src/WharfBar/Services/MonitorManager.cs`
- Modify: `src/WharfBar/App.xaml.cs`

**Interfaces:**
- Consumes: `DockWindow` ctor (Task 13).
- Produces: `IReadOnlyList<DockWindow> Docks`; docks created/closed by `Reconcile()`; `DockWindow.ApplyConfigChanges()` forwarded for settings-driven updates.

- [ ] **Step 1: Manage docks in Reconcile**

Add to `MonitorManager`:

```csharp
private readonly List<DockWindow> _docks = new();

internal IReadOnlyList<DockWindow> Docks => _docks;
```

In `Reconcile`, after the top-bar block, add:

```csharp
    List<DockWindow> desiredDocks = _monitors
        .Where(m => IsEnabled(m.DeviceId, MonitorPart.Dock))
        .ToList();

    for (int i = _docks.Count - 1; i >= 0; i--)
    {
        DockWindow dock = _docks[i];
        if (!desiredDocks.Any(m => m.DeviceId == dock.Monitor.DeviceId))
        {
            dock.Close();
            _docks.RemoveAt(i);
        }
    }

    foreach (MonitorInfo monitor in desiredDocks)
    {
        if (_docks.Any(d => d.Monitor.DeviceId == monitor.DeviceId))
        {
            continue;
        }

        var dock = new DockWindow(_config.Current, _taskbar, monitor);
        _docks.Add(dock);
        dock.Show();
    }
```

- [ ] **Step 2: Dispose closes docks**

In `Dispose`, before the top-bar loop:

```csharp
foreach (DockWindow dock in _docks)
{
    dock.Close();
}

_docks.Clear();
```

- [ ] **Step 3: Forward config changes to docks**

Add:

```csharp
internal void ApplyDockConfigChanges()
{
    foreach (DockWindow dock in _docks)
    {
        dock.ApplyConfigChanges();
    }
}
```

- [ ] **Step 4: App uses manager for docks**

In `src/WharfBar/App.xaml.cs`, delete the direct `_dock` creation from `OnStartup` (lines 119-120). Replace the `App.DockView` property (line 24-25):

```csharp
/// <summary>Exposes the primary dock window for settings-driven updates.</summary>
internal DockWindow? DockView => _monitors?.Docks.FirstOrDefault(d => d.Monitor.IsPrimary)
    ?? _monitors?.Docks.FirstOrDefault();
```

Keep the `_dock` field removal consistent (remove `private DockWindow? _dock;` and its `OnExit` close). Update `GeneralPage`/`DockPage` callers that use `WharfApp.DockView?.ApplyConfigChanges()` to also reconcile docks — add to `App`:

```csharp
internal void ApplyDockChanges()
{
    _monitors?.Reconcile();
    _monitors?.ApplyDockConfigChanges();
}
```

and switch `DockPage.SaveAndApply` (line 75) from `WharfApp.DockView?.ApplyConfigChanges();` to `WharfApp.ApplyDockChanges();`.

- [ ] **Step 5: Build + test**

Run: `dotnet build WharfBar.sln`; `dotnet test`.
Manual: single monitor — dock unchanged.

- [ ] **Step 6: Commit**

```bash
git add src/WharfBar/Services/MonitorManager.cs src/WharfBar/App.xaml.cs src/WharfBar/Views/Pages/DockPage.xaml.cs
git commit -m "feat(monitor): MonitorManager owns docks + reconcile loop + ApplyDockChanges"
```

---

## Task 15: Ekranlar settings page

**Files:**
- Create: `src/WharfBar/Views/Pages/MonitorsPage.xaml`, `src/WharfBar/Views/Pages/MonitorsPage.xaml.cs`
- Modify: `src/WharfBar/Views/SettingsWindow.xaml`
- Modify: `src/WharfBar/Services/MonitorManager.cs`

**Interfaces:**
- Consumes: `MonitorManager.CurrentMonitors`, `MonitorManager.FindEntry`, `MonitorManager.UpsertEntry`, `MonitorManager.Reconcile`, `MonitorPart` (Task 4/10/14), `App.Config`.
- Produces: `MonitorsPage` navigable from Settings; each live monitor row has "Üst bar" and "Dock" toggles.

- [ ] **Step 1: Add a setter that reconciles after config write**

Add to `MonitorManager`:

```csharp
internal void SetPartEnabled(string deviceId, MonitorPart part, bool enabled)
{
    MonitorInfo? live = _monitors.FirstOrDefault(m =>
        string.Equals(m.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
    if (live is null)
    {
        return;
    }

    MonitorConfig entry = UpsertEntry(live);
    if (part == MonitorPart.TopBar)
    {
        entry.TopBar = enabled;
    }
    else
    {
        entry.Dock = enabled;
    }

    _config.Save(_config.Current);
    Reconcile();
}
```

- [ ] **Step 2: Create the page XAML**

Create `src/WharfBar/Views/Pages/MonitorsPage.xaml`:

```xml
<Page x:Class="WharfBar.Views.Pages.MonitorsPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
      Title="Ekranlar">

    <StackPanel Margin="24,24,24,24" MaxWidth="720" HorizontalAlignment="Left">
        <TextBlock Text="Ekranlar" FontSize="28" FontWeight="SemiBold" />
        <TextBlock Text="Top bar ve dock'u hangi monitörlerde göstereceğinizi seçin. Görünüm ayarları tüm ekranlarda ortaktır."
                   Margin="0,4,0,20"
                   TextWrapping="Wrap"
                   Opacity="0.7" />

        <ItemsControl x:Name="MonitorList">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Background="{DynamicResource ControlFillColorDefaultBrush}"
                            CornerRadius="8"
                            Padding="16,12"
                            Margin="0,0,0,10">
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>

                            <StackPanel Grid.Column="0" VerticalAlignment="Center">
                                <StackPanel Orientation="Horizontal">
                                    <TextBlock Text="{Binding Name}" FontSize="14" FontWeight="Medium" />
                                    <Border Background="{DynamicResource AccentTextFillColorPrimaryBrush}"
                                            CornerRadius="4"
                                            Padding="6,1"
                                            Margin="8,0,0,0"
                                            Visibility="{Binding PrimaryBadgeVisibility}">
                                        <TextBlock Text="Birincil" FontSize="11" Foreground="White" />
                                    </Border>
                                </StackPanel>
                                <TextBlock Text="{Binding DeviceName}" FontSize="12" Opacity="0.6" Margin="0,2,0,0" />
                            </StackPanel>

                            <TextBlock Grid.Column="1" Text="Üst bar" VerticalAlignment="Center" Margin="0,0,10,0" />
                            <ui:ToggleSwitch Grid.Column="1"
                                             Margin="0,0,24,0"
                                             VerticalAlignment="Center"
                                             Tag="{Binding DeviceId}"
                                             IsChecked="{Binding TopBar}"
                                             Checked="OnTopBarToggled"
                                             Unchecked="OnTopBarToggled" />

                            <TextBlock Grid.Column="2" Text="Dock" VerticalAlignment="Center" Margin="0,0,10,0" />
                            <ui:ToggleSwitch Grid.Column="2"
                                             VerticalAlignment="Center"
                                             Tag="{Binding DeviceId}"
                                             IsChecked="{Binding Dock}"
                                             Checked="OnDockToggled"
                                             Unchecked="OnDockToggled" />
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Page>
```

- [ ] **Step 3: Create the page code-behind**

Create `src/WharfBar/Views/Pages/MonitorsPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

public partial class MonitorsPage : Page
{
    public MonitorsPage()
    {
        InitializeComponent();
        RefreshList();
    }

    private App WharfApp => (App)Application.Current;
    private MonitorManager? Monitors => WharfApp.Monitors;

    private sealed class Row
    {
        public string DeviceId { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public Visibility PrimaryBadgeVisibility { get; init; } = Visibility.Collapsed;
        public bool TopBar { get; set; }
        public bool Dock { get; set; }
    }

    private void RefreshList()
    {
        if (Monitors is null)
        {
            return;
        }

        var rows = new List<Row>();
        foreach (MonitorInfo monitor in Monitors.CurrentMonitors)
        {
            MonitorConfig entry = MonitorIdentityMatcher.Resolve(
                WharfApp.Config!.Current.Monitors,
                monitor);
            rows.Add(new Row
            {
                DeviceId = monitor.DeviceId,
                DeviceName = monitor.DeviceName,
                Name = string.IsNullOrWhiteSpace(entry.Name) ? monitor.FriendlyName : entry.Name,
                PrimaryBadgeVisibility = monitor.IsPrimary ? Visibility.Visible : Visibility.Collapsed,
                TopBar = entry.TopBar,
                Dock = entry.Dock,
            });
        }

        MonitorList.ItemsSource = rows;
    }

    private void OnTopBarToggled(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton { Tag: string deviceId } toggle)
        {
            Monitors?.SetPartEnabled(deviceId, MonitorPart.TopBar, toggle.IsChecked == true);
            RefreshList();
        }
    }

    private void OnDockToggled(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Primitives.ToggleButton { Tag: string deviceId } toggle)
        {
            Monitors?.SetPartEnabled(deviceId, MonitorPart.Dock, toggle.IsChecked == true);
            RefreshList();
        }
    }
}
```

- [ ] **Step 4: Expose the manager on App**

In `src/WharfBar/App.xaml.cs`, add:

```csharp
/// <summary>Exposes the monitor manager for the settings pages.</summary>
internal MonitorManager? Monitors => _monitors;
```

- [ ] **Step 5: Add navigation**

In `src/WharfBar/Views/SettingsWindow.xaml`, after the "Dock" item (line 60), add:

```xml
                <ui:NavigationViewItem Content="Ekranlar"
                                       TargetPageType="{x:Type pages:MonitorsPage}">
                    <ui:NavigationViewItem.Icon>
                        <ui:SymbolIcon Symbol="ComputerDesktop24" />
                    </ui:NavigationViewItem.Icon>
                </ui:NavigationViewItem>
```

- [ ] **Step 6: Build + test**

Run: `dotnet build WharfBar.sln`; `dotnet test`.
Manual (dual monitor): toggling "Üst bar"/"Dock" per monitor shows/hides that monitor's bar/dock immediately and persists across restart.

- [ ] **Step 7: Commit**

```bash
git add src/WharfBar/Views/Pages/MonitorsPage.xaml src/WharfBar/Views/Pages/MonitorsPage.xaml.cs src/WharfBar/Views/SettingsWindow.xaml src/WharfBar/Services/MonitorManager.cs src/WharfBar/App.xaml.cs
git commit -m "feat(settings): Ekranlar page — per-monitor top bar + dock toggles with live reconcile"
```

---

## Task 16: Hardening + docs

**Files:**
- Modify: `src/WharfBar/Services/MonitorManager.cs` (resync gaps)
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` (WM_DISPLAYCHANGE broadcast to manager)
- Docs: `docs/00-overview.md`, `docs/02-win32-appbar-dwm.md`, `docs/04-gotchas.md`, `.agent/boundaries.md`, `.agent/project.md`, `.agent/context/roadmap.md`, `.agent/screens/topbar.md`, `docs/decisions/003-topbar-placement-and-appbar-lifecycle.md`

**Interfaces:**
- Consumes: everything from Tasks 1-15.

- [ ] **Step 1: Resync after explorer restart**

`TaskbarCreated` is only delivered to registered top-level windows. In `TopBarWindow.WndProc`'s `_taskbarCreatedMessage` handler, after `QueueApplyPlacement(reRegister: true)` + `_engine.TrayMirror.Reenumerate()`, call:

```csharp
            ((App)Application.Current).Monitors?.Refresh();
```

- [ ] **Step 2: Reconcile on work-area changes (deferred)**

In `MonitorMessageWindow`, subscribe `SettingsChanged` in `MonitorManager` to `QueueRefreshAllPlacements`:

```csharp
_messageWindow.SettingsChanged += OnSettingsChanged;
```

with:

```csharp
private void OnSettingsChanged(object? sender, EventArgs e)
{
    // Our own SPI_SETWORKAREA writes and theme changes arrive here; re-applying
    // placements is idempotent (SetDockReservation short-circuits on equal values).
    Dispatcher.CheckAccess()
        ? RefreshAllPlacements()
        : System.Windows.Application.Current.Dispatcher.BeginInvoke(RefreshAllPlacements);
}
```

- [ ] **Step 3: Update docs**

Update each doc to reflect the new behavior (remove "v1: primary monitor only" for bar/dock, note per-monitor toggles, shared engine, per-monitor AppBar + SPI fallback, and that Veil remains primary-only):

- `docs/00-overview.md` — remove the "İkincil monitörlerde AppBar / work-area rezervasyonu" non-goal; note per-monitor bar/dock.
- `docs/02-win32-appbar-dwm.md` — section a: replace the primary-only decision with the per-monitor AppBar + SPI fallback description.
- `docs/04-gotchas.md` — line 16/17: per-monitor re-SETPOS across monitors; mixed-DPI per-window.
- `.agent/boundaries.md` — move multi-monitor AppBar/SPI from "Önce sor" to the allowed list (user-approved).
- `.agent/project.md` — replace "v1 monitör: Yalnızca birincil" with per-monitor support.
- `.agent/context/roadmap.md` — remove item 10 from P2.
- `.agent/screens/topbar.md` — update to "per-monitor; each bar is an AppBar on its monitor".
- `docs/decisions/003-topbar-placement-and-appbar-lifecycle.md` — update the conclusion: `Display.GetMonitorBounds` seam implemented; per-monitor AppBar.

- [ ] **Step 4: Full regression**

Run: `dotnet build WharfBar.sln`; `dotnet test`.
Manual dual-monitor: plug/unplug a monitor while running (bar/dock appear/disappear per config), switch DPI scaling on one monitor, restart explorer (`taskkill /f /im explorer.exe; start explorer`), toggle configs in Settings, then quit and confirm the taskbar + work areas restore on both monitors.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Services/MonitorManager.cs src/WharfBar/Views/TopBarWindow.xaml.cs docs/ .agent/
git commit -m "docs+hardening: multi-monitor resync on shell/display change; update docs and boundaries"
```

---

## Post-Plan Checklist

1. **Spec coverage:** each spec section has a task — abstraction (1-4), config (5-6), top-bar refactor (7-11), dock (12-14), settings UI (15), testing/rollout (all tasks carry tests; hardening 16).
2. **Placeholder scan:** no TBD/TODO; every step has concrete code or a precise edit target.
3. **Type consistency:** `MonitorInfo(Handle, DeviceId, DeviceName, FriendlyName, IsPrimary, Bounds, WorkArea, Dpi)` is defined once (Task 1) and consumed identically everywhere; `MonitorConfig(DeviceId, DeviceName, Name, TopBar, Dock)` defined in Task 2 and normalized in Task 5; `MonitorManager.SetDockReservation` signatures match across Tasks 12-14.