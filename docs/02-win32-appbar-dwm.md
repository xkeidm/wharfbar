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
  The poll is 4 s; the icon pixel is not pulled every round (refresh cadence: `TrayIconRefreshPolicy`, see the 25H2 section). `SendMessageTimeout` (`SMTO_ABORTIFHUNG`, 500 ms) does not
  lock the UI on a hung explorer.
- **Windows 11 25H2+ UIA fallback:** the classic `ToolbarWindow32` tray was removed in this version
  (on 25H2 there is no `SysPager`/`ToolbarWindow32` under `TrayNotifyWnd`). When `FindToolbarHwnd` returns
  empty, the mirror falls back to UI Automation: the `SystemTray.NormalButton` buttons under
  `Shell_TrayWnd → … → TaskbarFrameAutomationPeer` (name + `AutomationElement`); system icons are filtered out
  by a different class (`AccentButton`/`OmniButton*`/`ShowDesktopButton`) plus the input-indicator tooltip.
  There is no icon bitmap in UIA → `TrayCapture` pulls the parked+cloaked taskbar's own content with `PrintWindow(PW_RENDERFULLCONTENT)`
  and crops the button's `Image` child rect
  (the real icon, no flash; `TrayLetterGlyph` fallback if it fails). UIA
  children are exposed only while the taskbar is `SW_SHOW` → every poll reads them with `BeginTrayPeek(stealth:true)`
  (cloak + off-screen park), no flash. DWM cloak is rejected on some machines
  (`DwmSetWindowAttribute` does not return S_OK) → in that case `TrySetTrayVisibleStealth`
  keeps it invisible with a **WS_EX_LAYERED + alpha 0** fallback (UIA continues to see
  `IsWindowVisible=true`); if that fails too, the peek does not open and the poll is skipped.

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
   > being open. `TryRefreshViaBlip` captures the icons **before its own `finally` (`EndTrayPeek`)**
   > and returns a ready `TrayMirrorItem` list; `PollUia` uses only that list.
   > Lasting defense: `TrayCapture.IsBlankCapture` rejects a single-color/fully transparent crop →
   > the caller falls back to a letter glyph, **a black square is no longer possible**
> (tested with `TrayCaptureTests`).
   The mirror is rendered only on the primary monitor's
   top bar (the `trayMirror` section is not created on secondary bars). Click: a `SendInputMouse` physical click for all kinds
   (`InvokePattern` was not verified on 25H2 XAML tray buttons;
   a last-resort fallback if the rect cannot be found). The application's flyout/menu opens according to the parked
   taskbar position (the bottom edge) or the restored cursor → `TrayMenuReanchor`
   moves the application's newly appeared popup window under the mirror icon (`SetWindowPos`,
   work-area clamped; a `#32768` menu always, other classes are candidates only in the click region
   around the clicked button; fullscreen overlays never; 2.5 s watch, cloaked windows are not in the
   pre-click snapshot, and a 1.5 s re-assert counters owners that move their flyout back). Matching is NOT by pid:
   every 25H2 XAML button reports the explorer pid.

**Implemented:** the left brand logo (`BrandLogo.BarGlyph` / `logoStyle`; geometry, not a font glyph — visible on Mica); the exe/tray icon is `Assets/WharfBar.ico` (Dusk solid mark);
the right cluster's default LTR is `trayMirror · hidden · date/time · notification · controlCenter · network · battery`
(`topBar.statusItems`; customizable). `volume` / `brightness` / `media` are in the catalog and
off by default; right-click and the Top Bar (`Üst Çubuk`) setting list every item in the catalog.
**Show all** (`Tümünü göster`) inserts the missing ones next to their neighbor and does not disturb the saved order. Visible items
are ordered by dragging on the bar (a click under the threshold opens the flyout; mouse capture, not OLE).
Clock/date: a single-line combined label in the **resolved UI culture** (`LocalizationService` / `config.language`; e.g. `tr-TR` → `9 Ağu Paz 21:57`, `en-US` → `9 Aug Sun 21:57`, Segoe UI) + a bell glyph; both open the native Notification Center
(`Win+N` — calendar + notifications; there is no separate calendar `Popup`; there is no ADR-005 mirror).
`controlCenter` → QS **Main**; volume → QS **Mixer**; network → QS **Network** — a shared
**Quick settings** (`Hızlı ayarlar`) flyout (ADR-006); a layered `Popup`
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
// shell shows (File Explorer.lnk → Dosya Gezgini). szDisplayName is not an icon path;
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
    thread. Refresh first tries a stateless stealth peek; if that read returns an icon,
    `ABM_SETSTATE` is not flipped (that flip opens and closes the work area, and the desktop
    and windows jump). If it is empty, show-only latches off and the state blip is gated by
    `TrayStateBlipGate` (once per explorer instance; later only ≥10/≥20 min apart, user idle ≥3 min via
    `GetLastInputInfo`, and no maximized/bottom-touching window). Expedited requests never reach it.
    A mirror click reveals the auto-hide tray by hovering the bottom edge (`SetCursorPos` + an absolute
    `SendInput` move; no work-area change) and drops the state only as a fallback. Every tray-driven
    flip is logged as `state-change #N reason=…`. Crops are matted against the taskbar fill
    (`TrayIconMatte`, ring-median background, colour-to-alpha).
    The refresh runs on this cadence **whether or not the direct read returned buttons** (pixels need a
    shown tray; gating the peek on an empty read froze icon state such as Discord's mute icon). Show-only:
    60 s, 20 s while the last refresh found changed pixels; an out-of-band refresh (floor 15 s) follows a
    mirror click (+6 s), a theme/accent change (`WM_SETTINGCHANGE` `ImmersiveColorSet`,
    `WM_THEMECHANGED`, `WM_DWMCOLORIZATIONCOLORCHANGED` → `NotifyAppearanceChanged`, +3 s) and one
    inconclusive peek. One `PrintWindow` per peek (`TrayCapture.TryCaptureFrame`, skipped while
    `IsWindowVisible=False`), per-button crops fingerprinted by `TrayIconSignature`; unchanged pixels
    keep the cached `ImageSource` and an unchanged snapshot is not published. A click whose key is
    missing from the live tray (the app exited, the snapshot was held) is not sent to the old rect.
    An empty read does not clear the mirror (the last good snapshot is held for a bounded grace). Restore is
    `SetState(baseline)` + `SetTrayVisible(true)`; after a force kill, the stale watchdog
    file restores the baseline state.
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
handler. Drag-reorder works **only on fixed (`Pinned`) boxes**;
the drag payload is **path**-based via `DockItemStore.Normalize(item.Path)`, not via `Id`
(the `Id` of a pinned-and-running box comes from the running application). **The OLE risk (R1) is at window
level and covers reorder too:** `RegisterDragDrop` is per HWND; if a layered
(`WS_EX_LAYERED`) `Topmost` window is not accepted as a target, **no**
`DragOver`/`Drop` arrives on this window — both dropping a file from Explorer and in-app reorder fail.
A fallback exists only for **file drop** (Add (`Ekle`) in the settings list); reorder has no fallback,
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
a secondary bar has `AllowDrop=false`.

### user32.dll — excluding from screen capture (island, the last entry)

The pill is not a separate HWND. While `island.idleLabel = lastClip` and the idle face is showing a non-empty preview,
the pill is taken into its own window and only that window drops out of capture. The top bar
stays in the frame. During a fixed "Clipboard" (`Pano`), music, and a counter, the pill returns to the bar (ADR-012, ADR-015).

```csharp
// user32.dll — Native/WindowCapture.cs
SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity)
// WDA_NONE = 0
// WDA_EXCLUDEFROMCAPTURE = 0x11
```

Failure is written to the debug log with `GetLastWin32Error`; it is not swallowed silently.

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

### user32.dll — display topology (Quick Settings / Project (`Yansıt`))

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
