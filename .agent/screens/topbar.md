# Screen: Top bar

`src/WharfBar/Views/TopBarWindow.xaml` (+ `.xaml.cs`).

Height 30 DIP, an **AppBar** (work area reserved), always on top. A separate bar is built
for each enabled monitor (per-monitor; each bar is an AppBar on its own monitor, and the bar is moved onto that monitor
before it is registered). Visibility is turned on or off per monitor through the `monitors` config.
Background mode comes from config: `mica` (default, `AllowsTransparency=false`),
`transparent` / `tint` (`AllowsTransparency=true` — never together with Mica).

**Text contrast (`TopBarContrastPalette`):** the bar paints via its own `Bar.*` window
resources (`Bar.TextPrimary`, `Bar.HitFill`, `Bar.PillFill`, `Bar.PillFillHover`,
`Bar.PillStroke`, `Bar.SubtleFill`) instead of the WPF-UI brush keys directly. In Mica/Tint
those mirror the theme brushes exactly as before. In `transparent` on the **primary** bar the
palette is driven by the wallpaper: the strip behind the bar is decoded small once per
wallpaper path and measures average luma (`WallpaperTopStrip`, threshold 0.55) — a light strip
switches the bar to dark text + white plates, a dark strip keeps light text. Fully automatic,
no user setting; wallpaper reloads on sample cache change (3 s watch while transparent, hub
apply events, `WM_SETTINGCHANGE`). Flyout popups keep the theme look: they resolve resources
through the shared WPF-UI dictionaries, not the bar's `Bar.*` keys.

## Left cluster

- **Logo** (geometry drawing, not a font glyph — it does not disappear on Mica). `logoStyle` picks the shape: `wharfbar` (default; the solid small Dusk mark — sun + pier, no plank gaps, no sea lines) or `windows` (four panes). Both are one colour — `Bar.TextPrimary` (`TopBarContrastPalette`: theme-following, or wallpaper-driven on a transparent bar) — in an 18 DIP box on the shared 128 grid (`BrandLogo.BarGlyph`, ADR-019); the bar stays 30 DIP. **Left click opens the Launchpad** (the same window the dock tile toggles); **right click opens the menu**. The menu opens flush to the icon's left edge; `OnAppMenuLoaded` zeros the left margin of WPF-UI's 30 px effect margin — so the popup window is not clamped to the screen edge and does not shift the menu to the right:
  - **`Settings (PC)`** → `ms-settings:` · **`Profile`** → `ms-settings:yourinfo` · **`About this PC`** → `ms-settings:about`
  - **`Lock`** / **`Sleep`** → `PowerActions` (LockWorkStation / SetSuspendState)
  - **`Shut down`** / **`Restart`** → `PowerActions` (shutdown.exe `/s|/r /t 0`)
  - **`WharfBar Settings`** → [`settings.md`](settings.md)
  - **`About WharfBar`** → `AboutWindow`
  - **`Quit WharfBar`** → a clean exit (taskbar restore + `ABM_REMOVE`)

System stats (`CPU · GPU · RAM`) are in the same left cluster, on a single line. The sparkline of recent samples and the network speed (↓/↑ bytes/s) are on a card that opens when the pointer is over the text; if the line were placed under the text the 30 DIP bar would grow. If there is no GPU counter the GPU row on the card is hidden. There is no temperature. Network speed uses the same 4 s interval as `SystemStatsService`; the sample is taken off the UI and the bar is not refreshed when the text did not change. If stats are off, sampling stops. The Bluetooth connection notice looks every 10 s; if the list is the same the pill is not refreshed.

## Center — island pill (`IslandPill`, ADR-010 + ADR-011 + ADR-012)

While music is playing the now-playing pill shows the cover thumbnail and the track name — there is
no equalizer and no trailing satellite. When a paired
Bluetooth device connects, its name stays on the pill for a short time; the Bluetooth tile in quick settings
stays separate. The camera mirror opens from the gallery: the frame is inside the pill, a denied or busy camera
leaves text, and there is no fake frame.

**Motion:** the pill width morphs between idle, music, meter, and satellite states (quintic ease-out on grow, cubic on shrink, 140–260 ms); a face swap dips and restores content opacity and pops the glyph; satellites scale/fade in and out; hover lifts the fill, press scales the pill to 0.97 (80 ms, cubic) and release springs back (`PressOut` 220 ms `BackEase`), a focused pill shows the accent ring, and a hovered satellite gets a subtle fill. With system animations off (`SystemParameters.ClientAreaAnimation`) every transition is instant. Nothing animates while the pill is idle.

**Drag target (primary pill only):** while a `FileDrop` drag hovers the pill, the drag chrome takes over:
the glyph becomes the drop arrow (`E73E`), the caption becomes the localized drop badge
(`Island_DragBadge`), the stroke turns accent and an accent `DropShadowEffect` glow appears, and the
camera frame and both satellites collapse for the duration. The width grows
through the same `AnimatePillWidth` pipeline (a drag width term, not a second animation); `DragOver`
only refreshes the effect. A drop or a cancel (`DragLeave`/Esc) collapses it and rebuilds the frozen
face; the glow is detached when idle, and with `ClientAreaAnimation` off the states are static. A
successful drop (at least one record landed) fires the **swallow** micro-motion: 1 -> 0.96 (80 ms)
-> 1.05 (110 ms) -> 1 (90 ms), plus an accent flash that then detaches the glow.

**Drag preview (the shelf under the pill):** after the drag chrome has landed (`IslandMotion.DragPreviewOpenMs`,
160 ms; with animations off the panel opens on the next dispatcher turn, never inside the OLE
`DragEnter` callback), the same `IslandPopup` opens on the **Shelf** tab
(`TopBarWindow.ResolveHubTab`: a drag-active pill previews Shelf without changing `island.hubTab`), so
the user sees where the file will land. The preview is a normal **unpinned** panel opened through the
presence engine (`IslandPresence.BeginFileDrag`): the pill joins the panel and the existing
grow-from-the-pill open animation runs. While the drag lives, the hover-close grace is suppressed
(OLE owns the mouse and a pointer sample taken then is not evidence of a leave). The shelf body lights
up as the drop target (`ShelfDropGlow` overlay: accent tertiary fill + accent border, opacity pulse
0.35–1.0 every 720 ms, a static lit state with animations off) and a landed drop keeps the panel open
so the new chip is visible; only the records with an id above the pre-drop newest id slide/fade in
(one-shot, staggered for a batch). The pill is the guaranteed drop target; the open panel is wired
for the same drop too (its popup HWND already carries WPF's OLE drop registration — measured
`RegisterDragDrop` → `DRAGDROP_E_ALREADYREGISTERED`, layered with `WS_EX_NOACTIVATE`, so it cannot
steal activation; a live Explorer drag over the panel is still unexercised). A drag that leaves the
pill gets a 120 ms hand-off debounce (`IslandMotion.DragLeaveGraceDelayMs`, kept even with animations
off because the pill/panel drag events race across the popup's overlapping edge) before the preview
closes: a move back up cancels it, and moving down onto the panel hands the drag to the panel
instead. A canceled drag closes the preview with the normal close animation; a landed drop keeps it as
an ordinary hover panel that closes with the leave grace. A preview that opens during an existing pinned
panel does not hijack it (only a closed island opens the shelf preview).

While the island setting is off the pill collapses on every bar. On the **primary** bar, while the setting is on, the pill stays
(idle `Clipboard` or the last clip). On a **secondary** bar the pill is visible only while the face is not idle or while the panel
is open: music and the volume/brightness/battery meters. There is no clipboard list, no file drop, and no clipboard
satellite on the secondary; there is no second `IslandService`. The AppBar stays **30 DIP**;
multi-line states open under the same `IslandPopup`, aligned to the pill's **center**
(no second window, no nested `Popup`). Because `Placement=Bottom` aligns the left,
`IslandPopupAlign.HorizontalOffset` equates the panel center with the pill center. The offset
is written only from the **measured** width (XAML `Width` is not a fallback); while the width is 0
the panel stays at `Opacity=0`, then it is centered and appears. If the pill or the panel changes later
(DPI, body) it is recalculated and clamped to the work area; a hover-only pill-width change does not
re-align it (the centered pill keeps its screen center), so the panel does not drift under the pointer.
The popup is a separate window whose transparent shadow pad overlaps the pill's lower edge, so the
hover state is sampled geometrically (`IslandHoverRegion`: cursor vs pill/open-popup rects, not
`IsMouseOver`) and a 100 ms watch runs only while an unpinned panel is open. The panel's top edge overlaps the pill's bottom by ~2 DIP. While open, the pill's bottom
corners go flat, the fill matches the panel (`FlyoutSurface`), and the 1 DIP stroke takes the
panel rim's top color (`IslandFlyout.JoinRimColor` → `ApplyIslandJoinRim`, re-applied when the
album tint changes) so the fillet arc and the pill side meet without a color break. The panel
tint's first two gradient stops stay transparent, so the join and the tab strip stay on the flat
shared surface instead of showing a tinted step against the pill. The pill tooltip is suppressed
while the panel is open. On close it is a capsule again.

The pill is a capsule vertically centered inside the bar (`Height=24`, `CornerRadius=12`,
about **108 DIP** when idle — `IslandIdleDip`; a visible satellite grows it a little). Fill and the 1 px stroke are theme brushes
(`ControlFillColorDefaultBrush`, `ControlStrokeColorDefaultBrush`); hover is
`ControlFillColorSecondaryBrush` and the pill grows by ~4 DIP. Keyboard focus turns the stroke into the accent brush.
The same resources resolve in light and dark theme. Glyphs are Segoe MDL2 / Fluent (`AppIconFontFamily`).
Width changes take ~200 ms `CubicEase`. While `SystemParameters.ClientAreaAnimation` is off,
width, hover scale, and crossfade duration are 0. Idle width is the base; the track name,
the meter, and the satellites grow on top of it. The track-name ceiling also counts the satellite chrome
(`IslandPresence.TitleMaxWidth`).

- **Idle:** clipboard glyph (`E8C8`) + `island.idleLabel`. Default `clipboard` → **`Clipboard`**.
  `lastClip` → a 40-character preview of the last text record (if empty, `Clipboard` again). After
  150 ms of hover the same `IslandPopup` opens with the clipboard body (a short empty state if there is no record).
  About 300 ms after the pointer leaves. A click pins it. **Drag-and-drop** is only on the primary pill (`AllowDrop`).
- **Now playing** (`island.musicPresence`, default `persistent`): the pill widens into a note (`E90C`)
  + a single-line track name. When SMTC provided a thumbnail, the note is replaced by a 16 DIP rounded cover thumbnail (`IslandCoverThumb`, corner 4; only on the now-playing face, otherwise the glyph). There is no equalizer and no satellite on this face — the pill is cover/thumb + title only. The name is clipped with `IslandPresence.TitleMaxWidth` so it does not touch the left stats
  or the right strip. With `island.colorMode` = `album` the capsule itself takes a subtle cover tint (the
  joined pill takes the panel's top color — see **Color**), so the indicator reads as the album; idle and
  `neutral`/`system` keep the theme fill. After 150 ms of hover the music panel opens;
  about 300 ms after the pointer leaves (a pointer resting on the pill, or on the popup's transparent pad, keeps it open — see the hover note above). A click pins the panel. When the session ends the pill is idle. The chip tooltip, if SMTC
  provided position and duration, appends the same `m:ss / m:ss` text that is on the panel; a percent is not written.
- **Short duration** (`transient`): the pill stays idle. Playback, a track change, or play/pause
  opens the same panel for ~2.5 s. A click during that time pins it.
- **Volume / brightness / battery:** while the mixer or brightness panel is not open, an incoming change
  turns the pill into an icon + meter for ~1.6 s (`AccentFillColorDefaultBrush`), then it returns to the previous pill.
  A volume click opens the mixer, a brightness click opens the brightness flyout. The battery has no panel;
  the tooltip includes the percent. The battery appears when it is plugged in and once the percent drops below the threshold.
  The threshold is `island.lowBatteryPercent` (default 20, accepted 5–50). In island settings, `Low battery threshold`.
  When the threshold changes, the next drop can fire once again.
- **New copy / file drop:** a text face for a short time. There is no pulse on every copy; a successful drop gets the swallow micro-motion above.
- **Satellites:** HUD faces only, at most two, `FirstSatellite` / `SecondSatellite`. Music is `E90C`, clipboard is `E8C8`.
  A now-playing pill has no satellite. HUD + persistent music + the clipboard surface shows both (music, then clipboard). There is no clipboard satellite on the secondary bar. `transient` music is not a satellite. The same kind is not repeated.
  A click calls `ClickSatellite` and does not fall through to the pill click. While the pill is focused, Left/Right walks the selection among the visible satellites; the selected satellite has an accent stroke. Enter opens the selected satellite. If nothing is selected, Enter and Space are the pill's own click.

While `lastClip` is showing a real preview at idle, the pill is taken into its own window and only that
window is dropped from the frame with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`. The rest of the top bar
stays in the capture. On `Clipboard`, music, and the meter, the pill returns to the bar.

A pinned panel closes on an outside click, Esc, a second click on the pill, and `CloseFlyouts`. The pill
is `Focusable`. If no satellite is selected, Enter and Space open the same path as a click. In the music body, Left is the previous track and Right is the next
track. While the clipboard search box is focused, the arrows stay on it.
The panel opens by growing from the pill's width toward the body (top center, fade, ~200 ms); a gap
does not open. The scale is on the inner surface, not on the popup child (`PanelSurface`): WPF layout
uses the child's transformed corner, and if the scale stayed on the child the panel would open to the left of the pill and
stay there even after the scale returns to 1. Close is the reverse and a bit faster. Hub bodies (clipboard, wallpaper, music) crossfade inside the same popup in ~150 ms when the user picks a tab. On open, the last hub tab is restored from `island.hubTab` (`clipboard` \| `wallpaper` \| `music`); if that tab is **Music** and there is no SMTC session, it falls back to **Clipboard**. The **Music** tab is hidden when there is no session. On a **secondary** bar the hub shows **Music** only (no **Clipboard** / **Wallpaper** tabs; wallpaper is not attached). The clipboard affordance on the music body (primary only) switches to the **Clipboard** tab. While the music body is open the SMTC timeline is polled at ~250 ms while playing (1 s while paused);
`MediaSessionService` locally interpolates the frozen SMTC position (`MediaTimeline`), and `Changed`
fires only after a ≥200 ms move or when the published play/pause state changed (the poll also reconciles
an optimistic flip). A play/pause click is **instant**: `TogglePlayPause` publishes the flipped
`IsPlaying` before the SMTC command round-trip; the authoritative `PlaybackInfoChanged` (or the 1 s
optimistic window expiring) confirms or reverts it, and a playback-only change does not run the
metadata/artwork reload. The bar is visible when position and duration are both present. The
designed timeline draws a 5 DIP rounded track, a tinted fill and a 13 DIP thumb; the interaction layer is
a chrome-free `Slider`, and dragging seeks when the session `IsPlaybackPositionEnabled` is set (read-only
bar otherwise). While the panel is closed that read does not happen. If there is no duration, a percent
is not written.
The volume/brightness/battery meter's fill flows when the level changes.

**Panel look (`IslandFlyout`):** 480 DIP wide (`FlyoutRoot`; plus the 24 DIP shadow pad), 24 px corners, 16 px padding. A horizontal **hub tab strip** at the top switches bodies in place: **Clipboard** (clipboard glyph + label), **Wallpaper** (wallpaper glyph + label), **Music** (note + label; visible only while an SMTC session exists), **Shelf** (folder glyph + label; dropped-file chips with the chip menu and batch drag-out), **Prompter** (mic glyph `E720` + label; reading body, below), **Camera** (camera glyph + label; live mirror preview + on/off toggle + display-only mic row) and **Color** (palette glyph + label; session recent colors + screen picker: a full-monitor overlay with a cursor-centred 13×13 loupe whose centre pixel is the pick, the HEX/hint card below it flipping above near the monitor bottom, and Esc cancelling the overlay). Tab visibility rides the soft flags `island.shelfTab` / `island.prompterTab` / `island.cameraTab` / `island.colorpickerTab`; a saved tab whose flag is off falls back to Clipboard, and a file-drop face opens on Shelf. The active tab uses `ControlFillColorSecondaryBrush`. The strip keeps tab labels while they fit and folds them to icon-only (tooltips + automation names keep the names) once all seven tabs converge — the labeled strip does not fit the panel (~448 DIP usable). **Prompter body:** a multiline reading editor on top (placeholder "Type text, take it from the clipboard, or load a .txt…") over a control bar — play/pause round accent toggle with `Play`/`Pause` label swap, speed slider 0.5–3.0 with a live `1.0×` label, and `Back to start` / `.txt` / `Take from clipboard`. Play locks the editor read-only, sets its font to 17 DIP and auto-scrolls the editor's inner ScrollViewer at 66 DIP/s × (0.5–3.0) on a 16 ms timer (no ticks while the body is hidden or the panel closed; tab switching demotes to Paused); reaching the bottom returns to Editing and releases the `PrompterPinnedChanged(true→false)` pin; dragging the read-only text up/down scrolls the same offset without pausing. While there is nothing to scroll — the text fully fits in the editor or the scroll sits at the bottom — the play toggle is disabled with a reason tooltip (`Nothing to scroll; a longer text that does not fit the editor is required.` / `Scroll is at the bottom; use Back to start to start over.`) instead of a silent dead click. `Back to start` re-scrolls to the top and keeps the text; `.txt` opens a text-file dialog; `Take from clipboard` takes the newest text record of the island history (gentle no-op when empty). Music body: 112 px rounded cover with an album-tint bloom behind it, a Now Playing header with a state dot, an app-source chip with session chevrons (disabled with one session), 18 pt semibold title + 13 pt artist, the designed seek timeline, and a 48 DIP accent play button with 16 pt transport buttons. Animations (all gated by `ClientAreaAnimation`): play/pause glyph crossfade (140 ms), transport hover 1.06 / press 0.92 scale, a ring pulse on the play button while playing, cover-art crossfade on source change (220 ms), and the staggered entry (`IslandMotion.StaggerDelayMs`, 30 ms step; 0 with system animations off). Clipboard rows are 12 px-corner cards: a 32 px icon tile (text/file glyph), a preview line + a smaller meta line, and a delete button that shows only on hover (`Delete` key still works). **Wallpaper body** (`WallpaperGalleryView`): scrollable thumbnail grid in two groups — **WharfBar** (bundled pack next to the executable) and **Yours** (`%AppData%/WharfBar/wallpapers/`; `.jpg`/`.jpeg`/`.png`/`.webp`). Click a tile → `WallpaperService.TryApply` (documented `SPI_SETDESKWALLPAPER`; Windows fill/fit style is not changed). When `WallpaperTransitionPolicy` allows it, a brief **WorkerW-hosted** `WallpaperTransitionWindow` on the primary monitor (not topmost; behind desktop icons) shows the **previous** desktop image and fades opacity 1→0 (~450 ms, optional fixed blur) **before** the new wallpaper is committed; SPI runs when the fade completes — a deliberate crossfade-out of the old image, not a “reveal the new image” overlay. WorkerW/decode failure, `off`, or (with **Follow Windows animation** on) `SystemParameters.ClientAreaAnimation` false → instant SPI only; a new apply cancels any in-flight overlay. When `island.wallpaperAccent` is on (default), after SPI succeeds `WallpaperAccentApplier` runs (dominant color from the image → WharfBar `ApplicationAccentColorManager` + best-effort `SystemAccentWriter`; skipped if the color is out of range or decode fails). The tile matching the current desktop path (when it is under an allowed root) shows accent selection chrome; decode failures show a placeholder glyph but Apply still uses the file path. Footer **`Open folder`** opens the AppData wallpapers folder in Explorer (no in-hub add/delete). Transition mode and the follow-Windows toggle live on Settings → **Island** (`island.wallpaperTransition`, `island.wallpaperRespectSystemAnimation`). The panel is a unified silhouette with the pill: concave 10 DIP fillets beside the pill, drop shadow, gradient rim (`IslandSilhouette`, ADR-018); the popup is `AllowsTransparency` and padded 24 DIP for the shadow. There is no pin action on clipboard rows.

**Color (`island.colorMode`, ADR-018):** `album` (default) | `system` | `neutral`; unknown values fall back to `album`. The tint paints a full-panel gradient layer clipped to the island silhouette, starting at the fillet tips/join line, plus the outline rim, the cover bloom, the seek fill and the play/pause circle; with no tint those use `AccentFillColorDefaultBrush` and no tint layer. Alpha ladder (`IslandPalette` `PanelTint*` constants): join **0x28**, tab strip/header **0x32**, body **0x60**, bottom **0x8A** (deepened 16% toward black) — strong enough to read as the album and still ≥ AA for white primary text over the lightest allowed tint (asserted by `IslandTintTests`). The joined pill fill is the **same** composite (`FlyoutSurface.CreateTinted` with the join alpha), so the pill, fillets and panel top edge read as one surface (no seam); its rim is `IslandFlyout.JoinRimColor`. While the panel is closed but a session exists, the capsule gets a subtle tint over the bar's own capsule fill (`PillTintAmount` 0x1E ≈ 12 %, hover 0x36 ≈ 21 %); idle / `neutral` / `system` keep the plain theme fill. The play/pause circle uses the tint as its fill (hover lifted 14 % toward white) with a contrast-aware glyph (`IslandPalette.ReadableForeground`, luminance threshold 0.5 → white or near-black), and the ring pulse takes the same hue. Rules (`IslandPalette.Tint`): `neutral` → no tint; `system` → the system accent always; `album` → no tint unless a session exists, the system accent only when the cover has no usable color, else the cover color with its HSL lightness remapped into a readable 0.36–0.72 window. The cover color (`IslandPalette.Dominant`) is a saturation²/mid-brightness-weighted average of opaque pixels, tempered by the plain average, with a fallback to that average for gray/dark covers; computed on a 32×32 copy and cached by bitmap reference (`IslandTintSource`). The panel tints while an SMTC session exists (also when paused). The old body-clipped radial `AmbientGlow` is gone. The setting is the **`Island color`** card on the island settings page (ComboBox: Album color / System color / Neutral).

**Tint scope:** the panel tint belongs to the SMTC session, not to the visible hub body. Every
body (`Clipboard`, `Wallpaper`, `Shelf`, `Prompter`, `Camera`, `Color`) keeps the album tint while a
session exists (`SessionTint` / `ApplySessionTint` in `IslandFlyout`); switching tabs therefore no
longer drops the panel (and the joined pill) back to the neutral surface. A session end or a
`island.colorMode` change retints whatever body is open.

**Closed capsule tint:** the resting tint layer is normalized by the capsule plate's alpha
(`IslandPalette.ClosedPillAmount(plateAlpha, hover)`): the wallpaper-driven light-wallpaper plate on
a transparent primary bar is ~70 % opaque white and used to wash the album hue out while the same
album stayed visible on a theme plate (another bar / another machine). The tint cache key includes a
bar-palette revision, so a wallpaper change repaints the capsule instead of keeping a composite of
the old plate.

The rules are `IslandPresence` (duration, face, HUD priority, 150 ms hover, and the file-drag
preview panel lifetime: `BeginFileDrag` / `EndFileDrag`, which suppress the hover-close grace while
OLE owns the mouse). The window uses a one-shot timer; the duration is set with `NextDeadlineMs`.

## Right cluster — status strip (`StatusStrip`)

The default left-to-right order does **not** include `volume`, `brightness`, and `media` (the island HUD and
now playing carry them). Every catalog item (tray mirror, hidden icons, clock, notifications,
quick settings, volume, brightness, media, network, battery) is listed in the right-click menu and in Top bar settings;
a closed one can be turned on. The user's saved `statusItems` order is not stripped
and a hidden item is not turned back on by itself. **`Show all`** adds the missing ones beside their
catalog neighbor only when it is chosen.

A visible item, once the threshold (`MinimumHorizontalDragDistance` / `MinimumVerticalDragDistance`)
is crossed, is dragged on the bar: the item fades, a line in the accent color shows the gap it will be
dropped into, and on drop `topBar.statusItems` is written and applied to every monitor's bar.
A click under the threshold opens the flyout. The tray icon's own drag does not reorder the status strip.
The line is not a child of the strip; it does not shift the measure. Ordering does not use OLE `DoDragDrop`
(a transparent/tint bar is layered).

| id | Surface | Click behavior |
|----|---------|----------------|
| `trayOverflow` | `Hidden icons` (glyph `E70E`) | WharfBar pill (icon only); Windows overflow from inside it (Win+B) |
| `dateTime` | clock+date on one line; abbreviations follow resolved UI culture (`config.language` → `LocalizationService`) | `ClockPanelFlyout`: notification text + month calendar |
| `notifications` | bell glyph `EA8F` | Win+N (notification center) |
| `controlCenter` | quick settings glyph `E9CE` | `QuickSettingsFlyout` → **Main** tab |
| `volume` | volume glyph `E767` | `QuickSettingsFlyout` → **Mixer** tab |
| `brightness` | sun glyph `E706` | `BrightnessFlyout` (monitor cards, DDC/CI + WMI) |
| `media` | music-note glyph `E90B` | `MediaFlyout` (art + title/artist + controls + session picker); hidden when there is no session |
| `network` | network glyph `E701` | `QuickSettingsFlyout` → **Network** tab |
| `battery` | battery glyph + percent text | none (display only) |

## Tray pill

On the primary bar the chevron (`trayOverflow`) opens WharfBar's own pill. The pill, like the island
indicator, is a horizontal capsule (`FlyoutSurface`, corner radius 18, padding 4).
Only icons sit inside it; the application name is not written, the name is in the tooltip. After eight icons
it wraps downward, and the corners stay rounded. Icons in the mirror stay on the bar. A short click
on an icon is replayed on the real tray button with UI Automation (cursor-free, see below); the
popup it opens is moved under the clicked icon. Once the pointer
crosses the `MinimumHorizontalDragDistance` / `MinimumVerticalDragDistance` threshold, a drag
starts: dropping the icon on the chevron or on the open pill takes it off the bar
(mouse capture; OLE is not used on a layered bar). Dropping it from inside the pill back onto the right strip
puts the icon on the bar again. When empty the pill says **`no icon`**. The chevron at the end (`E70E`,
tooltip **`Open Windows hidden icons`**) opens Windows' hidden area, which cannot be enumerated, with `Win+B`.
The keys are in the `topBar.trayOverflowIcons` list. An icon that is not in the list (a new one)
stays visible. The record does not change the `statusItems` order.
There is no mirror on the secondary bar; the chevron there opens `Win+B` directly. A click is
replayed with UI Automation and **never moves the mouse**: left = the button's `InvokePattern`,
double = two invoked clicks, right = verified focus + the Menu key (`VK_APPS`, `Shift+F10`
fallback for owners that ignore it). The button element cached by the mirror read keeps working
while the tray is hidden (measured: a fresh walk returns 0 buttons, the cached element still
invokes), so the common click needs no peek and no work-area change; if the element died, one
bounded repair blip re-reads the tray and the gesture is replayed on the fresh element (an item no
longer in the tray is dropped from the mirror). A menu owner can warp the pointer to its real tray
icon when it anchors its menu (measured 0–20 ms after the Menu key); that warp is prevented —
`TrayPointerGuard` clips the pointer with `ClipCursor` to a 6 px box (`ClipMarginPx = 6`) right
before the Menu key and releases it after 150 ms (`ClipHoldMs = 150`), with a short escape watcher
(`TrayCursorWarp`) as the fallback — so the pointer stays where the user clicked (a high-integrity
owner that warps after the clip window cannot be restored; measured owners all warp within 20 ms).
The owner's popup/menu opens at the bottom edge and `TrayMenuReanchor` moves it under the
clicked icon: only a real popup (`WS_POPUP`,
no caption/thickframe, no `WS_EX_APPWINDOW`) or a `#32768` menu is moved — never a normal framed
application window — with one watcher process-wide so overlapping clicks cannot fight over a menu.

**State blip (the only tray read):** a parked auto-hide tray virtualizes its XAML buttons, so a
read taken while the bar is still in its 2 px sliver returns 0 buttons and proves nothing. The
read therefore drops `ABS_AUTOHIDE` (a work-area flip) and **verifies the effect** — it waits for
`IsFullyOnScreenNow` (bounded, `TrayAutoHideReveal.WaitFullyOnScreen`) and falls back to a verified
`SetWindowPos` (`TaskbarNative.TryEnsureTrayOnScreen`) before it reads; an unverified unpark skips
the read and classifies as `Inconclusive`, never as `ConfirmedEmpty`. The blip is gated by
`TrayStateBlipGate`: bootstrap of a never-filled mirror + a bounded retry series (3 blips, 30 s
apart), later only ≥10/≥20 min apart while the user is idle ≥3 min and no maximized/bottom-touching
window; never while a fullscreen foreground window covers the primary monitor (WharfBar's own
fullscreen colour picker included — a false "tray is empty" was measured there). Icons are captured
inside the blip. Every attempt logs `refresh outcome=… blip=True onScreen=… walkFailed=… count=…
attempt=…` to the tray diagnostics.
An empty read **never clears a populated mirror** — a stale icon beats a blank bar (and the
work-area jump needed to re-read it); the snapshot resets only when `Shell_TrayWnd` changes. When
the mirror is empty the chevron tooltip is honest about it: verified empty → "No tray icons on the
taskbar — open Windows' hidden icons (Win+B)"; not read yet → "Reading tray icons…". An empty bar
is therefore no longer silent (ADR-009 keeps hidden/overflow icons out of the mirror).

## Status strip right-click menu

`TopBarWindow.BuildStatusItemsMenu`:
- **Show/hide items** — checkboxes for the whole catalog (closed ones are in the list too)
- **`Background`** (submenu): Mica / Transparent / Tint radios + **`Opacity` slider**
  (enabled only for Tint, 0–100, debounced save)
- If an item was clicked: **`Move left` / `Move right`**
- **`Show all`** — turns on missing catalog items; dimmed if all of them are on
- **`Reset to default`** — `TopBarStatusItemIds.DefaultOrder`

## Quick settings flyout

`QuickSettingsFlyout` — a single Fluent popup (`AllowsTransparency=true`, `StaysOpen=true`; unrelated to the main window's
Mica); the Main / Mixer /
Network tabs are shared (ADR-006). Mixer: default device + dropdown + application
sessions. Network: connection card + live ↓/↑ traffic row + `Properties` section
(adapter, kind, IPv4/IPv6, gateway, DNS, MAC, link speed — `NetworkDetailsService`,
2 s poll while the page is open; Windows network-properties layout). The open point picks the tab from the icon that was clicked. The popup leaves a slight gap
under the bar with `VerticalOffset=10`; a 12 px gap on the right edge too (root Border `Margin="0,0,12,0"`,
it opens clamped to the right edge of the screen). The surface has no border, rounded corners + semi-transparent
(`FlyoutSurface`: theme `ApplicationBackgroundBrush` color, 85% alpha — the content behind is slightly
visible; refreshed on every open). On open, a Win11-style
**top-to-bottom** slide+fade animation (`AnimateFlyoutOpen`: it slides down ~10 px from under the bar,
170 ms CubicEase EaseOut); on close, the **exact reverse** of the open animation
(`AnimateFlyoutClose`: the content slides up/toward under the bar + fades, 110 ms — a touch
faster, then `IsOpen=false`). Close paths (`CloseFlyouts` /
`CloseQuickSettings` / `CloseBrightness`): outside click → `WM_ACTIVATEAPP` (topbar WndProc),
a click on the top bar (except the anchor buttons — those toggle/re-target), ESC
(`OnTopBarPreviewKeyDown`), toggle, AppMenu/tray/notification buttons. If the popup opens
again, a pending close animation is cancelled with `CancelPendingClose` (a reopened
popup does not close). The brightness flyout uses the same open/close animations.

While the panel is open, a page change (`ShowPage`, `QuickSettingsPageMotion`) slides and fades in 220 ms `CubicEase`
EaseOut. Forward (Main → Mixer / Network / Wi-Fi / Bluetooth / `Project`)
takes the outgoing page to the left and brings the new one in 20 DIP from the right; back is the exact reverse. Pages at the same depth
(such as Mixer ↔ Network) only crossfade. `PageHost` clips overflow with `ClipToBounds`;
the outgoing page does not receive clicks. If a new page is chosen before the transition finishes, the previous pair
is dropped and the animation starts over. This slide is absent on the flyout's first open — the open
animation already shows the panel.

### Wi-Fi page (network list + password panel)

`WifiService` (wlanapi.dll) live network list: SSID, signal bars (`WifiService.SignalBars`),
security, connected / saved-profile marking (2 s poll; only while the page is open). The same SSID
merges into one row. While the radio is off the list is empty and the summary says `Wi-Fi is off`.
A click on a protected network → a password panel on the same page (SSID, password box, `Connect` / `Cancel`, error text);
the password profile is plaintext (`protected=false`). After `WlanConnect` is accepted, it waits until the SSID
actually connects (at most 8 s); if it does not, the panel does not close. A click on the connected row
calls `WlanDisconnect`.
The error text also stays under the summary while the password panel is closed. Hidden and enterprise networks are not
connected from here. `Open Wi-Fi settings` → Windows Settings.

### Bluetooth page (device list)

`BluetoothService` lists paired classic and LE devices (`BluetoothDevice` +
`BluetoothLEDevice` selectors; the same address is one row). When the page opens the current list is drawn immediately.
Connected ones are marked with `Aep.IsConnected` or `ConnectionStatus`. A row
click does not connect; `Open Bluetooth settings` → Windows Settings. The radio on/off summary
is also updated while the page is open, when the tile changes.

### Project page (4 modes)

`ProjectionService` + `QueryDisplayConfig` (`QDC_DATABASE_CURRENT` = 0x4, `ref` count pointers) shows the current topology
as selected; a click applies it with `SetDisplayConfig`: **`PC screen only` / `Duplicate` /
`Extend` / `Second screen only`**. On a single display the other modes are dimmed and it says `No second display`;
if apply fails, `This mode could not be applied`.

### Energy saver tile

The `batterysaver` tile lights while the API, `SystemStatusFlag`, or a read-back of the `Always use energy saver` switch is on. A click flips that switch through UI Automation; the Settings window does not stay on screen. If the write does not stick, the tile does not light.

### Night light tile (real state)

`NightLightService` (CloudStore best-effort) reads the real state; the tile paints its active color from that.
A click toggles if the blob can be written. If the blob is not recognized the tile does not lie and
`ms-settings:nightlight` opens.

### Tile animations

On a tile active-state change, an **in-place** animation: the icon "pops" for 90 ms (active 1.15× / inactive 0.9×,
AutoReverse + CubicEase) + a 250 ms color transition (`ColorAnimation`, QuadraticEase) — the background
is eased between accent and base. Disabled tiles are at 0.45 opacity via `QsTileDisabled`.

## Brightness flyout

`BrightnessFlyout` — Monitorian style: master `All displays` slider + independent monitor
cards; a 50 ms debounced background queue. `MonitorBrightnessService` + `MonitorNative`
(DDC/CI + WMI hybrid).

## Media flyout

`MediaFlyout` — now-playing media through Windows SMTC (`MediaSessionService`, ADR-008):
rounded art (if there is no art, the music glyph `E90C` is the placeholder) + title (bold, one line,
application-name fallback if metadata could not be read) + artist; under that, `E892` `Previous` /
`E768`–`E769` `Play`-`Pause` (the glyph changes live with the state) / `E893` `Next`.
The flyout is a fixed **340 px** wide (`MinWidth=MaxWidth=340`) — the width does not jump when the content changes
(for example the session name).
If more than one session is active a picker row appears at the top: `E76B` ‹ application name › `E76C`
chevrons advance the selected session around the ring; controls go to the selected application.
The application name is resolved from the AUMID (`MediaAppNameResolver`, layered):
a Start Menu shortcut AUMID match yields the name Windows shows (for example Zen's
`F0DC299D809B9700` → "Zen", YouTube Music Desktop's `com.github.th-ch.youtube-music` →
"YouTube Music"); if there is no match, the running process's `FileDescription` (for example `chrome.exe` →
"Google Chrome"); if that is missing too, a simplified exe name (for example "Microsoft.ZuneMusic").
The selection policy **keeps the current session until it disappears** (`MediaSessionSelector`); only a
session that transitions into Playing (a player really starts; SMTC `Changing` during a track skip counts
as playing), the user's chevron pick, or the current session leaving changes it. A paused/stopped session
never steals the panel and is not dropped from the ring mid-skip (that used to hand the panel to the other
app), and a newly appearing playing session is promoted only when the current one is not itself playing.
On removal the fallback prefers a still-playing session. `SessionsChanged` only reconciles membership and
playback state; the full metadata/artwork reload runs only when the selected session changed (or on
`MediaPropertiesChanged`), while `PlaybackInfoChanged` publishes the playback state/timeline immediately.

- The top bar `media` item is visible only while a session exists (`_mediaActive`; the Battery/Volume
  pattern) — it disappears when the session ends; permanent show/hide + ordering from the right-click menu.
- Tooltip: "Title – Artist" (application name if there is no metadata). If SMTC provided position and duration, the same `m:ss / m:ss` is appended.
- The flyout does **not** close when the session ends; it returns to the `Nothing playing` text (closing is only
  the user: outside click / ESC / click again — the same animated close as QS/Brightness).
- On open the `FlyoutSurface` surface is refreshed; the same top-to-bottom slide+fade animation as QS.
- When the session (source) changes, the content arrives with a directional slide + fade: chevron › from the right, ‹
  from the left, a directionless fade on an automatic change (~150 ms, CubicEase EaseOut); track/play-stop updates
  inside the same session are not animated.
- If SMTC cannot be started the service stays passive (the item is not visible); it tries again after 10 s.

## Gotchas

- A background-mode change can recreate the window (the right-click menu opens again).
- `AllowsTransparency` is set from `BackgroundMode` in the constructor — it is not written into XAML.
- Tooltip fonts are Segoe UI (`AppTextFontFamily`) — there is no MDL2 tofu problem.
