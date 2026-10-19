# Screen: Top bar

`src/WharfBar/Views/TopBarWindow.xaml` (+ `.xaml.cs`).

Height 30 DIP, an **AppBar** (work area reserved), always on top. A separate bar is built
for each enabled monitor (per-monitor; each bar is an AppBar on its own monitor, and the bar is moved onto that monitor
before it is registered). Visibility is turned on or off per monitor through the `monitors` config.
Background mode comes from config: `mica` (default, `AllowsTransparency=false`),
`transparent` / `tint` (`AllowsTransparency=true` ÔÇö never together with Mica).

**Text contrast (`TopBarContrastPalette`):** the bar paints via its own `Bar.*` window
resources (`Bar.TextPrimary`, `Bar.HitFill`, `Bar.PillFill`, `Bar.PillFillHover`,
`Bar.PillStroke`, `Bar.SubtleFill`) instead of the WPF-UI brush keys directly. In Mica/Tint
those mirror the theme brushes exactly as before. In `transparent` on the **primary** bar the
palette is driven by the wallpaper: the strip behind the bar is decoded small once per
wallpaper path and measures average luma (`WallpaperTopStrip`, threshold 0.55) ÔÇö a light strip
switches the bar to dark text + white plates, a dark strip keeps light text. Fully automatic,
no user setting; wallpaper reloads on sample cache change (3 s watch while transparent, hub
apply events, `WM_SETTINGCHANGE`). Flyout popups keep the theme look: they resolve resources
through the shared WPF-UI dictionaries, not the bar's `Bar.*` keys.

## Left cluster

- **Logo** (geometry drawing, not a font glyph ÔÇö it does not disappear on Mica). `logoStyle` picks the shape: `wharfbar` (default; the solid small Dusk mark ÔÇö sun + pier, no plank gaps, no sea lines) or `windows` (four panes). Both are one colour ÔÇö `Bar.TextPrimary` (`TopBarContrastPalette`: theme-following, or wallpaper-driven on a transparent bar) ÔÇö in an 18 DIP box on the shared 128 grid (`BrandLogo.BarGlyph`, ADR-019); the bar stays 30 DIP. **Left and right click** open the menu. Launchpad does not open from this icon; it is on the dock tile (ADR-016). The menu opens flush to the icon's left edge; `OnAppMenuLoaded` zeros the left margin of WPF-UI's 30 px effect margin ÔÇö so the popup window is not clamped to the screen edge and does not shift the menu to the right:
  - **`Ayarlar (PC)` (Settings (PC))** ÔåÆ `ms-settings:` ┬À **`Profil` (Profile)** ÔåÆ `ms-settings:yourinfo` ┬À **`Bilgisayar hakk─▒nda` (About this PC)** ÔåÆ `ms-settings:about`
  - **`Bilgisayar─▒ kilitle` (Lock)** / **`Uyku` (Sleep)** ÔåÆ `PowerActions` (LockWorkStation / SetSuspendState)
  - **`Kapat` (Shut down)** / **`Yeniden ba┼şlat` (Restart)** ÔåÆ `PowerActions` (shutdown.exe `/s|/r /t 0`)
  - **`WharfBar Ayarlar─▒` (WharfBar Settings)** ÔåÆ [`settings.md`](settings.md)
  - **`WharfBar hakk─▒nda` (About WharfBar)** ÔåÆ `AboutWindow`
  - **`WharfBar'─▒ kapat` (Quit WharfBar)** ÔåÆ a clean exit (taskbar restore + `ABM_REMOVE`)

System stats (`CPU ┬À GPU ┬À RAM`) are in the same left cluster, on a single line. The sparkline of recent samples and the network speed (Ôåô/Ôåæ bytes/s) are on a card that opens when the pointer is over the text; if the line were placed under the text the 30 DIP bar would grow. If there is no GPU counter the GPU row on the card is hidden. There is no temperature. Network speed uses the same 4 s interval as `SystemStatsService`; the sample is taken off the UI and the bar is not refreshed when the text did not change. If stats are off, sampling stops. The music visualizer runs only while the now-playing face is up. The Bluetooth connection notice looks every 10 s; if the list is the same the pill is not refreshed.

## Center ÔÇö island pill (`IslandPill`, ADR-010 + ADR-011 + ADR-012)

While music is playing and the visualizer is on in the plugin gallery, the pill shows five bars of the
default audio device's peak. If the peak cannot be read the bars stay flat. When a paired
Bluetooth device connects, its name stays on the pill for a short time; the Bluetooth tile in quick settings
stays separate. The camera mirror opens from the gallery: the frame is inside the pill, a denied or busy camera
leaves text, and there is no fake frame. Widgets (clock, calendar) are on the desktop, not on the pill.

**Motion:** the pill width morphs between idle, music, meter, and satellite states (quintic ease-out on grow, cubic on shrink, 140ÔÇô260 ms); a face swap dips and restores content opacity and pops the glyph; satellites scale/fade in and out; hover lifts the fill, press scales the pill to 0.97 (80 ms, cubic) and release springs back (`PressOut` 220 ms `BackEase`), a focused pill shows the accent ring, and a hovered satellite gets a subtle fill. Visualizer bars attack smoothly and release per bar. With system animations off (`SystemParameters.ClientAreaAnimation`) every transition is instant. Nothing animates while the pill is idle.

While the island setting is off the pill collapses on every bar. On the **primary** bar, while the setting is on, the pill stays
(idle `Pano` (Clipboard) or the last clip). On a **secondary** bar the pill is visible only while the face is not idle or while the panel
is open: music and the volume/brightness/battery meters. There is no clipboard list, no file drop, and no clipboard
satellite on the secondary; there is no second `IslandService`. The AppBar stays **30 DIP**;
multi-line states open under the same `IslandPopup`, aligned to the pill's **center**
(no second window, no nested `Popup`). Because `Placement=Bottom` aligns the left,
`IslandPopupAlign.HorizontalOffset` equates the panel center with the pill center. The offset
is written only from the **measured** width (XAML `Width` is not a fallback); while the width is 0
the panel stays at `Opacity=0`, then it is centered and appears. If the pill or the panel changes later
(DPI, body, hover) it is recalculated and clamped to the work area. The panel's top edge overlaps the pill's bottom by ~2 DIP. While open, the pill's bottom
corners go flat and the fill matches the panel (`FlyoutSurface`); on close it is a capsule again.

The pill is a capsule vertically centered inside the bar (`Height=24`, `CornerRadius=12`,
about **108 DIP** when idle — `IslandIdleDip`; a visible satellite grows it a little). Fill and the 1 px stroke are theme brushes
(`ControlFillColorDefaultBrush`, `ControlStrokeColorDefaultBrush`); hover is
`ControlFillColorSecondaryBrush` and the pill grows by ~4 DIP. Keyboard focus turns the stroke into the accent brush.
The same resources resolve in light and dark theme. Glyphs are Segoe MDL2 / Fluent (`AppIconFontFamily`).
Width changes take ~200 ms `CubicEase`. While `SystemParameters.ClientAreaAnimation` is off,
width, hover scale, and crossfade duration are 0. Idle width is the base; the track name,
the meter, and the satellites grow on top of it. The track-name ceiling also counts the satellite chrome
(`IslandPresence.TitleMaxWidth`).

- **Idle:** clipboard glyph (`E8C8`) + `island.idleLabel`. Default `clipboard` → **`Pano` (Clipboard)**.
  `lastClip` → a 40-character preview of the last text record (if empty, `Pano` (Clipboard) again). After
  150 ms of hover the same `IslandPopup` opens with the clipboard body (a short empty state if there is no record).
  About 300 ms after the pointer leaves. A click pins it. **Drag-and-drop** is only on the primary pill (`AllowDrop`).
- **Now playing** (`island.musicPresence`, default `persistent`): the pill widens into a note (`E90C`)
  + a single-line track name. When SMTC provided a thumbnail, the note is replaced by a 16 DIP rounded cover thumbnail (`IslandCoverThumb`, corner 4; only on the now-playing face, otherwise the glyph). The five visualizer bars are 2.5 DIP wide. The name is clipped with `IslandPresence.TitleMaxWidth` so it does not touch the left stats
  or the right strip. After 150 ms of hover the music panel opens;
  about 300 ms after the pointer leaves. A click pins the panel. When the session ends the pill is idle. The chip tooltip, if SMTC
  provided position and duration, appends the same `m:ss / m:ss` text that is on the panel; a percent is not written.
- **Short duration** (`transient`): the pill stays idle. Playback, a track change, or play/pause
  opens the same panel for ~2.5 s. A click during that time pins it.
- **Volume / brightness / battery:** while the mixer or brightness panel is not open, an incoming change
  turns the pill into an icon + meter for ~1.6 s (`AccentFillColorDefaultBrush`), then it returns to the previous pill.
  A volume click opens the mixer, a brightness click opens the brightness flyout. The battery has no panel;
  the tooltip includes the percent. The battery appears when it is plugged in and once the percent drops below the threshold.
  The threshold is `island.lowBatteryPercent` (default 20, accepted 5–50). In island settings, `Düşük pil eşiği` (Low battery threshold).
  When the threshold changes, the next drop can fire once again.
- **New copy / file drop:** a text face for a short time. There is no pulse on every copy; the drop pulse stops.
- **Satellites:** at most two, `FirstSatellite` / `SecondSatellite`. Music is `E90C`, clipboard is `E8C8`.
  Now playing + the clipboard surface is the clipboard satellite only. HUD + persistent music + the clipboard surface shows both (music, then clipboard). There is no clipboard satellite on the secondary bar. `transient` music is not a satellite. The same kind is not repeated.
  A click calls `ClickSatellite` and does not fall through to the pill click. While the pill is focused, Left/Right walks the selection among the visible satellites; the selected satellite has an accent stroke. Enter opens the selected satellite. If nothing is selected, Enter and Space are the pill's own click.

While `lastClip` is showing a real preview at idle, the pill is taken into its own window and only that
window is dropped from the frame with `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`. The rest of the top bar
stays in the capture. On `Pano` (Clipboard), music, and the meter, the pill returns to the bar.

A pinned panel closes on an outside click, Esc, a second click on the pill, and `CloseFlyouts`. The pill
is `Focusable`. If no satellite is selected, Enter and Space open the same path as a click. In the music body, Left is the previous track and Right is the next
track. While the clipboard search box is focused, the arrows stay on it.
The panel opens by growing from the pill's width toward the body (top center, fade, ~200 ms); a gap
does not open. The scale is on the inner surface, not on the popup child (`PanelSurface`): WPF layout
uses the child's transformed corner, and if the scale stayed on the child the panel would open to the left of the pill and
stay there even after the scale returns to 1. Close is the reverse and a bit faster. Hub bodies (clipboard, wallpaper, music) crossfade inside the same popup in ~150 ms when the user picks a tab. On open, the last hub tab is restored from `island.hubTab` (`clipboard` \| `wallpaper` \| `music`); if that tab is **Müzik** and there is no SMTC session, it falls back to **Pano**. The **Müzik** tab is hidden when there is no session. On a **secondary** bar the hub shows **Müzik** only (no **Pano** / **Duvar kağıdı** tabs; wallpaper is not attached). The clipboard affordance on the music body (primary only) switches to the **Pano** tab. While the music body is open the SMTC timeline is re-read about once a second;
the bar is visible when position and duration are both present. If the session `IsPlaybackPositionEnabled`
is set, a slider sits in the bar's place and dragging seeks. While it is closed that read does not happen. If there is no duration, a percent is not written.
The volume/brightness/battery meter's fill flows when the level changes.

**Panel look (`IslandFlyout`):** 420 DIP wide, 24 px corners, 16 px padding. A horizontal **hub tab strip** at the top switches bodies in place: **Pano** (clipboard glyph + label), **Duvar kağıdı** (wallpaper glyph + label), **Müzik** (note + label; visible only while an SMTC session exists). The active tab uses `ControlFillColorSecondaryBrush`. Music body: 88 px cover (corner 14, note glyph placeholder), 16 pt semibold title, 13 pt artist, a round 44 DIP accent play button, a 4 DIP progress bar, and an ambient glow behind the content (radial gradient, top-left, 35% alpha of the tint; hidden with no tint). On open the cover, the title block, and the transport row reveal in order, each delayed by `IslandMotion.StaggerDelayMs` (30 ms step; 0 with system animations off). Clipboard rows are 12 px-corner cards: a 32 px icon tile (text/file glyph), a preview line + a smaller meta line, and a delete button that shows only on hover (`Delete` key still works). **Wallpaper body** (`WallpaperGalleryView`): scrollable thumbnail grid in two groups — **WharfBar** (bundled pack next to the executable) and **Senin** (`%AppData%/WharfBar/wallpapers/`; `.jpg`/`.jpeg`/`.png`/`.webp`). Click a tile → `WallpaperService.TryApply` (documented `SPI_SETDESKWALLPAPER`; Windows fill/fit style is not changed). When `WallpaperTransitionPolicy` allows it, a brief **WorkerW-hosted** `WallpaperTransitionWindow` on the primary monitor (not topmost; behind desktop icons) shows the **previous** desktop image and fades opacity 1→0 (~450 ms, optional fixed blur) **before** the new wallpaper is committed; SPI runs when the fade completes — a deliberate crossfade-out of the old image, not a “reveal the new image” overlay. WorkerW/decode failure, `off`, or (with **Windows animasyonunu takip et** on) `SystemParameters.ClientAreaAnimation` false → instant SPI only; a new apply cancels any in-flight overlay. When `island.wallpaperAccent` is on (default), after SPI succeeds `WallpaperAccentApplier` runs (dominant color from the image → WharfBar `ApplicationAccentColorManager` + best-effort `SystemAccentWriter`; skipped if the color is out of range or decode fails). The tile matching the current desktop path (when it is under an allowed root) shows accent selection chrome; decode failures show a placeholder glyph but Apply still uses the file path. Footer **`Klasörü aç`** opens the AppData wallpapers folder in Explorer (no in-hub add/delete). Transition mode and the follow-Windows toggle live on Settings → **Ada** (`island.wallpaperTransition`, `island.wallpaperRespectSystemAnimation`). The panel is a unified silhouette with the pill: concave 10 DIP fillets beside the pill, drop shadow, gradient rim (`IslandSilhouette`, ADR-018); the popup is `AllowsTransparency` and padded 24 DIP for the shadow. There is no pin action on clipboard rows.

**Color (`island.colorMode`, ADR-018):** `album` (default) | `system` | `neutral`; unknown values fall back to `album`. The tint colors the pill's visualizer bars, the panel's ambient glow, and the progress fill; with no tint they use `AccentFillColorDefaultBrush` and no glow. Rules (`IslandPalette.Tint`): `neutral` → no tint; `system` → the system accent always; `album` → no tint unless music is playing, the system accent if the cover has no usable color or its luminance is below 0.12 or above 0.90, else the cover color. The cover color (`IslandPalette.Dominant`) is a saturation- and mid-brightness-weighted average of opaque pixels with saturation ≥ 0.25, computed on a 32×32 copy and cached by bitmap reference (`IslandTintSource`). The panel only tints while an SMTC session exists. The setting is the **`Ada rengi`** (Island color) card on the island settings page (ComboBox: Albüm rengi / Sistem rengi / Nötr).

The rules are `IslandPresence` (duration, face, HUD priority, 150 ms hover). The window uses a one-shot timer; the duration is set with `NextDeadlineMs`.

## Right cluster — status strip (`StatusStrip`)

The default left-to-right order does **not** include `volume`, `brightness`, and `media` (the island HUD and
now playing carry them). Every catalog item (tray mirror, hidden icons, clock, notifications,
quick settings, volume, brightness, media, network, battery) is listed in the right-click menu and in Top bar settings;
a closed one can be turned on. The user's saved `statusItems` order is not stripped
and a hidden item is not turned back on by itself. **`Tümünü göster` (Show all)** adds the missing ones beside their
catalog neighbor only when it is chosen.

A visible item, once the threshold (`MinimumHorizontalDragDistance` / `MinimumVerticalDragDistance`)
is crossed, is dragged on the bar: the item fades, a line in the accent color shows the gap it will be
dropped into, and on drop `topBar.statusItems` is written and applied to every monitor's bar.
A click under the threshold opens the flyout. The tray icon's own drag does not reorder the status strip.
The line is not a child of the strip; it does not shift the measure. Ordering does not use OLE `DoDragDrop`
(a transparent/tint bar is layered).

| id | Surface | Click behavior |
|----|---------|----------------|
| `trayOverflow` | `Gizli simgeler` (Hidden icons) (glyph `E70E`) | WharfBar pill (icon only); Windows overflow from inside it (Win+B) |
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
it wraps downward, and the corners stay rounded. Icons in the mirror stay on the bar. A short click on an icon
is forwarded to that tray icon. Once the pointer
crosses the `MinimumHorizontalDragDistance` / `MinimumVerticalDragDistance` threshold, a drag
starts: dropping the icon on the chevron or on the open pill takes it off the bar
(mouse capture; OLE is not used on a layered bar). Dropping it from inside the pill back onto the right strip
puts the icon on the bar again. When empty the pill says **`simge yok` (no icon)**. The chevron at the end (`E70E`,
tooltip **`Windows gizli simgelerini aç` (Open Windows hidden icons)**) opens Windows' hidden area, which cannot be enumerated, with `Win+B`.
The keys are in the `topBar.trayOverflowIcons` list. An icon that is not in the list (a new one)
stays visible. The record does not change the `statusItems` order.
There is no mirror on the secondary bar; the chevron there opens `Win+B` directly.

## Status strip right-click menu

`TopBarWindow.BuildStatusItemsMenu`:
- **Show/hide items** — checkboxes for the whole catalog (closed ones are in the list too)
- **`Arka plan` (Background)** (submenu): Mica / Transparent / Tint radios + **`Opaklık` (Opacity) slider**
  (enabled only for Tint, 0–100, debounced save)
- If an item was clicked: **`Sola taşı` (Move left) / `Sağa taşı` (Move right)**
- **`Tümünü göster` (Show all)** — turns on missing catalog items; dimmed if all of them are on
- **`Varsayılana sıfırla` (Reset to default)** — `TopBarStatusItemIds.DefaultOrder`

## Quick settings flyout

`QuickSettingsFlyout` — a single Fluent popup (`AllowsTransparency=true`, `StaysOpen=true`; unrelated to the main window's
Mica); the Main / Mixer /
Network tabs are shared (ADR-006). Mixer: default device + dropdown + application
sessions. Network: connection card + live ↓/↑ traffic row + `Özellikler` (Properties) section
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
EaseOut. Forward (Main → Mixer / Network / Wi-Fi / Bluetooth / `Yansıt` (Project))
takes the outgoing page to the left and brings the new one in 20 DIP from the right; back is the exact reverse. Pages at the same depth
(such as Mixer ↔ Network) only crossfade. `PageHost` clips overflow with `ClipToBounds`;
the outgoing page does not receive clicks. If a new page is chosen before the transition finishes, the previous pair
is dropped and the animation starts over. This slide is absent on the flyout's first open — the open
animation already shows the panel.

### Wi-Fi page (network list + password panel)

`WifiService` (wlanapi.dll) live network list: SSID, signal bars (`WifiService.SignalBars`),
security, connected / saved-profile marking (2 s poll; only while the page is open). The same SSID
merges into one row. While the radio is off the list is empty and the summary says `Wi-Fi kapalı` (Wi-Fi is off).
A click on a protected network → a password panel on the same page (SSID, password box, `Bağlan` (Connect) / `İptal` (Cancel), error text);
the password profile is plaintext (`protected=false`). After `WlanConnect` is accepted, it waits until the SSID
actually connects (at most 8 s); if it does not, the panel does not close. A click on the connected row
calls `WlanDisconnect`.
The error text also stays under the summary while the password panel is closed. Hidden and enterprise networks are not
connected from here. `Wi-Fi ayarlarını aç` (Open Wi-Fi settings) → Windows Settings.

### Bluetooth page (device list)

`BluetoothService` lists paired classic and LE devices (`BluetoothDevice` +
`BluetoothLEDevice` selectors; the same address is one row). When the page opens the current list is drawn immediately.
Connected ones are marked with `Aep.IsConnected` or `ConnectionStatus`. A row
click does not connect; `Bluetooth ayarlarını aç` (Open Bluetooth settings) → Windows Settings. The radio on/off summary
is also updated while the page is open, when the tile changes.

### Project page (4 modes)

`ProjectionService` + `QueryDisplayConfig` (`QDC_DATABASE_CURRENT` = 0x4, `ref` count pointers) shows the current topology
as selected; a click applies it with `SetDisplayConfig`: **`Yalnızca PC ekranı` (PC screen only) / `Çoğalt` (Duplicate) /
`Genişlet` (Extend) / `Yalnızca 2. ekran` (Second screen only)**. On a single display the other modes are dimmed and it says `İkinci ekran yok` (No second display);
if apply fails, `Bu mod uygulanamadı` (This mode could not be applied).

### Energy saver tile

The `batterysaver` tile lights while the API, `SystemStatusFlag`, or a read-back of the `Her zaman enerji tasarrufu kullan` (Always use energy saver) switch is on. A click flips that switch through UI Automation; the Settings window does not stay on screen. If the write does not stick, the tile does not light.

### Night light tile (real state)

`NightLightService` (CloudStore best-effort) reads the real state; the tile paints its active color from that.
A click toggles if the blob can be written. If the blob is not recognized the tile does not lie and
`ms-settings:nightlight` opens.

### Tile animations

On a tile active-state change, an **in-place** animation: the icon "pops" for 90 ms (active 1.15× / inactive 0.9×,
AutoReverse + CubicEase) + a 250 ms color transition (`ColorAnimation`, QuadraticEase) — the background
is eased between accent and base. Disabled tiles are at 0.45 opacity via `QsTileDisabled`.

## Brightness flyout

`BrightnessFlyout` — Monitorian style: master `Tüm Ekranlar` (All displays) slider + independent monitor
cards; a 50 ms debounced background queue. `MonitorBrightnessService` + `MonitorNative`
(DDC/CI + WMI hybrid).

## Media flyout

`MediaFlyout` — now-playing media through Windows SMTC (`MediaSessionService`, ADR-008):
rounded art (if there is no art, the music glyph `E90C` is the placeholder) + title (bold, one line,
application-name fallback if metadata could not be read) + artist; under that, `E892` `Önceki` (Previous) /
`E768`–`E769` `Oynat`-`Duraklat` (Play-Pause) (the glyph changes live with the state) / `E893` `Sonraki` (Next).
The flyout is a fixed **340 px** wide (`MinWidth=MaxWidth=340`) — the width does not jump when the content changes
(for example the session name).
If more than one session is active a picker row appears at the top: `E76B` ‹ application name › `E76C`
chevrons advance the selected session around the ring; controls go to the selected application.
The application name is resolved from the AUMID (`MediaAppNameResolver`, layered):
a Start Menu shortcut AUMID match yields the name Windows shows (for example Zen's
`F0DC299D809B9700` → "Zen", YouTube Music Desktop's `com.github.th-ch.youtube-music` →
"YouTube Music"); if there is no match, the running process's `FileDescription` (for example `chrome.exe` →
"Google Chrome"); if that is missing too, a simplified exe name (for example "Microsoft.ZuneMusic").
The selection policy is the **most recently active** session (`MediaSessionSelector`).

- The top bar `media` item is visible only while a session exists (`_mediaActive`; the Battery/Volume
  pattern) — it disappears when the session ends; permanent show/hide + ordering from the right-click menu.
- Tooltip: "Title – Artist" (application name if there is no metadata). If SMTC provided position and duration, the same `m:ss / m:ss` is appended.
- The flyout does **not** close when the session ends; it returns to the `Çalan medya yok` (Nothing playing) text (closing is only
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
