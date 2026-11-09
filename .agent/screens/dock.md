# Screen: Dock

`src/WharfBar/Views/DockWindow.xaml` (+ `.xaml.cs`), `Views/Controls/DockStrip.cs`,
`Views/Controls/DockItemCell.xaml`.

A Windows 11 taskbar-style strip. The default is the bottom edge. **`Left edge`** in settings
(`dock.position` = `left`) stacks the plate vertically on the left edge, reserves a work area of that thickness
with `ABE_LEFT`, and shifts auto-hide to the left. Bottom positions (`bottom-left` /
`bottom-center` / `bottom-right`) stay horizontal. The plate is **not** an AppBar (ADR-001); it is a `Topmost` overlay,
placed with physical-pixel `SetWindowPos`, and WPF `Left/Top/Width` is not used. Auto-hide +
hit-test (`DockAutoHideController` + `DockHitTest`); it hides in a fullscreen app
(`ABN_FULLSCREENAPP`, even when auto-hide is off — the strip is topmost, so it would stay above the video).

While auto-hide is **off**, a sibling unpainted `ABE_BOTTOM` (`DockWorkAreaReservation`, a raw
window, alpha 0) reserves the full width at the plate height. The wallpaper stays. A maximized
window stops at this line; desktop icons use the same work-area edge.
While auto-hide is on, the registration is removed; windows cover the bottom fully.

> **Window = plate.** There is no magnification headroom; hover does not spill the icon off the plate.
> `dock.backgroundMode`: `mica` (default, `AllowsTransparency=false`, real DWM Mica,
> system rounded corner `DWMWCP_ROUND`) or `transparent` / `tint` (layered window, WPF corner radius
> 14 DIP, empty outside the plate). `AllowsTransparency` is not combined with Mica
> (`docs/04-gotchas.md`). When the mode changes the window is recreated (`RecreateDock`). The old `dock.surface`
> (`mica` / `acrylic`) both load as the `mica` mode. **No shadow.** Hover fills the inside of the button.

## Contents

Windows taskbar layout: **one** icon per application.

- **Left — Pinned** — first the Windows taskbar's `User Pinned\TaskBar` shortcuts,
  including closed pins, in taskbar order (`Taskband\Favorites` is read-only;
  if the name is missing from the blob, the file name). Then entries that are in `dock.items` and
  not on the taskbar. The same target appears once. When the app runs it stays **in the same box**
  (`IsRunning`). Closed pins have no capsule. It does not appear again on the right.
  `Pin to Dock / Unpin` writes `dock.items` and, when the target is a real file or folder, writes a `.lnk` into `User Pinned\TaskBar`. `Taskband` is not written.
- **Right — Running** — unpinned running applications, sorted by name (window handle when names tie).
  While `showRunningApps == false` this side is not produced at all; closed
  pins stay on the left.
- A minimized or background window, when its application is already on the strip, **does not become a second box**.
  Identity merges transitively (`RunningAppsSource.Collapse`): canonical exe path, AppUserModelId,
  or process id. A window of the same exe without an AUMID, and a second process, are the same box too.
  A process that never entered the running list becomes **one box per process** on the right
  (not per window). The `Windows` submenu lists windows of **all of the box's processes**
  (`MinimizedWindowsSource` cache; no `EnumWindows` on the UI thread).
- Multiple windows or processes stay in one box. While `WindowCount` > 1 a count badge sits at the icon's top
  right; the tooltip becomes `Name (N)`. The running capsule does not change: a long accent line when a window is
  open on the desktop, a short neutral line when all of them are minimized.
  The badge, the line, and the thumbnail count only windows whose center is on **this** dock's monitor
  (ADR-014). On a minimized window the center is read from the restore
  rectangle (`rcNormalPosition`), not from the icon point; otherwise an unpinned box would disappear.
  A cloaked minimized window stays running. Another virtual desktop's cloak is not counted.
  The pin list is the same on every monitor.
- **Launchpad** is at the far left of the strip. The glyph is the **brand logo tile** (ADR-019): a rounded tile (radius 28 on the 128 grid) at the normal icon size, with the solid small WharfBar mark (`logoStyle` = `wharfbar`, default; sun + pier, no plank gaps) or four Windows panes (`windows`). Colours come from `BrandPalette` (accent hue + effective light/dark theme; sun fixed per theme). The tile is a frozen, cached `DrawingImage` (`BrandLogo.DockTile`). On `WM_THEMECHANGED` / `WM_SYSCOLORCHANGE` / `WM_DWMCOLORIZATIONCOLORCHANGED` / `WM_SETTINGCHANGE("ImmersiveColorSet")`, and after a theme/accent/logo change in settings, only this cell's glyph is swapped (`RefreshBrandTile`); menu and tooltip stay. No timer. The icon does not grow on hover. A left click opens the box in the center of the primary screen; the box is visible before the scan finishes, and a warm catalog paints the full list on the first frame. The pad is keyboard-first: the search box is focused, Up/Down/Left/Right move between tiles (the first Up returns to search), Enter opens the focused tile, and Escape clears a non-empty query before it closes. Typing while a tile is focused goes back into the search box. In default view the body is Pinned (up to 6) → Recent (4, `Show all`) → All apps under letter headings with a grid/list toggle; while searching the matching apps come first (exact, prefix, word-start, substring), followed by matching recent files. Recent shortcuts Windows records for protocol jumps (`ms-*`, `name:`) and their ` (54)` launch counters are filtered out. The card shrinks to the screen and the phone panel is hidden when it does not fit; if a phone is connected, the vertical panel opens on the right of the box, the screenshot sits inside the phone frame, and the notification row uses the app icon. The wallpaper shows behind the box. The account chip always sits on the box's bottom strip. `Documents`, `Downloads`, `Pictures`, `Settings`, `Widget`, `Plugin`, and `Power` are chosen with `launchpad.tools`; a right click on the strip opens a checked menu and the selection is saved immediately. The button is 40 DIP, the glyph is centered; the card padding stays outside the corner curve.
  An unreachable `Programs` link is skipped. Widget and Plugin open the gallery (ADR-017): built-in items work, a local manifest is a record only. It is not written to `dock.items` or the User Pinned
  folder.
- **`Downloads`** sits immediately after the pin group, and **`Recycle Bin`** sits at the far right of
  the strip. Neither is written to `dock.items` or the User Pinned folder.

Matching (`PinMatches`): full path, case-insensitive (`\\?\` and `\??\` prefixes
are stripped); if the pin path is an AppUserModelId, the running `Id` or the group's other AUMIDs;
if the record is still a `.lnk`, the resolved target path or the shortcut's AppUserModelId. Folder pins
do not merge with `explorer.exe`. Two different exes that share a file name do not merge
(`System32` and `SysWOW64` are different full paths too). Different exe names stay separate when there is no shared AppUserModelId
or process id. `User Pinned` receives a `.lnk` on pin; `Taskband` is not written (ADR-005).

The strip is **one piece** — no separator between sections (the macOS experiment was rejected).

## Plate

Custom `DockStrip : Panel`. Slot width is the button, not the glyph. Measurement is `DockPlateMetrics`
(pure, tested): button = icon + 16 DIP (8 on each side), **4 DIP** between buttons, plate padding
**10×8 DIP**. Default icon **24 DIP** → button 40, plate height **56 DIP**. The old
28 DIP default is pulled to 24 on load; other values in the 16–36 range stay.

Hover fill is `ControlFillColorSecondaryBrush`, radius 4 DIP (`HoverPlate`). It is square and sits
on the button; an **8 DIP** strip remains at the bottom for the indicator (the plate is 8 DIP shorter than the button). The icon
does not grow. A window open on the desktop (`IsOpenOnDesktop`): a persistent square + under the icon,
**3 DIP** from the plate, a **16×3 DIP** capsule (on a 24 DIP icon; width scales with the glyph),
2 DIP from the bottom, color `AccentFillColorDefaultBrush`. An app sent to the background (`IsRunning` and
every window `IsIconic`): no square, a **8×3 DIP** neutral mini line at the bottom
(`TextFillColorPrimaryBrush`). A closed pin has no line; the fill is only on hover. With multiple windows/processes the accent count badge (`WindowCount`) sits at the glyph's top right,
not beside the line. A click still restores
a backgrounded window. Tooltip uses the system style
(250 ms delay, 8 s); when the count is greater than 1, `Name (N)`. The name is the shortcut's shell display name
(`SHGFI_DISPLAYNAME` — a `File Explorer.lnk` file becomes `File Explorer`). If the shell name only
repeats the file name, the application segment of the window title is used
(`wharfbar - File Explorer` → `File Explorer`). The balloon is at the horizontal center of the target; the gap
is 25 DIP and scales with DPI. With a single window and no thumbnail the surface card does not open
(it would be a second text balloon). While the card is open the tooltip closes. The card has no frame and no top light line.

`DockMagnification` / `DockStripLayout` stay pure and tested; the live strip pushes `MaxScale = 1`.
The window is `PlateWidth` / `PlateHeight` (an empty dock budgets one button). No `SetWindowPos` on hover.
There is no ScrollViewer — a large number of icons is clipped to the window width. `dock.magnification` stays in JSON and is read, and is not applied.
While icons appear or disappear, the plate width follows the strip's live width (`DockMotion`, 160 ms).

## Motion

Appear, disappear, the open/closed capsule, the hover fill, and the count badge last **160 ms**
(`DockMotion`). Slot and capsule width use `CubicEase` / `EaseInOut`; opacity uses
`EaseOut` on the way in and `EaseIn` on the way out. A new icon scales from 0 to 1 together with its slot and fades.
Hover does not grow the icon; the live strip scale is 1. The dock's first placement is instant; later changes ease.

## Interaction

| Action | Behavior |
|---|---|
| Left click | Bring forward / launch; a second click minimizes (`WindowActivationService`) |
| Left click (folder / `Downloads`) | Directory-stack flyout. A file uses `ShellExecuteW` (the registered verb). A folder goes down one level |
| Left click (trash) | `shell:RecycleBinFolder` |
| Hover (running app) | That app's windows, DWM thumbnails side by side. A click is `TryActivate` |
| Right click (icon) | The menu below. Downloads and the trash have only the title + `Open` |
| Right click (empty plate) | Background: Mica / Transparent / Tint + opacity (tint) |
| Drag | Reorder pinned items; an accent line shows the target; the click is suppressed |
| Drop | Adds `.lnk/.exe/.bat/.cmd`; dedupes by path |
| Drop (trash) | `SHFileOperationW` `FO_DELETE` \| `FOF_ALLOWUNDO`. A reparse directory is rejected |

Right-click menu (`BuildContextMenuForItem`) — item conditions come from the code:

1. **Title card** — icon + application name, always (not clickable).
2. **`Pin to Dock` / `Unpin from Dock`** — always; the text follows the pin state.
3. **`End task`** — always visible; **enabled if it is running**, otherwise `IsEnabled=false`.
4. **`Close window`** — the same condition as (3) and the same `IsEnabled` logic.
5. After the separator, **only when the path is not empty and the file exists on disk**
   (`!IsNullOrWhiteSpace(Path) && File.Exists(Path)`): **`Run as administrator`** ·
   **`Open file location`** · **`Open a new window`**. If there is no path these three **are not shown at all**.
6. **`Windows`** — the submenu is added only when the cell `IsRunning` **and** it holds a live window
   (`MainWindowHandle != 0`) **and** **at least one window** remains in the list for that process
   (not by `Section` — a pinned-and-running cell is `IsRunning` too). The menu
   is fed from `MinimizedWindowsSource`'s cache; `EnumWindows` is not called on the UI thread.

## Surface (ADR-014)

A single flyout (`SurfaceFlyout`): window thumbnails on hover, a directory
stack when a folder is clicked. No nested `Popup`. It drops when the DWM registration detaches and on close. On a cloaked, minimized,
or DWM-rejected window the title row remains, and there is no black square.

A thin progress bar under the icon paints the
`RangeValue` / `ItemStatus` reading the shell exposes on the taskbar button (normal percent, indeterminate, paused, error).
If the reading is empty there is no bar. A window that wants attention pulses the icon in accent for ~1.6 s;
a second flash restarts the duration, and activation ends it. Position does not move.

When a window minimizes, the captured frame scales toward that icon for ~200 ms.
When that window is restored from this dock (icon click or surface flyout), the cached frame grows back from the icon for ~200 ms while the real window stays DWM-cloaked (layered alpha 0 if cloak is refused). If animations are off, the capture is missing, or cloak/alpha both fail, restore is immediate. Restores from Alt-Tab or other shells are unchanged.
If `SystemParameters.ClientAreaAnimation` is off, or `PrintWindow` is empty, there is no animation.
A Store app's logo comes from the package API; if the file is missing, a letter glyph.

The live strip scale is 1. Hover does not call `SetWindowPos`. The plate is not an AppBar.

## Settings

[Settings → Dock](settings.md#dock): `autoHide`, `iconSize` (16–36, default 24),
`backgroundMode` (`Mica` / `Transparent` / `Tint`) + opacity 0–100 only for `Tint`, `position`
(bottom-left/center/right), `showRunningApps`, content list (add/remove/drag to reorder).
The same background menu opens by right-clicking the empty plate (not the icon's own menu).
A Mica ↔ transparent/tint transition recreates the dock. Position is **bottom-edge**
alignment only; a side dock is out of scope.

## Gotchas

`docs/04-gotchas.md` → the entries whose titles start with `Dock`. Also:

- OLE drop acceptance was **not measured**. Mica mode is not layered; transparent and tint modes
  are layered. If a layered `Topmost` window is not accepted as a target, file drop **and**
  in-app reorder fall together (`RegisterDragDrop` is per HWND).
- Drag and click are the same button; suppression is a one-shot, not a mode switch (`_suppressNextClick`), because
  the drop changes the order and changes the contents of the released cell.
- The plate rhythm was not verified with the mouse ([`conventions.md`](../conventions.md) § Test list).
