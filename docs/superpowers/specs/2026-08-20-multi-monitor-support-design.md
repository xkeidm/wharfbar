# Multi-Monitor Support for Top Bar & Dock — Design

- Date: 2026-08-20
- Status: Approved (design review)
- Scope: Top bar and dock visibility on secondary monitors, per-monitor toggles, shared engine refactor.

## Purpose

WharfBar currently renders a single top bar (primary-monitor AppBar) and a single dock (primary-monitor overlay + bottom work-area reservation). This design adds **per-monitor enable/disable** for the top bar and dock so the macOS-style experience can be extended to any connected display.

## Context

- v1 hard limit: primary monitor only (`docs/00-overview.md` non-goal; `.agent/project.md`; `.agent/boundaries.md` "Önce sor").
- The intended expansion seam is `Display.GetPrimaryMonitorBounds` (ADR-003 conclusion, `docs/decisions/003`).
- Existing architecture:
  - Single `TopBarWindow` (owns AppBar via `AppBar.cs`, ~15 per-window services created in `OnSourceInitialized`, primary-only placement in `ApplyPlacement`).
  - Single `DockWindow` (overlay; bottom reservation via `TaskbarVisibilityService.SetDockReservation(px)` → `TaskbarNative.TrySetPrimaryWorkArea`).
  - `MonitorBrightnessService` is the only all-monitor enumerator today (`EnumDisplayMonitors`), used for brightness only.
  - All monitor/DPI change handling is per-window WndProc (`WM_DISPLAYCHANGE`/`WM_DPICHANGED`/`TaskbarCreated`/`ABN_POSCHANGED`).
  - `AppConfig` (SchemaVersion 1) has no monitor fields.

## Decisions (from brainstorm, user-approved)

1. **Visibility scope:** per-monitor toggle for top bar and dock, independent switches. Default: primary = both on, others off (preserves current behavior).
2. **Settings model:** global appearance (bar height, icon size, surface, tint) + per-monitor visibility only.
3. **Dock behavior:** existing `DockConfig.AutoHide` (global) governs each dock's auto-hide; the top bar is always visible on enabled monitors.
4. **Taskbars:** unchanged — all Windows taskbars are hidden while WharfBar runs (`TaskbarVisibilityService`).
5. **Architecture:** shared engine + thin per-monitor windows (Approach A). Rejected: N full self-contained windows (service duplication), single virtual-screen-spanning window (PMv2 per-monitor DPI incompatible with per-monitor AppBar).
6. **Top reservation mechanism:** per-monitor `SHAppBarMessage` (AppBar per HWND). Fallback if a shell version misbehaves: per-monitor `SPI_SETWORKAREA` (applies to the monitor containing the supplied rect; confirmed in Win32 docs). The fallback is a documented boundary extension approved here.
7. **Veil:** stays primary-only (out of scope).

## 1. Monitor Abstraction

### 1.1 `MonitorInfo` + enumeration (`Native/MonitorNative.cs`)

New record:

```csharp
internal sealed record MonitorInfo(
    IntPtr Handle,            // HMONITOR
    string DeviceId,          // EnumDisplayDevices DeviceID (registry path, stable across reboots)
    string DeviceName,        // szDevice, e.g. "\\\\.\\DISPLAY1"
    string FriendlyName,      // e.g. "DELL U2720Q"
    bool IsPrimary,
    RECT Bounds,
    RECT WorkArea,
    uint Dpi);
```

- `MonitorNative.Enumerate()` → `IReadOnlyList<MonitorInfo>` via `EnumDisplayMonitors` + `GetMonitorInfo(MONITORINFOEX)` + `EnumDisplayDevices`, primary first.
- **Identity strategy:** match by `DeviceId` first; fall back to `DeviceName` (the number can change on replug/reorder). `FriendlyName` is display-only.

### 1.2 `MonitorManager` service (`Services/MonitorManager.cs`)

- Owns a hidden **message-only window** (`HWND_MESSAGE`) whose WndProc listens for `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`, `WM_DPICHANGED`; re-enumerates and raises `MonitorChanged`.
- Exposes `CurrentMonitors`, `IsEnabled(deviceId, part)` where `part` is `TopBar` or `Dock`.
- Owns the window sets (top bars, docks) and reconciles them against `config × monitors` on startup, monitor change, and config change.
- `MonitorIdentityMatcher` (pure static, unit-tested): matches live monitors to config entries; new/unmatched monitors default to off (except primary).

## 2. Config Model (SchemaVersion 1 → 2)

New `AppConfig.Monitors` list:

```json
"Monitors": [
  { "DeviceId": "PCI\\VEN_...", "Name": "DELL U2720Q", "TopBar": true, "Dock": false }
]
```

- `TopBar`/`Dock` independent per-monitor switches.
- Migration (v1 → v2): no `Monitors` field → synthesize entry for the primary monitor with both on; secondary monitors default off. `ConfigService` normalizes on load (prunes invalid entries, keeps unmatched entries for settings display).
- `SchemaVersion` bumped to 2; existing migration path in `ConfigService` extended.

## 3. Top Bar Refactor

### 3.1 `TopBarEngine` (shared services)

New `Services/TopBarEngine.cs` owns the once-created services and their lifecycle:

StatusService, VolumeService, AudioMixerService, NightLightService, WifiService, BluetoothService, ProjectionService, QuickSettingsService, MonitorBrightnessService, MediaSessionService, TrayMirrorService, TrayOverflowInvoker, NotificationCenterInvoker.

- Services are observable (`Changed` events) → each per-monitor window subscribes and updates its own UI.
- Per-window flyout hosts (`QuickSettingsHost`, `BrightnessHost`, `MediaHost`) are attached by the window to the shared services (`engine.Attach(host, ...)`); state is shared, presentation opens on the clicked monitor.
- Remains per-window in `TopBarWindow`: WndProc, AppBar, placement, status-strip interaction, flyout open/close, fullscreen z-order, shutdown hooks.

### 3.2 Per-monitor AppBar

Each `TopBarWindow` registers its own `AppBar` with its own HWND (`ABM_NEW`). The shell binds the AppBar to the edge of the monitor the window occupies → per-monitor top reservation (Windows 8+). Placement is re-applied on `WM_DISPLAYCHANGE`/`ABN_POSCHANGED` (already present).

**Fallback:** if a specific shell version fails to reserve the secondary edge, use per-monitor `SPI_SETWORKAREA` for the top strip (applies to the monitor containing the rect). Approved boundary extension; gated behind runtime detection, not default.

### 3.3 Placement + DPI

- `Display.GetPrimaryMonitorBounds` generalizes to `Display.GetMonitorBounds(MonitorInfo)` (the ADR-003 seam). `GetDpiForWindow` stays per-window (PMv2 correct on mixed DPI).
- `TopBarWindow` receives its target `MonitorInfo` in the constructor; `ApplyPlacement` uses that monitor's bounds/DPI.
- `App.RecreateTopBar` (Mica↔translucent flip) becomes a `MonitorManager` operation that rebuilds all top bar windows.

## 4. Dock per Monitor

- `DockWindow` receives its target `MonitorInfo`; enabled monitors each get a `DockWindow` at their bottom edge.
- `TaskbarVisibilityService.SetDockReservation(px)` generalizes to `SetDockReservation(MonitorInfo, px)`; `ReservedBottomPx` becomes per-monitor. `TaskbarNative.TrySetPrimaryWorkArea` gains a per-monitor variant (monitor containing the rect).
- Auto-hide (global `DockConfig.AutoHide`): on → no reservation; off → per-monitor reservation.

## 5. Lifecycle & Reconciliation

- **Startup:** `MonitorManager` → resolve config × monitors → create `TopBarEngine` → create top bars (primary first) → create docks.
- **Monitor change:** re-enumerate, diff against current window sets, create/close windows, re-apply placements, refresh `MainWindow` if primary changed.
- **Config change (settings):** live reconcile (enable/disable per monitor).
- **Exit:** close all windows, `ABM_REMOVE` all AppBars, restore taskbars (existing path).
- `App.MainWindow` remains the primary monitor's `TopBarWindow` (preserves shutdown/activation semantics); others are auxiliary windows.

## 6. Settings UI — "Ekranlar" Section

New page in Settings listing detected monitors (friendly name + "Birincil" badge) with two toggles each: **Üst bar** and **Dock**. Toggles write config and trigger live reconciliation. Global appearance settings unchanged.

## 7. Testing

- **Unit (xUnit):** `MonitorIdentityMatcher` (matching/fallback/defaults), config migration v1→v2, per-monitor placement/reservation math (`AppBarPlacement`, `Display`, dock geometry).
- **Integration (manual):** two-monitor rig — per-monitor AppBar reservation, mixed DPI, plug/unplug, live config toggles, shutdown taskbar restore, explorer restart.

## 8. Rollout Plan (incremental)

| Phase | Content | Verification |
|-------|---------|--------------|
| 1 | Monitor abstraction: `MonitorInfo`/enumerate, `MonitorManager` (message-only window), `MonitorIdentityMatcher` | unit tests |
| 2 | Config v2 + migration (`Monitors` list) | unit tests |
| 3 | `TopBarEngine` extraction — single-window regression (primary still works) | build + unit + manual single-screen |
| 4 | Per-monitor top bars: N windows, per-monitor AppBar/placement, display-change reconciliation | manual dual-screen |
| 5 | Per-monitor docks + per-monitor bottom reservation | manual dual-screen |
| 6 | Ekranlar settings page + live reconciliation | manual |
| 7 | Hardening: plug/unplug, mixed DPI, explorer restart, shutdown; **docs update** (00-overview non-goal removal, boundaries, ADR-003, screens docs) | full regression |

Each phase is independently verifiable; the app stays runnable after Phase 3 onward.

## Out of Scope

- Per-monitor appearance overrides (global visuals only).
- Secondary-monitor Veil.
- Per-monitor taskbar hiding (global hide kept).
- Per-monitor dock configuration beyond visibility + existing global auto-hide.