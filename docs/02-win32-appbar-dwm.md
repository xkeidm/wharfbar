# 02 — Win32: AppBar, DWM, Tray, Icons

## a) AppBar — reserving screen space

`Topmost=true` alone is not enough; so that other windows do not go underneath, an AppBar must be registered with Windows (what the taskbar does).

### API summary

```csharp
[DllImport("shell32.dll")]
static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

// ABM_NEW = 0x00, ABM_REMOVE = 0x01, ABM_QUERYPOS = 0x02, ABM_SETPOS = 0x03
// ABE_TOP = 1, ABE_BOTTOM = 3
```

**Flow:** `ABM_NEW` → `ABM_QUERYPOS` → `ABM_SETPOS` → on close, **always** `ABM_REMOVE`.

If `ABM_REMOVE` is missed, the reserved area stays stuck; the user usually has to restart `explorer.exe`.

### Callbacks

On AppBar registration, define a custom window message with `uCallbackMessage`. Handle the incoming `ABN_*`
notifications (e.g. `ABN_POSCHANGED`, `ABN_FULLSCREENAPP`, `ABN_WINDOWARRANGE`).
**Do not call `SHAppBarMessage` inside `ABN_POSCHANGED`** — explorer notifies with `SendMessage`;
`ABM_SETPOS` from inside deadlocks (a wait cursor on the top bar, no clicks). `BeginInvoke` +
skip SETPOS on the same rect. Fullscreen hiding and repositioning are wired from here.

### Multiple monitors

During `ABM_NEW` the AppBar binds to the monitor the window is on; if the bar is moved to the target monitor
first and registered after that, each bar reserves an edge on its own monitor. Placement
is computed from that monitor's physical-pixel bounds with `Display.GetMonitorBounds` (ADR-003);
each monitor is turned on and off separately in config (`monitors`).

On the primary monitor this path works one-to-one with the taskbar. On secondary monitors shell
compatibility can be more fragile, so a manual work-area expansion with `SystemParametersInfo(SPI_SETWORKAREA)`
 was planned as a fallback (use it carefully; it can clash with other shell components — see boundaries "Do"). **25H2 (2026-08-29):** `SPI_SETWORKAREA` does not work;
the fallback path is dead. Until a shell that supports a work-area override arrives, **the bottom strip can be released only
with `ABM_SETSTATE(ABS_AUTOHIDE)`** (the taskbar-hiding
section below) — `SPI_SETWORKAREA` is not needed.

> **Trap (2026-09-30):** Looking at this API from the outside (from a process that is **not** registered)
> and concluding that "everything is dead on 25H2" is misleading. `SPI_SETWORKAREA` really does
> return `TRUE` and **silently does nothing** (measured) — but releasing the bottom strip
> is not tied to this path anyway; it is tied to `ABM_SETSTATE(ABS_AUTOHIDE)`, and that **works**.
> Likewise, probing `ABM_SETSTATE` from a process that is not registered gives a false negative:
> only a call from **a registered AppBar** has an effect. Measure while WharfBar is running.

### Explorer crash / restart

When the shell restarts, the AppBar registration is lost.

**Implemented solution:** `RegisterWindowMessage("TaskbarCreated")` is listened for; when the message arrives, `ABM_NEW`
is run again and placement is re-applied. `ABM_REMOVE` is not required beforehand, because the old registration
left with the dead shell. See ADR-003.

### The dock and the AppBar

An AppBar is appropriate for the top bar. The dock plate is an overlay (Topmost + auto-hide); the plate HWND is not made an AppBar — `ABM_QUERYPOS` stretches the window to the monitor width on the bottom edge. See ADR-001.

While `dock.autoHide=false`, a sibling window (`DockWorkAreaReservation`) reserves a full-width strip at the plate height with an `ABE_BOTTOM` registration. The window is created with raw `CreateWindowEx` (`WS_EX_LAYERED` + alpha 0 + `WS_EX_TRANSPARENT`): a WPF `HwndSource` paints this strip black, because DWM redirection ignores `LWA_ALPHA`. The wallpaper stays visible, and the click falls through to the desktop. A maximized window follows the work area, so it stops at the top of the dock; desktop icons use the same edge. Auto-hide calls `ABM_REMOVE`. `SPI_SETWORKAREA` is not used for this job (a silent no-op on 25H2). On exit the registration is released with `ABM_REMOVE`.

### Keeping the strip clear — there is no "forbid placement" API

The AppBar contract only shapes the **work area**: maximize, snap and initial window placement respect the reserved strip, but a **manual drag** can always put a window there (a window may also restore from its own saved position, or be placed by its application). Windows offers no API that forbids a rectangle to other processes' windows — the taskbar has the same property and relies on painting over the strip.

`TopBarStripGuard` enforces the strip instead of painting it:

- An out-of-context `SetWinEventHook` (documented; nothing is injected into the other process, same pattern as `DockShellHook`) listens for `EVENT_SYSTEM_MOVESIZEEND` (`0x000B` — the interactive move/resize loop has finished) and `EVENT_OBJECT_SHOW` (`0x8002` — a window was shown/restored). The callback runs on the installing thread's (WPF UI) message loop, so it only validates cheaply and posts the hwnd.
- The deferred pass re-reads the window rect and the monitor's `rcMonitor` / `rcWork` (`Display.GetMonitorBounds` / `GetMonitorWorkArea`), and `StripIntrusionPolicy` decides: no clamp when the monitor has no top AppBar, when the window is already below `rcWork.Top`, when it is entirely above the screen, or when it is fullscreen (a window covering its whole monitor — the bar hides for it via `ABN_FULLSCREENAPP`). Otherwise the window moves to `rcWork.Top` with one `SetWindowPos(..., SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_ASYNCWINDOWPOS)`.
- `SWP_ASYNCWINDOWPOS` queues the move to the owning thread, so a hung/busy application cannot stall the AppBar UI thread; the guard never moves a window **while** it is being dragged (only after release), so Windows' own move loop is never fought.
- Eligibility (`IsEligible`): visible, not minimized/maximized, top-level (`WS_CHILD` absent), with a caption (`WS_CAPTION` both bits — menus, popups, OSDs and splash screens do not have one), and neither `WS_EX_TOOLWINDOW` nor `WS_EX_NOACTIVATE`. WharfBar's own windows are skipped by `WINEVENT_SKIPOWNPROCESS` (and by pid in the sweep).
- `QueueSweep()` runs one coalesced `EnumWindows` pass at startup, on `WM_DPICHANGED` / `WM_DISPLAYCHANGE` and whenever a top bar is created/rebuilt, so windows already sitting under a bar are pushed out too.

Consequence: a window dragged under the bar is visible through a `transparent` bar *while it is being dragged* and is pushed out when the mouse is released. Nothing is painted over the strip, so the live wallpaper — Windows slideshows and animated/dynamic walls included — is always exact; there is no snapshot to keep in sync (see `docs/04-gotchas.md`).

---

## b) DWM / Mica / backdrop

```csharp
// DwmSetWindowAttribute / DwmExtendFrameIntoClientArea
// DWMWA_USE_IMMERSIVE_DARK_MODE = 20
// DWMWA_WINDOW_CORNER_PREFERENCE = 33  (1 = do not round, 2 = round)
// DWMWA_SYSTEMBACKDROP_TYPE = 38       (2 = Mica, 3 = Acrylic, 4 = MicaAlt)
```

**Preference order:** The **WPF-UI** backdrop APIs first. Raw DWM Mica only if WPF-UI is not enough, or for fine-tuning; avoid a clash. Implementation: `Services/WindowAppearance.cs` + `Native/Dwm.cs`. Detail: ADR-004.

**Critical trap:** WPF `AllowsTransparency=true` breaks DWM composition (Mica).

**Top-bar pattern that was applied (Phase 3):**

- An opaque window; `AllowsTransparency` off; `WindowStyle=None` (to get past the caption min-height of ~39 px)
- Once the backdrop is verified, the background is a `Transparent` brush; otherwise an opaque fallback
- `MARGINS` = `-1` with `DwmExtendFrameIntoClientArea` (the glass region; a caption is not required)
- Corner: the top bar uses `DWMWCP_DONOTROUND`; the dock (Phase 4) may use round

> Leaving a caption with `WindowChrome` is not required for Mica, and it inflates the 30 DIP strip
> to DefWindowProc's min height (~39) — it is not used (ADR-004).

---

## c) System tray / status indicators

```csharp
Shell_NotifyIcon  // NIM_ADD / MODIFY / DELETE
```

**Important:** `Shell_NotifyIcon` is for adding/updating **our own** tray icon. Reading **system tray icons** such as Wi-Fi, volume, and battery is not supported by an official API.

For top-bar indicators:

- Read the state from **.NET / Win32 APIs** such as `NetworkInformation`, the power APIs (`GetSystemPowerStatus`), and the clock
- Use our own icon set (**Segoe MDL2 Assets** glyphs on the top bar — a WPF-UI `SymbolIcon` could come out empty on a Mica AppBar strip;
  indicators other than the clock were disappearing)
- Optional: WharfBar's own `NotifyIcon` — implementation: `TrayIconService` + `NotifyIconHost` (Quit menu); this is not system-icon reading
- **Tray hybrid + mirror (ADR-005 + ADR-009):** third-party icons are mirrored on the top bar **with documented comctl32
  toolbar messages** (`TB_BUTTONCOUNT`, `TB_GETBUTTON` (index; `TBBUTTON` → `idCommand`/`iBitmap`),
  `TB_GETIMAGELIST` + `ImageList_GetIcon`, `TB_GETITEMRECT`, `TB_GETBUTTONTEXT` (command id));
  the click is forwarded with `SendMessageTimeout` mouse messages.
  There is no `ReadProcessMemory` / injection / undocumented enumeration. Hidden/overflow icons
  cannot be mirrored (they exist only in the open popup); `TrayOverflowInvoker` (`Win+B` / chevron) remains.
  The poll is 4 s; icon pixels are only pulled inside a gated state blip (see the 25H2 section). `SendMessageTimeout` (`SMTO_ABORTIFHUNG`, 500 ms) does not
  lock the UI on a hung explorer.
- **Windows 11 25H2+ UIA fallback:** the classic `ToolbarWindow32` tray was removed in this version
  (on 25H2 there is no `SysPager`/`ToolbarWindow32` under `TrayNotifyWnd`). When `FindToolbarHwnd` returns
  empty, the mirror falls back to UI Automation: the `SystemTray.NormalButton` buttons under
  `Shell_TrayWnd → … → TaskbarFrameAutomationPeer` (name + `AutomationElement`); system icons are filtered out
  by a different class (`AccentButton`/`OmniButton*`/`ShowDesktopButton`) plus the input-indicator tooltip.
  There is no icon bitmap in UIA → `TrayCapture` pulls the parked+cloaked taskbar's own content with `PrintWindow(PW_RENDERFULLCONTENT)`
  and crops the button's `Image` child rect
  (the real icon, no flash; `TrayLetterGlyph` fallback if it fails). UIA
  children are exposed only while the taskbar is `SW_SHOW` **and unparked** → the gated state blip reads them with
  `BeginTrayPeek(stealth:true)` (cloak + off-screen park) after it dropped `ABS_AUTOHIDE`, no flash. DWM cloak is rejected on some machines
  (`DwmSetWindowAttribute` does not return S_OK) → in that case `TrySetTrayVisibleStealth`
  keeps it invisible with a **WS_EX_LAYERED + alpha 0** fallback (UIA continues to see
  `IsWindowVisible=true`); if that fails too, the peek does not open and the poll is skipped.

   **Deferred bootstrap (2026-11-10):** the state blip is deferred while a visible maximized window sits
   on the primary monitor (dropping the auto-hide state moves it twice). That check never spent a
   bootstrap attempt, so it used to defer the *only* read for the whole session — an empty bar with no
   tray icons (field report: dual-monitor PC, `state-blip blocked reason=Bootstrap attempt=0
   (maximized/affected window)` every 4 s). A never-filled bootstrap blocked for more than
   `TrayStateBlipGate.MaxBlockedBootstrapWait` (30 s) now runs one forced blip
   (`state-blip forced after Ns blocked …`); the bounded attempt series still caps the reflows. The
   check only counts windows the user can see (cloaked windows — other virtual desktops, suspended
   UWP — tool/no-activate overlays and WharfBar's own windows are ignored) and logs the culprit's
   class/title. The fullscreen skip is **primary-monitor only**: a fullscreen app on a secondary
   monitor no longer blocks the read (the blip reflows the primary work area only), and the skip is
   logged once per transition (`state-blip skipped: fullscreen foreground on primary …`).

   > **Icon capture has to be INSIDE the PEEK (2026-09-30, live measurement + fix).**
   > `PrintWindow(PW_RENDERFULLCONTENT)` **writes no pixels at all into the DIB while the taskbar
   > `IsWindowVisible=False`, but returns `TRUE`** → a `1920x48` fully black (`[0,0,0,255]`) bitmap
   > is published as "successful" and the top bar shows **black squares**. The measured difference:
   >
   > | State | `IsWindowVisible` | Cropped pixels |
   > |---|---|---|
   > | peek closed (race) | `False` | `[0,0,0,255]` ×9 — flat black |
   > | peek open | `True` | `[67,67,67] [68,68,68] [33,33,33]…` — the real taskbar |
   >
   > **alpha 0 does not block `PrintWindow`** — the only determinant is `IsWindowVisible`, that is, the peek
   > being open. The state-blip read captures the icons **before its own `finally` (`EndTrayPeek`)**
   > and returns a ready `TrayMirrorItem` list; `PollUia` uses only that list.
   > Lasting defense: `TrayCapture.IsBlankCapture` rejects a single-color/fully transparent crop →
   > the caller falls back to a letter glyph, **a black square is no longer possible**
> (tested with `TrayCaptureTests`).
   The mirror is rendered only on the primary monitor's
   top bar (the `trayMirror` section is not created on secondary bars).

   **Click — cursor-free (2026-11-09, live-measured on 25H2):** the click is replayed on the tray
   button through UI Automation, and the click itself never moves the physical pointer. Left click =
   `InvokePattern.Invoke()` on the button element cached in the mirror snapshot; double-left = two
   invoked clicks 90 ms apart (UIA cannot express a double-click); right click = verified
   `SetFocus()` + the Menu key (`VK_APPS`, with `Shift+F10` as the fallback for owners that ignore
   it) — UIA has no right-click action, and a physical click would move the user's pointer to the
   bottom edge. Two measured facts make this work while the taskbar stays hidden: (1) every
   `SystemTray.NormalButton` exposes `InvokePattern`, and (2) a cached element keeps working after
   the tray is hidden (`SW_HIDE` + alpha 0), even though a fresh UIA walk then returns 0 buttons.
   The menu key is only sent after `AutomationElement.FocusedElement` proves the tray button is
   focused; otherwise it would open a context menu inside the user's application. The owner (or the
   shell's menu helper) may still call <c>SetCursorPos</c> to anchor its menu at the real tray icon,
   measured on this machine (1920×1080, physical px, 0–20 ms after the Menu key) at the bottom edge:
   Epic 1309,1056 · RustDesk 1341,1056 · Monitorian 1373,1056 · AMD 1501,1056 · NVIDIA 1405,1079.
   Undo cannot cover every owner: NVIDIA's tray menu belongs to <c>NVDisplay.Container</c> (SYSTEM
   integrity, class <c>NotificationIconWindowClass</c>), and while that window is in the foreground
   the medium-integrity WharfBar process cannot call <c>SetCursorPos</c> (it returns FALSE; the old
   undo ignored the result and logged a false “cursor restored”), so the pointer stayed at the
   bottom-right. The warp is now **prevented**: after the tray button has UIA focus and immediately
   before the Menu key — a foreground change releases an earlier clip — <see cref="TrayPointerGuard"/>
   confines the cursor with <c>ClipCursor</c> to a box of <c>ClipMarginPx = 6</c> physical px around
   the pointer's position at click time, so the owner's <c>SetCursorPos</c> is system-clamped near
   the click (measured with NVIDIA: a 1405,1079 warp became 907,107 when the click point was
   900,100). The clip is released after <c>ClipHoldMs = 150</c> ms (every measured warp lands within
   20 ms); the OS also releases it if the process exits, so a crash cannot leave the cursor
   confined. A short escape watcher (3 ms poll, 600 ms watch) still puts the pointer back if a warp
   lands in the bottom tray-anchor band (<see cref="TrayCursorWarp"/>), and a refused
   <c>SetCursorPos</c> is logged honestly (“cursor warp NOT undone … foreground blocks
   <c>SetCursorPos</c>”) instead of a false restore. If the cached
   element has died (explorer rebuilt its XAML tree / restarted), one bounded **repair round**
   unparks the tray with a state blip, re-reads the live buttons, re-captures the icons and replays
   the gesture on the fresh element; an item that is no longer in the tray is dropped from the
   mirror. A concurrent second click is dropped while the first session runs.
   The application's flyout/menu still opens according to the parked taskbar position (the bottom
   edge) → `TrayMenuReanchor` moves the newly appeared popup under the mirror icon (`SetWindowPos`,
   work-area clamped; a `#32768` menu always, other classes only when their style is a real popup
   (`WS_POPUP`, no caption/thickframe, no `WS_EX_APPWINDOW`) and they overlap the click zone —
   a normal framed application window is never moved; fullscreen overlays, cloaked, hung and
   own-process windows never; 2.5 s watch, cloaked windows are not in the pre-click snapshot, and
   a 1.5 s re-assert counters owners that move their flyout back). Matching is NOT by pid: every
   25H2 XAML button reports the explorer pid. One process-wide `ReanchorWatchGate` allows a single
   watcher and per-hwnd ownership claims, so two overlapping clicks cannot fight over a menu.

**Implemented:** the left brand logo (`BrandLogo.BarGlyph` / `logoStyle`; geometry, not a font glyph — visible on Mica); the exe/tray icon is `Assets/WharfBar.ico` (Dusk solid mark);
the right cluster's default LTR is `trayMirror · hidden · date/time · notification · controlCenter · network · battery`
(`topBar.statusItems`; customizable). `volume` / `brightness` / `media` are in the catalog and
off by default; right-click and the Top Bar setting list every item in the catalog.
**Show all** inserts the missing ones next to their neighbor and does not disturb the saved order. Visible items
are ordered by dragging on the bar (a click under the threshold opens the flyout; mouse capture, not OLE).
Clock/date: a single-line combined label in the **resolved UI culture** (`LocalizationService` / `config.language`; e.g. `tr-TR` → `9 Ağu Paz 21:57`, `en-US` → `9 Aug Sun 21:57`, Segoe UI) + a bell glyph; both open the native Notification Center
(`Win+N` — calendar + notifications; there is no separate calendar `Popup`; there is no ADR-005 mirror).
`controlCenter` → QS **Main**; volume → QS **Mixer**; network → QS **Network** — a shared
**Quick settings** flyout (ADR-006); a layered `Popup`
for `PopupAnimation=Fade` (`AllowsTransparency=true`). The rule is unchanged: Mica + `AllowsTransparency`
is forbidden **on the main window**; because a popup is a separate window, it does not affect Mica.

**Overflow + a hidden taskbar:** while `Shell_TrayWnd` is `SW_HIDE`, a plain `Win+B` is a no-op.
`TrayOverflowInvoker` first shows the tray temporarily with `BeginTrayPeek`, opens the overflow,
then `EndTrayPeek`. On exit, a full `Restore`.

**The WharfBar pill:** on the primary bar the chevron opens a horizontal capsule for the icons in the mirror.
The capsule has only the icon; the name is in the tooltip. The keys the user placed
are in `topBar.trayOverflowIcons`; an icon that is not in the list stays on the bar. A short click
keeps forwarding. Putting one back on the bar is dropping it from inside the pill onto the right strip. Windows'
hidden area, which cannot be enumerated, uses the same `TrayOverflowInvoker` path via the chevron at the end of the pill.
On a secondary bar the chevron opens `Win+B` directly.

---

## d) Audio (Core Audio) — quick settings

```
IMMDeviceEnumerator / IMMDevice          // device list + the default
IAudioEndpointVolume                     // master level / mute
IAudioSessionManager2 / ISimpleAudioVolume // application sessions
IPolicyConfig.SetDefaultEndpoint         // the default output (ADR-006; a narrow exception)
```

Raw COM is in `Native/AudioEndpointVolume.cs`, `CoreAudioDevices.cs`, and `CoreAudioSessions.cs`;
the UI goes through `QuickSettingsFlyout`.

---

## e) Application icon (dock)

```csharp
// Preference: SHGetFileInfo(SHGFI_ICONLOCATION) + SHDefExtractIcon(the desired px)
//         — shrinks from the shell jumbo source; 16/32 bitmaps are not scaled up
// Fallback:  SHGetFileInfo(SHGFI_SYSICONINDEX) + SHGetImageList(SHIL_JUMBO 256 / SHIL_EXTRALARGE 48)
// Last:    SHGFI_ICON | LARGEICON / SMALLICON
// HICON → CreateBitmapSourceFromHIcon; drawing UseLayoutRounding + SnapsToDevicePixels
//
// Application name: SHGetFileInfo(SHGFI_DISPLAYNAME) — not the shortcut's file name, the name the
// shell shows (File Explorer.lnk → File Explorer). szDisplayName is not an icon path;
// it is not confused with SHGFI_ICONLOCATION. If the result is the same as the file name, the
// application name in the last " - " piece of the window title is used.
```

A window open on the desktop (`IsOpenOnDesktop`): a persistent square (`ControlFillColorSecondaryBrush`)
and a **16×3 DIP** capsule **3 DIP** away from the plate (on a 24 DIP icon), 2 DIP from the bottom, color
`AccentFillColorDefaultBrush`. A minimized application (`IsRunning`, every window
`IsIconic`): no square, an **8×3 DIP** neutral mini line at the bottom (`TextFillColorPrimaryBrush`).
No line on a closed pin. `IsRunning` remains, and a click restores. The button's bottom **8 DIP**
strip is reserved for both lines.

The default `dock.iconSize` is **24 DIP**. The button is glyph + 16 DIP, the gap is 4, the plate padding is 10×8;
at 24 the button is 40 and the plate height is **56 DIP**. The window equals the plate (scale 1).
Placement: the primary monitor's bottom edge (`BottomGapDip = 0`; there is no work-area gap from the hidden taskbar).
It fits a classic `.exe` / `.lnk`.
The **UWP/Store** logo is resolved with `Windows.Management.Deployment` + `SHLoadIndirectString`;
if the file is missing, a letter glyph (ADR-014). A broken image path is not left behind.

The icon bitmap is requested at glyph size (`ResolveIconPixelSize` = `iconSize`, clipped to 16–256 px).

Horizontal alignment is chosen with `dock.position`: `bottom-left` / `bottom-center` / `bottom-right`.
`DockPositions.Normalize` returns `bottom-center` on an invalid/empty value (a side dock is out of scope:
work-area reservation and slide-down hiding do not change — only X is aligned). Edge snapping
uses 12 DIP of inner padding.

**Minimized windows:** `WindowCensus` does a **single** `EnumWindows` pass —
the `WindowEnumeration.GetTopLevelCandidates` table is taken, and the minimized subset is split out
with a separate `IsIconic(hwnd)` call per window. Minimized windows keep `WS_VISIBLE`,
so the `IsCandidate` filter alone is not enough; a second `EnumWindows` pass is also
unnecessary. Do not call `EnumWindows` on the UI thread (it locks on a hung HWND) — the window
table is scanned by `WindowCensus` on a background thread once every 3 s, once for all docks. `RunningAppsSource` does not run a second `EnumWindows` pass.

---

## f) Hiding / restoring the taskbar (ADR-005)

While WharfBar is running, the primary monitor's taskbar is hidden; the state is captured at startup
and restored on the exit/crash paths (`ABM_REMOVE` discipline). Implementation: `TaskbarVisibilityService`
+ `Native/TaskbarNative.cs`.

**Chosen hide path (plan):**

1. **Baseline:** `ABM_GETSTATE` (the auto-hide flag) + the primary `SPI_GETWORKAREA` +
   `FindWindow("Shell_TrayWnd")` — stored for restore. (On 25H2 `ABM_GETSTATE`
   is unreliable: it returns 0 even when auto-hide is on.)
2. **Hide:** `ShowWindow` + `SWP_HIDEWINDOW` (`FindWindow("Shell_TrayWnd")` and
`FindWindowEx` for `Shell_SecondaryTrayWnd`; there is no full `EnumWindows` on the UI thread —
    a hung window leaves a wait cursor on the AppBar). **(Historical: `ABM_SETSTATE` used to be
    unused because of the edge-reveal concern — on 2026-08-30 it became the strip solution itself.)**

   **The 25H2 fact (measured 2026-08-29, verified again 2026-09-30 — Win11 25H2 build 26200.9457):**
   `SW_HIDE` alone leaves the AppBar reservation (~48px ghost strip), and
   expanding with `SPI_SETWORKAREA` **does not work** (measured: it returns `TRUE`, the work area
   does not change; some call shapes also give `ERROR_INVALID_PARAMETER (87)`).

   **Solution — native auto-hide state (ADR-005 note 5, verified to work):** the hide flow
   issues `ABM_SETSTATE(ABS_AUTOHIDE)` and stays in that state for the runtime. Explorer parks the
   taskbar in an ~2px edge clamp and **removes the bottom reservation from the work area**.
   The table measured while WharfBar is running:

   | | `ABM_GETSTATE` | `Shell_TrayWnd` visible | tray top | `SPI_GETWORKAREA.Bottom` | bottom gap |
   |---|---|---|---|---|---|
   | application closed | 0 | True | 1032 | 1032 | 48 |
   | **while the application is running** | **1** | **False** | **1078** (parked) | **1080** | **0** |

   → maximized windows cover the bottom strip **full screen**, and no window is touched
   (a maximize compensation with `SetWindowPos` was tried and removed because it produced a 7 ms fight).
   In the same measurement all 9 children were hidden (`DesktopWindowContentBridge` included) → there is no XAML ghost.

   > **Measurement trap (2026-09-30):** `ABM_SETSTATE`/`ABM_SETPOS` take effect **only when a registered AppBar**
   > calls them. If they are called from a process that is not registered, they return `TRUE` but nothing
   > happens — this leads to the wrong conclusion that "the API is dead". Verify while WharfBar is running.

   The tray mirror cannot be read
    on a parked tray (XAML content is virtualized): desktop-root UIA reads are done from a dedicated STA dispatcher
    thread. There is **no quiet hover read any more** (it moved the user's pointer to the bottom edge and, on the
    2026-11-09 test machine, always timed out): the only read that exposes the buttons is a **state blip** that
    drops `ABS_AUTOHIDE` (which unparks the bar and flaps the work area) — gated by
    `TrayStateBlipGate` (bootstrap of a never-filled mirror, its bounded retries, later only ≥10/≥20 min apart while
    the user is idle ≥3 min, and never while a fullscreen foreground window covers the primary monitor — the
    2026-11-09 false "tray is empty" happened under WharfBar's own fullscreen colour picker). A read taken while the
    bar is still in its 2 px sliver returns 0 buttons and proves nothing, so the blip verifies the effect
    (`TrayAutoHideReveal.WaitFullyOnScreen`, then a verified `TaskbarNative.TryEnsureTrayOnScreen`) and classifies an
    unverified unpark as `Inconclusive`, never as evidence. Every flip is logged as `state-change #N reason=…`.
    Icons are captured **inside** the blip (one `PrintWindow` + per-button crops; a peek already ended paints black)
    and matted against the taskbar fill (`TrayIconMatte`, ring-median background, colour-to-alpha).
    A read that comes back empty **never clears a populated mirror**: a stale icon beats a blank bar (and the
    work-area jump needed to read again); the snapshot is only reset when `Shell_TrayWnd` changes (explorer restart).
    Pixels refresh when the gated blip runs, or when a click repair re-reads the tray; unchanged pixels keep the
    cached `ImageSource` and an unchanged snapshot is not published. A click whose key is missing from the live tray
    (the app exited, the snapshot was held) is not sent to the old rect. Unnamed and repeated UIA buttons get
    index-suffixed keys (`TrayMirrorOrder.UniqueKeys`) so de-duplication does not drop icons. Restore is
    `SetState(baseline)` + `SetTrayVisible(true)`; after a force kill, the stale watchdog file restores the baseline state.
3. **After an AppBar `SETPOS`:** if the shell can write the bottom strip back,
   `EnsureWorkAreaExpanded`. There is no `ReapplyHide` inside `ApplyPlacement` — hide +
   `SETPOS` producing an `ABN_POSCHANGED` loop yields a wait cursor on the top bar and cancels dock auto-hide.
   `ABN_POSCHANGED` / `ABN_STATECHANGE` are handled with `Dispatcher.BeginInvoke`
   (a deadlock if `SHAppBarMessage` runs inside explorer's `SendMessage`). `ABM_SETPOS` only if
   the QUERYPOS result differs from the last commit. `WorkAreaChanged` only when placement
   has actually changed.    `WM_WINDOWPOSCHANGING` clamps to the approved AppBar rect:
   when the phantom bottom strip returns, WPF pushes the bar to the top of the work area (`y=30`); a stuck
   frame remains on the reservation strip. On the same push the dock overlay stays
   in the auto-hide range with `ClampSlideY` (cached Y; no `Measure` inside
   `WM_WINDOWPOSCHANGING`). While it is on the approved rect, `ABM_WINDOWPOSCHANGED` is skipped —
   otherwise WPF's work-area push freezes the UI on every layout via explorer `SendMessage`.
4. **Keep-hidden:** a 250 ms `FindWindow` / `FindWindowEx` — `SW_HIDE` on a tray that is visible
   outside a peek. **It runs on a background `System.Threading.Timer`**
   (it does not depend on the WPF dispatcher; a busy UI thread can never
   starve the re-hide to death). The peek protects the tray only while it is **fresh**: an orphaned
   peek stuck because `EndTrayPeek` was missed is force-ended by keep-hidden once it passes
   `TrayPeekTracker`'s 45 s limit (`Reset` +
   re-hide) — the taskbar therefore can never stay visible forever.
   Even if the HWND stays hidden, explorer can
   `SETPOS` its taskbar AppBar again; if the bottom of the work area has shrunk, `EnsureWorkAreaExpanded`
   at most once a second — **on 25H2+ the `WorkAreaExpandPolicy` circuit breaker
   steps in:** a call that gets `ERROR_INVALID_PARAMETER` twice in a row is skipped for 10 min
   (a single probe instead of hitting a dead API once a second), and after 10 min
   it is tried again with a single probe; `TaskbarCreated` (shell restart) resets the breaker.
   `SPI_SETWORKAREA` (`SPIF_SENDCHANGE`) broadcasts to every top-level window and
   blocks for ~1 s on healthy shells — the call was moved to a background
   thread so the UI thread does not freeze (guard: a single expansion at a time; an expansion that races
   with restore is rolled back to the baseline).
   `ReapplyHide` after `Shell_NotifyIcon` and for `TaskbarCreated`.
5. **Restore:** `ShowWindow(SW_SHOW)` + `ABM_SETSTATE(baseline)` +
   `SPI_SETWORKAREA(baseline work area)` (on 25H2+ the SPI step is skipped by the circuit breaker;
   once the tray is visible, explorer gets the strip approved itself); monitoring stops.
6. **Exit order:** taskbar restore → `ABM_REMOVE` (`TopBarWindow.ReleaseShellState`).
7. **`TaskbarCreated`:** AppBar re-registration + `ReapplyHide`.

Multiple monitors: work-area expansion is targeted per monitor, for that monitor
(on 25H2+ the API is dead — above); dock reservation is per monitor with `SetDockReservation(Monitor, heightPx)`
(see boundaries "Do").

A force kill can skip the restore → [`04-gotchas.md`](04-gotchas.md).

### advapi32.dll — process integrity (restricted launch)

Windows enforces UIPI between integrity levels, so a process **below medium** integrity cannot drive the
shell: `ShowWindow(Shell_TrayWnd, SW_HIDE)`, `SHAppBarMessage(ABM_NEW/SETPOS)` and `SetWindowLong` are
silently dropped. `Native/ProcessIntegrity.cs` reads the current token's integrity RID at startup;
`App.OnStartup` then warns (OK continues, Cancel quits) before any shell state is touched.

```csharp
// advapi32.dll
OpenProcessToken(IntPtr process, uint access, out IntPtr token)  // TOKEN_QUERY (0x0008)
GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned)
  // TokenIntegrityLevel = 25
GetLengthSid(IntPtr sid)
// kernel32.dll
GetCurrentProcess() / CloseHandle(IntPtr handle)
```

- `TOKEN_MANDATORY_LABEL` → `SID_AND_ATTRIBUTES.Sid` points at the `S-1-16-N` SID; the last
  sub-authority is the RID (0 untrusted / 4096 low / 8192 medium / 12288 high). The pure
  `TryReadLastSubAuthority` validates the SID header and sub-authority count before reading the last
  little-endian value, so a malformed or authority-only SID is reported as unreadable.
- `TryGetCurrentRid(out int rid)` returns `false` (and `rid = -1`) when the token cannot be read; an
  unreadable token is never treated as restricted, so a failed probe cannot block startup. RID `0`
  (`SECURITY_MANDATORY_UNTRUSTED_RID`) **is** below medium — the restricted-launch warning covers it.

---

## g) The running-application list (dock)

Top-level visible windows are scanned with `EnumWindows` on a **background thread** (a hung HWND on the UI
leaves a wait cursor); `IsHungAppWindow` is skipped. `Shell_TrayWnd`, `Progman`,
`WorkerW`, `ApplicationFrameWindow`, and our own process are excluded
(`Native/WindowEnumeration.cs`). `ApplicationFrameWindow` is the ApplicationFrameHost frame of UWP applications
(the real application window, `Windows.UI.Core.CoreWindow`,
is enumerated separately); if it is not excluded, every UWP application appears twice.
For the path, `QueryFullProcessImageName` (`Process.MainModule` can lock the UI). For grouping,
`GetProp(ApplicationUserModelID)` (`AppUserModelId.cs`; on Win11 it returns
empty on UWP windows, and excluding `ApplicationFrameWindow` provides the dedup). Processes under
`C:\Windows\SystemApps\` (system helper hosts such as Windows Input Experience / TextInputHost)
do not count as running applications (`RunningAppsSource`). `Process.MainWindowHandle` is unreliable for WharfBar's own windows
(`ShowInTaskbar=false`) — focusing is done
with `WindowActivationService` + `EnumWindows` + a PID filter.

A dock icon click (a taskbar-like toggle): minimized → `SW_RESTORE` + `SetForegroundWindow`;
in the background → bring it forward; if it is already in front → `SW_MINIMIZE`. Because a dock click can steal focus,
`GetForegroundWindow` is recorded at the `PreviewMouseLeftButtonDown` instant; the same process PID
also counts as “in front” (child windows such as Electron).

**Hover preview (`DockSurfaceFlyout`):** one card per window of the box — a still preview plus the
window title; up to 8 cards, 4 per row, open windows first and minimized windows last
(`DockWindowPicker`, pure and unit-tested). A click on the card (the preview or the title) activates
that exact window; a minimized one goes through the dock's restore cue. Window collection is the same
cached census table as the badge: no `EnumWindows` on the UI thread. A minimized window's
`GetWindowRect` is the `-32000` icon point, so its monitor is read from the restored rectangle
(`GetWindowPlacement.rcNormalPosition`) — without that every minimized window silently fell out of
both the flyout and the right-click `Windows` submenu.

The preview is **not** a DWM thumbnail. `DwmRegisterThumbnail` returns `E_INVALIDARG` when the
destination is a layered window *or* a child window; the flyout is a layered `Popup` and an
`HwndHost` preview surface would be a child HWND, so every registration failed and the earlier
feature shipped as title-only rows. Each card is now one `PrintWindow` frame
(`DockWindowPreview` + `DockWindowSnapshot`), captured on a worker, scaled down on the UI thread,
and drawn as a plain WPF image; the whole card is therefore a normal WPF hit-test target. A
minimized window has no live frame (its `PrintWindow` is a ~160×28 iconic stub), so it uses the
frame the minimize moment captured, otherwise the app icon and the title.

### Show Desktop sliver (top bar, Windows-style)

A 7 DIP hover plate pinned to the screen's right edge (`ShowDesktopEdge`, a sibling of the
status strip, not a strip slot). Click 1 minimizes every top-level window; click 2 brings back
exactly the recorded set; the cycle repeats. The core (`Services/ShowDesktopToggle.cs`) is pure
and unit tested; the window set comes from a dedicated `EnumWindows` pass (`Native/ShowDesktopNative.cs`)
on a background thread — census rules (visible / not hung / not cloaked / no owner / tool windows out /
own PID out) with `ApplicationFrameWindow` **kept in** (a UWP frame is a real window here), and
minimized windows excluded (the user minimized those themselves; the restore must not undo them).

```csharp
[DllImport("user32.dll", SetLastError = true)]
static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
// SW_MINIMIZE = 6, SW_RESTORE = 9
```

`ShowWindowAsync` (not `ShowWindow`) because a stale window must not block the pass. Calls are
serialized through one gate (`ShowDesktopService.RunClick`) so a double-click cannot interleave
the phase. Windows opened between the two clicks are not in the restore set by design.

**Hover and window size:** the live scale is **1**. Hover paints the square (corner 4 DIP,
`ControlFillColorSecondaryBrush`); the square ends above the bottom **8 DIP** strip. On a window open on the desktop,
the same square and a **16×3 DIP** `AccentFillColorDefaultBrush` line are present. On a minimized
application there is no square, and there is an **8×3 DIP** neutral mini line at the bottom. `DockMagnification` /
`DockStripLayout` stay pure and tested; `dock.magnification` is loaded and is not applied.
There is **no** section separator.

The window is `DockPlateMetrics.PlateWidth` / `PlateHeight` (an empty dock budgets one button).
`SetWindowPos` is **not called** during hover. There is no ScrollViewer; a large number of icons is clipped
to the screen width.

**Dock surface:** `dock.backgroundMode`. `mica` (the default): the window is the plate, real DWM Mica,
`DWMWCP_ROUND`, `AllowsTransparency=false`. `transparent` and `tint`: the window is rebuilt,
it is layered, and the corner is 14 DIP in WPF; the tint fill is dark `#202020` / light `#FAFAFA`, alpha =
`tintOpacity`/100. The old `dock.surface` (`mica` and `acrylic`) drops to `mica` mode on load.
Mica and `AllowsTransparency` are not used together (`docs/04-gotchas.md`).

**Drag and drop:** Dock dragging for reorder uses the `DoDragDrop(StringFormat)` path,
while adding a file uses the `DataFormats.FileDrop` path — one is a `Move` effect and the other is `Copy`,
so `DragOver` does **not mark** the file drop on the cell (`Handled = false`) and bubbles it to the surface
handler. Drag-reorder works **only on fixed (`Pinned`) boxes** — taskbar-pinned tiles included;
the drag payload is **path**-based via `DockItemStore.Normalize(item.Path)`, not via `Id`
(the `Id` of a pinned-and-running box comes from the running application). A drop freezes the whole
left-group sequence into `dock.itemOrder` (`DockItemStore.ReorderOnStrip`): the Windows side is read-only
(ADR-005), so the visual order of a taskbar pin can persist only on the dock side. **The OLE risk (R1) is at window
level and covers reorder too:** `RegisterDragDrop` is per HWND; if a layered
(`WS_EX_LAYERED`) `Topmost` window is not accepted as a target, **no**
`DragOver`/`Drop` arrives on this window — both dropping a file from Explorer and in-app reorder fail.
A fallback exists only for **file drop** (Add in the settings list); reorder has no fallback,
so acceptance must be **measured** live, and manual verification should try reorder first. The root `Grid`
is the drop target but it **has no background** — because the window is sized to the widest magnified width,
a background would have added an ~57 DIP ghost hit area.

---

## h) DPI and display change

- Manifest: Per-Monitor V2
- Listen for `WM_DPICHANGED` and display-change notifications; update the AppBar `QUERYPOS`/`SETPOS` and the window position
- Handlers should be gathered in the `TopBarWindow` / `AppBar` layer
- **Do not compute the position with WPF's DIP-based `Left/Top/Width/Height`** — they assume the primary monitor's
  scale. Compute physical pixels with `GetDpiForWindow` + `GetMonitorInfo`, and apply them with `SetWindowPos`
  (ADR-003).
- The dock (`DockWindow`) takes the `MonitorInfo.Dpi` value at configuration time and refreshes it with the high word of the
  `WM_DPICHANGED` `wParam` (`_monitorDpi`); if `wParam` returns zero, it falls back to `MonitorInfo.Dpi`.
  Placement and the edge probe read only this field — they do not read a `MonitorInfo` copy directly.
  A scale change also rebuilds the strip: because the icon is held in the `AppIconService` cache with a size key,
  it is not enlarged from the old raster.

---

## i) Hit-testing (dock / overlay)

On an overlay dock, the mouse leaving the bar needs hit-test strategies for auto-hide and for “should the click go to the window underneath?”. On a top bar that uses an AppBar, the work area is already set aside, so the hit-test need is different.

**Phase 8 (implemented):** while visible / being shown, `DockHitTest` uses `ApplyInteractive` (`WS_EX_TRANSPARENT` cleared); while hidden or in an off-screen animation, `ApplyPassthrough` (`WS_EX_TRANSPARENT` set) — the click passes to the window underneath. `DockAutoHideController` manages the edge-band poll + the slide animation.

---

## j) AllowUnsafeBlocks

`AllowUnsafeBlocks` is **not required** for basic `[DllImport]` P/Invoke. Turn it on only if pointer arithmetic or an `unsafe` block is actually written.

---

## k) Global shortcut (RegisterHotKey)

A system-wide shortcut for the overlay (ADR-007) and similar toggles; implementation: `Native/Hotkey.cs` + `VeilService`.

```csharp
RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk)      // user32.dll
UnregisterHotKey(IntPtr hWnd, int id)                               // user32.dll
// fsModifiers: MOD_ALT=0x1, MOD_CONTROL=0x2, MOD_SHIFT=0x4, MOD_WIN=0x8
// WM_HOTKEY = 0x0312; wParam = id; message-only HwndSource (ParentWindow=-3)
```

- For receiving messages, a **message-only** window: `HwndSourceParameters` + `ParentWindow = new IntPtr(-3)` (the TrayIconService pattern); `WM_HOTKEY` → toggle via `AddHook`.
- A `RegisterHotKey` failure (a clash with another application) is not a crash: no registration + a warning, and the config is not corrupted.
- **Exit guarantee:** `UnregisterHotKey` is in `VeilService.Dispose` (normal exit + `App.Exit`); if it is missed, the shortcut cannot be registered again on the second start.

---

## l) Dock surface (ADR-014)

The plate is not an AppBar; the live scale stays 1. Detail: ADR-014.

```csharp
DwmRegisterThumbnail / DwmUpdateThumbnailProperties / DwmUnregisterThumbnail   // dwmapi.dll
RegisterShellHookWindow / DeregisterShellHookWindow                           // user32.dll
RegisterWindowMessage("SHELLHOOK"); HSHELL_FLASH = HSHELL_REDRAW | 0x8000
SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART,..., WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS)
PrintWindow(PW_RENDERFULLCONTENT)                                             // user32.dll
SHGetKnownFolderPath(FOLDERID_Downloads)                                      // shell32.dll
SHFileOperationW(FO_DELETE | FOF_ALLOWUNDO)                                   // shell32.dll — no permanent delete
ShellExecuteW(verb: null)                                                     // shell32.dll
SHLoadIndirectString                                                          // shlwapi.dll
SHGetStockIconInfo(SIID_RECYCLER)
PackageManager.FindPackagesForUser / Package.GetLogo                          // Windows.Management.Deployment
```

Progress is not read with `GetProgressValue`. The taskbar button is read with UI Automation (`RangeValue`, `ItemStatus`); if there is no exposed data, the bar is not drawn.

---

### user32.dll / kernel32.dll — clipboard — Island

The documented clipboard API; implementation: `Native/ClipboardNative.cs` + `Services/ClipboardMessageWindow.cs`
(ADR-010).

```csharp
// user32.dll
OpenClipboard(IntPtr hWndNewOwner) / CloseClipboard      // OpenClipboard can block
EmptyClipboard                                           // IRREVERSIBLY deletes the previous copy
IsClipboardFormatAvailable(uint format)
GetClipboardData(uint format)                              // CF_UNICODETEXT = 13
SetClipboardData(uint format, IntPtr hMem)                 // on success it TAKES OWNERSHIP of the memory
AddClipboardFormatListener(IntPtr hwnd)
RemoveClipboardFormatListener(IntPtr hwnd)
RegisterClipboardFormatW(string name)                      // "CanIncludeInClipboardHistory"
GetClipboardOwner                                        // clipboard-owner HWND (IntPtr.Zero if there is none)
GetWindowThreadProcessId(IntPtr hwnd, out uint pid)
// WM_CLIPBOARDUPDATE = 0x031D (message-only HwndSource + AddHook)

// kernel32.dll — the block given to SetClipboardData
GlobalAlloc(uint flags, IntPtr bytes)   // GMEM_MOVEABLE = 0x0002
GlobalLock(IntPtr mem) / GlobalUnlock(IntPtr mem) / GlobalSize(IntPtr mem) / GlobalFree(IntPtr mem)
```

- **Reading must be on an STA thread.** `OpenClipboard` blocks if another process has locked it;
  if it is done on the UI thread, the whole bar freezes. `System.Windows.Clipboard` is **not used** in the store
  (it is bound to the UI thread) — `IslandService` sets up its own STA dispatcher, the `TrayMirrorService`
  pattern. If it is locked, it waits briefly a few times and silently drops the event.
- **Ownership:** an `OpenClipboard(NULL)` + `EmptyClipboard` call sets the clipboard owner to `NULL` —
  the previous owner (the source application) is no longer the owner. `EmptyClipboard` deletes the user's previous
  copy **with no undo**; therefore the blocks are allocated and filled **before** `EmptyClipboard`,
  and every later error requires both `GlobalFree` **and a log**. On success, `SetClipboardData`
  takes ownership of the memory → `GlobalFree` is **not done** from that point (on failure the caller is responsible).
- **Opt-out signal:** Windows' own `Win+V` history uses the `CanIncludeInClipboardHistory` format;
  DWORD `0` = "do not put this content in history". The read path does not take an entry if the DWORD is 0, and it also does not take an entry if the format
  id **cannot be obtained** with `RegisterClipboardFormatW` (fail-closed). The write path
  (`TryWriteText`) **writes the same flag along with it** — otherwise our own `WM_CLIPBOARDUPDATE` recaptures
  our own entry. If the block is shorter than 4 bytes it is not read and counts as 0 (no over-read).
- `CF_UNICODETEXT` is **null-terminated UTF-16**; if UTF-8 is written, reading it back produces corrupt characters.
- `QueryFullProcessImageName` (kernel32) is already in `NativeMethods` as the canonical declaration;
  it is reused when resolving the process name from the clipboard-owner HWND (`GetClipboardOwner` →
  `GetWindowThreadProcessId` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` →
  `QueryFullProcessImageName` → the file name). For elevated / other-session owners, `OpenProcess`
  gives `ERROR_ACCESS_DENIED` — that is normal, and the source-application field stays empty.

> **Trap — `RemoveClipboardFormatListener` is NOT idempotent (measured, 5/5 tries).**
> While a registration exists, the first `Remove` returns `TRUE`; the second and later ones return `FALSE` + `0x57`
> (`ERROR_INVALID_WINDOW`). A window that was never added also gives `0x57`. So one safe call
> **turns into an error** with a second call. It must be called only while registered, and only once;
> the `ClipboardMessageWindow._disposed` flag guarantees this.

On the WPF side, drag-and-drop works with an OLE drop target (`AllowDrop=True` + `DragOver`/`Drop`);
this is the project's **first** use of `AllowDrop`, and acceptance of a registered **AppBar** window
as a drop target was **not measured live** (see `docs/04-gotchas.md`). Drop is open only on the **primary** pill;
a secondary bar has `AllowDrop=false`. The island popup child (`IslandFlyout`) is also `AllowDrop` on
the primary bar so the shelf preview panel can take a drop that follows the surface down: the popup is
a separate, layered HWND (measured `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, not `WS_EX_TRANSPARENT`) and
WPF already registers every `HwndSource` popup as an OLE drop target (`RegisterDragDrop` on that HWND
returns `DRAGDROP_E_ALREADYREGISTERED`), so the panel drop path is reachable; a live Explorer drag over
the panel was still not exercised, so this stays an extension to the pill target, not a replacement.

### user32.dll — excluding from screen capture (island, the last entry)

The pill is not a separate HWND. While `island.idleLabel = lastClip` and the idle face is showing a non-empty preview,
the pill is taken into its own window and only that window drops out of capture. The top bar
stays in the frame. During a fixed "Clipboard", music, and a counter, the pill returns to the bar (ADR-012, ADR-015).

```csharp
// user32.dll — Native/WindowCapture.cs
SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity)
// WDA_NONE = 0
// WDA_EXCLUDEFROMCAPTURE = 0x11
```

Failure is written to the debug log with `GetLastWin32Error`; it is not swallowed silently.

### gdi32.dll — screen capture for the island color picker (ADR-010 addendum)

Implementation: `Native/ScreenBitBlt.cs` + `Services/ScreenSampler.cs`. Documented GDI only —
no `GetPixel`, no undocumented surface.

```csharp
// user32.dll
GetDC(IntPtr hWnd) / ReleaseDC(IntPtr hWnd, IntPtr hDC)    // screen DC; released in finally
GetSystemMetrics(int index)  // SM_X/YVIRTUALSCREEN = 76/77, SM_CX/CYVIRTUALSCREEN = 78/79 (union bounds)
// gdi32.dll
CreateCompatibleDC(IntPtr hdc) / DeleteDC(IntPtr hdc)
CreateCompatibleBitmap(IntPtr hdc, int cx, int cy) / DeleteObject(IntPtr hObj)
SelectObject(IntPtr hdc, IntPtr hgdiObj)
BitBlt(IntPtr hdcDest, int x, int y, int w, int h, IntPtr hdcSrc, int x1, int y1, uint rop)
  // rop = SRCCOPY (0x00CC0020)
GetDIBits(IntPtr hdc, IntPtr hBitmap, uint startScan, uint scanLines, byte[]? bits,
          ref BITMAPINFO bmi, uint usage)  // DIB_RGB_COLORS; 32bpp → top-down BGRA
```

- Coordinates are **physical pixels**: the app is Per-Monitor V2 aware, nothing is rescaled;
  `TryCapture` / `TryCaptureRect` clamp the request into the virtual screen.
- Every DC, bitmap, and selection is released in the caller's `finally` blocks.
- The picker captures its **frozen frame before** its own dim mask shows — see `docs/04-gotchas.md`.

### wlanapi.dll — Wi-Fi list / connecting (Quick Settings)

- `WlanOpenHandle(2, NULL, &ver, &handle)` → `ERROR_SUCCESS`
- `WlanEnumInterfaces(handle, NULL, &list)` — WLAN_INTERFACE_INFO_LIST (InterfaceGuid)
- `WlanScan(handle, &guid, NULL, NULL, NULL)` — triggers a scan (the result is asynchronous; it shows up in the cache of later
  GetAvailableNetworkList calls). If it is not triggered, a stale cache is listed.
- `WlanGetAvailableNetworkList(handle, &guid, 0, NULL, &list)` — WLAN_AVAILABLE_NETWORK_LIST
  (dwFlags & CONNECTED/HAS_PROFILE, wlanSignalQuality 0-100, bSecurityEnabled).
  **Trap:** The struct must match the native layout exactly (628 bytes, `strProfileName` is the first field,
  `dwFlags`+`dwReserved` are at the end, there is no RSSI field) — `WlanStructLayoutTests` locks this in.
  A deviation was shifting the SSID text and breaking the signal. An empty SSID = a hidden network.
- `WlanSetProfile(handle, &guid, 0, xml, NULL, TRUE, NULL, &reason)` — a persistent profile.
  In a plaintext password profile, `<protected>false</protected>` is required; `true` expects an encrypted
  key blob, and `WlanSetProfile` rejects plaintext. A profile is not written for enterprise (802.1X) networks.
- `WlanConnect(handle, &guid, &params)` — WLAN_CONNECTION_PARAMETERS (mode=profile)
- `WlanDisconnect(handle, &guid, NULL)` — disconnects the connected network (the connected row on the Wifi page)
- `WlanQueryInterface(handle, &guid, wlan_intf_opcode_current_connection,...)` — the current SSID.
  The value of `wlan_interface_state_connected` is **1** (4 = disconnected).
- `WlanEnumInterfaces` selects the connected one when there is more than one adapter.
- `WlanFreeMemory` / `WlanCloseHandle`

### user32.dll — display topology (Quick Settings / Project)

- `GetDisplayConfigBufferSizes(QDC_DATABASE_CURRENT | QDC_VIRTUAL_MODE_AWARE, &paths, &modes)`
  then `QueryDisplayConfig` with the real path/mode buffers — the current topology.
  `QDC_DATABASE_CURRENT` is **0x4**, `QDC_VIRTUAL_MODE_AWARE` is **0x10**. A zero buffer returns
  87 on this system. 0x2 is `QDC_ONLY_ACTIVE_PATHS`; together with the topology pointer
  it also returns 87 (the old code thought the flag was 0x2 and always fell back to a guess).
  **Trap:** If the read still fails, the mode is guessed from GetSystemMetrics geometry
  (`ProjectionService.GuessCurrentMode`).
  DISPLAYCONFIG_TOPOLOGY_ID values are 1, 2, 4, and 8 (not consecutive — wingdi.h).
- `SetDisplayConfig(0, NULL, 0, NULL, SDC_TOPOLOGY_x | SDC_APPLY)` — the only proven reliable combination
  for changing topology. Extra flags (SDC_USE_SUPPLIED_DISPLAY_CONFIG,
  SDC_ALLOW_CHANGES, SDC_SAVE_TO_DATABASE) return 87 on a hybrid GPU; SDC_APPLY +
  SDC_VALIDATE together is also invalid.

### user32.dll / powrprof.dll — power actions (the top-bar logo menu)

- `LockWorkStation` (user32) — lock the session
- `SetSuspendState(FALSE, FALSE, FALSE)` (powrprof) — sleep
- `shutdown.exe /s|/r /t 0` — shut down / restart (not P/Invoke, `Process.Start`)

---

## m) Windows Personalization theme (Appearance light/dark)

When the user sets Appearance theme to **light** or **dark**, WharfBar mirrors Windows “Choose your mode” (apps and shell together — not Custom per-app chrome). Implementation: `Native/SystemThemeWriter.cs` + shared `Native/ImmersiveColorNotify.cs` (also used by `SystemAccentWriter` after accent writes).

**Registry (HKCU, best-effort, silent on failure; no capture/restore on WharfBar exit):**

```
Software\Microsoft\Windows\CurrentVersion\Themes\Personalize
  AppsUseLightTheme     REG_DWORD  1 = light, 0 = dark
  SystemUsesLightTheme  REG_DWORD  same value as AppsUseLightTheme
```

When `config.theme` is **system**, these keys are **not** written — WharfBar only follows Windows via existing `AppThemePreference` refresh on `ImmersiveColorSet`.

**Broadcast (same pattern as accent sync):**

```csharp
// user32.dll — NativeMethods.SendMessageTimeout
SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 1000, ...);
// lParam is a Unicode string pointer; ImmersiveColorNotify.Broadcast() centralizes this.
```

Failure does not block WharfBar theme apply. Fixed **light/dark** Appearance changes broadcast `ImmersiveColorSet`; wallpaper pair sync from that path is explicit in `AppearancePage` (external Windows theme changes do not flip hub pairs unless `theme=system`). Detail: [`05-theme-windows-wallpaper.md`](05-theme-windows-wallpaper.md).

---

## n) Windows key hook (ADR-020)

A bare Windows key tap opens the Launchpad. The shell owns the Win-key hotkey, so the documented hotkey API cannot take it:

```csharp
RegisterHotKey(NULL, id, MOD_WIN /* 0x8 */, 0)   // -> FALSE, GetLastError() = 1409 (already registered)
```

Instead `Native/LowLevelKeyboardHook.cs` installs one global low-level keyboard hook on its own message-loop thread (`SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0)`), and `Services/WindowsKeyLauncher.cs` + `Services/WindowsKeyGesture.cs` decide what to do:

```csharp
// user32.dll / kernel32.dll
SetWindowsHookEx(WH_KEYBOARD_LL /* 13 */, proc, hMod, 0)
CallNextHookEx(NULL, code, wParam, lParam)      // return (IntPtr)1 to swallow the event
UnhookWindowsHookEx(hook)
GetMessage/PeekMessage + PostThreadMessage(WM_QUIT)   // own thread lifetime
```

KBDLLHOOKSTRUCT: `vkCode`, `scanCode`, `flags` (`LLKHF_INJECTED = 0x10`), `time`, `dwExtraInfo`.

- **Both physical Win key events are swallowed** (down and up). The shell's `MOD_WIN + vk 0` hotkey therefore never arms, and `Start` does not open on the tap.
- **Chords stay alive:** the first non-Win key pressed while Win is held makes the hook `SendInput` the Win key-down and pass the physical key; on the Win key-up the physical event is swallowed and a synthetic Win key-up is sent. `Win+E`, `Win+Tab`, `Win+Shift+S` and friends behave as before.
- **Injected input is never touched** (`LLKHF_INJECTED`): the dock Task View tile's `Win+Tab` and the `SendInputKeys` helpers (`Win+B/N/P`) pass through unchanged, so the app cannot take its own shortcuts hostage.
- **Key state:** blocking only the key-up leaves the OS key state down (the next key press is interpreted as `Win+<key>`); blocking both events and never touching the state for a tap is what keeps the keyboard clean afterwards (`docs/04-gotchas.md`).
- The hook lives on a dedicated thread because a blocked WPF dispatcher would let Windows time the hook out (`LowLevelHooksTimeout`) and silently drop it. The handler only returns "swallow/pass"; the Launchpad opens through `Dispatcher.BeginInvoke`. `App.OnExit` disposes the hook (unhook + `WM_QUIT`) before the taskbar/AppBar restore.
- Install failure is not fatal: no registration, a `Debug.WriteLine`, and the app runs without the Windows-key shortcut. A restricted (below-medium-integrity) launch cannot install a desktop-wide hook.

