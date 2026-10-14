# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
as described in [CONTRIBUTING.md](CONTRIBUTING.md).

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
