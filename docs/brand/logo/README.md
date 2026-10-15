# WharfBar logo (Dusk)

Mark: a pier running out to the horizon with a low sun. Overview and do/don't: `wharfbar-logo-sheet.svg`.

**Canonical mark (approved):** the small solid pier + sun (no plank gaps, no sea lines). That is what the top bar glyph, the dock tile (`BrandLogo`), the exe/tray icon (`src/WharfBar/Assets/WharfBar.ico`), Settings / About, and the README hero use. Older full / planked SVGs remain as optional wallpaper or marketing variants.

All files share one 128x128 grid, so they are interchangeable.

| File | Use |
|------|-----|
| `wharfbar-icon-small.svg` / `wharfbar-icon.png` | **App identity** — solid small mark on a dusk tile (exe, tray, About, README). |
| `wharfbar-icon.svg` (`-dusk`) | Marketing / planked full mark (optional). |
| `wharfbar-icon-{dawn,harbor,twilight,graphite,sand}.svg` | Alternative colorways (themes, accent choice, seasonal). |
| `wharfbar-mark-on-dark.svg` / `-on-light.svg` | Transparent background, full planked mark. About / splash / wallpaper. |
| `wharfbar-mark-mono-black.svg` / `-mono-white.svg` | One colour, fixed. Print, docs, accent backgrounds. |
| `wharfbar-mark-mono.svg` | One colour via `currentColor` (web / CSS); full mark. |
| `wharfbar-mark-small-*.svg` | Solid small mark for top bar / tray: `-mono` (currentColor), `-on-dark`, `-on-light`. |

## Theming

The app builds the dock tile from the user's accent colour and the light/dark theme. Convert the accent to HSL (h, s, l), then:

| Role | Dark theme | Light theme |
|------|-----------|-------------|
| bg (tile) | `hsl(h, min(s,0.45), 0.17)` | `hsl(h, min(s,0.40), 0.94)` |
| pier (and Windows panes) | `hsl(h, min(s,0.35), 0.94)` | `hsl(h, min(s,0.55), 0.20)` |
| sun | `#F2B66B` | `#E59A45` |

Accent `#0078D4` computed: dark = bg `#182E3F`, pier `#EAF0F5`; light = bg `#EAF1F6`, pier `#17374F`. (`BrandPalette` still exposes a `lines` role for older planked assets; the in-app solid mark does not use it.)

The top bar glyph is always one colour (follows the bar foreground, `currentColor`). User option: `wharfbar` or `windows` logo, for both top bar and dock.

| File | Use |
|------|-----|
| `wharfbar-mark-small-mono.svg` | Top bar glyph, WharfBar option (solid small). |
| `windows-mark-mono.svg` | Top bar glyph, Windows option (four panes, currentColor). |
| `wharfbar-dock-{dark,light}.svg` | Dock tile (solid small), accent `#0078D4`. |
| `wharfbar-dock-small-{dark,light}.svg` | Same geometry (kept for naming; identical to the dock tiles above). |
| `windows-dock-{dark,light}.svg` | Windows option dock tile (bg role + panes in pier role). |
| `wharfbar-theme-sheet.svg` | Preview matrix: 6 accents x dark/light, plus top bar glyph strip. |

## Rules

- Clear space around the mark: at least the sun radius.
- Full tile: minimum 24 px. Below that use the `small` variants.
- Do not stretch, rotate, recolour outside the colorways, add heavy shadows, or lower opacity.
- Pier must contrast with the background: use `on-dark` on dark, `on-light` on light.

## In the app (WPF)

WPF does not render SVG natively. Assets are wired as follows:

- Top bar / dock glyph: path data in `BrandLogo` (`Geometry` / `DrawingImage`); theme colours stay sharp at any DPI.
- Window / exe / tray icon: `src/WharfBar/Assets/WharfBar.ico` (16–256, solid small dusk tile). Rebuild with `python tools/render-app-icon.py`. Also ships `WharfBar.png` (128) for Settings title bar, About, and window `Icon`.
- Wallpapers: place the `on-dark` / `on-light` mark centered on the horizon line.
