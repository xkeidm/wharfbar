# WharfBar

A Windows menu bar and dock: a thin top strip that reserves screen space, and a dock for pinned and running apps. WharfBar is inspired by Windows, macOS, and Linux desktops. The look is Windows Fluent, with no Apple branding or icon language.

**Türkçe:** [README.tr.md](README.tr.md)

## What it is

WharfBar draws its own top bar and dock on the Windows desktop.

The top bar is a real AppBar (`ABE_TOP`, 30 DIP). Maximized windows stop under it. The dock is a topmost overlay. While WharfBar is running, the Windows taskbar is hidden and returns to its previous state on a normal quit.

Each Windows app keeps its own menu. The top bar carries the clock, status, tray, and controls. When a choice is in conflict, the order is low resource use, then a native Fluent look, then simplicity.

## Screenshots

Illustrative product stills of the Fluent top bar, dock, and settings. No animated GIF yet — capturing a live session here would reserve an AppBar strip, so these mocks stand in until real captures land.

| Top bar | Dock | Settings |
|:-------:|:----:|:--------:|
| ![WharfBar top bar](docs/media/wharfbar-topbar.jpg) | ![WharfBar dock](docs/media/wharfbar-dock.jpg) | ![WharfBar settings](docs/media/wharfbar-settings.jpg) |

## Why run it

- Clock, CPU / GPU / RAM, network, battery, notifications, and quick settings sit on one strip, and the work area is actually reserved.
- Pinned apps, including ones that are closed, and running apps share a dock, one tile per app, while the Windows taskbar stays out of the way.
- Clipboard history, now playing, and short volume, brightness, and battery readouts live in the center pill.
- Wi-Fi, display projection, monitor brightness, and media controls cover the common cases from the bar.
- Idle work stays sparse. Every dock shares one window census (about every 3 seconds). Stats are sampled every 4 seconds off the UI thread and stop when you hide them. The tray mirror polls every 4 seconds, and the icon bitmap is not recaptured on every pass.

## Features

### Top bar

One bar per enabled monitor, each registered as an AppBar on that monitor. Visibility is the `monitors` config.

- **Left:** a drawn Windows logo menu (Windows Settings, profile, lock, sleep, shut down, restart, WharfBar settings, About, and quit) and a one-line `CPU · GPU · RAM` readout. Hover opens a sparkline and network throughput. Multiple GPUs can be listed separately (`topBar.showGpuPerAdapter`). If the GPU counter is missing, that part is omitted. There is no temperature.
- **Center:** the island pill.
- **Right:** a status strip you can show, hide, and reorder from the right-click menu or Settings → Top bar. The default order is tray mirror, hidden-icons chevron, clock, notifications, quick settings, network, battery. Volume, brightness, and media stay in the catalog and start hidden, because the island already shows them. A saved order is kept.
- The clock is a single `tr-TR` line. Click opens notification text and a month calendar. The bell sends `Win+N`.
- Background is Mica, transparent, or a tint plus an opacity slider. Mica and `AllowsTransparency` are never combined.

### Island

On the primary bar the pill idles as **Clipboard** (`Pano`), or shows the last clip when `island.idleLabel` is `lastClip`. It expands for now playing (`island.musicPresence` is `persistent` by default, or `transient`) and briefly for volume, brightness, and battery. At most two satellites sit beside it. Clipboard history and file drops belong to the primary pill. A secondary monitor’s pill shows music and those counters only. The bar stays 30 DIP; the panel opens under the pill.

Low battery uses `island.lowBatteryPercent` (default 20, allowed range 5–50).

### Dock

A topmost overlay: bottom-left, bottom-center, bottom-right, or the left edge. It can auto-hide, and it hides while another app is fullscreen.

- **Left:** the Windows taskbar’s `User Pinned\TaskBar` shortcuts, read in taskbar order, including apps that are not running, then entries in `dock.items` that are not already there. A running pin stays on that same tile.
- **Right:** running apps that are not pinned, one tile per app (canonical path, AppUserModelID, or process id). Extra windows are a count badge and a **Windows** submenu. A minimized window is not a second tile.
- The glyph stays a fixed size. The default is 24 DIP, the button 40, the gap 4 DIP. Hover fills the button and can show a DWM thumbnail of that app’s windows on this monitor. A progress bar appears under the icon when UI Automation reports one. An icon that wants attention gets a short accent pulse.
- Pin and unpin write `dock.items`. A file or folder target also gets a `.lnk` in `User Pinned\TaskBar`. `Taskband` is left untouched.
- Drag to reorder. Drop `.lnk`, `.exe`, `.bat`, or `.cmd` from Explorer. Right-click: title card, pin or unpin, end task, close window, run as administrator, open file location, new window, window list.
- Background is Mica, transparent, or tint, chosen separately from the top bar.
- **Launchpad** (far left), **Downloads**, and **Recycle Bin** (far right) are built-in tiles and are not written into the pin folder. A folder pin opens a stack. Store packages use the package icon when it resolves, and a letter glyph when it does not. Badges, the running mark, and thumbnails count windows on that dock’s monitor.

### Taskbar

While WharfBar runs, the Windows taskbar is hidden. A normal quit restores the state from before launch. After a force-kill, a watchdog restores the taskbar in about half a second. The reserved top strip is separate; see the warning below.

### Quick settings

One flyout, opened from the quick-settings, volume, or network icon: **Main**, **Mixer**, and **Network**.

- Wi-Fi: a live network list, connect, and disconnect through `wlanapi`. Profiles are stored as plain text. The list is empty while the radio is off. Hidden and enterprise networks are left to Windows Settings.
- Bluetooth: the radio, plus paired classic and LE devices. Connecting a device stays in Windows Settings.
- Night light follows the real setting when it can be written. When it cannot, the tile opens Windows Settings.
- Project: PC screen only, duplicate, extend, or second screen only.
- The energy-saver tile tracks the system switch and toggles it.
- The veil tile toggles the color overlay.

### Brightness

A separate flyout: one master slider for every display, and a card per monitor. Desktop monitors use DDC/CI; internal panels use WMI. Writes go through a background queue with a 50 ms debounce.

### Media

Windows SMTC. The status-strip media icon appears only while a session exists. The flyout shows cover art, title, artist, play/pause, previous, and next, and a session picker when more than one app is active. The island can keep now playing on the pill.

### Veil

A click-through full-screen color overlay on the primary monitor. Toggle it from quick settings or with the shortcut (`Ctrl+F11` by default). Color, opacity, and the shortcut are on the Veil settings page.

### Tray

WharfBar’s own notification icon has a quit item, **WharfBar'ı kapat** (Quit WharfBar). On the primary bar, third-party tray icons are mirrored with documented toolbar messages and clicks are forwarded. Chosen icons can sit in a pill behind the chevron; the chevron also opens Windows’ hidden icons with `Win+B`. System icons are filtered out. Icons Windows already keeps in the overflow stay there. On Windows 11 25H2 and later, where that toolbar is gone, the mirror uses UI Automation. A secondary bar has the chevron only. Icons are not read from another process’s memory.

### Launchpad and plugins

The Launchpad tile opens one centered surface over the wallpaper. It can open before the app scan finishes. Apps are grouped under letter headings. The Windows logo opens the logo menu, not Launchpad. A phone column appears only while a phone is connected: name, battery, and notification count from the Your Phone start card, and a screenshot inside the phone frame. Missing fields stay hidden.

Widget and Plugin open the same catalog as Settings → Plugins. The pieces that run are the island visualizer, the Bluetooth connection notice, the camera mirror, and the desktop clock and calendar. A local `%AppData%/WharfBar/plugins/<folder>/manifest.json` is an on/off registration. Plugin assemblies are not loaded. Top bar and dock plugin slots are stored and not drawn.

### Config, startup, one instance, DPI

- Config lives in `%AppData%/WharfBar/config.json` (`schemaVersion`). Settings pages: General, Appearance, Dock, Displays, Top bar, Veil, Island, Plugins, About. Changes apply immediately.
- Autostart is a `WharfBar.lnk` in the Startup folder (`AppConfig.autostart`), not the Registry Run key.
- A named mutex makes a second instance exit.
- DPI awareness is Per-Monitor V2 (`app.manifest`).

## Requirements

- Windows 10 version 19041 or later. The project targets `net8.0-windows10.0.19041.0`.
- The .NET SDK 8 or 9. An SDK 9 install can build the `net8.0` target.
- There is no `global.json` on purpose. Pinning the SDK to 8.x breaks the build on a machine that only has the .NET 9 SDK.

## Build and run

```bash
git clone https://github.com/xkeidm/wharfbar.git
cd wharfbar

dotnet build WharfBar.sln
dotnet run --project src/WharfBar
```

Or double-click `run.bat` in the repo root. It builds, then starts `WharfBar.exe` detached, so closing that console does not kill the app.

> **Warning.** Running the app reserves a strip along the top of the screen and hides the Windows taskbar. A force-kill (`Stop-Process -Force`, detaching the debugger, closing a raw `dotnet run` console) skips `ABM_REMOVE`. The taskbar watchdog restores the taskbar in about 0.5 seconds. The reserved strip can stick until `explorer.exe` is restarted. A normal quit (tray **WharfBar'ı kapat** (Quit WharfBar), or Alt+F4) restores both. Prefer `run.bat` to closing a `dotnet run` window.

A second instance exits because of the named mutex.

`dotnet test` runs `tests/WharfBar.Tests` (xUnit).

## How to quit

Click the WharfBar notification icon and choose **WharfBar'ı kapat** (Quit WharfBar), or focus the bar and press **Alt+F4**. The Windows logo menu has the same quit item.

`Process.MainWindowHandle` is `0` for this process (`ShowInTaskbar=false` gives the window a hidden owner), so `CloseMainWindow` from a script does nothing. Find the window by PID and send `WM_CLOSE`.

## Project layout

```
WharfBar.sln
src/WharfBar/
  Views/          TopBarWindow, DockWindow, SettingsWindow, flyouts
  Views/Pages/    General, Appearance, Dock, Displays, TopBar, Veil, Island, Plugins, About
  Native/         AppBar, Dwm, ShellIcon, tray, DDC/CI
  Models/         AppConfig and related records
  Services/       config, dock, status, tray, island, quick settings
  app.manifest    Per-Monitor V2 DPI
tests/WharfBar.Tests/
docs/
```

The code root is `src/WharfBar/`. UI lives in `Views/`, so the folder name is not confused with Win32.

## Docs

- [docs/00-overview.md](docs/00-overview.md) — purpose, goals, and scope
- [docs/01-architecture.md](docs/01-architecture.md) — folders, services, and config
- [docs/04-gotchas.md](docs/04-gotchas.md) — AppBar, Mica, and DPI traps
- [AGENTS.md](AGENTS.md) — build commands and the agent index
- [CHANGELOG.md](CHANGELOG.md) — release notes (Keep a Changelog)
- [SECURITY.md](SECURITY.md) — private vulnerability reporting

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md).
Bug reports and feature requests use the GitHub issue templates.
Versioning rules are in CONTRIBUTING; security reports go through [SECURITY.md](SECURITY.md).

## Status

**Beta (`1.0.0-beta`).** See [CHANGELOG.md](CHANGELOG.md). The public roadmap is [GitHub Milestones](https://github.com/xkeidm/wharfbar/milestones): live work under Beta polish / Post-beta / Stable 1.0; history under Shipped through 1.0.0-beta; consciously deferred work under Deferred / out of scope (open parking lot).

## License

[MIT](LICENSE). [WPF-UI](https://github.com/lepoco/wpfui) is MIT as well.
