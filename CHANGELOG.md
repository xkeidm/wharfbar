# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
as described in [CONTRIBUTING.md](CONTRIBUTING.md).

## [Unreleased]

### Changed

- **The volume mixer shows the mute state on the application icon.** A muted session dims its icon
  to 45 % and draws a diagonal slash across it (a theme-following stroke), instead of leaving a
  full-brightness icon next to a disabled slider. An application whose icon cannot be read now
  falls back to the same letter tile the dock/tray use, so the row still reads as that app; only a
  session with no owning process (`System sounds`) keeps the speaker glyph.

### Fixed

- **The Quick settings panel no longer changes width when switching pages.** The flyout sized to its
  content (360–380 DIP), so the volume/mixer page came out ~19 DIP wider than Main and the panel
  jumped sideways on every page change. The flyout now has one fixed 360 DIP width for every page (the
  long output-device name trims with an ellipsis instead of widening the panel).
- **The top bar keeps its reserved strip after an `explorer.exe` restart.** The `TaskbarCreated`
  re-register path skipped `ABM_SETPOS` because the approved rect matched the one committed before
  the shell died — but the fresh shell has no approved rect for the window, so the work area top
  stayed at `0` and the bar floated over the desktop until the next placement change. Re-registering
  now forgets the last approved rect first (the dock reservation already did this). Verified live by
  killing `explorer.exe` with the shell running: work area `0,30,…` within a second.
- **A display change no longer leaves a bar or the dock on the old monitor geometry.** Each window
  captured its `MonitorInfo` at construction, and placement, the dock's bottom AppBar reservation and
  the per-monitor window filter read that copy — a resolution/arrangement/DPI change therefore kept
  the window on the stale bounds until a restart. `MonitorManager` now re-points every existing bar
  and dock at the live entry after a display change (`MonitorWindowSync.ShouldRepoint`), the dock
  re-applies its placement and reservation, and the dock composer re-filters running windows against
  the new bounds.
- **The taskbar watchdog now also releases the top-bar AppBar reservation after a force kill.**
  The baseline file records the HWND of every per-monitor top bar (`appBarHwnds`) and the watchdog
  replays `ABM_REMOVE` for them before restoring the taskbar, because a dead process cannot run it
  and `ABM_REMOVE` is matched on the hwnd, not on the caller. Measured 2026-11-15 on Windows 11
  25H2 (build 26200): the shell already drops a dead window's registration by itself — a force kill
  left no ghost strip and the work area was back to the baseline within ~1 s — so the replay is a
  no-op there and the documented "restart explorer.exe" advice is now a last resort. Handing the
  watchdog a live foreign registration proved the replay releases the strip.

## [1.0.0-beta.6] — 2026-11-15

### Added

- **First-run setup:** a fresh install no longer drops you straight into the shell. WharfBar now opens a
  setup wizard over the running bar and dock — welcome, look and feel (theme, accent, logo), top bar items
  and background, dock position/size/background, wallpaper, a how-to-use tour and a finish page (start with
  Windows, language). Because the shell is already up, every choice previews on the real top bar, dock and
  desktop behind the window. `config.onboardingCompleted` (soft field) marks it done; an existing config from
  an older version, or one reset after corruption, counts as completed, so only a new install sees the wizard.
  Settings → General → **Run setup again** reopens it, and `WharfBar.exe --setup` forces it for QA.
  Steps change with a Windows-style page transition: the whole page slides in from the right on Continue
  and from the left on Back (220 ms, ease-out, fading in, and no slide when Windows animations are off).
- Turkish + English copy for the setup wizard (`Onboarding_*` strings) and a first-run marker test suite
  (`OnboardingConfigTests`).

### Changed

- A **light/dark switch is now one coordinated crossfade of the whole shell.** Every top bar, dock,
  Settings, setup wizard and gallery surface is captured, the theme is swapped underneath, and the old
  frame fades out over 180 ms (instant when Windows client-area animations are off). The palette change
  no longer happens in visible steps per window, and the switch no longer freezes the shell while Windows
  is notified of the new personalization theme. The wallpaper theme-partner fade starts only after the
  shell crossfade, and a system-driven switch (`theme=system`) goes through the same path.
- `theme=system` now actually follows external Windows light/dark flips: WPF-UI's cached system theme is
  refreshed from the registry before the resolved theme is compared, so an OS flip is no longer silently
  ignored (previously the shell kept the previous palette until an in-app change).
- The Appearance settings page and the setup wizard share one theme/accent/logo apply pipeline
  (`Services/AppearanceApplier`), so both behave identically (including Windows personalization writes).
- **The default status strip now follows Windows' tray corner** (catalog v6): tray mirror → hidden icons
  → volume → brightness → network → battery → quick settings → clock → notifications, with **volume and
  brightness on by default**. `media` stays in the catalog but off. The setup wizard's Top bar step lists
  the items in that order. A saved list that is still exactly the old untouched default is migrated once
  (`TopBarStatusItemIds.LegacyDefaultOrder` / `IsLegacyDefault`); an order the user reordered or toggled is
  never rewritten.

### Fixed

- **The desktop icon arrangement no longer changes when WharfBar opens.** Reserving the top bar and dock
  changes the monitor work area, and Windows 11 re-lays-out the desktop icons on every work-area change:
  25H2 re-expresses the layout on the new grid (the icon pitch changes too, 76×91 → 76×90 after the 30 DIP
  bar) and the multi-change startup (taskbar strip release + top AppBar + dock AppBar) could re-pack the
  whole desktop into columns, losing the user's arrangement for the session; a raw position write does not
  stick while the work area differs. A new `DesktopIconLayoutGuard` captures the layout before the first
  work-area change as monitor + grid cell through the documented shell view (`IFolderView`; no explorer
  memory), watches the per-monitor work areas, re-projects the arrangement once the shell settles (a drag
  made just before a change is adopted), and merges edits made during the session into the restored work
  areas on exit. It stands down for auto-arranged / off-grid layouts and when the shell view is unavailable.
  Multi-monitor layouts are projected per monitor.
- Hovering the system-tray icons no longer changes the background of the whole tray area. The mirrored
  tray strip painted a hover plate of its own on top of the per-icon plate, so the pointer anywhere in the
  strip — including the gaps and the outer padding — lit the entire block with a double wash (measured:
  rest 28 → strip 55 → strip + icon 79 luma). The strip is a container now (`StatusFrame`: same hit area,
  cursor and drag slot, no hover trigger) and the highlight belongs to the icon under the pointer, as in
  Windows.
- The top bar's background now always stays the selected `topBar.backgroundMode` (`mica` / `transparent`
  / `tint`). WPF-UI repaints `Application.MainWindow` on every theme apply — and the main window is the
  primary top bar — so a theme/accent apply from Settings, the setup wizard or the gallery could leave a
  transparent bar with an opaque fill or a stray Mica material behind it (the reported "the background
  changes by itself"). Theme applies now never touch the bar (`ThemeApplyGuard` detaches the main window
  from the library), the bar re-asserts its mode on every library theme apply, a backdrop read-back clears
  a stray Mica attribute in the same frame, and a background watcher corrects any other foreign write
  immediately. A rebuild also hands `Application.MainWindow` to the replacement bar before the old one
  closes, removing a window in which the bar's compensation could not run.
- Window personalization writes no longer run their blocking system-wide broadcast on the UI thread
  (`SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, "ImmersiveColorSet")`): a theme or wallpaper-accent
  change used to wait for every top-level window in the system and could re-enter WharfBar's own window
  procedures mid-switch. The registry values are still written; only the notification moved to the thread
  pool.
- The setup wizard no longer stalls for about half a second when the wallpaper step opens (and again when you
  come back to it): every visit built a fresh step that synchronously decoded the 20 bundled photos at 336 px
  in its constructor, 450–600 ms of work that landed inside the page transition. Tiles now paint immediately
  and the thumbnails decode in the background, two at a time, into a frozen per-file cache shared by every
  visit (bounded at 96 entries); the wizard prewarms the gallery 2 s after it opens, so the step normally
  appears with every preview ready. A step change now costs a few milliseconds of page construction plus
  layout instead of that decode.
- `Strings.Designer.cs` had drifted from `Strings.resx`: seven keys (`Appearance_Logo_Section/Title/Description/
  WharfBar/Windows`, `Island_WallpaperAccent_Title/Description`) had no generated accessor, so `{x:Static}`
  could not use them (markup compile MC3011).
- The setup wizard's Dock and Top bar steps no longer crash while XAML is still being parsed: a slider's
  `ValueChanged` fired before the value label existed, which aborted the whole window load.
- The top-bar CPU reading no longer lags Task Manager. CPU/RAM now sample every second (the GPU PDH wildcard and network counters keep their 4 s cadence), so the CPU number is a 1 s average instead of a 4 s average that could be up to 4 s stale — that window made the bar show a high value after the load had already dropped, while Task Manager's 1 s total showed the current figure. The counter itself is unchanged: `GetSystemTimes` busy share, the same `% Processor Time` figure Task Manager's total tracks (`% Processor Utility` is frequency-weighted and was never displayed). The hover card's sparkline therefore covers the latest ~24 s (one point per second) instead of the previous ~96 s.

## [1.0.0-beta.5] — 2026-11-12

### Added

- A bare **Windows key tap opens the Launchpad** (a second tap closes it) while every `Win+<key>` shortcut keeps working. The shell owns the Win-key hotkey (`RegisterHotKey(hwnd, id, MOD_WIN, 0)` reports "already registered"), so a low-level keyboard hook swallows both physical Win key events and synthesizes the key only when a chord key arrives — which also keeps the Start menu from opening on the tap and leaves the keyboard state clean. Injected input is untouched, so the app's own `Win+Tab` / `Win+B/N/P` gestures still work (ADR-020).
- A **Task View** built-in dock tile (`builtin:taskview`, "Task View" / Turkish "Görev Görünümü") right after Launchpad. A click toggles the Windows Task View with a synthesized `Win+Tab`; the themed glyph follows light/dark and accent changes like the Launchpad brand tile. It is not written to `dock.items` or the User Pinned folder.

### Changed

- The top-bar brand logo **no longer opens the Launchpad**: left click now opens the logo menu (the same menu as right click, anchored under the icon; click elsewhere or press Esc to close). The Launchpad still opens from its dock tile and from the Windows key.

### Fixed

- A window dragged under the top bar can no longer stay there: the AppBar only shapes the work area (maximize/snap respect it, a manual drag does not) and a `transparent` bar is see-through, so nothing covered the strip. `TopBarStripGuard` now enforces it: out-of-context `EVENT_SYSTEM_MOVESIZEEND` / `EVENT_OBJECT_SHOW` hooks push a normal window back below the bar after the mouse is released (one `SetWindowPos`, no size/z-order/activation change, fullscreen/tool/own windows excluded), plus a sweep at startup, on display change and on a top-bar rebuild. Nothing is painted over the strip, so a `transparent` bar stays see-through and the live wallpaper (slideshows and animated walls included) is always exact.
- Light mode: the short dock line under a minimized app stayed in the dark-theme colour (white on a light plate, effectively invisible) because the brush was read once from the theme dictionary; the dock running/minimized indicator and its progress lane now use live resource references and follow light/dark/accent immediately.
- A pinned light (or dark) theme could be silently overridden by the system palette: the settings window (and the gallery) used WPF-UI's `SystemThemeWatcher`, which applies the SYSTEM theme on the first watch and on every later OS theme/colorization change — the Appearance page kept showing "Light" while the whole app ran dark. Both windows now re-resolve the configured theme (`AppThemeWatcher`).
- Switching the theme from Settings to "System" re-applies the dock chrome explicitly; the dock's DWM backdrop no longer keeps the previous dark/light tint until the next system broadcast.
- Dock window-preview cards use the theme's subtle fill instead of a hardcoded white wash (invisible on the light card).
- The island panel no longer snaps open at full size: `Popup.Opened` is raised after the popup's first layout pass, which already starts the grow-from-the-pill reveal, and the Opened handler was cancelling that live morph as if it were a stale close — the panel jumped to its final scale/opacity in one paint, so the documented ~200 ms grow was never visible. The window now tracks the open morph and only drops a stale close.
- The island pill/satellite tooltip no longer opens over the hub tab strip: the 150 ms hover that opens the panel can beat the tooltip's own delay, and `ToolTipService.SetIsEnabled(false)` only blocks later requests — an instance that was already open stayed on screen. Tooltips are now refused through `ToolTipOpening` while the pill is joined, and an open instance is dismissed when the panel opens.
- Clicking the island pill while its panel is open now works over the whole pill: the popup window starts 12 DIP above the panel top and covers the pill's lower half, and its shadow pad had no WPF hit target, so the click was swallowed (and pad clicks leaked to the app behind). The popup child now has a 1/255-alpha hit surface: a click over the pill runs the pill's own activation (pin/toggle) and a click in the shadow pad is a light dismiss. The bar is activated on mouse-up — activating on mouse-down made the same click's up event run the pill click a second time and toggle the pin straight back off.
- The merged pill-and-panel edge stays correct while the pill morphs: the concave fillets were built only on popup alignment, so a centered width change (hover, a title/HUD face, satellites) left the notch at the old pill width (a volume HUD left it ~40 DIP too narrow). The join is now rebuilt for the pill's new edges without moving the panel.
- The island hover panel no longer gets stuck after the pointer has been over another app: the geometric hover sample used `Mouse.GetPosition`, which is the last position this process received and goes stale (a click-close left it reading "over the pill", so the panel would not re-open and a leave never armed the close grace). It now samples the real cursor (`GetCursorPos`).
- A close that is still animating no longer tears the panel down when the presence engine emits `Panel=None` twice, and a panel presented again while the close animates (pointer back / a click pins it) cancels that close instead of closing and re-opening (a blink).

## [1.0.0-beta.4] — 2026-11-10

### Fixed

- The tray mirror no longer stays empty for the whole session when the bootstrap state blip is deferred by a maximized window on the primary monitor: with the bar empty the blocked path never spent an attempt, so the only read could be deferred forever (2026-11-10 field report: dual-monitor PC, `state-blip blocked reason=Bootstrap attempt=0 (maximized/affected window)` repeating every 4 s, no tray icons on the bar). A never-filled bootstrap blocked for more than `TrayStateBlipGate.MaxBlockedBootstrapWait` (30 s) now runs one forced blip (`state-blip forced after Ns blocked …`), and the bounded attempt series still caps the work-area reflows.
- The tray mirror's blocking-window checks only count windows the user can see: cloaked windows (other virtual desktops, suspended UWP), tool/no-activate overlays and WharfBar's own windows no longer defer the bootstrap read, and the blocked log names the culprit (`class='…' title='…'`) so the window can be found.
- The fullscreen-foreground check now measures the primary monitor instead of the foreground window's own monitor, so a fullscreen app on a secondary monitor can no longer block the primary tray read silently; the skip is logged once per transition (`state-blip skipped: fullscreen foreground on primary …`).

## [1.0.0-beta.3] — 2026-11-09

### Added

- Left click on the top-bar brand logo opens the Launchpad (the same window the dock tile toggles); the app menu stays on right click.
- Windows-style Show Desktop sliver at the right edge of the top bar: the first click minimizes every open window, the second restores exactly what it put down (windows closed in the meantime are skipped; windows the user minimized themselves are never touched).
- Island hub **Shelf** (`shelf`) tab: dropped files stay as draggable chips (extension badge + name); `Open with app` opens with the default app, `Remove from list` removes one, and `Empty shelf` empties the shelf (delete paths stay inside the `Drop` folder).
- Island hub **Color** (`colorpicker`) tab: a session-scoped recent-colors list plus `Screen picker` — a click on the screen copies the color under the loupe as HEX (never sampled through the dim mask; the value does not enter Win+V history, paste flow is Ctrl+V).
- Island hub **Prompter** (`prompter`) tab: a reading auto-scroll view — paste (`Paste from clipboard` takes the newest text of the island history), load a `.txt`, or type, then Play/Pause with a 0.5–3.0× speed slider and `Back to start`; the panel stays open while playing, and reaching the bottom returns to the editor. Play disables with a reason tooltip while there is nothing to scroll (the text fully fits, or the scroll is already at the bottom) instead of silently doing nothing.
- Island hub **Camera** (`camera`) tab: a live preview of the camera mirror (rounded, with a placeholder + status while it is closed), a Camera row with an on/off toggle, and a display-only Microphone row. The toggle rides the same `island.cameraMirror` switch as the widget-gallery toggle — one capture, one owner (`CameraMirrorHost.Apply`), requested from the body through an event.
- Island shelf accepts dropped folders too: same record, folder badge, opened in Explorer from the original path (the `Drop` copy stays inside the app folder).
- Shelf chips support Ctrl/Shift multi-select with a batch OLE drag-out of every selected path, a hover × to remove one record, and a chip menu with `Show file location`, `Copy path`, `Copy file` (hidden for folders) and `Open with app`; `Drag all out` on the shelf body starts a press-and-drag batch.
- The island pill is a live drop target: a file drag hovering the primary pill grows it, swaps the glyph for the drop arrow, shows the localized `Drop → shelf` badge and the accent/glow stroke; a drop where at least one record landed plays a swallow micro-motion.
- Dropping onto the pill opens the **shelf under the pill**: after the drag chrome lands, the island panel previews the Shelf tab (grow-from-the-pill open animation), the shelf lights up with a pulsing accent drop-target glow, a landed drop keeps the panel open with the new chip sliding/fading in, a canceled drag closes it with the normal close animation, and the open preview panel accepts the drop too (the pill stays the guaranteed target).
- Screen picker is now a macOS-style loupe over the **full monitor**: a round cursor-centred 13×13 magnifier of the frozen frame, a hairline cross + centre dot, and a glass HEX chip; the pick is the crop's exact centre pixel (up to 6 px from the cursor when the crop clamps at a frame edge).
- Restricted-launch guard: when the process token is below medium integrity (a sandboxed terminal or a low-integrity parent launched WharfBar), Windows silently drops the taskbar hide and the AppBar reservation. Startup now detects it with `ProcessIntegrity` and warns — OK continues, Cancel quits — instead of leaving a taskbar that stays visible under a bar that overlaps the desktop.
- Island music panel redesign: the panel is tinted from the album art as a full-silhouette gradient (the old body-clipped radial wash is gone), the cover is larger with an album-tint bloom and a crossfade on track change, and the player has a Now Playing header with a state dot, an app-source chip with session chevrons, a designed seek timeline (rounded track, tinted fill, thumb; the default Windows slider chrome is gone) and animated play/pause and transport interactions. All motion respects Windows "Animation effects".
- Downloadable Windows releases: a per-user installer (`WharfBar-Setup-<version>-x64.exe`, no admin/UAC, Start-menu entry, Add/Remove entry) and a self-contained portable zip (`WharfBar-<version>-win-x64-portable.zip`) published to GitHub Releases by a tag-driven workflow (`.github/workflows/release.yml`). The same assets are reproducible locally with `installer/build-release.ps1`, and `SHA256SUMS.txt` ships beside them. Settings stay in `%AppData%\WharfBar` across upgrades and uninstall.
- `WharfBar.exe --quit`: a graceful cross-process shutdown for the installer and uninstaller (exit `0` = quit or no instance, `1` = still running / older build, `2` = error). It signals the running instance to take the normal exit path — taskbar restore plus AppBar removal — instead of a force-kill that would leave the reserved top strip on screen until Explorer restarts.
- Dock hover preview is a real window picker: hovering a dock box shows a card per window with a still preview and its title (up to 8, four per row, open windows before minimized), and clicking a card — the preview or the title — activates or restores that exact window. Single-window apps get a card too. It fixes the case where several File Explorer windows collapsed into one box and no window could be chosen from it.
- Dock window cards work for minimized windows as well: a minimized window is placed from its restored rectangle instead of the `-32000` iconic point, so it no longer falls out of the hover cards and the right-click `Windows` submenu. It shows the frame captured when WharfBar saw it minimize, otherwise the app icon with its title.

### Changed

- Tray mirror clicks are now **cursor-free**: clicking a mirrored tray icon no longer moves the mouse pointer to the bottom edge. Left click replays `InvokePattern` on the button element cached with the mirror snapshot (double = two invokes), right click uses verified keyboard focus + the Menu key (`VK_APPS`, `Shift+F10` fallback), and the owner's popup is moved under the clicked icon. A cached UIA element keeps working while the hidden taskbar virtualizes its buttons — measured on 25H2 — so the common click needs no tray peek, no `ABM_SETSTATE` flip and no pointer input; if the element died, one bounded repair round unparks the tray, re-reads the buttons and replays the gesture. When the menu owner warps the pointer to its real tray icon to anchor its menu (measured 0–20 ms after the gesture), the jump is prevented with a short cursor clip (`TrayPointerGuard`, released after `ClipHoldMs = 150` ms) instead of undone afterwards. The mirrored icons stay stable: an empty read never clears a populated mirror, and the quiet hover-read that used to move the pointer to the bottom edge is gone — reads are gated state blips like the bootstrap one.
- The screen picker overlay covers the whole monitor (was the work area) so the dock/top-bar strips no longer cut the loupe off.
- The screen picker keeps the loupe on the cursor and flips its HEX/hint card above the loupe when the bottom of the monitor leaves no room (instead of the pair being clamped up and the loupe drifting away from the pointer); the 13×13 magnifier grid is sized in whole device pixels per DPI so its cells stay uniform, and the first paint samples and places the affordances at the cursor before the mouse moves.
- Launchpad opens at full size on the first frame: the Start Menu catalog + recent files are captured once into a process-wide cache (30 min TTL, refreshed in the background) instead of growing in 16-app slices during an open.
- Launchpad shell icons are decoded ahead of time in idle slices after startup (the whole cached catalog; one shared icon cache for the warm-up and the face).
- Launchpad redesign for keyboard and small screens: the search box is focused and results come first (exact → prefix → word-start → substring ranking), the default body is Pinned (up to 6) → Recent (4 with `Show all`) → all apps under letter headings, arrow keys move between tiles with a visible accent focus ring, Enter opens the focused tile, `Esc` clears a query before closing, and typing on a focused tile returns to search. The card shrinks to the screen DIPs (520–680 wide, down to 360 tall), the phone panel is hidden when the card plus panel cannot fit, and the phone notice list scrolls inside its panel.
- Launchpad Recent drops Windows protocol jumps (`ms-*`, `name:`) and their ` (54)` launch counters, keeps the newest entry per document, and accepts `*.url` Start Menu shortcuts beside `*.lnk` (Steam/web apps) while skipping the Startup folder.
- Launchpad footer tools are grouped (folders | system | power) with separators and the grid/list toggle is a compact icon button instead of a text link.
- Launchpad face polish: eased hover fill (animated template overlay, with a resource-swap fallback), 0.97 press scale on the flat buttons, secondary-colour letter headers, larger tile icons (44/40 px) and 12 px tile labels.
- Island hub tab resolve: when the latest pill face is a file drop, the panel opens on the Shelf tab; a saved `shelf`/`prompter`/`camera`/`colorpicker` tab whose config flag is off falls back to Clipboard.
- The island pill's trailing music equalizer (five bars) and its `island.visualizer` gallery switch are removed; an old config that still carries `island.visualizer` loads and ignores the field. The now-playing pill is just the cover/thumbnail and the track title, and it no longer carries the clipboard satellite (HUD faces keep their satellites).
- Island album tint: gray and dark covers now tint the panel from their representative color (vibrant average fallback + lightness remap) instead of falling back to the system accent. Wallpaper accent keeps its own saturation gate so a gray wallpaper still does not repaint the Windows accent.
- Island hover panel stability: the pointer can rest on the pill or on the popup's transparent shadow pad without the hover-opened panel closing and reopening, and a hover-only pill-width change no longer nudges the panel horizontally.
- Island album tint reaches the whole indicator: the panel gradient starts at the fillet tips/join instead of staying transparent at the top, the joined pill fill is the same tinted surface (`FlyoutSurface.CreateTinted`) so there is no seam, the closed capsule carries a subtle album tint while a session exists, and the music play/pause circle uses the album color (hover lifts toward white; the glyph switches between white and near-black by luminance) instead of the system accent. The `0x28/0x32/0x60/0x8A` alpha ladder keeps white primary text at AA contrast (IslandTintTests).
- Exit order: the single-instance mutex is now released after the taskbar/work-area restore, so `--quit` (and therefore the installer) observes a clean shell before the binary is replaced.

### Fixed

- Esc cancels the screen picker again: the overlay is deliberately not OS-activated, so it never received `WM_KEYDOWN` and the old `Keyboard.Focus` path could not fire. A bare `VK_ESCAPE` global hotkey is now registered only for the overlay's lifetime (released on settle/close), with the keyboard-focus path kept as the fallback when the registration is refused.
- The dock hover preview no longer draws title-only rows: the cards registered DWM thumbnails on the flyout's child HWND, which `DwmRegisterThumbnail` always rejects with `E_INVALIDARG` (a layered window and a child window are both invalid destinations), so no preview ever appeared and clicking the picture area did nothing. Each card is now a `PrintWindow` frame in a plain WPF image with the whole card as the click target; the system tooltip is also suppressed before it can land over the cards.
- The top-bar tray mirror no longer blanks itself for the whole session after a bad start: the state-blip bootstrap verifies that the parked auto-hide tray actually came on screen (`ABM_SETSTATE` is asynchronous; a read of the still-parked, virtualized tray says nothing about it being empty) before it reads, an unverified unpark is `Inconclusive` instead of `ConfirmedEmpty`, and a mirror that was never filled gets a bounded retry series (3 blips, 30 s apart) that a verified-empty tray stops at once. Every refresh attempt logs its outcome, and the chevron tooltip explains an empty mirror ("Reading tray icons…" / "No tray icons on the taskbar — open Windows' hidden icons (Win+B)").
- A mirrored tray icon click no longer opens the menu at the bottom edge or fails silently on right click: the owner's popup is re-anchored under the clicked icon, and only a real popup or a `#32768` menu may be moved — a normal framed application window can no longer be teleported to the top bar (the diag showed `MonitorianPlus` / `Chrome_WidgetWin_1` frames moved to y=33). Cloaked/hung/fullscreen/own-process windows are rejected, and a process-wide single-watcher gate with per-window ownership stops two overlapping clicks from fighting over the same menu.
- The tray mirror no longer clears a populated snapshot on an "empty" read: only a new `Shell_TrayWnd` (explorer restart) resets it. A read of a tray that explorer virtualized — including a false empty measured while WharfBar's own fullscreen colour picker covered the monitor, which now also skips the state blip — keeps the last good icons. This is the stability fix behind the mirror icons disappearing.
- The island panel keeps its album tint across hub bodies: the tint is session-scoped (`SessionTint`) instead of being cleared on every non-Music tab, so Clipboard/Wallpaper/Shelf/Prompter/Camera/Color no longer lose the color the Music body has (and a secondary bar can no longer show a different tint than the primary for the same session).
- The closed island capsule no longer loses the album hue on a light wallpaper: the tint layer is normalized by the capsule plate's alpha (`IslandPalette.ClosedPillAmount`), the tint cache is invalidated by a bar-palette revision so a wallpaper change repaints it, and hover sampling uses the geometry-based island hover instead of `IsMouseOver`.
- The music body's clipboard affordance is visible again on the primary bar (it was declared collapsed in XAML and never re-enabled), and a status-strip width change now refreshes the island pill face on secondary bars too instead of leaving their title ceiling stale.
- A shelf drop that landed nothing no longer plays the swallow motion or the drop chrome, and a press that ends in a cancel no longer leaves the pill pulse gate armed.
- The island drag-target chrome no longer clips its extra elements, leaks the glow effect, or falls back to a Turkish badge.
- The tray mirror no longer blanks itself when the parked tray reads empty: an empty read keeps the last good icons (`TrayBlipOutcome.ConfirmedEmpty` included), and only a shell restart resets the snapshot.
- Unnamed or repeated UIA tray buttons get distinct index-suffixed keys, so icon de-duplication cannot drop them from the mirror.
- A settings write that cannot reach the disk (a read-only profile, or a restricted/low-integrity launch that cannot write into `%AppData%`) no longer crashes the app: `ConfigService.Save` keeps the in-memory config (`LastSaveError`) and the session continues. The restricted-launch warning now says settings will not be saved in that mode.
- Unhandled exceptions (dispatcher, app domain, task scheduler) are written to a best-effort crash log — `%LOCALAPPDATA%\WharfBar\logs\`, falling back to `%TEMP%` and the app folder — with the process integrity RID, so an APPCRASH leaves a managed stack instead of only an event-log code.
- Launchpad tiles, toolbar buttons and the power button are visible again: the shared flat `ControlTemplate` built its content presenter and hover/focus overlays in a grid but never attached that grid to the template's root border, so every flat button painted as an empty transparent square (headers and the phone panel still worked, and logical-tree tests passed because they read `Button.Content`). A visual-tree regression test now covers the template.
- Launchpad search no longer misses ASCII-I names on a Turkish install (`instagram` finds `Instagram`), ranks prefix/exact hits above substring hits, de-duplicates dock pins that also exist in the Start Menu, and keeps the results section above the fold.
- The Launchpad no longer scans the Start Menu twice at startup: warm-up and the window share one in-flight scan, a stale snapshot is repainted in a single pass instead of collapsing to 16 tiles and growing back, and a failed Recent read no longer erases the app catalog (each source degrades on its own).
- Launchpad keeps the keyboard in the pad after a rebuild: view toggle / show-all / catalog refresh restore focus instead of dropping it, Enter opens the arrow-selected tile, `Esc` closes only after the query is cleared, and the auto-dismiss reopen guard no longer swallows a deliberate reopen after `Esc` or an app launch.
- A power/tool context menu that closes no longer leaves the Launchpad on screen without keyboard focus (the pad re-activates after the menu closes); `WM_DPICHANGED` re-places the overlay after WPF's own rect handling; opening the pad now falls back to `SetForegroundWindow` when the topmost dock wins the activation race.
- Island music panel metadata and timeline now update live: SMTC's position is a still image between app reports, so `MediaSessionService` interpolates it locally from `LastUpdatedTime` + `PlaybackRate` (clamped, paused-safe) and the panel polls at ~250 ms while playing. A late metadata/artwork reload can no longer overwrite a newer track, and a republished cover with the same title/artist refreshes instead of serving the cached bitmap.
- The hover-opened island panel no longer flickers while the pointer is over the pill: the popup is a separate window whose transparent pad overlaps the pill, so a fake `MouseLeave` used to arm the 300 ms hover close; the hover state is now sampled from the real cursor against the pill/panel geometry.
- The pill-to-panel join looks like one surface: the joined pill fill and the panel's top edge share the exact same tinted-surface composite (no color step), the joined pill stroke follows the panel rim's top color so the fillet meets the pill side without a color break, and the pill tooltip no longer floats over the tab strip while the panel is open.
- Play/pause in the island music panel reacts instantly: the click flips the published `IsPlaying` (and the glyph) before the SMTC command round-trip, a 1 s optimistic window keeps the 250 ms panel poll from undoing it until `PlaybackInfoChanged` is authoritative, a refused command reverts, and a playback-only change no longer runs the async metadata/artwork reload (the panel used to wait for that reload before showing pause). The timeline poll also raises on an `IsPlaying` change and transient SMTC read failures are non-destructive.
- Skipping a track no longer moves the panel to another app: a paused leftover session (for example a paused Zen browser) can neither steal the selection nor become the fallback while the player briefly reports `Changing`/`Opened` during the skip. The selection stays on the current session until it disappears; only a transition into Playing, a chevron pick, or removal changes it (a track skip counts as playing), and removal falls back to a still-playing session. This is covered by the new `MediaSessionSelector` policy tests.
- The tray mirror keeps the pointer at the clicked icon: the menu owner's `SetCursorPos` warp is prevented with a short cursor clip instead of undone afterwards. `TrayPointerGuard` confines the pointer with `ClipCursor` to a 6 px box (`ClipMarginPx = 6`) right before the Menu key and releases it after 150 ms (`ClipHoldMs = 150`); a high-integrity owner (NVIDIA's `NVDisplay.Container`) refuses a later restore, which is why the clip exists, and a short escape watcher puts the pointer back if a warp still lands in the tray-anchor band, logging honestly when `SetCursorPos` is blocked.

## [1.0.0-beta] — 2026-10-14

First public beta of the shell tree (AppBar top bar, dock, quick settings, island, and related services).
Completed themes are summarized under [Shipped through 1.0.0-beta](https://github.com/xkeidm/wharfbar/milestone/4);
ongoing work uses [GitHub Milestones](https://github.com/xkeidm/wharfbar/milestones).

### Added

- WPF top bar with AppBar reservation (NEW / QUERYPOS / SETPOS / REMOVE), Mica / Fluent chrome, DPI-aware layout, mutex single-instance, JSON config, and Startup-folder autostart.
- Dock overlay: live pins and running apps, auto-hide / hit-test, context menu, Explorer drop, drag reorder, per-app grouping, window previews / progress / folders, and plate background options.
- Quick Settings flyout with live Wi-Fi, Bluetooth, Night light, and projection (display) tiles, plus volume mixer integration.
- Color overlay (veil): fullscreen layered window, QS tile, global hotkey, and settings page.
- Media panel via Windows SMTC (session flyout, multi-session picker) and island music presence.
- Top-bar island: clipboard history, dropped-file shelf, HUD faces / satellites, and settings.
- Tray hybrid: native overflow (`Win+B`), read-only tray mirror (documented toolbar / UIA paths), and overflow capsule for unselected icons.
- System stats on the top bar (CPU / GPU / RAM; optional per-GPU), sampled off the UI thread.
- Monitor brightness flyout (DDC/CI + WMI hybrid).
- Taskbar hide/restore while running, plus a `--taskbar-watchdog` child that restores the taskbar after a force-kill.
- Plugin catalog (local on/off manifest; no assembly load) and Launchpad gallery widget restore.
- Status-strip customization: reorderable items, clock panel, and tray icons parked in a capsule.
- Multi-monitor scaffolding (per-monitor top bar / dock toggles and reconcile).
- Open-source prep: CONTRIBUTING, Code of Conduct, issue / PR templates; public roadmap via GitHub Milestones.

### Changed

- Dock model revised toward macOS-style groups: left reads taskbar `User Pinned` (and `dock.items`); right shows unpinned running apps; magnification ceiling 1.15×; Windows-style strip metrics.
- Idle work quieted (dock window scans, stats polling) for lower background cost.
- Docs and agent context point at Milestones for “what’s next”; local roadmap keeps completed-phase history.

### Fixed

- Taskbar stay-hidden path, tray peek / blank capture, and work-area circuit breaker notes for Windows 11 25H2+.
- Veil Alt-chord shortcut capture; island restore / drop-path hardening; dock rebuild, OLE click clashes, and magnification clamp from older configs.
- Settings NavigationView page hosting; top-bar status drag drop targets; PDH counter ABI used by system stats (watchdog path).
- Launchpad phone-frame screenshot sizing and assorted flyout / mixer edge cases from the beta tree.
