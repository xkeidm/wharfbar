# 03 — Development Order

Recommended phases. Manual verification at the end of each phase; on phases that use an AppBar, work with the awareness that after a crash the fix is `ABM_REMOVE` / an explorer restart.

> Phase 1–7 are done. **Phase 8 code is in the working tree** (manual verification is pending).
> **Phase 9 code is in the working tree** (a commit + 9a/9b/9c manual verification are pending).
> **The dock redesign (2026-10-04) code is in the working tree** — verification with the mouse is pending.
> **Media panel code is in the working tree** (manual verification is pending; below).

## Phase 0 — Repo / docs ✓

- [x] `AGENTS.md`, `.agent/`, `docs/`, thin Cursor rules
- [x] WPF project scaffold (`src/WharfBar/` + root `WharfBar.sln`)

## Phase 1 — Skeleton window ✓

- [x] `dotnet new wpf` + WPF-UI + manifest (Per-Monitor V2)
- [x] `TopBarWindow`: `WindowStyle=None`, `Topmost=true`, `ShowInTaskbar=false`
- [x] Fixed height (30 DIP), a plain color; **no Mica** — position and size only
- [x] Placement in physical pixels + `GetDpiForWindow`; WPF DIP `Left/Top` is not used
- [x] Single-instance Mutex
- [x] `DockWindow` — arrived with Phase 4 (overlay + demo pins)

## Phase 2 — Top bar AppBar ✓

- [x] `SHAppBarMessage` flow: NEW → QUERYPOS → SETPOS
- [x] `ABM_REMOVE`: `OnClosing`, `Application.Exit`, `SessionEnding`, `ProcessExit`,
 unhandled-error paths — idempotent (`Interlocked`)
- [x] Callback message (`RegisterWindowMessage`) + handling of `ABN_POSCHANGED` / `ABN_STATECHANGE` /
 `ABN_FULLSCREENAPP`
- [x] Re-registration after an explorer restart via the `TaskbarCreated` message
- [x] Repositioning with `WM_DPICHANGED` / `WM_DISPLAYCHANGE`
- [x] **Verified (by script, 1920×1080 @ 100%):** work area `T=0` → `T=30`, on exit it returns
 exactly to the baseline, a maximized window stays under the bar, the second instance exits on its own.
 Remaining human-eye items live under [GitHub Milestones](https://github.com/xkeidm/wharfbar/milestones)
 (see also [`../.agent/context/roadmap.md`](../.agent/context/roadmap.md) for completed-phase history).

## Phase 3 — Visuals ✓

- [x] WPF-UI theme dictionaries + `ApplicationThemeManager` (system light/dark)
- [x] WPF-UI `WindowBackdropType.Mica` first; raw DWM only as fallback / verification
- [x] `WindowStyle=None` + `DwmExtendFrameIntoClientArea` (30 DIP; no caption min-height)
- [x] Corner: `DWMWCP_DONOTROUND` — ADR-004
- [x] Runtime theme: `WM_SETTINGCHANGE` / `ImmersiveColorSet` (+ theme/color messages)
- [x] No `AllowsTransparency=true`; an opaque fallback if there is no backdrop
- [x] The build is clean; the work area was verified by script (baseline on close)

## Phase 4 — Dock ✓

> **Redesigned on 2026-10-04** (ADR-005 revision): the list below describes that day's state.
> The dock no longer mirrors the taskbar; it uses its own `dock.items` list,
> it is drawn as a centered plate (Mica / transparent / tinted, live scale 1), and it has drag-reorder / file drop / position /
> a settings-page content list.

- [x] Overlay: Topmost, no AppBar (ADR-001); the work-area bottom edge, centered
- [x] Demo pinned-app list UI (`DemoPinnedApps.All` — 4 applications; persistent config is Phase 5)
- [x] Hover growth: `ScaleTransform` + `Storyboard` (1.2×, 180 ms)
- [x] On click, `ProcessLauncherService` / `ShellIcon` + `AppIconService`
- [x] `FullscreenState` bridge (`TopBarWindow` → hide the dock)
- [x] Dock repositioning after AppBar placement via `WorkAreaChanged.Notify`
- [x] `DockAutoHideController` stub (always visible; real auto-hide is a later phase)
- [x] `DockHitTest` stub (fully interactive; passthrough was postponed)
- [x] Mica + `DWMWCP_ROUND` corners; DPI / display / theme refresh

## Phase 5 — Config and persistence ✓

- [x] `AppConfig` schema + `ConfigService` (`System.Text.Json`, camelCase)
- [x] `%AppData%/WharfBar/config.json` — created with the demo pins on first run
- [x] Pinned apps are loaded into the dock from config; `theme` (`system`/`light`/`dark`); dock `iconSize` / `autoHide` (auto-hide behavior is still a stub)
- [x] A broken / unsupported `schemaVersion` → a `.bak` backup + return to the defaults

## Phase 6 — Autostart and resilience ✓

- [x] Startup-folder shortcut (`AutostartService` → `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\WharfBar.lnk`)
- [x] Sync with `AppConfig.autostart`: on load (`App.OnStartup`) and on every `ConfigService.Save`
- [x] AppBar re-registration after an Explorer / shell restart — in Phase 2 (`TaskbarCreated`)
- [x] Fullscreen-hide bridge — Phase 2/4 (`ABN_FULLSCREENAPP` + `FullscreenState`); hit-test polish is a later phase

## Phase 7 — Status indicators / clock ✓

- [x] Live clock (`StatusService` + the `TopBarWindow` right strip)
- [x] Network + battery:.NET `NetworkInformation` + `GetSystemPowerStatus`; WPF-UI Fluent icons
- [x] Its own tray icon (`Shell_NotifyIcon` / `TrayIconService`) — the Quit menu only
- [x] Volume indicator + quick-settings flyout (ADR-006; Core Audio + mixer + network)

> The tray icon does **not read** the system Wi-Fi/battery icons; it is only WharfBar's own exit menu.

## Phase 8 — Dock auto-hide / hit-test polish ✓ (code)

> Preferably applied **after Phase 9a/9b**: when the taskbar is hidden, the dock's bottom-edge geometry changes.
> The code is in the working tree; the manual verification below is pending.

`dock.autoHide` behavior was implemented with `DockAutoHideController` + `DockHitTest`:

- [x] Show the dock when the mouse approaches the edge band; hide it when the mouse moves away (animated)
- [x] While hidden / collapsed, pass the click through to the window underneath with `WS_EX_TRANSPARENT` (`DockHitTest.ApplyPassthrough`)
- [x] While visible, keep hit-testing fully interactive (`DockHitTest.ApplyInteractive`)
- [x] Make the clash between fullscreen (`FullscreenState`) and auto-hide explicit
- [x] When `autoHide: false`, the dock is always visible
- [ ] Manual verification: maximized window, game/video fullscreen, edge hover

## Phase 9 — Replacing the shell (live dock + tray hybrid + taskbar) ✓ (code)

ADR-005.
The code is in the working tree; a commit and the manual items below are still pending. Sub-phases:

### 9a — Taskbar lifecycle

- [x] At startup, capture the taskbar state (`ABM_GETSTATE` + `Shell_TrayWnd` visibility); hide it
- [x] On the exit + crash paths, restore the previous state (restore → `ABM_REMOVE`; idempotent)
- [x] Re-apply the hide after `TaskbarCreated` (`ReapplyHide`)
- [ ] Manual: Quit → restore; awareness of the force-kill gotcha

### 9b — Dock (independent list + sections)

> **On 2026-10-04** the pin source was moved to `dock.items`. **On 2026-10-07** the left group again
> **reads** the `User Pinned` folder, read-only (`TaskbarPinnedAppsSource`); nothing is written to the folder.
> “Pin to the dock / remove” (`Dock'a sabitle / kaldır`) stayed as `dock.items` (hide/restore is 9a's subject).
> What still holds: `EnumWindows` + AUMID running grouping, and the composer's sectioned merge.

- [x] `User Pinned\TaskBar` `.lnk` scan, read-only (`TaskbarPinnedAppsSource`) — removed on 2026-10-04, **reading came back on 2026-10-07**; no writing
- [x] `EnumWindows` + AppUserModelID running grouping (`RunningAppsSource`)
- [x] `config.pinnedApps` is only a seed/fallback (`DockAppsComposer`) — **turned into a migration on 2026-10-04:** the old list is moved into `dock.items` once and then cleared (`ConfigService.MigrateDockItems`)
- [x] Combined pin + running UI; a placeholder for UWP (no broken icon path) — **on 2026-10-04, three sections:** Pinned | Running | Minimized + macOS-style magnification/reflow
- [ ] Manual: add/remove a pin, running indicator, launch / focus — **also not verified as of 2026-10-04:** magnification/reflow, drag-reorder, and file drop were not tried with the mouse (see `docs/04-gotchas.md`)

### 9c — Tray hybrid button

- [x] A “tray / hidden icons” (`Gizli simgeler`) button on the top bar → native overflow (`TrayOverflowInvoker` / `Win+B`)
- [x] No RPM / undocumented enumeration; foreign-icon drawing only via the documented `TB_*` mirror (ADR-009)
- [x] Optional: volume indicator + quick settings (ADR-006; extended after 9c)
- [ ] Manual: the button opens the overflow; third-party icons are mirrored on the top bar (ADR-009)

### Tray mirror (ADR-009) ✓ (code)

Mirrors third-party tray icons on the top bar with documented comctl32 `TB_*` messages
(`trayMirror` catalog v5); a system-icon filter; click forward; hidden icons via `Win+B`.

## Media panel (SMTC) ✓ (code)

Implemented outside the phase order, as a separate feature after Phase 9 (ADR-008).
- [x] `MediaSnapshot` model (`Idle` + `HasSession`) + `MediaSessionSelector` (most-recently-active policy, `MoveCurrent` ring) — unit tested
- [x] `MediaSessionService` — SMTC (`GlobalSystemMediaTransportControlsSessionManager`) monitoring: snapshot + Play/Pause (`Oynat/Duraklat`) / Next/Previous (`İleri`/`Geri`) + `Changed`/`SessionsChanged`; events are marshaled to the UI Dispatcher; init failure retries in 10 s; `Dispose` (`_disposed` guard)
- [x] Catalog `media` item + catalog **v4** migration (`EnsureMedia` → after `brightness`)
- [x] `MediaFlyout` (cover + title/artist + controls + session picker) + top-bar wiring (dynamic visibility, tooltip, animated open/close)
- [ ] Manual verification: the item appears while media is playing; the controls work; multi-session picker; right-click persistent hide; config migration (the 10-item list in the brief)

## Phase 10+ (candidates after v1 — the order is not fixed)

- [ ] Secondary monitor / work-area strategy
- [ ] UWP/Store icons (the full path)
- [x] Dock work-area reservation (auto-hide OFF): a sibling `ABE_BOTTOM` (`DockWorkAreaReservation`), bottom edge = the top of the dock; the plate is not an AppBar (ADR-001). `SPI_SETWORKAREA` is dead on 25H2, so it does not do this job. Auto-hide ON: `ABM_REMOVE`, a maximized window covers the bottom completely
- [ ] Richer status menus / notification-center-like UI
