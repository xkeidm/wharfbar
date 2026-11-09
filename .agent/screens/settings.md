# Screen: Settings window

`src/WharfBar/Views/SettingsWindow.xaml` + `Views/Pages/*`.

Opens from: top bar logo → `WharfBar Settings` (or the tray menu). WPF-UI
`NavigationView`, Mica, left navigation: **`General` · `Appearance` · `Dock` · `Displays` · `Top bar` ·
`Overlay` · `Island` · `Plugins`** + **`About`** at the bottom.

## General (`GeneralPage`)

- **`Start with Windows`** — Startup folder `WharfBar.lnk` (not Registry Run;
  `AutostartService`, config `autostart`)
- **`Language`** — `System` / `English` / `Türkçe` (config `language`: `system` / `en` / `tr`); applies on restart (`LocalizationService`)

## Appearance (`AppearancePage`)

- **`Accent color style`** — `System accent color` / `Fixed theme mode` (config `useSystemAccentColor`)
- **`Application theme`** — `System default` / `Light` / `Dark` (config `theme`)
- **`Logo style`** — `WharfBar` / `Windows` (config `logoStyle`, default `wharfbar`; ADR-019). Labels are `Appearance_Logo_*` in `Strings.resx` / `Strings.tr.resx`. Applies live to every top bar and dock (`App.ApplyLogoChanges`); a theme or accent change on this page also re-tints the dock tile
- **`Top bar background`** — `Mica (Blur)` / `Transparent` / `Tint`
  (config `backgroundMode`) + **`Opacity`** slider 0–100 (enabled only for Tint, config `tintOpacity`)

## Dock (`DockPage`)

- **Content list** — `dock.items` rows (label + path); `Add` (file picker), remove with `×`,
  drag the `⋮⋮` grip to reorder
- **`Auto-hide`** toggle (config `dock.autoHide`)
- **`Icon size`** slider 16–36 DIP (config `dock.iconSize`; default 24). The old default of 28 is pulled to 24 on load.
- **`Background`** ComboBox (config `dock.backgroundMode`: `mica` / `transparent` / `tint`; independent of the top bar) + **`Opacity`** 0–100 (only `tint`, config `dock.tintOpacity`, default 40). A Mica ↔ transparent/tint transition recreates the dock window. The old `dock.surface` (`mica`/`acrylic`) becomes `mica` on load.
- **`Position`** ComboBox (config `dock.position`: `Left` / `Center` / `Right`; bottom edge only)
- **`Show running apps`** toggle (config `dock.showRunningApps`; while `false` the unpinned
  running apps on the right are not produced at all)

## Island (`IslandPage`)

- **Wallpaper — transition effect** — ComboBox: Off / Fade / Blur + fade (config `island.wallpaperTransition`: `off` / `fade` / `blurFade`; default **`blurFade`**; unknown → `blurFade`). Primary monitor only: when not `off`, overlay crossfades the previous wallpaper out under WorkerW, then SPI applies the new image when the fade completes (instant SPI when off or overlay skipped).
- **Follow Windows animation** — toggle (config `island.wallpaperRespectSystemAnimation`, default **`true`**). When on, no overlay if `SystemParameters.ClientAreaAnimation` is false; the ComboBox is disabled and a short note explains why. Unchecking re-enables the effect regardless of Windows Accessibility → animation effects.
- **Match accent to wallpaper** (`Island_WallpaperAccent_*`) — toggle (config `island.wallpaperAccent`, default **`true`**). After a successful wallpaper apply from the island hub, WharfBar refreshes its accent (and dock logo tile) from the image’s dominant color; Windows accent is updated best-effort via HKCU (undocumented keys). Does not change wallpaper apply (SPI-only) or exit restore behavior.
- Other Island cards (visibility, persistence, idle label, color mode, low battery, maintenance) unchanged — see [`topbar.md`](topbar.md) for hub/gallery behavior.

## Top bar (`TopBarPage`)

- **Status item cards** — every item in the catalog (closed ones at the bottom, under the `Off` heading).
  A checkbox shows/hides; visible ones are reordered by dragging the `⋮⋮` grip.
  **`Show all`** turns on the missing ones (it does not disturb the saved order). **`Reset to default`**
  returns to `DefaultOrder` (config `topBar.statusItems`)

## Plugins (`PluginsPage`)

The Widget and Plugin tools in Launchpad open the same catalog in a separate window (`GalleryWindow`).

- **`Slots`** — Top bar (`topBar`), Dock (`dock`), Desktop (`desktop`), Island (`island`).
  Top bar and dock are a record only in v1; they are not drawn.
- **`Built-in`** — Bluetooth notice, camera mirror (island).
  Switches: `island.bluetoothNotice`,
  `island.cameraMirror`.
- **`Local`** — `%AppData%/WharfBar/plugins/<folder>/manifest.json`. When empty, a sample manifest
  and **`Open folder`** are shown. An enabled id is written into `plugins.enabled`. An assembly is not loaded.

## About (`AboutPage`)

- Version / credits (content equivalent to `AboutWindow`).

## Notes

- Changes are written to config immediately (`ConfigService.Save`) and applied live to the top bar
  with `ApplyConfigChanges`.
- Long pages scroll in their own `ScrollViewer`. On every `Page` root,
  `ScrollViewer.CanContentScroll="False"` is required — otherwise `NavigationView`
  measures the page at infinite height and the wheel scrolls nothing
  (`docs/04-gotchas.md`).
- Pages use cards in the `PowerToysCard` style (Fluent `CardBackgroundFillColorDefaultBrush`).
- Card header icons / the logo preview use the accent color: `AccentFillColorDefaultBrush`. About / Settings title bar / exe use the Dusk app icon (`Assets/WharfBar.png` / `.ico`)
  (WPF-UI 4.3.0's `SystemAccentColorPrimaryBrush` is a fixed blue and does not update when the accent is applied —
  do not use that accent color for icons; detail: `docs/04-gotchas.md`).
- The status item list cannot be emptied: if the last item is hidden, `dateTime` is added automatically.
