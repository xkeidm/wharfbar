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
- Island hub **Raf** (`shelf`) tab: dropped files stay as draggable chips (extension badge + name); `Uygulama ile aç` opens with the default app, `Listeden kaldır` removes one, and `Rafını boşalt` empties the shelf (delete paths stay inside the `Drop` folder).
- Island hub **Renk** (`colorpicker`) tab: a session-scoped recent-colors list plus `Ekran seçici` — a click on the screen copies the averaged 3-px color as HEX (never sampled through the dim mask; the value does not enter Win+V history, paste flow is Ctrl+V).
- Island hub **Prompter** (`prompter`) tab: a reading auto-scroll view — paste (`Panodan al` takes the newest text of the island history), load a `.txt`, or type, then Oynat/Duraklat with a 0.5–3.0× speed slider and `Başa dön`; the panel stays open while playing, and reaching the bottom returns to the editor. Oynat disables with a reason tooltip while there is nothing to scroll (the text fully fits, or the scroll is already at the bottom) instead of silently doing nothing.

### Changed

- Launchpad opens at full size on the first frame: the Start Menu catalog + recent files are captured once into a process-wide cache (30 min TTL, refreshed in the background) instead of growing in 16-app slices during an open.
- Launchpad shell icons are decoded ahead of time in idle slices after startup (first 64 apps; one shared icon cache for the warm-up and the face).
- Launchpad face polish: eased hover fill (animated template overlay, with a resource-swap fallback), 0.97 press scale on the flat buttons, secondary-colour letter headers, larger tile icons (44/40 px) and 12 px tile labels.
- Island hub tab resolve: when the latest pill face is a file drop, the panel opens on the Raf tab; a saved `shelf`/`prompter`/`colorpicker` tab whose config flag is off falls back to Clipboard.

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
