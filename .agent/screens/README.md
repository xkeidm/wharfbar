# Screens — summary index

A tour of WharfBar's user interface: each screen's detailed page is below. Code side:
`src/WharfBar/Views/`; config counterparts are `src/WharfBar/Models/AppConfig.cs`.

| Screen | Page | Summary |
|--------|------|---------|
| **Top bar** | [`topbar.md`](topbar.md) | 30 DIP AppBar; left: brand logo menu (`logoStyle`, default Dusk mark); center: island pill (idle `Clipboard` or the last clip, now playing, volume/brightness/battery, satellites; clipboard on the primary, secondary is music and meters only); right: status strip (right-click to show/hide, reorder, background + opacity). |
| **Dock** | [`dock.md`](dock.md) | Bottom-edge overlay; read-only taskbar pins on the left (including closed ones) + `dock.items`, unpinned running apps on the right, one icon per application; plate button rhythm (icon 24, button 40, gap 4), no magnification, background `mica`/`transparent`/`tint`, drag-to-reorder, file drop, bottom-left/center/right position. |
| **Settings** | [`settings.md`](settings.md) | Opens from the logo/tray menu; **General · Appearance · Dock · Displays · Top bar · Overlay · Island · Plugins · About** (WPF-UI NavigationView). |
| **Quick settings flyout** | [`topbar.md`](topbar.md#quick-settings-flyout) | One popup shared by the `controlCenter`/volume/network icons: Main / Mixer / Network tabs (ADR-006); Wi-Fi (live network list + password panel), Bluetooth (device list), `Project` (4 modes), night light tile (real state + toggle), overlay tile (color overlay on/off, `\uE790`; ADR-007), tile animations. |
| **Brightness flyout** | [`topbar.md`](topbar.md#brightness-flyout) | Monitorian-style DDC/CI + WMI; master slider + monitor cards. |
| **Media flyout** | [`topbar.md`](topbar.md#media-flyout) | SMTC now playing: art, title/artist, `Play`/`Pause` + `Next`/`Previous`, multi-session picker; the item is visible only while a session is active |

Shared notes:

- Status items / options are applied live from config (`ApplyConfigChanges`).
- Right-click menus use WPF-UI `BasedOn` styles (App.xaml; Aero2 icon gutters are not overwritten).
- v1 is the **primary monitor** only; every window is Per-Monitor V2 DPI.
- Startup can show a warning dialog when the process token is below medium integrity
  (`ProcessIntegrity`): OK continues anyway, Cancel quits before any shell state is touched.
