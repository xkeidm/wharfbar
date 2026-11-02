# Boundaries — Do / Ask First / Never

## Do (allowed)

- Documentation and `.agent/` updates (within this skeleton's scope)
- Top bar AppBar flow (NEW / QUERYPOS / SETPOS / REMOVE)
- Dock overlay (Topmost, auto-hide, animation)
- JSON config + schema version
- Mutex single-instance
- Startup folder autostart
- Classic `.exe`/`.lnk` icons via `SHGetFileInfo`
- Reading status indicators with .NET APIs (quick settings through real APIs: wlanapi + WinRT Radio/Bluetooth + SetDisplayConfig; detail in docs/02)
- WPF-UI backdrop / theme
- DPI / display change / AppBar callback handlers
- Fullscreen hiding (bar/dock)
- AppBar re-registration after an Explorer restart
- **Hiding / auto-hiding the Windows taskbar while WharfBar is running** — capture the state
  at startup; on the exit and crash paths **restore the previous state** (ADR-005; the same discipline
  as `ABM_REMOVE`). No permanent damage (while WharfBar is closed).
- **Multi-monitor** AppBar: per-monitor top bar / dock (`monitors` config; per-monitor
  on/off), each bar is an AppBar on its own monitor. **The `SPI_SETWORKAREA` fallback is dead on 25H2+**
  (the API returns `ERROR_INVALID_PARAMETER` — the `WorkAreaExpandPolicy` circuit breaker skips it);
  there is no non-AppBar edge reservation until a shell that supports a work-area override ships.
- **Hidden taskbar + maximize coverage (ADR-005 note 5, confirmed again on 2026-09-30):**
  the hide flow issues `ABM_SETSTATE(ABS_AUTOHIDE)` and stays in that state while running —
  Explorer parks the bar in a ~2 px clamp, the bottom reservation leaves the work area, and maximized
  windows cover the full screen. Measured while WharfBar was running: `ABM_GETSTATE=1`, the taskbar
  is invisible, `SPI_GETWORKAREA.Bottom`=1080 → **bottom gap 0**; with the app closed, 1032/48.
  `SW_HIDE` alone is not enough (it leaves a ~48 px ghost). External `SetWindowPos`
  interference with maximized windows is FORBIDDEN (it produces a 7 ms fight with Firefox/WinForms window managers — tried,
  removed).
- **Measurement trap:** `ABM_SETSTATE`/`ABM_SETPOS` take effect **only when a registered AppBar** calls them;
  probing from outside returns `TRUE` and does nothing, which produces a false "the API is dead" result
  (this mistake was made on 2026-09-30). `SPI_SETWORKAREA` really is a silent no-op;
  its circuit breaker (`WorkAreaExpandPolicy`) therefore looks at the **effect**, not the return value.
- **Tray icon capture has to happen inside the PEEK:** while the tray `IsWindowVisible=False`, `PrintWindow(PW_RENDERFULLCONTENT)`
  returns `TRUE` without writing pixels into the DIB → a black bitmap. Alpha 0 is **not**
  the blocker; what decides it is that the peek is open. `TryRefreshViaBlip` captures the icons
  before `EndTrayPeek`. `TrayCapture.IsBlankCapture` rejects an empty crop →
  letter-glyph fallback. If capture is moved outside the peek, the black square comes back.
  The tray mirror cannot be read with UIA on a parked tray:
  reads run from the desktop root on a dedicated STA dispatcher thread. Refresh tries a stateless
  stealth peek first; if icons arrive, `ABM_SETSTATE` is not toggled (work-area jump). If empty,
  show-only latches off and the state blip runs only through `TrayStateBlipGate` (once per explorer
  instance, then ≥10/≥20 min apart with the user idle ≥3 min and no maximized/bottom-touching window).
  **Never** let a click, theme or retry request trigger a state blip; a stale icon beats a jumping desktop.
  Clicks reveal the auto-hide tray by hovering the bottom edge (`SetCursorPos`/`SendInput`, cursor
  restored); the state drop is only the fallback. Flips are counted in the diag log (`state-change #`).
  The show-only refresh runs on its cadence even when the direct read has buttons (60 s / 20 s while
  icons change; one extra refresh after a mirror click or a theme change, floor 15 s). Crops are
  colour-keyed against the taskbar fill (`TrayIconMatte`). One `PrintWindow` per peek; pixels are
  compared by signature and an unchanged snapshot is not published. An empty read does not clear the mirror (the last good
  snapshot is held for a bounded grace). After a force kill, a stale watchdog file restores the baseline state
  (poisoned baseline chain — happened live on 2026-08-30).
- Live dock: the left group **reads the taskbar `User Pinned` shortcuts** (including closed pins,
  in taskbar order) + entries that are in `dock.items` but not on the taskbar + `EnumWindows`
  / AUMID. `Dock’a sabitle / Dock’tan kaldır` (Pin to Dock / Unpin from Dock) changes the `dock.items` list and, when the target
  is a file or folder, writes a `.lnk` into `User Pinned\TaskBar` (ADR-005). The same application (canonical exe path, AppUserModelId, or
  process id; transitive) **is one box** — a second window in the background, or a second process of the same exe,
  does not open a new icon on the right; the count is on that box's badge
- **Dock reorder persistence** (2026-10-22): dragging any left-group tile (taskbar pins included)
  freezes the full visual sequence into `dock.itemOrder` (normalized paths, `DockItemStore`
  owns it). The Windows side stays read-only — `User Pinned` and `Taskband` are never written to
  reorder (ADR-005). Empty `itemOrder` means taskbar order first, then `dock.items` order; a
  settings-page row drag unfreezes that key so it follows `dock.items` again. Stale keys are
  ignored by the composer and drop out on the next freeze
- **Taskbar pin folder** (ADR-005, 2026-10-11): if the `Dock'a sabitle / kaldır` (Pin to Dock / Unpin) target is a file or folder, it writes a `.lnk` into `User Pinned\TaskBar` or deletes the matching shortcut. The path cannot leave that folder. `Taskband` is not written. `TaskbarPinnedAppsSource` keeps reading
- The dock plate does not grow with the icon. `DockMagnification` / `DockStripLayout` stay pure and tested;
  the live strip scale is 1, the window is the plate size, and `SetWindowPos` is not called on hover
  (docs/04-gotchas.md)
- A single "tray overflow" button on the top bar (`Win+B` / chevron) + **tray mirror (ADR-009):** read third-party
  tray icons **read-only with documented comctl32 toolbar messages** (`TB_BUTTONCOUNT`,
  `TB_GETBUTTON` (index; `TBBUTTON` → `idCommand`/`iBitmap`), `TB_GETIMAGELIST` +
  `ImageList_GetIcon`, `TB_GETITEMRECT`, `TB_GETBUTTONTEXT` (command id)) and draw them on the top
  bar + click-forward with `SendMessage`. Icons in the mirror are dragged into the WharfBar
  pill (`topBar.trayOverflowIcons`); the pill is horizontal and draws icons only, the name
  is in the tooltip. An icon that is not in the list stays on the bar. Undo is a drop from inside the pill back onto the right strip.
  Windows' hidden area, which is outside enumeration, opens with the chevron at the end of the pill via `Win+B`.
  **The mirror is visible only on the primary monitor's top bar**
  (the `trayMirror` section is never created on secondary bars). **25H2+ UIA fallback:** when the classic
  `ToolbarWindow32`
  is removed, the mirror falls back to UI Automation (`SystemTray.NormalButton` filter); real icons
  are captured from the parked+cloaked taskbar with `PrintWindow(PW_RENDERFULLCONTENT)` and cropped from the button rect
  (no flash); letter-glyph fallback if capture fails; left click is `InvokePattern`, right/double
  click is a visible peek + a physical click — UIA cannot express right/double click. A right click on a mirror icon is delivered **on button release** (while it is held, the top bar capture swallows the click); `AllowSetForegroundWindow(ASFW_ANY)` immediately before the physical click. Menu contents are the owner's HMENU / flyout — WharfBar does not draw them. If the stealth peek's DWM cloak
  is refused, the **WS_EX_LAYERED + alpha 0** fallback is used (UIA visibility is preserved, no flash);
  if that fails too, the peek does not open. **Popup re-anchor:** after the physical click, a popup window that newly appears around the clicked button
  (`TrayMenuReanchor`) is moved under the mirror icon —
  documented `SetWindowPos` only; no memory read/injection. The match is **geometric** (click
  region + size + `#32768` menu class; fullscreen overlays excluded): every 25H2 XAML tray
  button reports the explorer pid, so matching by pid cannot tell them apart.
- **Island / clipboard history (ADR-010):** documented clipboard P/Invokes
  (`OpenClipboard`/`GetClipboardData`/`SetClipboardData`/`EmptyClipboard`/
  `AddClipboardFormatListener`/`RegisterClipboardFormatW`/`GlobalAlloc`) + a **message-only**
  `HwndSource` listener (the existing `MonitorMessageWindow` pattern) + a separate **atomic store**
  (`island.json` + `Drop\` folder — clipboard history is not written into `config.json`, `ConfigService.Save`
  rewrites the file from scratch on the UI thread) + the existing flyout pattern (`Popup` +
  `AnimateFlyoutOpen/Close`, **no nested `Popup`**).
  - **Respect the `CanIncludeInClipboardHistory` opt-out:** if the format DWORD is 0, no record is taken;
     if the format id **cannot be obtained either**, no record is taken (fail-closed). The `Panoya geri koy` (Put back on the clipboard) write
     writes the same flag, otherwise the island captures its own copy back.
  - `System.Windows.Clipboard` is **not used** — clipboard reads are on a separate STA thread
     (`OpenClipboard` blocks), and file copy/delete is on the STA too.
  - `Clear` does **not delete** dropped file copies (deliberate); only `DeleteDroppedFiles`
     deletes, and it also covers orphans by scanning the `Drop` folder. Delete paths are
     checked against the `Drop` prefix (a hand-edited `island.json` cannot delete the user's file).
  - Clipboard history is visible only on the **primary** monitor's bar (the service is a single instance). The secondary pill is music and meters: no clipboard list, no file drop, and no clipboard satellite.
  - The shelf (`CanOpenShelfFile`) stores a path and can open it with the default app. A dropped
    **folder** is a legitimate record too: the same `IslandEntryKind.File`, the original path, and
    **no `Drop` copy** (new drops do not copy). `CanOpenShelfFile` accepts a real directory (the
    default app opens Explorer); reparse-point directories, device paths, and missing paths are
    rejected — the existing guards are unchanged. `Remove` drops the record and cleans the legacy
    `%AppData%\WharfBar\Drop` copy through the pre-existing
    `ApplySnapshot(deleteDroppedFiles: true)` contract; **the original file is never touched**, and a
    drop made since folder support has no copy, so removing it is record-only. Deleting a file
    outside the `Drop` folder is forbidden; the delete path checks the `Drop` prefix. The shelf
    **Raf** tab renders the timeline as chips (Ctrl/Shift multi-select) and is a drag **source**
    too — a chip, or every selected chip, leaves as a real OLE drag of plain `DataFormats.FileDrop`
    paths (files and folders; the body menu `Tümünü dışarı sürükle` arms the same drag);
    `Rafını boşalt` stays the only mass delete.
  - **Screen color picker (ADR-010 addendum):** screen reads are documented GDI only (`Native/ScreenBitBlt` — `BitBlt` + `GetDIBits`; **no `GetPixel`, no memory read**). The overlay captures its frozen frame **before** the dim mask shows — colors are never sampled through the mask. The overlay covers the primary **work area** topmost while the island panel closes first; flags are `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` + `Dwm.SetSystemBackdrop(None)` (no Mica on a layered window). The pick copies HEX through `IslandService.PutOnClipboard`, which reuses `TryWriteText` and so marks its record `CanIncludeInClipboardHistory=0` (nothing enters Win+V history; paste flow is `Ctrl+V`). Recent colors are session-scoped (`IslandPickedColors`, no disk).
  - **Prompter tab (ADR-010 addendum, phase 2):** reading auto-scroll on the **primary** bar only. The `.txt` loader is a plain `OpenFileDialog` (`.txt` filter; only the user-picked file is read) and `Panodan al` is a **read-only** `IslandService.TryLastClipboardText` over the island history — no clipboard read. While Playing, the playback pin suppresses the panel's auto-close funnel (an explicit close always wins via a one-shot marker); the scroll speed is **0** when `ClientAreaAnimation` is off. Oynat is gated by the pure `CanStartPlayback`: with nothing to scroll (short text, or the scroll at the bottom) the button disables with a reason tooltip — no silent dead click. No new package.
  - **Camera tab (ADR-010 addendum, phase 3):** the camera mirror stays **one capture with one owner** — `CameraMirrorHost.Apply(bool)` is the only start/stop path. The island body raises `CameraMirrorRequested` only (it never calls `Apply`); the top-bar handler and the widget-gallery toggle both land there and both write the same `island.cameraMirror` bool. The microphone is **read-only** (`AudioEndpointVolume.TryGetCaptureMute`, capture dataflow, no write method). No new package.
  - **The compact island stays inside the 30 DIP bar** (ADR-011, ADR-012, ADR-013): idle / now playing / volume-brightness-battery meter / at most two satellite pills do not grow the AppBar, do not open a second `Topmost` window, and do not build a nested `Popup`. During HUD, persistent music and the clipboard satellite can stand together. While the pill is focused, Left/Right selects a satellite. Low-battery threshold `lowBatteryPercent` (default 20, 5–50). The multi-line panel is today's single `IslandPopup`. The clock panel (notification text + month) and the stats sparkline are not on the island pill; the sparkline opens on hover over the text, and the bar stays 30 DIP. The energy-saver tile lights when the API or the system flag is on; a click flips the `Her zaman enerji tasarrufu kullan` (Always use energy saver) switch through UI Automation, the window does not stay on screen, and there is no undocumented COM/WNF (ADR-013).
  - **No new package.**
- **Island hub wallpaper (v1):** apply only through documented `SystemParametersInfo` (`SPI_SETDESKWALLPAPER` / read-back `SPI_GETDESKWALLPAPER` in `DesktopWallpaper`); do not rewrite registry wallpaper style (Fill/Fit/…) in v1. Gallery paths are limited to the bundled `Wallpapers/` directory beside the executable and `%AppData%/WharfBar/wallpapers/` (`WallpaperService.IsAllowedPath`); the hub UI and `AttachWallpaper` run on the **primary** bar only. Optional transition crossfade hosts under the shell **WorkerW** behind desktop icons (`DesktopWallpaperHost` / Progman `0x052C`) so icons and chrome stay visible; if WorkerW is unavailable the apply is instant. No restore of the previous wallpaper on WharfBar exit; no per-monitor wallpapers in v1. **Wallpaper accent (optional):** when `island.wallpaperAccent` is on (default), after a successful hub apply WharfBar updates its own accent via `ApplicationAccentColorManager.Apply(..., systemGlassColor: false, systemAccentColor: false)` and **`SystemAccentWriter`** best-effort writes undocumented HKCU `Accent` / `DWM` keys + `WM_SETTINGCHANGE` (`ImmersiveColorSet`); no `explorer.exe` restart; **no restore on exit**; SPI remains wallpaper-only (accent is not part of the SPI path).
- **Wallpaper transition overlay (v1):** `WallpaperTransitionWindow` crossfades previous→next **under WorkerW**, then SPI commits when the fade ends (instant SPI if WorkerW/decode fails). Window flags: `WindowStyle=None`, `AllowsTransparency=true`, `ShowActivated=false`, no taskbar, **not** topmost, **not** on the AppBar/Mica window. **Must** create the HWND at 1×1 off-screen, `SetParent` to WorkerW, then grow to the WorkerW client — a top-level monitor-sized `Show()` triggers `ABN_FULLSCREENAPP` and collapses the dock. Teardown hides/shrinks before unparenting for the same reason. SPI remains the **only** apply path; no `IDesktopWallpaper` / undocumented COM. Default **`island.wallpaperRespectSystemAnimation=true`**: skip the overlay when `SystemParameters.ClientAreaAnimation` is false unless the user turns follow off. Config: soft `island.wallpaperTransition` (`off` \| `fade` \| `blurFade`, default `blurFade`) + `island.wallpaperRespectSystemAnimation`; no new packages.
- **Local plugin catalog (ADR-017):** `%AppData%/WharfBar/plugins/<folder>/manifest.json`
  is read. `plugins.enabled` is an on/off record. Built-in island switches (visualizer,
  Bluetooth notice, camera mirror) and the desktop clock/calendar work. Top bar and dock
  slots are not drawn in v1. An assembly is not loaded, and no code comes in from the internet.


## Ask first (user approval)

- **UWP/Store** icon support (the full AppxManifest path)
- Autostart via Registry `Run` (the Startup folder is preferred)
- Turning on `AllowUnsafeBlocks`
- Making raw DWM Mica primary instead of the WPF-UI backdrop
- Making the dock plate an AppBar (ADR-001; a sibling
  `ABE_BOTTOM` window that reserves the visible dock's strip is allowed — `DockWorkAreaReservation`, 2026-10-10)
- Adding an external dependency (new packages other than WPF-UI)
- Adding `global.json` / pinning the SDK version (the machine has only the .NET 9 SDK; pinning
  to 8.x breaks the build)
- A breaking config schema change (without a migration plan)
- Making the taskbar-hide strategy permanently other than the default, or trying it without restore

## Never

- **Skipping `ABM_REMOVE`** on exit paths (leaving a registered AppBar)
- **Not restoring the taskbar state** on exit / crash paths (leaving it hidden / auto-hide)
  — ADR-005
- **Mica/DWM backdrop together with `AllowsTransparency=true`**
- "Solving" system / third-party tray icons **with `ReadProcessMemory`, injection/subclass, or
  an undocumented API** — `Shell_NotifyIcon` is only for WharfBar's own icon.
  (A read-only mirror with documented comctl32 `TB_*` messages is allowed by ADR-009.)
- Permanently breaking the taskbar without asking the user (including while WharfBar is closed)
- In v1, leaving the UWP icon path "half" done and shipping a broken experience
- Pasting the whole Win32 guide into Cursor rules (the canonical source is `.agent/` + `docs/`)
- Committing secret keys / personal config
- Silently reversing an architecture decision without updating the documentation
- Writing the `Taskband` value. Writing a `.lnk` outside `User Pinned\TaskBar`, or deleting a file outside that folder in the name of unpinning (ADR-005)
- Loading an assembly from a plugin manifest, downloading code, or starting a process from that record (ADR-017)
