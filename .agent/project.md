# WharfBar — Project Brief (AI)

## One sentence

On Windows, a **top bar + dock** inspired by Windows, macOS, and Linux desktops: .NET 8 WPF + WPF-UI + Win32 P/Invoke.

## Priority (when they conflict)

1. Low resource use → 2. Windows Fluent native look → 3. Simplicity.
 No Apple brand or macOS icon language. Detail: [`../docs/00-overview.md`](../docs/00-overview.md).

## Product

- **Brand logo (ADR-019):** the WharfBar "Dusk" mark by default (solid small pier + sun; no plank gaps), the Windows logo when `logoStyle` = `windows` (Settings → Appearance). Top bar: one colour (bar foreground). Dock launch tile: themed from the accent + light/dark (`BrandPalette`), re-tinted on theme/accent messages; vector geometry in `BrandLogo`.
- **Top bar:** logo on the left (drawn, `logoStyle`) + system stats (`CPU x% · GPU y% · RAM z%`;
 on multiple GPUs a `GPU0/GPU1` split: `topBar.showGpuPerAdapter`;
 `topBar.showSystemStats`, `SystemStatsService`; hovering shows the sparkline and network speed, the bar stays 30 DIP) + menu (`About` / `Close`); on the right, the status strip
 (`trayMirror` → `trayOverflow` → `dateTime` → `notifications` → `controlCenter` →
 `network` → `battery`; customizable). `volume` / `brightness` / `media` stay in the catalog
 but are not in the default order (ADR-011); a saved `statusItems` list is not stripped. Island pill in the center:
 idle `Clipboard` or, with `island.idleLabel=lastClip`, the last clip, now playing via `island.musicPresence`
 (`persistent` default / `transient`), a short meter for volume, brightness, and battery,
 at most two satellites on a HUD face (persistent music and clipboard together; a now-playing pill has none). Low-battery threshold
 `island.lowBatteryPercent` (default 20, accepted 5–50). Optional tint `island.colorMode` (`album` default / `system` / `neutral`, ADR-018): in `album` the pill (subtle resting tint, full panel-top color while joined), the panel gradient and the music play/pause circle follow the cover color. Clipboard history is on the primary bar; the secondary pill is music and meters. The multi-line panel is a single
 popup with a **hub tab strip** (`Clipboard` / `Wallpaper` / `Music` / `Shelf` / `Prompter` / `Camera` / `Color`; last tab `island.hubTab`, tab visibility via `island.shelfTab` / `island.prompterTab` / `island.cameraTab` / `island.colorpickerTab` — ADR-010 addendum; labels fold to icons when all seven tabs converge); wallpaper is a thumbnail gallery (bundled `Wallpapers/` + `%AppData%/WharfBar/wallpapers/`, click apply via documented SPI, primary bar only; optional **`island.wallpaperAccent`** syncs WharfBar + best-effort Windows accent from the image after apply, default **on**). **Appearance light/dark** also writes Windows Personalization (`SystemThemeWriter`); **system** leaves Windows alone. Bundled `*-light`/`*-dark` pairs auto-switch with the resolved theme when applied from the hub (`island.lastHubWallpaper`; gallery still shows two tiles). Optional transition (`island.wallpaperTransition`: `off` / `fade` / `blurFade`, default blur+fade): when policy allows, a WorkerW-hosted primary-monitor overlay crossfades the **previous** image out (~450 ms, optional blur), then SPI commits the new wallpaper on fade complete (respects Windows via `island.wallpaperRespectSystemAnimation`, default on); otherwise instant SPI. The **`Color`** tab keeps session-scoped recent picks; its **screen picker** is a full-monitor overlay with a cursor-centred 13×13 loupe over the frozen frame (the pick is the loupe's exact centre pixel, `ScreenSampler.TryCropBox`; the HEX goes to the clipboard outside Win+V history). The AppBar stays 30 DIP (ADR-012, ADR-013). Background personalization: `mica` / `transparent` / `tint`
 + opacity slider (right-click menu). The clock is a single line formatted with the resolved UI culture (`config.language` → `LocalizationService`; e.g. `tr-TR` or `en-US`); a clock click is notification text + a month calendar; the bell → native `Win+N`;
 `controlCenter` → QS Main; volume → QS Mixer; brightness → `BrightnessFlyout` (Monitorian DDC/CI + WMI hybrid);
 media → `MediaFlyout` (SMTC; hidden when there is no session, ADR-008); network → QS Network (ADR-006); the energy-saver tile lights from the API/flag or from a read-back of `Always use energy saver`, and a click flips that switch (ADR-013); **AppBar** + tray hybrid.
- **Dock:** bottom strip (overlay; Topmost ± auto-hide). On the left, taskbar pins are **read-only**
 (including closed ones, in taskbar order; ADR-005 2026-10-07 — the folder is **not written**)
 plus entries that are in `dock.items` and not there. A running pin stays on that same single icon. On the right,
 only unpinned running apps. A minimized or background window does not become a second icon;
 the same application (canonical path, AppUserModelId, or pid) is one box, and multiple
 windows/processes are counted on that box. The icon does not grow.
 Default icon **24 DIP**, button 40, gap **4 DIP**, plate padding 10×8 DIP. Background
 `mica` / `transparent` / `tint` (independent of the top bar).
 `Pin to Dock / Unpin` writes `dock.items` and, when the target is a file or folder, writes a `.lnk` into
 `User Pinned\TaskBar`. `Taskband` is not written. Drag-to-reorder, file drop from Explorer,
 bottom-left/center/right and the left edge; right-click context menu (title card, `Pin to Dock`/`Unpin`,
 `End task`, `Close window`, `Run as administrator`, `Open file location`, `New window`,
 **`Windows`**).
 DWM thumbnail on hover; a taskbar progress bar under the icon (no bar when UIA is empty);
 a short accent pulse on an icon that wants attention. The badge, the line, and the thumbnail count only windows on that dock's
 monitor. Launchpad, Downloads, and the trash are built-in tiles
 (not written to the pin folder). Launchpad opens from the dock tile (the themed brand logo tile, ADR-019)
 and from a left click on the top-bar brand logo; right click on the logo opens the menu.
 The face is singular: centered, above the wallpaper; the box opens before the scan finishes, and apps sit under the `All` letter heading (ADR-016).
 The pad is keyboard-first (search focused, arrow keys between tiles, Enter opens, Escape clears then closes);
 the card shrinks to the screen and the phone panel is hidden when it does not fit.
 The Widget and Plugin tools open the gallery; Settings → `Plugins` shows the same list (ADR-017).
 Built-in island switches. A local `manifest.json` is an on/off record only;
 an assembly is not loaded. Top bar and dock slots are not drawn in v1.
 The phone row appears only while a device is connected: name, battery, and notification count from the YourPhone start card; a missing field is hidden.
 Folder stack, minimize snapshot, dock restore reverse cue from the icon/flyout, and Store logo are ADR-014.
- **Taskbar:** hide/auto-hide while WharfBar is running; restore on exit (ADR-005).
- **Monitor:** Per-monitor top bar / dock — each monitor is turned on or off separately through the `monitors` config;
 every bar runs on one shared engine; each bar
 is an AppBar on its own monitor (the `SPI_SETWORKAREA` fallback is dead on 25H2+ — ADR-005 addendum).
 Veil is limited to the primary monitor.
- **No global menu** — the focused app's File/Edit menu is not moved onto the 30 DIP bar.
 WharfBar's own settings tabs are already in the Settings window (ADR-015).
- **UI language:** `system` / `en` / `tr` on **General** (`LocalizationService`, `.resx`); restart to apply (Phase 0).

## Stack (summary)

.NET 8 WPF, WPF-UI, `net8.0-windows10.0.19041.0`, Per-Monitor V2 DPI. `AllowUnsafeBlocks` is not required for basic P/Invoke. Detail: [`context/stack.md`](context/stack.md).

## Architecture summary

- UI folder: **`Views/`** (or `UI/`) — do not use `/Windows` (it collides with Win32).
- Native: `AppBar`, DWM (fallback), `ShellIcon`.
- Services: config, launcher, icon, autostart (Startup folder), Mutex, status (.NET API).
- Config: `%AppData%/WharfBar/config.json` + `schemaVersion`.

Full structure: [`../docs/01-architecture.md`](../docs/01-architecture.md).

## Critical technical facts (do not forget)

1. **`Shell_NotifyIcon` is not reading the system tray.** .NET APIs + WharfBar's own icons for Wi-Fi/battery/volume.
2. **A dock AppBar is not required** — overlay is preferred (ADR-001). The top bar uses an AppBar.
3. **WPF-UI backdrop first**; do not fight it with raw DWM Mica. **`AllowsTransparency=true` + Mica is forbidden.**
4. **AppBar:** `ABM_REMOVE` on exit; no `SHAppBarMessage` inside `ABN_POSCHANGED` (`BeginInvoke`, skip SETPOS when the rect is the same); clamp `WM_WINDOWPOSCHANGING` to the approved rect (phantom taskbar work-area push; the dock clamp does not `Measure`); no `ABM_WINDOWPOSCHANGED` on an approved rect; re-register after an explorer restart; DPI/display handlers.
5. **v1:** the Store logo is resolved with `PackageManager` + `SHLoadIndirectString`, and a letter glyph when the asset is missing (ADR-014); taskbar hide + restore (ADR-005); single-instance Mutex.
6. **Autostart:** Startup folder > Registry Run (`AutostartService` + `AppConfig.autostart`).
7. **Hit-testing + auto-hide** is implemented on the dock (`DockHitTest` + `DockAutoHideController`; `FullscreenState` bridge). 
8. **Status:** clock / network / battery via `.NET` + `GetSystemPowerStatus`; volume via Core Audio; quick settings
 (ADR-006; `controlCenter` → Main, volume → Mixer); the tray has only its own Quit menu.
 Top bar left cluster: brand logo menu (`logoStyle`) + system stats (`GetSystemTimes` /
 `GlobalMemoryStatusEx` / PDH `PdhAddEnglishCounterW`; if there is no GPU counter the GPU section is hidden).
9. **Tray mirror (ADR-009):** foreign tray icons are read read-only with documented comctl32 `TB_*` messages
 + `SendMessage` click-forward; RPM/injection/undocumented APIs are **forbidden**;
 hidden icons are out of scope (`Win+B` remains); system icons are dropped by the tooltip filter.
 On Windows 11 25H2 (no classic toolbar) the mirror reads the XAML tray through UI Automation and
 hover-reveals the parked auto-hide bar for each refresh only while the pointer is idle (or the mirror
 is still empty) — no `ABM_SETSTATE`, no work-area flip. An empty read is not evidence: only a
 revealed tray that read back empty clears the snapshot.
10. **Dock pins (ADR-005, 2026-10-07):** the left group **reads** the `User Pinned` folder
 (it does not write it). `dock.items` remains the pin/unpin path. Hiding/restoring the taskbar
 is the subject of §3. The magnification engine is pure (`DockMagnification` / `DockStripLayout`); the window
 is not resized on hover.
11. **A sub-medium integrity token cannot drive the shell.** UIPI silently drops
 `ShowWindow(Shell_TrayWnd, SW_HIDE)`, `SHAppBarMessage(ABM_NEW/ABM_SETPOS)` and `SetWindowLong`, so a
 WharfBar launched from a sandboxed terminal (or any low-integrity parent) runs with the taskbar
 visible and no top-edge reservation. `ProcessIntegrity` detects this at startup and warns; the fix
 is to launch normally (Explorer, or `run.bat` from a normal terminal). See
 [`docs/04-gotchas.md`](../docs/04-gotchas.md).

## Document map

| File | Contents |
|------|----------|
| `docs/00-overview.md` | Purpose / non-goals |
| `docs/01-architecture.md` | Folders, config schema |
| `docs/02-win32-appbar-dwm.md` | AppBar/DWM/tray/icons |
| `docs/03-development-order.md` | Phases |
| `docs/04-gotchas.md` | Gotchas |

## Current repo state

**Phase 1–9 + Phase 8 + the dock redesign (2026-10-04) code is in the working tree** (manual verification
is pending). Code root is
`src/WharfBar/`, solution at the root `WharfBar.sln`. Working: top bar window (DPI-aware layout),
AppBar registration + guaranteed `ABM_REMOVE`, `ABN_*` callbacks, re-registration after an explorer restart,
single-instance Mutex, WPF-UI Mica + system light/dark (or config `theme`), square corners
(`DWMWCP_DONOTROUND`, ADR-004), dock overlay (`DockWindow`; read-only taskbar pins on the left
+ `dock.items`, unpinned running apps on the right, one icon per application; no magnification,
icon 24 / button 40 / gap 4 DIP, mica or layered tint background, drag-to-reorder, file drop, position choice, process launch / window focus, `Windows`
submenu, live DPI, fullscreen hide, `WorkAreaChanged` reposition, auto-hide +
hit-test (`DockAutoHideController`, `DockHitTest`)), `ConfigService` → `%AppData%/WharfBar/config.json`,
`AutostartService` → Startup folder `WharfBar.lnk` (`AppConfig.autostart`), clock + network + battery
(`StatusService`, top bar right strip; single-line clock following UI culture; **Segoe MDL2** glyphs + overflow +
`controlCenter`), WharfBar's own tray icon + Quit (`TrayIconService`),
taskbar hide/restore (`TaskbarVisibilityService`; keep-hidden watch; restore on exit → `ABM_REMOVE`),
live running apps (`RunningAppsSource`) + minimized windows (`MinimizedWindowsSource`)
→ `DockAppsComposer`, top bar tray overflow button (`TrayOverflowInvoker` / `Win+B`).
Dock default icon **24 DIP** / button **40** / plate **56 DIP** (padding 10×8, the running capsule 8×3 is inside the button), icon gap 4 DIP, the window is the plate size,
flush to the bottom edge of the monitor (`SHGetFileInfo` + `SHDefExtractIcon`, fallback `IImageList` jumbo/extra-large; 16/32 px are not scaled up).
Top bar tooltips are Segoe UI (no leftover MDL2 tofu). Overflow: peek-show on a hidden taskbar.
Left menu: `About` + `Close` (`Application.Shutdown` → restore + `ABM_REMOVE`). **Quick settings**
(ADR-006): a shared flyout from the `controlCenter`/volume/network icon (Main / Mixer / Network);
single device + dropdown + app sessions; network status / Settings. Page
changes inside an open panel are a 220 ms slide+fade (`QuickSettingsPageMotion`); none on the first open. Live network features
are done: live Wi-Fi list + connect and disconnect (`WifiService`, wlanapi.dll; plaintext profile
`protected=false`; the list is empty while the radio is off), classic+LE Bluetooth device list (`BluetoothService`),
night light falls back to Settings when it cannot be written (`NightLightService`,
CloudStore best-effort + session override), native project panel with 4 modes (`ProjectionService`,
`QueryDisplayConfig`/`SetDisplayConfig`), tile animations (in-place pop + color transition).
**Color overlay (veil):** fullscreen color overlay from the QS tile + a global shortcut (Ctrl+F11);
`Overlay` settings page (color/opacity/shortcut).
**Media panel (SMTC):** top bar `media` item (visible only while a session exists) + media
flyout (art, title/artist, `Play`/`Pause` + `Next`/`Previous`, chevron
picker when there are multiple sessions); `MediaSessionService` (Windows SMTC watch + commands + 10 s retry),
`MediaSessionSelector` (most-recently-active policy), `MediaSnapshot`; catalog **v4**
migration (`media` is inserted after `brightness`).
**Tray mirror (ADR-009):** top bar `trayMirror` item (catalog **v5**) — reads third-party icons from the real tray
toolbar (TrayNotifyWnd/SysPager/ToolbarWindow32),
filters out system icons, 4 s poll (the icon pixel is refreshed by a peek on the `TrayIconRefreshPolicy` cadence,
compared by `TrayIconSignature`; crops matted against the taskbar fill by `TrayIconMatte`; an unchanged
snapshot is not published; the work-area-flapping state blip is gated by `TrayStateBlipGate`), click forward
(hover reveal via `TrayAutoHideReveal`, state drop only as fallback). **Windows 11 25H2+ UIA fallback:**
because the classic toolbar was removed, `TrayUiaReader` (SystemTray.NormalButton) + `TrayCapture`
(real icon capture from the parked+cloaked taskbar via PrintWindow; letter glyph on failure) + UIA
`InvokePattern`/peek click.
**Monitor brightness control:**
Monitorian-style DDC/CI + WMI hybrid service (`MonitorBrightnessService`, `MonitorNative`) and
`BrightnessFlyout` (TopBar brightness glyph `&#xE706;`, master `All displays` slider, independent monitor cards, 50 ms debounced background queue).
**System stats:** in the top bar's left cluster, beside the brand logo,
`CPU x% · GPU y% · RAM z%` (`SystemStatsService`, 4 s, off the UI thread; not refreshed when the visible text did not change; `topBar.showSystemStats`
toggle at Settings → `Top bar`). GPU PDH `\GPU Engine(*)\Utilization Percentage`
(`PdhAddEnglishCounterW`) — engine instances are grouped into a physical adapter by LUID;
with `topBar.showGpuPerAdapter` on, multiple GPUs show `GPU0 x% · GPU1 y%` (a single
total when off; no difference on one GPU). If the counter is missing the GPU section is hidden; the first sample is `--`.
Build: `dotnet build WharfBar.sln`. Detail: [`../docs/01-architecture.md`](../docs/01-architecture.md).

## Before implementing

Read [`boundaries.md`](boundaries.md) + the relevant `docs/` file. Do not treat the Cursor rules as the only source.
