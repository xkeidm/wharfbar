# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
as described in [CONTRIBUTING.md](CONTRIBUTING.md).

## [Unreleased]

### Added

- Desktop clock and calendar sit on the shell desktop. Drag the grip to move one; it settles beside your icons and remembers the spot.
- Left click on the top-bar brand logo opens the Launchpad (the same window the dock tile toggles); the app menu stays on right click.
- Windows-style Show Desktop sliver at the right edge of the top bar: the first click minimizes every open window, the second restores exactly what it put down (windows closed in the meantime are skipped; windows the user minimized themselves are never touched).
- Island hub **Shelf** (`shelf`) tab: dropped files stay as draggable chips (extension badge + name); `Open with app` opens with the default app, `Remove from list` removes one, and `Empty shelf` empties the shelf (delete paths stay inside the `Drop` folder).
- Island hub **Color** (`colorpicker`) tab: a session-scoped recent-colors list plus `Screen picker` — a click on the screen copies the color under the loupe as HEX (never sampled through the dim mask; the value does not enter Win+V history, paste flow is Ctrl+V).
- Island hub **Prompter** (`prompter`) tab: a reading auto-scroll view — paste (`Paste from clipboard` takes the newest text of the island history), load a `.txt`, or type, then Play/Pause with a 0.5–3.0× speed slider and `Back to start`; the panel stays open while playing, and reaching the bottom returns to the editor. Play disables with a reason tooltip while there is nothing to scroll (the text fully fits, or the scroll is already at the bottom) instead of silently doing nothing.
- Island hub **Camera** (`camera`) tab: a live preview of the camera mirror (rounded, with a placeholder + status while it is closed), a Camera row with an on/off toggle, and a display-only Microphone row. The toggle rides the same `island.cameraMirror` switch as the widget-gallery toggle — one capture, one owner (`CameraMirrorHost.Apply`), requested from the body through an event.
- Island shelf accepts dropped folders too: same record, folder badge, opened in Explorer from the original path (the `Drop` copy stays inside the app folder).
- Shelf chips support Ctrl/Shift multi-select with a batch OLE drag-out of every selected path, a hover × to remove one record, and a chip menu with `Show file location`, `Copy path`, `Copy file` (hidden for folders) and `Open with app`; `Drag all out` on the shelf body starts a press-and-drag batch.
- The island pill is a live drop target: a file drag hovering the primary pill grows it, swaps the glyph for the drop arrow, shows the localized `Drop → shelf` badge and the accent/glow stroke; a drop where at least one record landed plays a swallow micro-motion.
- Screen picker is now a macOS-style loupe over the **full monitor**: a round cursor-centred 13×13 magnifier of the frozen frame, a hairline cross + centre dot, and a glass HEX chip; the pick is the crop's exact centre pixel (up to 6 px from the cursor when the crop clamps at a frame edge).
- Restricted-launch guard: when the process token is below medium integrity (a sandboxed terminal or a low-integrity parent launched WharfBar), Windows silently drops the taskbar hide and the AppBar reservation. Startup now detects it with `ProcessIntegrity` and warns — OK continues, Cancel quits — instead of leaving a taskbar that stays visible under a bar that overlaps the desktop.

### Changed

- Tray mirror on Windows 11 25H2: the parked auto-hide taskbar virtualizes its tray buttons, so the periodic refresh hover-reveals the tray the same way a click does — while the pointer is idle (or while the mirror is still empty, capped at 3 attempts per session), never under a fullscreen app, and backing off for 10 min after 2 misses. The reveal restores the cursor only if the user has not moved it, WharfBar's own auto-hide dock suppresses its edge-band show while the reveal owns the pointer, and an empty read clears the mirror only when the tray was confirmed fully on screen. There is no `ABM_SETSTATE` and no work-area jump; only the rare idle-gated maintenance blip (and only in a shell that already blipped once) still flips the strip.
- The screen picker overlay covers the whole monitor (was the work area) so the dock/top-bar strips no longer cut the loupe off.
- Launchpad opens at full size on the first frame: the Start Menu catalog + recent files are captured once into a process-wide cache (30 min TTL, refreshed in the background) instead of growing in 16-app slices during an open.
- Launchpad shell icons are decoded ahead of time in idle slices after startup (first 64 apps; one shared icon cache for the warm-up and the face).
- Launchpad face polish: eased hover fill (animated template overlay, with a resource-swap fallback), 0.97 press scale on the flat buttons, secondary-colour letter headers, larger tile icons (44/40 px) and 12 px tile labels.
- Island hub tab resolve: when the latest pill face is a file drop, the panel opens on the Shelf tab; a saved `shelf`/`prompter`/`camera`/`colorpicker` tab whose config flag is off falls back to Clipboard.

### Fixed

- A shelf drop that landed nothing no longer plays the swallow motion or the drop chrome, and a press that ends in a cancel no longer leaves the pill pulse gate armed.
- The island drag-target chrome no longer clips its extra elements, leaks the glow effect, or falls back to a Turkish badge.
- The tray mirror no longer blanks itself when the parked tray reads empty: only a refresh that actually showed the tray and confirmed it empty (`TrayBlipOutcome.ConfirmedEmpty`) clears the snapshot; throttled, busy, fullscreen and reveal-timeout outcomes keep the last good icons.
- Unnamed or repeated UIA tray buttons get distinct index-suffixed keys, so icon de-duplication cannot drop them from the mirror.
- A settings write that cannot reach the disk (a read-only profile, or a restricted/low-integrity launch that cannot write into `%AppData%`) no longer crashes the app: `ConfigService.Save` keeps the in-memory config (`LastSaveError`) and the session continues. The restricted-launch warning now says settings will not be saved in that mode.
- Unhandled exceptions (dispatcher, app domain, task scheduler) are written to a best-effort crash log — `%LOCALAPPDATA%\WharfBar\logs\`, falling back to `%TEMP%` and the app folder — with the process integrity RID, so an APPCRASH leaves a managed stack instead of only an event-log code.

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
