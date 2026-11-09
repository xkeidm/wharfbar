# Roadmap

## Public backlog (source of truth)

**What’s next is tracked in [GitHub Milestones](https://github.com/xkeidm/wharfbar/milestones), not in this file.**

| Milestone | Role |
|-----------|------|
| [Shipped through 1.0.0-beta](https://github.com/xkeidm/wharfbar/milestone/4) | Closed history of major work already in the tree |
| [Beta polish](https://github.com/xkeidm/wharfbar/milestone/1) | Manual verification / stability before leaving beta |
| [Post-beta / v1 candidates](https://github.com/xkeidm/wharfbar/milestone/2) | Feature candidates after beta polish |
| [Stable 1.0](https://github.com/xkeidm/wharfbar/milestone/3) | Exit criteria for the first non-beta release |
| [Deferred / out of scope](https://github.com/xkeidm/wharfbar/milestone/5) | Parking lot for deferred directions (open issues; not active sprint) |

- Open work → issues under Beta polish, Post-beta, or Stable 1.0.
- Agents: prefer milestone issues for planning; the completed-phase notes below are optional local context (GitHub Shipped issues are authoritative for history).
- Deferred / forbidden detail also lives in [boundaries.md](../boundaries.md).

## Now (completed)

- Documentation + agent skeleton (Approach B) ✓
- **Phase 1 — Skeleton window ✓** — `src/WharfBar/` scaffold, `WharfBar.sln`, WPF-UI,
 PerMonitorV2 manifest, `TopBarWindow` (DPI-aware physical-pixel layout), Mutex
- **Phase 2 — AppBar ✓** — `Native/AppBar.cs`, NEW → QUERYPOS → SETPOS, idempotent `ABM_REMOVE`,
 `ABN_*` callbacks, re-registration after `TaskbarCreated`, fullscreen z-order
- **Phase 3 — Visuals ✓** — WPF-UI Mica + system theme, `DWMWCP_DONOTROUND`,
 `WindowAppearance` / `Native/Dwm.cs`, ADR-004
- **Phase 4 — Dock ✓** — `DockWindow` overlay (Topmost, no AppBar), pins,
 `ShellIcon` / `AppIconService`, `ProcessLauncherService`, `FullscreenState`,
 `DockAutoHideController` / `DockHitTest` stub, Mica + rounded corners
- **Phase 5 — Config ✓** — `AppConfig` / `ConfigService`, `%AppData%/WharfBar/config.json`
- **Phase 6 — Autostart ✓** — Startup folder `WharfBar.lnk` (`AppConfig.autostart`)
- **Phase 7 — Clock / status ✓** — `StatusService` (clock, network, battery); `TrayIconService` + Quit
- Build is clean; the work area was verified by script (see below)
- **Design approval ✓** — live dock + tray hybrid + taskbar hide/restore (ADR-005).
- **Phase 9 — Replacing the shell (ADR-005) ✓ (code)** —
 - **9a** `TaskbarVisibilityService` — capture (`ABM_GETSTATE` + `Shell_TrayWnd` visibility) → hide → restore
 - **9b** Live pins (`TaskbarPinnedAppsSource`) + running (`RunningAppsSource` / `EnumWindows` + AUMID); `DockAppsComposer` — **revised on 2026-10-04:** the pin source was removed, the dock uses its own `dock.items` list (the "Dock redesign" entry above)
 - **9c** Top bar overflow button (`TrayOverflowInvoker` / `Win+B`)
 - Code is in the working tree; waiting on the user to commit + manual UI verification of 9a/9b/9c
- **Phase 8 — Dock auto-hide / hit-test polish ✓ (code)**
 - `DockAutoHideController` — edge-band poll, show/hide animation, fullscreen bridge
 - `DockHitTest` — `ApplyInteractive` / `ApplyPassthrough` (`WS_EX_TRANSPARENT`)
 - Code is in the working tree; manual verification is pending
- **Monitor brightness control (Monitorian integration) ✓** — `MonitorBrightnessService`, `MonitorNative` (DDC/CI `dxva2.dll` + WMI `WmiMonitorBrightness` hybrid), `BrightnessFlyout` (TopBar `brightness` glyph, master `All displays` slider, independent monitor cards, 50 ms debounced background queue, catalog v2).
- **Dock right-click context menu ✓** — `DockWindow` right-click context menu (application icon/name title card, `Pin to Dock`/`Unpin`, `End task` `Process.Kill`, `Close window` `WM_CLOSE`,
 `Run as administrator`, `Open file location`, `New window`) and 100% screen-width support.
- **Dock redesign — macOS logic ✓ (code; mouse verification pending)** — ADR-005
 revision: the write path is `dock.items`. **2026-10-07:** the left group reads the `User Pinned` folder read-only
 (`TaskbarPinnedAppsSource`); the folder is not written. Pins on the left (including closed ones, the same icon once running), unpinned
 running apps on the right, one box per application; no magnification, plate button rhythm (icon 24)
 (`DockStrip` + pure `DockMagnification` / `DockStripLayout`), a fixed window budget
 (no `SetWindowPos` on hover), drag-to-reorder,
 file drop from Explorer, bottom-left/center/right position, live DPI via `WM_DPICHANGED`,
 `Windows` submenu (from the `MinimizedWindowsSource` cache), a content list on the settings page.
 
 **Live end-to-end verification was not done** — plate modes (Mica / transparent / tint), OLE drop acceptance, and click
 suppression were not tried by hand (limited to the interval calculation; see `docs/04-gotchas.md`).
- **Color overlay (veil) ✓ (code; manual verification pending)** — fullscreen layered overlay (`VeilWindow`, click-through + 280 ms fade), `VeilService` (state + fade + `RegisterHotKey` host + config persistence), QS `veil` tile + global shortcut (default Ctrl+F11, changeable in config), `Overlay` settings page (`VeilPage`; color/opacity/shortcut). Decision: ADR-007.
- **Media panel (SMTC) ✓ (code; manual verification pending)** — top bar `media` item (visible only while a session exists; after `brightness`, catalog **v4** migration) + media flyout (art, title/artist, `Play`/`Pause` + `Next`/`Previous`, chevron picker when there are multiple sessions). `MediaSessionService` (Windows SMTC watch: snapshot + commands + `Changed`/`SessionsChanged` + 10 s retry), `MediaSessionSelector` (most-recently-active policy + `MoveCurrent` ring), `MediaSnapshot` model. Decision: ADR-008.
- **Tray mirror (ADR-009) ✓** — top bar `trayMirror` (read-only mirror with documented comctl32 `TB_*` messages
 + `SendMessage` click-forward; system-icon filter; 4 s poll; hidden
 icons stay on `Win+B`). **25H2+ UIA fallback ✓ (manually verified):** because the classic toolbar
 was removed, `TrayUiaReader` (`SystemTray.NormalButton` filter) + `TrayCapture` (real icon capture via PrintWindow)
 + UIA `InvokePattern`/peek click.
- **System stats ✓ (code; manual verification pending)** — beside the Windows
 logo in the top bar's left cluster, `CPU x% · GPU y% · RAM z%`; `SystemStatsService` (4 s, off the UI;
 `GetSystemTimes` + `GlobalMemoryStatusEx` + PDH `\GPU Engine(*)\Utilization Percentage`
 via `PdhAddEnglishCounterW`; if there is no GPU counter the GPU section is hidden), pure calculation methods
 unit-tested (`SystemStatsTests`, 26 tests), `topBar.showSystemStats` toggle
 (Settings → `Top bar`). **Per-GPU display ✓ (code):** engine instances are grouped into a physical adapter by the LUID in the instance name;
 with `topBar.showGpuPerAdapter` on, multiple GPUs show
 `GPU0 x% · GPU1 y%` (LUID order; it can differ from Task Manager's numbering).
 The Settings → `Top bar` page was refreshed: live preview strip, ToggleSwitches,
 ⋮⋮ drag-and-drop ordering of status items (manual verification pending).
- **Taskbar watchdog ✓ (code + end-to-end verified 2026-08-25)** — the PDH `PtrToStringUni`
 access violation (`PDH_FMT_COUNTERVALUE` 8↔16 byte ABI bug; fixed + ABI test) skipped the managed
 handlers, so the `WharfBar.exe --taskbar-watchdog` child process watches the baseline
 (`%LOCALAPPDATA%/WharfBar/taskbar-watchdog.json`) and brings the taskbar back on a death without restore;
 a stale file self-heals at startup. AppBar reservation is out of scope (explorer restart).
 Force-kill test: the tray is visible in ~0.5 s.
- **Island — clipboard history + dropped files ✓ (code)** — an island in the **center** of the top bar
 (not a separate window: a centered `Border` + `Popup` panel in `TopBarWindow` XAML; a separate
 `Topmost` window was rejected because of z-order/focus/Mica risk). `Native/ClipboardNative`
 (documented clipboard P/Invokes + the `CanIncludeInClipboardHistory` opt-out), message-only
 `ClipboardMessageWindow`, a separate STA read thread, a pure `IslandPruner` core, an atomic
 `IslandStore` (`island.json` + `Drop\` folder; `config.json` is not used), `IslandService`
 lifecycle (750 ms debounce), `IslandFlyout` panel (search + `ScrollViewer` list, keyboard
 `↑↓`/`Enter`/`Delete`/`Esc`) and `IslandPage` settings. Decision: ADR-010;
 
 **Live end-to-end verification was not done** — island/right-cluster overlap on a narrow monitor, the first
 `AllowDrop` (OLE target), and the pulse animation's end value were not measured (limited to the interval
 calculation; see `task-8-report.md`).
 Compact island (ADR-011, 2026-10-07): the pill stays inside the AppBar. Idle is the clipboard; music
 uses `island.musicPresence` (`persistent` default); volume/brightness is a short meter. The panel is a single popup.
- **Plugin catalog (ADR-017) ✓ (code)** — Launchpad Widget/Plugin and Settings → `Plugins`.
 Built-in island switches. A local `manifest.json` is an
 on/off record only; an assembly is not loaded. Top bar and dock slots are not drawn.
- **Launchpad open speed + top-bar logo ✓ (code; manual verification pending)** — the Start Menu
  scan (`LaunchpadCatalog.List` + recent) is captured once into `LaunchpadCatalogCache`
  (TTL 30 min, refreshed in the background), so the pad paints the **full list on the first frame**
  instead of growing in 16-app slices. `LaunchpadWarmup` decodes the cached catalog's shell icons on
  the UI thread in idle slices (8 per pass, first 64; a shared `AppIconService` store with the face).
  The top-bar brand logo now **left-opens the launchpad** (`LaunchpadWindow.Toggle`, same window as
  the dock tile); the app menu stays on **right click** via the existing `ContextMenu`. Face polish:
  eased hover (animated overlay in the flat template, resource-swap fallback), 0.97 press scale,
  secondary-colour letter headers, tile icon/text sizes (44/40 px icons, 12 px labels).
- **Launchpad usability redesign ✓ (code; screenshot-verified with a render harness)** — the shared flat
  button template was missing its content grid, so every tile/tool rendered blank (fixed; a visual-tree
  test now guards it). The face is keyboard-first: search focused, arrow keys over the tile grids
  (`LaunchpadTileNav`), Enter opens, `Esc` clears then closes, typing on a tile returns to search, accent
  focus ring. Search results come first with exact/prefix/word/substring ranking (`LaunchpadCatalog.Rank`)
  and culture-independent I/i folding (`LaunchpadSearch`); the default body is Pinned (≤6) → Recent (4,
  `Show all`) → all apps. Recent drops protocol shortcuts and ` (n)` launch counters. Sizing is
  screen-aware (`LaunchpadLayout`: card 520–680 / down to 360, phone panel hidden when it cannot fit,
  phone notices scroll). Cache loads are single-flight, stale data repaints in one pass, warm-up decodes
  the whole catalog, and a failed Recent read no longer wipes the app list. Tests:
  `LaunchpadRedesignTests`, plus new cache/recent/catalog cases.
- **Island widgets phase 1 — shelf + color picker ✓ (code; manual verification pending)** — the hub
  grows two primary-bar tabs: **Shelf** (`shelf`) — dropped-file chips (ext badge + name),
  `Open with app` (`CanOpenShelfFile` → `OpenShelfFile`), `Remove from list`, `Empty shelf`, and a
  real OLE drag-out per chip — and **Color** (`colorpicker`): a session-scoped recent-colors list
  (`IslandPickedColors`, top 8) + `Screen picker` → `ColorPickerWindow` (work-area topmost, frozen
  BitBlt frame **before** the dim mask, pick → `PutOnClipboard` HEX with the Win+V opt-out). Soft
  config `island.shelfTab` / `island.colorpickerTab` (default on); tab resolve adds the Drop-face
  → Shelf default and a Clipboard fallback for disabled tabs (`ResolveHubTab`). Motion: ratio-lerped
  morph durations (`MorphOpenMs` 230→170 / `MorphCloseMs` 150→110, `BackEase(0.28)`/`CubicEase` on
  the scale axis only). New capture plumbing: `Native/ScreenBitBlt`, `Services/ScreenSampler`,
  `Services/ColorMath`. Tests: `ColorMathTests`, `ScreenSamplerTests`, `IslandPickedColorsTests`,
  `IslandMotionTests`, `IslandClipboardWriteTests`, hub-tab fallback tests.
- **Island widgets phase 2 — prompter reading tab ✓ (code; manual verification pending)** — a sixth
  primary-bar tab, **Prompter** (`prompter`; soft `island.prompterTab`, default on; tab order now
  Clipboard → Wallpaper → Music → Shelf → Prompter → Color, disabled-tab fallback Clipboard in
  `ResolveHubTab`): a multiline reading editor over a control bar — play/pause toggle
  (`Play`/`Pause`), speed slider 0.5–3.0 (live `1.0×` label), `Back to start`, `.txt` load,
  `Take from clipboard` (read-only `IslandService.TryLastClipboardText`). Pure engine `PrompterMotion`
  (66 px/s @1×, 17 DIP reading font, 16 ms tick, play gate `CanStartPlayback`): end-of-content →
  Editing + unpin, manual drag never pauses, the speed is 0 when `ClientAreaAnimation` is off,
  and the play toggle disables with a reason tooltip while there is nothing to scroll (no silent
  dead click; `ScrollChanged`-driven refresh). Playback pin: the panel's
  auto-close is suppressed while Playing (explicit close still wins). The tab strip folds labels
  to icons when all six tabs converge. Tests: `PrompterMotionTests`, `IslandServiceTextPickTests`,
  `IslandConfigTests` / `IslandHubTests` / `IslandFlyoutHubTabsTests` extensions.
- **Island widgets phase 3 — camera tab ✓ (code; manual verification pending)** — a seventh
  primary-bar tab, **Camera** (`camera`; soft `island.cameraTab`, default on; enum now Clipboard →
  Wallpaper → Music → Shelf → Prompter → Camera → Color, and a fourth disabled-tab Clipboard
  fallback arm in `ResolveHubTab`): a rounded preview (`CameraPreviewHost` Height 180,
  size-adaptive 14 DIP clip; `CameraPreviewImage` `Stretch=Uniform`), placeholder + status when
  closed (host off → `Off`; status "Camera" → `Active`; otherwise verbatim, e.g.
  `No camera`), a Camera row with the on/off toggle, and a display-only Microphone row
  (`Active` / `Muted` / `Unknown`; read at `PresentHub` + a 2000 ms timer while the body is
  visible). The toggle raises `CameraMirrorRequested` only; the window handler applies
  `PluginCatalog.SetEnabled` + `CameraMirrorHost.Apply` + config save — the same three steps the
  widget-gallery toggle uses (one `island.cameraMirror` bool). `CameraMirrorHost` keeps being the
  single capture owner; Native adds the read-only `AudioEndpointVolume.TryGetCaptureMute`
  (capture dataflow; render paths byte-identical). Tests: `IslandConfigTests.Defaults_MatchSpec_CameraTab`,
  `IslandHubTests` camera rows (`Normalize_StringValue_ReturnsExpectedTab`, `ToConfig_Camera_WritesCameraId`,
  `Resolve_SavedCamera_MusicAvailable_Resolves`),
  `IslandFlyoutHubTabsTests.TabIdForBody_CameraId_MatchesTag`.
  Manual QA (human, from the phase plan): closed body shows `Off` + placeholder without a
  crash; body on/off syncs the pill face instantly, both directions, together with
  Settings > Widgets; no capture device → `Unknown`, mute/active text follows the system mic;
  `island.json` `"hubTab":"camera"` + `"cameraTab":false` opens on Clipboard; the 7-button strip still
  folds labels by measurement (no count rule); tab switch / panel close / app detach leaves the
  live subscription + mic timer while the pill keeps its life, and a returning tab re-attaches.
- **Screen picker facelift — macOS loupe ✓ (code; manual verification pending)** — the
  `ColorPickerWindow` overlay drops the full-span crosshair + flat swatch card and now covers the
  **full monitor** (was the work area — the dock/top-bar appbar strips cut the loupe off). The
  affordance is a cursor-centred round loupe: a `MagnifierGrid` (13×13) crop of the frozen BitBlt
  frame rendered into a `WriteableBitmap` with nearest-neighbour scaling inside a circular clip,
  hairline cross + centre dot, a bordered 4 px white ring with a softened drop shadow, a glass
  HEX chip (swatch + mono HEX) and hint/status line below. The picked colour is now the loupe's
  exact centre pixel instead of a 3×3 average. New pure seam `ScreenSampler.TryCropBox`
  (centre-clamped BGRA box crop, tight output stride) — tests in `SnapshotMagnifierTests`.
- **Island shelf — drag-target chrome + usability ✓ (code; manual verification pending)** — the
  primary pill is a live drop target: a `FileDrop` drag hovering it grows the pill through
  `AnimatePillWidth`, swaps the glyph for the drop arrow (`E73E`), sets the caption to the
  `Island_DragBadge` string (`Drop → shelf`, localized), draws the accent stroke + glow, and
  collapses the camera frame and both satellites for the duration (a cancel via
  `DragLeave`/Esc restores the frozen face; reduced motion gets static states). A drop where at
  least one record landed fires the swallow micro-motion (1 → 0.96 → 1.05 → 1 + accent flash).
  Shelf usability: dropped folders are records too (same `IslandEntryKind.File`, original path, no
  `Drop` copy, folder badge); Ctrl/Shift chip multi-select with batch drag-out of every selected
  path; a body context menu `Drag all out` (press-and-drag); a hover `×` that removes one
  record; and the chip menu `Show file location` / `Copy path` / `Copy file` (CF_HDROP
  through `ClipboardNative`, hidden for folders). The shipped surface is described above and in
  [`docs/01-architecture.md`](../../docs/01-architecture.md).
- **Tray mirror — quiet reveal + evidence-only clearing ✓ (code; manual verification pending)** — on
  Windows 11 25H2 the parked auto-hide taskbar virtualizes its XAML tray buttons, so the periodic
  pixel refresh now hover-reveals the tray exactly like a click (`TrayAutoHideReveal.TryReveal`:
  bottom-edge hover with real input, cursor restored, no `ABM_SETSTATE`, no work-area change) before
  reading. New pure gate `TrayRevealPolicy` (reveal only while the pointer is idle ≥1.5 s or the
  mirror is still empty, the empty-mirror bypass capped at 3 attempts per shell; 2 consecutive
  misses back off for 10 min) plus a real-fullscreen skip in `ShouldAttemptQuietRefresh`; the loud
  state blip stays the gated fallback (maintenance only in a shell that already blipped). The reveal
  also suppresses WharfBar's own dock edge-band show while it owns the cursor, and restores the mouse
  only if the user has not moved it. Empty reads are no longer evidence: `TrayBlipOutcome` (`Skipped` /
  `Inconclusive` / `ConfirmedEmpty` / `Refreshed`) and a 220 ms confirmation read mean only a tray
  confirmed fully on screen that read back empty clears the mirror (`ShouldHoldSnapshotOnEmptyRead`);
  unnamed/repeated buttons now get index-suffixed keys
  (`TrayMirrorOrder.UniqueKeys`) so de-duplication cannot drop them. Tests: `TrayRevealPolicyTests`,
  `TrayMirrorEmptyReadTests` (rewritten around the evidence rule), `TrayMirrorOrderTests`,
  `TrayButtonMatcherTests`, `TrayStateBlipGateTests`, `IdleCadenceTests`, `TrayAutoHideRevealTests`. Details: `docs/02`/`docs/04` and
  [`docs/01-architecture.md`](../../docs/01-architecture.md).

- **Restricted-launch guard — token integrity ✓ (code; manual verification pending)** — a process token
  below medium integrity (a sandboxed terminal or a low-integrity parent) cannot drive the shell:
  UIPI silently drops `ShowWindow(Shell_TrayWnd, SW_HIDE)`, `SHAppBarMessage(ABM_NEW/SETPOS)` and
  `SetWindowLong`, so the taskbar stays visible under a bar that reserves nothing. New
  `Native/ProcessIntegrity` reads the token's integrity RID (`OpenProcessToken` +
  `GetTokenInformation(TokenIntegrityLevel)`; unreadable → `-1`, never restricted) and
  `App.OnStartup` warns before any shell state is touched — OK continues, Cancel quits. New
  `Startup_RestrictedLaunch_*` strings (en/tr) and `ProcessIntegrityTests`. Details: `docs/02`/`docs/04`
  and [`docs/01-architecture.md`](../../docs/01-architecture.md).

- **Downloadable releases — installer + portable zip + CI pipeline ✓ (code)** — the first public
  downloadable beta: a per-user Inno Setup 6 installer (`installer/WharfBar.iss`;
  `WharfBar-Setup-<version>-x64.exe`, no admin/UAC, Start-menu entry, uninstaller in Settings → Apps)
  and a portable zip (`WharfBar-<version>-win-x64-portable.zip`), both from one self-contained
  single-file win-x64 publish built by `installer/build-release.ps1` (no .NET install needed), plus
  `SHA256SUMS.txt`. `.github/workflows/release.yml` builds the same three assets on `windows-latest`
  from a `v<semver>` tag and publishes a GitHub Release (`gh release create`, generated notes; a
  version with `-` is a prerelease), while `workflow_dispatch` is a dry run without a Release. New
  `--quit` channel (`Program.Main` + `QuitChannel`) lets Setup/uninstall ask the running instance to
  quit through its normal exit path instead of force-killing it; `App.OnExit` releases the
  single-instance mutex last, so exit code `0` means the taskbar and AppBars are restored. Settings
  stay in `%APPDATA%\WharfBar` and survive upgrades/uninstall; the app owns the Startup shortcut.
  The build is unsigned (SmartScreen may warn). Details:
  [`docs/07-packaging-and-releases.md`](../../docs/07-packaging-and-releases.md).

## Former to-do list (moved to milestones)

The P0 / P1 / P2 open items below were filed as GitHub issues. Do not treat this section as the live backlog.

| Former priority | Now tracked under |
|-----------------|-------------------|
| Completed phase / feature history | [Shipped through 1.0.0-beta](https://github.com/xkeidm/wharfbar/milestone/4) (#11–#16, closed) |
| P0 / P1 manual verification | [Beta polish](https://github.com/xkeidm/wharfbar/milestone/1) |
| P2 candidates + later gaps (plugin slots, AppBar crash strip, multi-monitor parity) | [Post-beta / v1 candidates](https://github.com/xkeidm/wharfbar/milestone/2) |
| Exit beta / first stable release | [Stable 1.0](https://github.com/xkeidm/wharfbar/milestone/3) |
| Consciously deferred / parking lot | [Deferred / out of scope](https://github.com/xkeidm/wharfbar/milestone/5) (#17–#22, open) |

### Consciously deferred / forbidden

Tracked as open issues under [Deferred / out of scope](https://github.com/xkeidm/wharfbar/milestone/5). Hard safety rules still apply in [boundaries.md](../boundaries.md) until an issue is promoted and approved.

## Phase 1–3 verification status

Measurement environment: 1920×1080, **100% scale (96 DPI)**, taskbar at the bottom (48 px).

### Verified on the machine ✓

Measured with `SystemParametersInfo(SPI_GETWORKAREA)`, by script:

| Check | Result |
|-------|--------|
| Work area reservation | Baseline `T=0` → `T=30` while running; exactly the expected 30 px |
| Bar window rect | `L=0 T=0 R=1920 B=30` — identical to the reserved strip |
| Single instance (Mutex) | The second process exited on its own (code 0); the work area did not change |
| Graceful close | `WM_CLOSE` → process code 0 |
| **Work area restore** | **Identical** to the baseline |
| Maximized window | Stays under the bar (verified in Phase 2) |

### Phase 3 visual notes

- Mica / translucency cannot be verified reliably from a screenshot (DWM composition + wallpaper);
 it is supported by attribute reads + a non-opaque client, and a human eye is required.
- The light/dark runtime switch must be verified by changing the theme by hand.

Detailed checklist: [`../../docs/03-development-order.md`](../../docs/03-development-order.md)
