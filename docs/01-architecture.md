# 01 — Architecture

> Phase 1–9 + Phase 8 + **the dock redesign (2026-10-04)** code is in the working tree. Below,
> the **current** structure and the **target** structure are marked separately.

## Stack

-.NET 8 WPF, `UseWPF`, target TFM: `net8.0-windows10.0.19041.0`
- WPF-UI package (Fluent controls, backdrop preference order: WPF-UI first)
- `app.manifest` → Per-Monitor V2 DPI
- `AllowUnsafeBlocks`: **not required** for basic P/Invoke; turn it on only if `unsafe` code is actually written

## Why WPF (not Flutter / Electron)

The resource-use priority dominates: Flutter/Electron embed their own render engine (Skia/Chromium) in the
package; for a simple dock/bar that is unnecessary RAM and startup cost. WPF uses the
Windows compositor through DirectX; a typical light dock/bar aims to stay in the ~30–60 MB RAM band.

Fully native (C++/Win32 + Direct2D) would have been lighter still, but there would have been no ready design
system for Fluent (Mica, theme) — for a personal project, WPF + WPF-UI strikes the balance.

## Idle load

While the application is open, continuous work is sparse. While a panel is closed, Wi-Fi, the mixer, and network details are not polled.

| Work | Cadence | Note |
|----|--------|-----|
| Clock | 1 s | The UI is not refreshed if the text is the same |
| Window census | 3 s, single pass | `WindowCensus`. Each dock does not run its own `EnumWindows` round; the process path is in the pid cache |
| CPU / GPU / RAM / network speed | 4 s, off the UI | `SystemStatsService`. The PDH GPU wildcard counter is expensive. If the visible text and the network label have not changed, there is no `Changed`. If `showSystemStats` is off, the timer stops |
| Tray mirror | 4 s | No new timer; the 4 s poll schedules the pixel refresh. Icons are cached by pixel signature; an unchanged snapshot raises no `Changed`. Pixel refresh (one `PrintWindow` per peek): show-only peek every 60 s (20 s while icons are changing); one extra show-only refresh ≥15 s apart after a mirror click, a theme/accent change, or an inconclusive peek. Where show-only cannot read the parked tray it latches off and the work-area-flapping state blip runs only via `TrayStateBlipGate`: once per explorer instance (bootstrap, not while maximized), then ≥10 min (empty mirror) / ≥20 min (upkeep) **and** user idle ≥3 min **and** no maximized/bottom-touching window — never from a click, theme or retry request. A click session re-captures the strip when the last capture is ≥15 s old (no extra timer). Re-anchor: a bounded 2.5 s watch + 1.5 s re-assert per click only |
| Dock auto-hide | 200 ms while hidden, 50 ms while visible | While hidden, the hide timer does not run |
| Dock progress | 2 s / 20 s | UI Automation is 2 s only while there is a running application and the tray button is open; otherwise 20 s |
| Bluetooth notification | 10 s | If the list is the same, the island is not refreshed. 2 s while the Quick Settings Bluetooth page is open |
| Visualizer | 50 ms, only while on the music face | Endpoint peak → attack/staggered release; meter COM stays open |
| Volume indicator | 1 s | A single Core Audio scalar read |

> Early project notes assumed both windows were AppBars. The current decision: the top bar is an AppBar,
> the dock is an overlay (ADR-001). The folder name is **`Views/`**, not `/Windows`.

## Known architectural limit — no global menu

Windows has no system-wide application menu the way macOS does. Application-specific menus like File/Edit/View
**will not** be on the top bar — only the clock, status indicators, and the control
area. This is a deliberate scope limit, not a missing feature.

## Current folder structure (Phase 1–9)

**Views** is used so the folder name `/Windows` is not confused with Win32.

```
WharfBar.sln # at the repo root, a single project
.gitignore
src/WharfBar/
 Views/
 TopBarWindow.xaml(.cs) # top bar; AppBar; left brand logo menu (logoStyle); CENTER island (IslandPill); right status/overflow; WndProc
 QuickSettingsFlyout.xaml(.cs) # Win11-style quick settings (volume/network; ADR-006)
 BrightnessFlyout.xaml(.cs) # Monitor brightness flyout (DDC/CI + WMI Monitorian)
 MediaFlyout.xaml(.cs) # Media flyout: cover/title/artist/controls + session picker (ADR-008)
 IslandFlyout.xaml(.cs) # Island hub panel: tab strip (Pano / Duvar kağıdı / Müzik / Raf / Prompter / Renk) + body host (clipboard, wallpaper, music, shelf, prompter, color picker; ADR-010)
 ColorPickerWindow.xaml(.cs) # Screen color picker overlay: fullscreen work-area topmost (frozen BitBlt frame + loupe; ADR-010 addendum)
 WallpaperGalleryView.xaml(.cs) # Wallpaper hub body: thumbnail grid, click apply, Open folder
 WallpaperTransitionWindow.xaml(.cs) # WorkerW wallpaper crossfade (SPI after fade; 1×1 then parent)
 AboutWindow.xaml(.cs) # About (`Hakkında`) dialog (no AppBar)
 DockWindow.xaml(.cs) # bottom overlay; a three-section dock strip from its own list; plate (Mica / transparent / tinted); fullscreen / work-area
 VeilWindow.xaml(.cs) # fullscreen overlay (click-through, fade; ADR-007)
 SettingsWindow.xaml(.cs) # Settings main window (NavigationView)
 GalleryWindow.xaml(.cs) # Launchpad widget / plugin gallery (ADR-017)
 PluginGalleryView.xaml(.cs) # card list: built-in + local manifest, slot description
 DockItemViewModel.cs # the slot's draw-ready snapshot (icon + tooltip + section flags)
 Views/Controls/
 DockStrip.cs # strip `Panel`: live scale 1 (the math is in `DockStripLayout`)
 DockItemCell.xaml(.cs) # a single slot: button + glyph + hover fill + running pill
 Views/Pages/
 GeneralPage.xaml(.cs) # Settings: general (autostart, language picker + restart note)
 Resources/
 Strings.resx # neutral English UI strings (fallback)
 Strings.tr.resx # Turkish satellite; missing keys fall back to neutral
 Strings.Designer.cs # generated accessors for XAML {x:Static} and C#
 AppearancePage.xaml(.cs) # Settings: appearance (theme → WharfBar + optional Windows via SystemThemeWriter; wallpaper pair sync)
 DockPage.xaml(.cs) # Settings: dock (content list, auto-hide, icon size, background, position, running apps)
 TopBarPage.xaml(.cs) # Settings: top bar (status items)
 VeilPage.xaml(.cs) # Settings: Overlay (`Kaplama`) (color/opacity/shortcut; ADR-007)
 IslandPage.xaml(.cs) # Settings: Island (`Ada`) (visibility/persistence/limits/maintenance; ADR-010)
 PluginsPage.xaml(.cs) # Settings: plugin and widget catalog (ADR-017)
 AboutPage.xaml(.cs) # Settings: about (`Hakkında`)
 Native/
 NativeMethods.cs # DllImports, RECT / APPBARDATA / MONITORINFO / MARGINS
 MonitorNative.cs # dxva2.dll (DDC/CI) + user32.dll monitor P/Invoke
 AppBar.cs # SHAppBarMessage wrapper; QUERYPOS/SETPOS; idempotent ABM_REMOVE
 AppBarPlacement.cs # SETPOS no-op, WM_WINDOWPOSCHANGING clamp, dock slide range
 Display.cs # primary monitor bounds + DPI → physical pixels
 Dwm.cs # corner, immersive dark, backdrop fallback, frame extend
 ShellIcon.cs # IShellItemImageFactory + SHGetFileInfo icon extraction / Destroy
 TaskbarNative.cs # Shell_TrayWnd + ABM_GETSTATE/SETSTATE wrapper
 SendInputKeys.cs # Win+B / Win+N / Win+P / Space / Enter
 TrayOverflowNative.cs # overflow window / TrayNotifyWnd chevron / Win+B
 TrayToolbar.cs # real tray toolbar TB_* reader + click forward (ADR-009)
 WindowEnumeration.cs # EnumWindows candidates (Shell_TrayWnd and similar excluded)
 AppUserModelId.cs # GetCurrentProcessExplicitAppUserModelID
 DockHitTest.cs # WS_EX_TRANSPARENT toggle (ApplyInteractive / ApplyPassthrough)
 PowerStatus.cs # GetSystemPowerStatus wrapper
 NotifyIconHost.cs # Shell_NotifyIcon ADD/DELETE (its own tray icon)
 AudioEndpointVolume.cs # master volume / mute (Core Audio)
 CoreAudioDevices.cs # render device list + default selection
 CoreAudioSessions.cs # application session volumes
 Hotkey.cs # RegisterHotKey P/Invoke + shortcut parser (ADR-007)
 DesktopWallpaper.cs # SPI_SETDESKWALLPAPER / SPI_GETDESKWALLPAPER thin wrapper (island wallpaper gallery)
 ScreenBitBlt.cs # BitBlt + GetDIBits screen capture (BGRA top-down, physical px, virtual-screen clamp; island color picker)
 SystemAccentPack.cs # PURE: accent DWORD + palette bytes for HKCU write
 SystemAccentWriter.cs # best-effort undocumented HKCU Accent/DWM + ImmersiveColorNotify broadcast
 ImmersiveColorNotify.cs # shared WM_SETTINGCHANGE ("ImmersiveColorSet") for accent + Windows theme writers
 SystemThemeWriter.cs # best-effort HKCU Personalize light/dark (AppsUseLightTheme / SystemUsesLightTheme); no exit restore
 ClipboardNative.cs # clipboard P/Invokes (OpenClipboard/GlobalAlloc/listener) + opt-out read/write (ADR-010)
 Models/
 PinnedApp.cs # pin record (path, args, label)
 DockAppItem.cs # unified dock row + `DockSection` (Pinned | Running | Minimized)
 DemoPinnedApps.cs # the default 4 pins (config seed)
 TopBarStatusItemIds.cs # status-strip id catalog + Normalize
 MediaSnapshot.cs # playing-media snapshot (SMTC; Idle + HasSession) — ADR-008
 IslandEntry.cs # island entry (Text/File, preview, file path, time, source application) — ADR-010
 AppConfig.cs # JSON root schema + DockConfig + TopBarConfig + IslandConfig
 Services/
 SingleInstanceGuard.cs # named Mutex
 WindowAppearance.cs # WPF-UI theme + backdrop; DWM fine-tuning
 AppThemePreference.cs # config theme → ApplicationTheme
 BrandPalette.cs # PURE: accent + dark/light → logo tile colours (HSL, per-role saturation caps; ADR-019)
 BrandLogo.cs # Dusk mark + Windows logo geometry (128 grid, frozen); bar glyph + cached dock tile DrawingImage (ADR-019)
 ConfigService.cs # read/write %AppData%/WharfBar/config.json
 LocalizationService.cs # resolve config.language → CultureInfo; Apply at startup
 AppIconService.cs # ShellIcon → WPF ImageSource (DPI-sized)
 ProcessLauncherService.cs # Process.Start launcher
 WindowActivationService.cs # focusing / minimize toggle on a repeated click
 FullscreenState.cs # in-process fullscreen signal (TopBar → Dock)
 WorkAreaChanged.cs # dock reposition event after AppBar placement
 DockAutoHideController.cs # edge-band poll, show/hide animation, state machine
 AutostartService.cs # Startup-folder.lnk; synced with AppConfig.autostart
 StatusService.cs # Clock + network/battery (.NET / GetSystemPowerStatus)
 SystemStatsService.cs # CPU/GPU/RAM sampling (GetSystemTimes / GlobalMemoryStatusEx / PDH)
 MediaSessionSelector.cs # session selection policy: most recently active; MoveCurrent ring (ADR-008)
 MediaSessionService.cs # SMTC monitoring: snapshot + play/pause/next/previous + picker (ADR-008)
 MonitorBrightnessService.cs # Monitor scan, DDC/CI + WMI brightness read/write (debounced)
 VolumeService.cs # master volume poll
 AudioMixerService.cs # device + application mixer
 QuickSettingsService.cs # quick-setting tiles / network actions (including the veil tile)
 WifiService.cs # network list + connect via the Wlan API (QS Wifi page)
 NetworkDetailsService.cs # Network page "Properties" (`Özellikler`): IP/gateway/DNS/MAC/link speed + live ↓/↑ (IPv4 byte delta)
 BluetoothService.cs # paired BT devices + connected state (QS Bluetooth page)
 NightLightService.cs # CloudStore best-effort night-light read/write + session override
 ProjectionService.cs # projection modes via SetDisplayConfig (QS Project page)
 VeilService.cs # overlay state, fade, hotkey host, config persistence (ADR-007)
 ClipboardMessageWindow.cs # clipboard-change listener (message-only HwndSource; WM_CLIPBOARDUPDATE) — ADR-010
 IslandPruner.cs # the island's PURE core: accept/dedupe/prune/search/preview/drop name — ADR-010
 IslandStore.cs # atomic write of %AppData%/WharfBar/island.json + %AppData%/WharfBar/Drop — ADR-010
 IslandHub.cs # hub tab enum + config normalize/resolve (Music-unavailable → Clipboard; flag gating in TopBarWindow) — unit tested
 ScreenSampler.cs # 3 px screen-box sample (pure AverageBgra/RgbAt math; GDI via ScreenBitBlt)
 WallpaperCatalog.cs # PURE merge/list of bundled + AppData wallpaper paths (.jpg/.jpeg/.png/.webp)
 WallpaperAccent.cs # PURE-ish: decode wallpaper → IslandPalette.Dominant → luminance window
 WallpaperAccentApplier.cs # after SPI success: WPF-UI Apply + ApplyLogoChanges + SystemAccentWriter
 WallpaperService.cs # SPI apply + current-path read; path guard; optional transition overlay; optional accent side effect; `HubApplied` → remember hub path
 WallpaperPair.cs # PURE: `-light`/`-dark` sibling path resolution (same folder, case-insensitive suffix)
 WallpaperThemeSyncPolicy.cs # PURE: external Windows follow only in `system` theme; skip HighContrast
 WallpaperThemeSync.cs # `island.lastHubWallpaper` + partner apply when resolved theme changes
 WallpaperTransitionPolicy.cs # PURE: ShouldAnimate(mode, respectSystem, clientAreaAnimation) + UsesBlur
 WallpaperTransitionMotion.cs # PURE: fade ms, blur radius, decode width (~½ primary px)
 IslandService.cs # island lifecycle: STA read thread, a single timeline, debounced write — ADR-010
 ColorMath.cs # PURE color formatting: #RRGGBB (uppercase) / rgb(r,g,b) — unit tested
 IslandPickedColors.cs # session-scoped recent colors (top 8; never written to disk) — unit tested
 TrayIconService.cs # Its own Shell_NotifyIcon + Quit menu
 TaskbarVisibilityService.cs # taskbar capture / hide / restore + watchdog spawn/self-heal
 TaskbarWatchdogState.cs # watchdog baseline JSON store (%LOCALAPPDATA%/WharfBar)
 TaskbarWatchdog.cs # --taskbar-watchdog mode: brings the taskbar back from outside if the process dies without a restore
 DockItemStore.cs # the dock's ordered, deduplicated, persistent fixed list (App is a single instance)
 RunningAppsSource.cs # EnumWindows in the background; one identity per application via path + AUMID + pid
 WindowCensus.cs # a single EnumWindows for all docks (3 s) + pid path cache
 MinimizedWindowsSource.cs # the minimized subset from the WindowCensus table
 DockAppsComposer.cs # taskbar pins on the left (read-only) + dock.items, unpinned running apps on the right
 TaskbarPinnedAppsSource.cs # read-only scan of User Pinned\TaskBar *.lnk; no writing
 DockStripBuilder.cs # adds a 1 px separator slot on section changes
 DockMagnification.cs # PURE magnification math: falloff curve, effect radius, smoothing independent of frame rate
 DockStripLayout.cs # PURE horizontal layout: slot scales, reflow offsets, window budget (max width/height)
 DockDropFormats.cs # dropped-file filter (.lnk/.exe/.bat/.cmd) + path resolution
 ShortcutResolver.cs #.lnk → target path / args / label (read direction; the `.lnk` **write** path was removed on 2026-10-04)
 PluginCatalog.cs # local manifest catalog + built-in on/off (ADR-017); no assembly is loaded
 LaunchpadCatalogCache.cs # the process-wide Start Menu + recent snapshot (TTL: 30 min; Warm in the background; PURE `IsStale` — unit tested)
 LaunchpadWarmup.cs # decodes the cached catalog's icons in idle UI-thread slices (8 per pass, first 64) into the shared `AppIconService`
 TrayOverflowInvoker.cs # peek-show + native overflow + hide again
 TrayMirrorService.cs # polls the tray toolbar, filters system icons, publishes a `TrayMirrorItem` snapshot (ADR-009)
 NotificationCenterInvoker.cs # Win+N
 App.xaml(.cs) # Startup: taskbar hide → TopBar + Dock + tray
 app.manifest # PerMonitorV2 + longPathAware + asInvoker
 WharfBar.csproj
```

`MainWindow.xaml(.cs)`, which came from the `dotnet new wpf` template, was deleted; `StartupUri` was removed and
startup was moved into `App.OnStartup`.

Dock auto-hide / hit-test (Phase 8) is implemented.
Replacing the shell (Phase 9) is ADR-005.
The dock redesign is the ADR-005 revision: an independent list and a magnification engine.
Color overlay (veil) is ADR-007. The media panel (SMTC) is ADR-008.
The island (clipboard history + dropped files) is ADR-010.

## Responsibilities

| Component | Job |
|---------|--------|
| `TopBarWindow` | AppBar; **brand logo: left click opens the Launchpad, right click the app menu**; **island in the center** (`IslandPill` + `IslandPopup`); right cluster; quick settings; `dateTime` + `notifications` (Win+N); `controlCenter` → QS Main; `volume` → QS Mixer; `brightness` → `BrightnessFlyout`; `media` → `MediaFlyout` (hidden when there is no session); `network` → QS Network |
| `QuickSettingsFlyout` | Win11-style quick settings (tiles / volume mixer / network). Page changes in an open panel are a 220 ms slide+fade (`QuickSettingsPageMotion`); there is none on the first open — ADR-006 |
| `BrightnessFlyout` | Monitorian-style monitor brightness popover (master slider + independent monitor cards) |
| `MediaSessionService` | SMTC (`GlobalSystemMediaTransportControlsSessionManager`) monitoring: snapshot (cover/title/artist/playback state), commands (Play/Pause (`Oynat/Duraklat`), Next/Previous (`İleri`/`Geri`)), session selection; events are marshaled to the Dispatcher where it was created; init failure retries in 10 s; `Changed` / `SessionsChanged` (ADR-008) |
| `MediaSessionSelector` | Pure selection policy: the most recently active session is first; advance in the picker ring with `MoveCurrent`; only playing/paused sessions — unit tested (ADR-008) |
| `MediaAppNameResolver` | AUMID → Windows application name (layered): Start Menu shortcut AUMID match (e.g. "Zen", "YouTube Music"), then the running process's `FileDescription` (e.g. `chrome.exe` → "Google Chrome"), then a simplified exe name; a cache per AUMID — unit tested (ADR-008) |
| `MediaFlyout` | Flyout content: session picker (if there is more than one), cover + title/artist, Play/Pause (`Oynat/Duraklat`) + Next/Previous (`İleri`/`Geri`); fixed 340px width; when there is no session, "No media playing" (`Çalan medya yok`) (the popup does not close) |
| `IslandService` | The island's lifecycle: a message-only clipboard listener, a **separate STA read thread**, a single unified timeline, disk writes debounced by 750 ms; `Changed` / `EntryAdded` are raised on the UI dispatcher; file copy/delete is on the STA; `Dispose` → flush the pending write (ADR-010) |
| `IslandPruner` | The island's **pure** core: accept rule (opt-out, 64 KB, empty/whitespace), moving consecutive identical content up, day/`MaxItems` pruning, search matching, preview/relative-time formatting, drop file-name generation — unit tested (ADR-010) |
| `IslandStore` | Atomic **replacement** of `%AppData%/WharfBar/island.json` (metadata) (tmp + `File.Replace`) + `%AppData%/WharfBar/Drop` (dropped files); a single `_ioGate` lock; a corrupt file becomes `.bak` (`.bak.1` on a second corruption), a 64 MB size cap, tolerant per-entry reading; **does not sort** (ADR-010) |
| `ClipboardNative` | Documented clipboard P/Invokes; `CanIncludeInClipboardHistory` opt-out read **and** write; clipboard-owner process name (`GetClipboardOwner` → pid → `QueryFullProcessImageName`) (ADR-010) |
| `ClipboardMessageWindow` | Message-only `HwndSource` (`ParentWindow = -3`) + `AddClipboardFormatListener`; `WM_CLIPBOARDUPDATE` → `ClipboardChanged`; `Dispose` removes the listener **once** (the API is not idempotent) (ADR-010) |
| `IslandFlyout` | Hub shell: horizontal tab strip + `BodyHost` swaps clipboard / wallpaper / music / **shelf chips** / **prompter reading editor** / **color picker** inside the same popup (no nested `Popup`). `PresentHub` restores `island.hubTab` on open (primary); Music tab hidden without SMTC; secondary bars keep only Music. The tab strip keeps label text while it fits and folds labels to icon-only (tooltips + automation names keep the names) once all six tabs converge — the 420 DIP panel cannot hold six labeled tabs. Clipboard body: search + list (`MaxRenderedRows = 200`); wallpaper body → `WallpaperGalleryView`; shelf body: chips with context menu + OLE drag-out; picker body: recent colors + `ScreenPickerRequested`; music body unchanged (ADR-010) |
| `PrompterMotion` / island **Prompter** body | Pure motion engine (`BaseSpeedDip 66`, `ReadingFontDip 17`, `Toggle`, `ClampMultiplier 0.5–3.0`, `SpeedDipPerSec` zero with client-area animation off, `UpdateOffset`, `ClampOffset`, `EndEpsilonDip 0.25`, `CanStartPlayback` — the single play gate) — unit tested — plus the read-view body: multiline editor on top; Playing locks it read-only, bumps the font to `ReadingFontDip`, auto-scrolls the editor's **inner** ScrollViewer (`PART_ContentHost`) on a 16 ms `DispatcherTimer` (started only while Playing and the body is visible; demoted Paused on tab switch and stopped+Editing on panel close); reaching the bottom returns to Editing and fires `PrompterPinnedChanged(false)`; manual vertical drag scrolls the same offset without pausing. Control bar: play/pause toggle (+ label swap `Oynat`/`Duraklat`; disabled with a reason tooltip while `CanStartPlayback` is false — text fully fits → `Kaydırılacak bölüm yok…`, scroll at the bottom → `Kaydırma en altta…`; refresh rides the inner ScrollViewer's `ScrollChanged`, deferred once at tab open so the first measurement sees settled layout), speed slider 0.5–3.0 with a live `1.0×` label, `Başa dön` (rescroll, keep text), `.txt` loader, `Panodan al` (.IslandService.TryLastClipboardText, gentle no-op when empty) (island prompter phase 2) |
| `WallpaperGalleryView` | Thumbnail grid (**WharfBar** bundled + **Senin** AppData groups); click → `WallpaperService.TryApply`; selection chrome when the current desktop path matches a catalog entry; **`Klasörü aç`** → Explorer on `%AppData%/WharfBar/wallpapers/` |
| `WallpaperCatalog` | **Pure** file listing from two roots; extension filter; stable sort by file name — unit tested |
| `WallpaperService` | `TryApply` / `TryGetCurrentPath`; rejects paths outside bundled `Wallpapers/` and `%AppData%/WharfBar/wallpapers/`. When `TransitionRequest` + policy allow: WorkerW crossfade first, **SPI on fade complete**; else instant SPI. Cancels/replaces an in-flight overlay on re-apply. After successful apply, if `island.wallpaperAccent`: `WallpaperAccentApplier` (SPI path unchanged). Hub apply raises `HubApplied` → `WallpaperThemeSync.Remember` (and partner auto-apply updates the same field) |
| `WallpaperAccent` / `WallpaperAccentApplier` | Sample dominant color from the applied image (same luminance window as island album tint); WharfBar accent via `ApplicationAccentColorManager.Apply(..., systemAccentColor: false)` + `App.ApplyLogoChanges`; then `SystemAccentWriter.TryApply` (best-effort HKCU) |
| `SystemAccentWriter` / `SystemAccentPack` | Undocumented `HKCU\...\Explorer\Accent` + `DWM` values + `WM_SETTINGCHANGE` (`ImmersiveColorSet`); silent failure; no explorer restart; no exit restore |
| `WallpaperTransitionPolicy` | Pure matrix: `off` → never; `fade`/`blurFade` + `respectSystem` + `!ClientAreaAnimation` → no; `respectSystem=false` → yes for non-`off`. `UsesBlur` only for `blurFade` — unit tested |
| `WallpaperTransitionMotion` | Pure timing/sizing: fade/blur durations, blur radius, decode width ≈ half primary monitor width (clamped) |
| `WallpaperTransitionWindow` | WorkerW-hosted crossfade (behind icons); HWND starts 1×1 off-screen, parents under WorkerW, then fills the client — avoids top-level fullscreen → `ABN_FULLSCREENAPP` dock collapse; SPI runs from the completed callback |
| `DesktopWallpaperHost` | Progman `0x052C` + enum to locate the wallpaper WorkerW behind `SHELLDLL_DefView` |
| `DesktopWallpaper` | Documented `SystemParametersInfo` only; v1 does not change Windows wallpaper style (Fill/Fit/…) |
| `IslandHub` | `IslandHubTab` + `Normalize` / `Resolve(saved, musicAvailable)`; maps to `island.hubTab` soft strings — unit tested |
| `ScreenBitBlt` | Documented GDI capture: `BitBlt` (SRCCOPY) + `GetDIBits` (32bpp top-down BGRA) via `GetDC`/`CreateCompatibleDC`/`CreateCompatibleBitmap`; `TryCapture` / `TryCaptureRect` in physical pixels clamped into the virtual screen; every DC / bitmap / selection released in `finally` |
| `ScreenSampler` | Small screen box read for the picker: `TrySample` (clamped box at physical px), pure `AverageBgra` (3 px box — a stray sub-pixel edge does not dominate), low-quality fallback `RgbAt` |
| `ColorMath` | Pure formatting: `ToHex` (#RRGGBB, uppercase) / `ToRgb` (`rgb(r,g,b)`) — unit tested |
| `IslandPickedColors` | Session-scoped recent colors for the picker body (top 8, pure `Pushed`: newest first, duplicate → move up, prune from the oldest; transparent is not stored); never written to disk — unit tested |
| `VeilService` | Overlay state + fade + hotkey host (message-only `HwndSource`); `SetEnabled` → `Config.Save`; `UnregisterHotKey` on exit (ADR-007) |
| `VeilWindow` | Fullscreen layered overlay (solid color fill, `Topmost`, permanent `WS_EX_TRANSPARENT` passthrough, 280 ms fade); primary-monitor rect + re-placement on `WM_DISPLAYCHANGE` / `WM_DPICHANGED` |
| `HotkeyNative` / `HotkeyParser` | `RegisterHotKey` / `UnregisterHotKey` P/Invoke; `Ctrl+F11` string ↔ (modifiers, vk) parse/format (ADR-007) |
| `AboutWindow` | About (`Hakkında`) dialog; no shell registration |
| `MonitorBrightnessService` | DDC/CI (desktop `dxva2.dll`) + WMI (`WmiMonitorBrightness`) scan; a 50ms debounced background write |
| `MonitorNative` | `dxva2.dll` P/Invoke functions + `user32.dll` monitor queries |
| `VolumeService` / `AudioEndpointVolume` | Default render-endpoint volume / mute (Core Audio) |
| `AudioMixerService` / `CoreAudioDevices` / `CoreAudioSessions` | A single device + dropdown + application session volumes (ADR-006) |
| `QuickSettingsService` | Network detail, WinRT radio, Settings URI / Win+P |
| `NotificationCenterInvoker` | Native notification center (`Win+N`) |
| `AppBar` | `ABM_NEW` / `QUERYPOS` / `SETPOS` / `REMOVE`; no SETPOS on the same rect; `ABN_*` dispatch; re-registration |
| `AppBarPlacement` | QUERYPOS no-op, approved-rect clamp, dock auto-hide Y range |
| `NativeMethods` | Raw `DllImport` + structs + constants; `internal`, not called directly from the UI |
| `Display` | Primary monitor bounds, DPI, DIP → pixel conversion |
| `Dwm` | Corner preference, immersive dark, backdrop fallback, frame extend |
| `WindowAppearance` | WPF-UI theme + Mica; DWM fine-tuning; theme refresh |
| `SingleInstanceGuard` | Named Mutex; blocks a second instance |
| `DockWindow` | Bottom overlay; read-only taskbar pins + `dock.items` on the left, unpinned running apps on the right, one icon per application; button rhythm (icon 24, button 40, gap 4, plate padding 10×8), no magnification; background `mica` (DWM, window = plate) or `transparent`/`tint` (layered, corner 14); drag-reorder, file drop, position (bottom-left/center/right), a "Windows" (`Pencereler`) submenu; no AppBar |
| `TaskbarVisibilityService` | Capture the taskbar baseline at startup; hide `Shell_TrayWnd` / secondary; keep-hidden monitoring (no `ReapplyHide` inside `ApplyPlacement`); restore on exit. The baseline is written to the watchdog state file, the `--taskbar-watchdog` child process is spawned; if a stale file exists, the tray is shown first at startup (self-heal) |
| `TaskbarWatchdog` | Headless mode (`Program.Main` branches, WPF does not start): watches PID + start-time, and applies the baseline if the process dies without a restore. Safe against PID reuse and a new instance rewriting the file |
| `RunningAppsSource` | `EnumWindows` background thread + `IsHungAppWindow`. Windows are merged on application identity: canonical exe path, AppUserModelId, or pid (transitive). A second process is not dropped; `WindowCount` counts the windows |
| `WindowCensus` | A **single** `EnumWindows` pass for all docks, 3 s, background thread. The process path stays in the cache until the pid disappears. `RunningAppsSource` and `MinimizedWindowsSource` share this snapshot |
| `MinimizedWindowsSource` | Holds the minimized subset of the `WindowCensus` table; `ForProcess(pid)` from the `Windows` cache — the "Windows" (`Pencereler`) menu does **not** call `EnumWindows` on the UI thread (it locks on a hung HWND) |
| `DockItemStore` | The dock's write list: ordered, deduplicated by normalized path, a persistent fixed list (`dock.items`); `TryAdd`/`Remove`/`Move`/`Reload` + `Changed`. The list core is in the `*Core` statics → tested without touching the disk. The key is **always** the normalized path, not `Id` (in a pinned-and-running box, `Id` comes from the running application). Also owns `dock.itemOrder` (frozen left-group display order): `SetOrder`/`ReorderOnStrip` persist it, `Move` unfreezes the moved key |
| `TaskbarPinnedAppsSource` | `User Pinned\TaskBar\*.lnk` **read-only**. The order is the file name in the `Taskband\Favorites` blob; otherwise the file name. It writes neither to the folder nor to the registry |
| `DockAppsComposer` | Left: taskbar pin order, then those that exist only in `dock.items` (the same target once) — unless `dock.itemOrder` freezes a sequence: mentioned tiles in that order, unmentioned trail behind, stale keys ignored. Right: unpinned running apps. Match: full path (`\\?\` is stripped), AppUserModelId, or a resolved `.lnk`; folder pins do not merge with `explorer.exe`. A closed pin has `IsRunning` false. The same application (path / AUMID / pid) is a single box; a count badge while `WindowCount` > 1. A minimized window, or a window behind, does not become a second box |
| `DockStripBuilder` / `DockStripLayout` / `DockMagnification` / `DockPlateMetrics` | One item per row (no separator). Live strip scale is 1; plate metrics are `DockPlateMetrics` (button, gap 4, padding 10×8). The magnification math stays tested and is not applied to the window |
| `ShortcutResolver` | Resolve `.lnk` → target path / args / label (so a dropped shortcut can enter the dock list). The dynamic `.lnk` **creation** path was removed on 2026-10-04, together with `TogglePin`, which wrote a `.lnk` to the taskbar |
| `WindowActivationService` | Focus the running window; if it is already in front, `SW_MINIMIZE` on a repeated click |
| `TrayOverflowInvoker` | Peek-show the hidden tray → `TrayOverflowNative` → overflow; then hide again |
| `ShellIcon` | `IShellItemImageFactory` (DPI px) + `SHGetFileInfo` fallback; destroy the icon / bitmap |
| `AppIconService` | `ShellIcon` → WPF `ImageSource` (size is the cache key) |
| `ProcessLauncherService` | `Process.Start` on a pin click |
| `FullscreenState` | `TopBarWindow` `ABN_FULLSCREENAPP` → hide/show the dock |
| `WorkAreaChanged` | Reposition the dock after top-bar AppBar placement |
| `DockAutoHideController` | `dock.autoHide` state + edge-band poll; show/hide animation; fullscreen bridge |
| `DockHitTest` | `ApplyInteractive` / `ApplyPassthrough` — `WS_EX_TRANSPARENT` toggle |
| `DemoPinnedApps` | The default 4 pins — the first `config.json` seed |
| `AppConfig` / `DockConfig` | JSON schema (`schemaVersion` 2) |
| `ConfigService` | Read/write JSON; `.bak` + default on a broken/incompatible schema |
| `AppThemePreference` | The `theme` field → `ApplicationTheme`; `Resolve()` drives wallpaper pair sync |
| `SystemThemeWriter` / `ImmersiveColorNotify` | When Appearance sets **light** or **dark**, best-effort `HKCU\...\Themes\Personalize` (`AppsUseLightTheme`, `SystemUsesLightTheme`) + `WM_SETTINGCHANGE` (`ImmersiveColorSet`); **system** skips the registry write; silent failure; no exit restore (same policy as accent) |
| `WallpaperPair` | Pure `-light` ↔ `-dark` partner path when the sibling file exists; unpaired names (solids, `turkey-*`, brand PNGs) → no-op — unit tested |
| `WallpaperThemeSync` | Remembers hub-applied paths in `island.lastHubWallpaper`; `FollowResolvedTheme` applies the partner via `WallpaperService.TryApply` (transition + accent unchanged). **Appearance** → sync after theme change (`fromExternalWindows: false`). **`theme=system`** → sync after bars/dock refresh on `ImmersiveColorSet` (`fromExternalWindows: true`). Fixed light/dark ignores external Windows theme for wallpaper |
| `BrandPalette` | Pure: accent colour + `isDark` → tile background / pier / sea lines / sun (ADR-019) — unit tested |
| `BrandLogo` | One home for the logo geometry: `BarGlyph(logoStyle)` (one-colour top bar shape) and `DockTile(logoStyle, …, sizePx)` (themed launch tile; solid small mark at every size). Accent: fresh `GetColorizationColor` with the system accent on, else the applied `SystemAccentColor` resource |
| `AutostartService` | Startup-folder `WharfBar.lnk`; create/delete with `autostart` true/false |
| `LocalizationService` | Normalizes `config.language` (`system` \| `en` \| `tr`); `ResolveCulture` maps `system` → OS UI culture (`tr*` → `tr-TR`, else `en-US`); `Apply` sets `CurrentCulture` / `CurrentUICulture` and default thread cultures **before** the first window (beside theme). Changing language in Settings → General saves immediately and takes effect on the **next launch** (optional Quit now); there is no live UI rebuild |
| `StatusService` | Clock + network/battery; `.NET NetworkInformation` + `GetSystemPowerStatus`; clock/date formatting uses `CurrentCulture` (follows UI language) |
| `NetworkDetailsService` | Network-page "Properties" (`Özellikler`) (2 s poll, while the page is open): the active adapter (Wi-Fi > Ethernet priority) → IP/gateway/DNS/MAC/link speed; live ↓/↑ Mbps from the `GetIPv4Statistics` byte delta, a counter reset or a duration of 0 → null ("—"). The pure calculation methods are unit tested |
| `SystemStatsService` | The top bar's left-cluster CPU/GPU/RAM reading (4 s, off the UI thread); `GetSystemTimes` + `GlobalMemoryStatusEx` + PDH `\GPU Engine(*)\Utilization Percentage` (`PdhAddEnglishCounterW`, independent of localization). The UI is not refreshed if the visible text and the network label have not changed. Sampling stops if `showSystemStats` is off. GPU engine samples are grouped onto the physical adapter by the LUID in the sample name (`ParseAdapterKey`/`GroupByAdapters`); software adapters (`DXGI_ADAPTER_FLAG_SOFTWARE` — Microsoft Basic Render Driver/WARP, `GetSoftwareAdapterLuidKeys`) are excluded: Windows counts them as a D3D adapter, but they are not a physical GPU, and Task Manager does not list them either. When `topBar.showGpuPerAdapter` is on, `GPU0 x% · GPU1 y%`; when it is off, a single total. The pure calculation methods are unit tested |
| `TrayIconService` / `NotifyIconHost` | Its own tray icon + Quit menu (`Shell_NotifyIcon`) |
| `TrayMirrorService` | Polls the tray toolbar every 4 s, filters system icons, and publishes a `TrayMirrorItem` snapshot only when it changed; icon pixels live in `TrayIconCache` (keyed by `TrayIconSignature`) and a refresh peek on the `TrayIconRefreshPolicy` cadence re-captures them; click forward (ADR-009) |
| `PowerStatus` | Battery / AC line (`GetSystemPowerStatus`) |

## Island (ADR-010)

It is **not** a separate window: the island is **inside** the top bar, a
`Border` centered in the middle cell of the Grid; the panel is a `Popup` inside the same window.

```
TopBarWindow.xaml — root Grid (no columns; the three clusters share the same cell)
 ├─ StackPanel (left) : logo + system stats
 ├─ Border "IslandPill" (HorizontalAlignment=Center, AllowDrop) ← island
 └─ StackPanel "StatusStrip" (right) : tray mirror, clock, notification, QS, network, battery …
 (volume / brightness / media are in the catalog; they are not in the default order — ADR-011)

Popup "IslandPopup" (Placement=Bottom, PlacementTarget=IslandPill, StaysOpen=True,
 AllowsTransparency=True, PopupAnimation=None)
 └─ local:IslandFlyout (hub tab strip + BodyHost: clipboard | WallpaperGalleryView | music | shelf chips | prompter reading editor | color picker)
```

The island is only on the primary bar, a pill centered so the AppBar stays **30 DIP** (ADR-011).
When idle, a clipboard glyph; in `persistent` music, a note + the track name; on a volume/brightness change, a short counter.
The multi-line panel is the same `IslandPopup` with an in-panel **hub** (`IslandFlyout`): six tabs on the primary bar, Music-only chrome on secondary bars. Last selected tab is persisted as `island.hubTab` (soft string; no `schemaVersion` bump). Wallpaper apply is primary-bar only (`AttachWallpaper`). The rules are `IslandPresence`. Click and
file drop stay on the same control.

### Wallpaper apply + transition

```
WallpaperGalleryView click
  → WallpaperService.TryApply(path)
       → read previous desktop path (SPI get)
       → if policy + decode + WorkerW OK:
            WallpaperTransitionWindow (1×1 → SetParent(WorkerW) → fill client)
              → crossfade previous → next behind icons
              → on complete: DesktopWallpaper.TrySet (SPI)
         else: DesktopWallpaper.TrySet immediately
       → if island.wallpaperAccent: WallpaperAccentApplier (WharfBar + best-effort system accent; not SPI)
```

SPI remains the only **wallpaper** apply path. Decode/WorkerW failure → instant SPI. A concurrent apply cancels the prior overlay. Accent sync is a post-success side effect; it does not run on exit restore.

### Theme ↔ Windows + hub wallpaper pairs

Design detail: [`05-theme-windows-wallpaper.md`](05-theme-windows-wallpaper.md).

```
Appearance theme change (light | dark)
  → AppConfig + AppThemePreference + ApplicationThemeManager (existing)
  → SystemThemeWriter.TryApply (HKCU Personalize + ImmersiveColorNotify)
  → App.FollowWallpaperTheme(fromExternalWindows: false)

Appearance theme = system
  → no SystemThemeWriter; WharfBar follows Windows via existing Refresh on ImmersiveColorSet
  → TopBar/Dock ImmersiveColorSet → App.FollowWallpaperTheme(fromExternalWindows: true)

Hub WallpaperGalleryView click
  → WallpaperService.TryApply → HubApplied → WallpaperThemeSync.Remember(full path)
  → if resolved theme differs from current `-light`/`-dark` variant and sibling exists:
       FollowResolvedTheme may apply partner (same transition/accent rules)
```

Gallery UI still shows separate light and dark thumbnails; pairing is backend-only.

### New files and their jobs

| File | Job |
|-------|-------|
| `Native/ClipboardNative.cs` | Documented clipboard P/Invokes: `OpenClipboard`/`CloseClipboard`, `GetClipboardData`, `SetClipboardData`, `EmptyClipboard`, `IsClipboardFormatAvailable`, `RegisterClipboardFormatW`, `GlobalAlloc`/`GlobalLock`/`GlobalUnlock`/`GlobalFree`/`GlobalSize`, `Add`/`RemoveClipboardFormatListener`, `GetClipboardOwner` + `GetWindowThreadProcessId`. `TryReadText` reads the opt-out flag **fail-closed**; `TryWriteText` writes the text as UTF-16 **and** writes the opt-out along with it; `GetOwnerProcessName` returns the source application name |
| `Models/IslandEntry.cs` | `internal enum IslandEntryKind { Text, File }` + `internal sealed record IslandEntry(long Id, IslandEntryKind Kind, string Text, string? FilePath, DateTime CapturedAtUtc, string? SourceApp)` — the record of the single unified timeline |
| `Services/IslandPruner.cs` | **Pure** core (no Win32 and no file system → fully tested): `ShouldCapture`, `Prepend` (consecutive identical → move up), `Prune` (day + `MaxItems`, no day limit in session mode), `Matches`, `FormatPreview`, `FormatRelativeTime`, `BuildDropFileName` |
| `Services/IslandPresence.cs` | Pill face, music-panel duration, HUD priority (no WPF). `TitleMaxWidth` is the narrow-monitor cap |
| `Services/IslandStore.cs` | A separate persistence file: `island.json` (tmp + `File.Replace`), the `Drop` folder; a single `_ioGate` lock; `.bak` / `.bak.1`; a 64 MB size cap; one corrupt field does not drop the whole history; **does not sort** (order is the consumer's job) |
| `Services/ClipboardMessageWindow.cs` | Message-only `HwndSource` (`ParentWindow = -3`, the existing `MonitorMessageWindow` pattern); `WM_CLIPBOARDUPDATE` raises `ClipboardChanged` on the UI thread |
| `Services/IslandService.cs` | Lifecycle + orchestration: the STA read thread, `Entries`/`Latest`, `ApplyConfig`, `AddDroppedFiles`, `PutBack`, `OpenShelfFile` (guarded by `CanOpenShelfFile`), `PutOnClipboard` (picker copy; separate writer twin of `PutBack`), `TryLastClipboardText` (prompter `Panodan al`; read-only newest-text pick), `Remove`, `Clear`, `DeleteDroppedFiles`, `Changed`/`EntryAdded`, `Dispose` |
| `Views/IslandFlyout.xaml(.cs)` | Hub panel: tab strip + six bodies; `Attach(service)` for clipboard while open; `AttachWallpaper` on primary only; `PresentHub` / `HubTabChanged` (user tab click → config save); shelf chips (context menu + OLE drag-out), the prompter reading body (`PrompterMotion` + playback pin), and the picker body (`ScreenPickerRequested`, `ShowPickerCopyFeedback`) |
| `Views/WallpaperGalleryView.xaml(.cs)` | Wallpaper hub body; `Attach(WallpaperService)`; bundled pack under `Wallpapers/` (abstract/scenic pairs, solids, Turkey city photos, brand marks — see that folder’s README; copy-to-output beside the exe) |
| `Services/WallpaperCatalog.cs` | Lists image files from bundled + user directories |
| `Services/WallpaperService.cs` | Apply guard + SPI delegate |
| `Native/DesktopWallpaper.cs` | `SPI_SETDESKWALLPAPER` / `SPI_GETDESKWALLPAPER` |
| `Services/IslandHub.cs` | Tab enum + config normalization |
| `Native/ScreenBitBlt.cs` | GDI capture (`BitBlt` + `GetDIBits`, top-down BGRA): `TryCapture` / `TryCaptureRect`; no `GetPixel` |
| `Views/ColorPickerWindow.xaml(.cs)` | Screen color picker overlay: fullscreen work-area topmost, loupe + HEX card, `Settled`/`Cancelled`; a frozen BitBlt frame is captured before the dim mask shows |
| `Services/ScreenSampler.cs` / `ColorMath.cs` / `IslandPickedColors.cs` | Sampling (3 px box average) + pure formatting + session-scoped recent-colors store |
| `Views/Pages/IslandPage.xaml(.cs)` | Settings page: visibility, persistence, days/limit, wallpaper transition ComboBox + **Windows animasyonunu takip et** + **Match accent to wallpaper** toggles, maintenance actions; island service fields need `Config.Save` **+** `IslandService.ApplyConfig` where applicable |

### Clipboard reading on the STA thread

`OpenClipboard` can block; if it is done on the UI thread, the whole bar freezes. `System.Windows.Clipboard`
is **not used** in the store (it is bound to the UI thread). Flow:

1. `WM_CLIPBOARDUPDATE` arrives on the UI thread (`ClipboardMessageWindow`).
2. The work is queued onto `IslandService`'s **separate STA dispatcher thread** (`Dispatcher.Run`,
 `ApartmentState.STA`, `WharfBar.Island`). The signal is shared via `volatile Dispatcher?` and
 readiness is waited on for 1 s; if it is late, the capture is **silently skipped but
 logged** (so it does not stay invisible).
3. If it is locked, wait briefly 3 × 40 ms and retry; if it still will not open, the event is **silently
 dropped**. A retry is never done on the UI thread.
4. The result returns to the UI thread with `BeginInvoke`: adding the entry, pruning, and `Changed` happen **on the UI**
 (the single timeline is UI-only). File copy/delete is on the STA as well.

### A separate persistence file

History is **not written** to `config.json`: `ConfigService.Save` rewrites the whole file on the UI
thread on every call, while clipboard changes are high-frequency. Instead:

| Path | Content |
|-----|--------|
| `%AppData%\WharfBar\island.json` | Entry metadata (`version` + `entries`); 750 ms debounce, atomic replacement |
| `%AppData%\WharfBar\Drop\` | Copies of dropped files (the original is not touched) |

While `island.persistence = "session"`, **metadata** is never written (`Commit` calls `ScheduleSave` only in persistent
mode) and `island.json` is not read; `Drop` copies of dropped files
are written to disk regardless of the mode. A file-write error is not shown to the user; the in-memory
list keeps working and is retried on the next change. File **content** does not enter the on-disk metadata.

### Scope

v1: only the primary monitor's bar (the same limit as `trayMirror` — the service is a single instance in `TopBarEngine`,
`AttachIsland` is called only on the primary). When `Enabled=false` the pill is not drawn,
the listener is not set up, and `island.json` is not read; existing entries are left without touching the disk.
`Clear` does **not** delete file copies (deliberate); only `DeleteDroppedFiles` deletes them, and that
action scans the `Drop` folder so it also covers orphans.

### Redesign and color mode (ADR-018)

- **Decision:** the tint is optional and user-selectable (`island.colorMode`); the default is `album` (tints from the cover while music plays; neutral otherwise).
- **Deferred (not built):** progress bar that thickens on hover (thumb only while dragging); pill-shaped search box with icon; large-icon empty state; image row type with type badge; restyled meter and satellites; theme-aware contrast window for the album tint (the 0.12–0.90 luminance window is theme-independent); play-button focus visual.
- **Join:** the island `Popup` is `AllowsTransparency` (its own layered window, allowed; the Mica ban applies to the bar window only), so the panel is one **silhouette** (`IslandSilhouette`, pure geometry): 24 DIP corners and 10 DIP concave fillets that flare up beside the pill, a drop shadow, and a top-lit gradient rim (tinted when a color is active). The popup child is padded 24 DIP for the shadow; `VerticalOffset` is -12. Near a screen edge WPF may nudge the popup up to 24 DIP, drifting the join. Manual check: the transparent strip over the pill must not swallow pill clicks.
- **Pin:** no pin action on clipboard rows (`IslandService` has no pin support).
- **Code:** `Services/IslandPalette` (pure: `Dominant`, `Luminance`, `Tint`) + `Views/IslandTintSource` (32×32 `TransformedBitmap` copy, cached by bitmap reference, UI thread) → `TopBarWindow.ApplyIslandTint` (pill bars) and `IslandFlyout.ApplyTint` (ambient glow + progress fill). Motion numbers are in `Services/IslandMotion` (pure; `Gate` zeroes them when `ClientAreaAnimation` is off).

### Island widgets phase 1 — shelf + screen color picker (ADR-010 addendum)

- **Decision:** the hub grows two more tabs on the primary bar: **Raf** (`shelf`, the existing
  dropped-file timeline rendered as chips) and **Renk** (`colorpicker`, a screen color picker).
  Soft fields: `island.hubTab` accepts `shelf` / `colorpicker`, and two new soft bools
  `island.shelfTab` / `island.colorpickerTab` (default **`true`**) gate the tabs; `schemaVersion` stays **2**.
- **Tab resolve (`TopBarWindow.Island.ResolveHubTab`):** on the primary bar, when the latest pill face
  is the **Drop** face and the shelf tab is on, the panel opens on the **Raf** tab (the user just
  dropped files, they want to see them). Otherwise the saved tab goes through `IslandHub.Resolve`
  (unknown → Clipboard; Music saved without an SMTC session → Clipboard, unchanged), and afterwards
  a `Shelf`/`Prompter`/`ColorPicker` resolve whose config flag is off falls back to **Clipboard**. All three
  flag-arms live in `ResolveHubTab`; secondary bars keep Music only.
- **Shelf tab:** each dropped entry is a chip — a 3-letter extension badge + the file name.
  Chip context menu: **`Uygulama ile aç`** (guarded by `IslandService.CanOpenShelfFile` →
  `OpenShelfFile`; the `Drop`-prefix and device-namespace guards unchanged) and **`Listeden kaldır`**.
  Panel footer **`Rafını boşalt`** → `DeleteDroppedFiles` (the same `Drop`-folder orphan scan).
  The chip is also a drag **source**: a real OLE drag-out (`DragDrop.DoDragDrop`,
  `DataFormats.FileDrop`, the file path) drops a copy into Explorer/folder windows.
- **Color picker tab:** session-scoped recent colors (`IslandPickedColors`, top 8, pure `Pushed`
  — no disk, never enters `island.json`). **`Ekran seçici`** closes the island panel first and
  opens `ColorPickerWindow`: a transparent topmost covering the primary monitor's **work area**
  (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, `ShowActivated=false`, `Dwm.SetSystemBackdrop(None)` —
  no Mica on a layered window). A frozen `BitBlt` frame of the monitor is captured **before**
  the dim mask shows and every sample comes from that frame (sampling after the mask sits down
  returns darkened colors — see `docs/04-gotchas.md`). Pick → `ColorMath.ToHex` → `IslandService.PutOnClipboard`
  (a dedicated clipboard **write** twin on the STA thread; `TryWriteText` marks its own record with
  `CanIncludeInClipboardHistory=0`, so the pick never enters Win+V history — paste flow is `Ctrl+V`);
  the hub comes back after the overlay closes and lights a ✓/✗ copy feedback in the Renk body
  (the overlay closes on feedback after 900 ms success / 1.6 s failure, with a 3 s failsafe).
- **Motion:** the pill→panel morph gets a ratio-driven duration — `IslandMotion.MorphOpenMs` /
  `MorphCloseMs` lerp **230→170 ms** (open) and **150→110 ms** (close) as the pill/panel width
  ratio rises (clamped 0.18–1); easing is `BackEase(0.28)` on open / `CubicEase` on close and is
  applied to the **scale axis only** (`MorphEase` — a fade must not overshoot). The general chip
  flyout constants became `ChipFlyoutOpenMs` 230 / `ChipFlyoutCloseMs` 150. Everything stays
  `RenderTransform`-based on the inner panel surface (`IslandHost.PanelSurface`) and is zeroed by
  `ClientAreaAnimation` (reduced motion).

### Island prompter reading tab (ADR-010 addendum, phase 2)

- **Decision:** the hub grows a sixth primary-bar tab, **Prompter** (`prompter`): a reading
  auto-scroll view. Soft field `island.prompterTab` (default **`true`**) gates the tab;
  `schemaVersion` stays **2**. Primary bar only (config flag + `_primarySurface` — the Faz 1
  Raf/Renk clone); secondary bars keep Music only.
- **Tab order / resolve:** the enum order is Clipboard → Wallpaper → Music → Shelf → Prompter →
  ColorPicker. `IslandHub.Resolve` is unchanged (unknown → Clipboard; Music saved without an SMTC
  session → Clipboard), and `ResolveHubTab` gains a third flag-arm: a saved `prompter` tab whose
  config flag is off falls back to **Clipboard**.
- **Body + playback:** a multiline reading editor over a control bar — play/pause toggle (label
  swap `Oynat`/`Duraklat`), speed slider 0.5–3.0 (tick 0.1, live `1.0×` label), `Başa dön`,
  `.txt` load (`OpenFileDialog`, `.txt` filter), and `Panodan al` → the additive read-only
  `IslandService.TryLastClipboardText` (the newest text record of the island history; gentle
  no-op when empty — no clipboard read). Playing locks the editor read-only, bumps the font to
  17 DIP and auto-scrolls its inner `ScrollViewer` on a 16 ms `DispatcherTimer`; reaching the
  bottom returns to Editing and unpins; manual vertical drag never pauses. Oynat is gated by the
  pure `CanStartPlayback`: with nothing to scroll below the current offset the request returns to
  Editing, and the button side of the same decision disables the toggle with a reason tooltip
  (`Kaydırılacak bölüm yok…` / `Kaydırma en altta…`) — short text and end-of-reading are spoken
  dead ends, not silent ones (refresh rides `ScrollChanged`, deferred at tab open). The math is
  `PrompterMotion` (pure: `BaseSpeedDip` 66 px/s @1×, `ReadingFontDip` 17, `Toggle`,
  `ClampMultiplier` 0.5–3.0, `SpeedDipPerSec`, `UpdateOffset`, `ClampOffset`,
  `EndEpsilonDip 0.25`, `CanStartPlayback`); see
  `docs/04-gotchas.md` for the sharp edges (playback pin vs. auto-close;
  `ClientAreaAnimation=false` keeps the speed at 0; the enable check measures with the
  editing font).
- **Playback pin (deliberate):** while Playing, the auto-close funnel is guarded at the
  `SyncIslandPopup` `Panel == None` branch — a hover-out duration or a transient-hold expiry no
  longer closes the panel. An explicit close (the pill click) always wins via a one-shot marker;
  unpinning runs a catch-up sync, and the subscription is torn down in `DetachIsland`.

## Brand logo and logo style (ADR-019)

- **Decision:** WharfBar shows its own "Dusk" mark (solid pier to the horizon + low sun; no plank gaps, no sea lines; source SVGs in `docs/brand/logo/`) by default. `logoStyle` = `windows` brings back the Windows logo. One setting covers the top bar and the dock on every monitor.
- **Top bar:** always one colour (`TextFillColorPrimaryBrush`), the solid small mark or four panes, 18 DIP box. Replaces the old drawn Windows logo; the menu and the 30 DIP bar are unchanged.
- **Dock:** the Launchpad tile becomes a rounded brand tile at the normal icon size; click and menu unchanged. Themed colours (`BrandPalette.For(accent, isDark)`), with accent → HSL `(h, s, l)`:

| Role | Dark | Light |
|------|------|-------|
| tile background | `hsl(h, min(s,0.45), 0.17)` | `hsl(h, min(s,0.40), 0.94)` |
| pier (and Windows panes) | `hsl(h, min(s,0.35), 0.94)` | `hsl(h, min(s,0.55), 0.20)` |
| sea lines | `hsl(h, min(s,0.50), 0.38)` | `hsl(h, min(s,0.40), 0.72)` |
| sun | `#F2B66B` | `#E59A45` |

  Rounded to the nearest byte; a grey accent has `h = 0`, `s = 0`. `#0078D4` → dark `#182E3F` / `#EAF0F5` / `#306791`, light `#EAF1F6` / `#17374F` / `#9BBBD4` (`BrandPaletteTests`).
- **Geometry:** WPF vector geometry only (no SVG library, no new package), 128 grid, frozen; tiles are cached `DrawingImage`s keyed by style and palette. The WharfBar mark is the small solid pier + sun at every size (plank gaps and sea lines were dropped for clarity at dock scale).
- **App identity:** `ApplicationIcon` = `Assets/WharfBar.ico` (multi-size dusk tile); Settings title bar, About, and window chrome use `Assets/WharfBar.png`. Tray falls back to the exe icon via `SHGetFileInfo`. Rebuild rasters with `python tools/render-app-icon.py`.
- **Live update:** no timer. The dock's existing theme messages (`WM_THEMECHANGED`, `WM_SYSCOLORCHANGE`, `WM_DWMCOLORIZATIONCOLORCHANGED`, `WM_SETTINGCHANGE("ImmersiveColorSet")`) re-apply the surface and then swap the launch cell's glyph. Settings → Görünüm (logo, theme, accent) calls `App.ApplyLogoChanges`. With the system accent on, the tile reads `ApplicationAccentColorManager.GetColorizationColor()` fresh; other WPF-UI accent brushes still update only when `ApplySystemAccent` runs.

## Window model

```
┌─────────────────────────────────────┐
│ TopBarWindow (AppBar, ABE_TOP) │ ← work area reserved
├─────────────────────────────────────┤
│ │
│ Desktop / apps │
│ │
├─────────────────────────────────────┤
│ DockWindow (Topmost overlay) │ ← usually no AppBar
└─────────────────────────────────────┘
```


## Lifecycle

Implemented (Phase 1–9):

1. `App.OnStartup` → `SingleInstanceGuard.TryAcquire`; if the mutex cannot be acquired, `Shutdown`
2. `ConfigService.LoadOrCreate` → `%AppData%/WharfBar/config.json`; `LocalizationService.Apply(config.Language)` then `AppThemePreference` is applied
3. `AutostartService.Apply(config.Autostart)` — syncs the Startup-folder shortcut
4. `TaskbarVisibilityService.CaptureAndHide` — if a stale watchdog file exists, the tray is shown first
 (self-heal); `ABM_GETSTATE` + `Shell_TrayWnd` visibility baseline; the baseline is written to the watchdog
 file; then hide with `ShowWindow(SW_HIDE)` and spawn the `--taskbar-watchdog` child process
5. `TopBarWindow.Show` — AppBar + left Windows menu + `StatusService` / overflow
6. `DockWindow.Show(config)` — overlay; `TaskbarPinnedAppsSource` (read-only) + `DockItemStore` + `RunningAppsSource` + `MinimizedWindowsSource`
 → `DockAppsComposer` (pins on the left, including closed ones; unpinned running apps on the right); no AppBar registration
7. `TrayIconService` — its own tray icon + Quit menu
8. `OnSourceInitialized` (TopBar) → `HwndSource.AddHook`; `RegisterWindowMessage("TaskbarCreated")`;
 create the `AppBar` (`ABE_TOP`, callback message `WharfBarAppBarMessage`); the status strip is bound;
 the media service is set up: `new MediaSessionService` → `Changed`/`SessionsChanged` subscriptions →
 `MediaHost.Attach(_media)` → `_ = _media.InitializeAsync` (ADR-008)
9. `OnSourceInitialized` (Dock) → hook; `WindowAppearance`; `DockHitTest.ApplyInteractive`;
 `FullscreenState` / `WorkAreaChanged` subscriptions; the three sources + the composer are set up; the first
 `RebuildStrip`; placement
10. Exit paths are wired → `ABM_NEW` → placement is applied → `WorkAreaChanged.Notify`
11. Exit: `DisposeOwnedServices` (service subscriptions are released; `_media.Dispose`) →
 **taskbar restore** (`TaskbarVisibilityService.Restore`) → tray dispose → close the dock
 → `ABM_REMOVE` (idempotent; `ReleaseShellState` order: restore first, then remove)

### Placement math (physical pixels)

WPF's DIP-based `Left/Top/Width/Height` properties are **not used** — they assume the primary
monitor's scale. Instead:

1. `GetDpiForWindow(hwnd)` → the current monitor's DPI (96 if it returns 0)
2. `dip * dpi / 96` → bar height in pixels
3. `MonitorFromWindow(MONITOR_DEFAULTTOPRIMARY)` + `GetMonitorInfo` → primary monitor bounds
4. `ABM_QUERYPOS` → the edge the shell approved; the opposite edge is written back so that it preserves the height
5. `ABM_SETPOS` → move to the approved rect with `SetWindowPos`

### The `ABM_REMOVE` guarantee

`AppBar.Remove` is guarded with `Interlocked.Exchange`: only the first call reaches the shell, so
binding it to more than one path is safe. The wired paths:

| Path | Scenario it covers |
|-----|-------------------|
| `Window.OnClosing` | Normal close |
| `Application.Exit` | Exit via `Shutdown` |
| `Application.SessionEnding` | Logoff / restart |
| `AppDomain.ProcessExit` | Process end without a window |
| `DispatcherUnhandledException` | Unhandled error on the UI thread |
| `AppDomain.UnhandledException` | Unhandled error on other threads |

After `Remove`, `Register` is rejected; this stops a late `TaskbarCreated`
message that arrives during shutdown from re-registering the AppBar.

### Message handling (`TopBarWindow.WndProc`)

| Message | Behavior |
|-------|----------|
| `ABN_POSCHANGED`, `ABN_STATECHANGE` | Queue placement with `Dispatcher.BeginInvoke`; `ABM_SETPOS` only when the QUERYPOS result has changed (deadlock / wait cursor if `SHAppBarMessage` runs inside explorer's `SendMessage`) |
| `ABN_FULLSCREENAPP` | `HWND_BOTTOM` while a fullscreen application is present, `HWND_TOPMOST` when it ends; `FullscreenState.Instance.Set` |
| `ABN_WINDOWARRANGE` | No handling in v1 |
| `TaskbarCreated` | `ABM_NEW` again (explorer restarted) + placement + `TaskbarVisibilityService.ReapplyHide` |
| `WM_DPICHANGED`, `WM_DISPLAYCHANGE` | Re-apply placement with `Dispatcher.BeginInvoke` |
| `WM_WINDOWPOSCHANGED` | `ABM_WINDOWPOSCHANGED` (skipped during our own move) |
| `WM_WINDOWPOSCHANGING` | Clamp to the approved AppBar rect (phantom work-area push; a bar under the bar) |
| `WM_ACTIVATE` | `ABM_ACTIVATE` |

## Config schema (`schemaVersion` 2)

Location: `%AppData%/WharfBar/config.json` 
Implementation: `Models/AppConfig.cs` + `Services/ConfigService.cs` (camelCase JSON, indented).

```json
{
 "schemaVersion": 2,
 "theme": "system",
 "language": "system",
 "logoStyle": "wharfbar",
 "autostart": true,
 "dock": {
 "autoHide": true,
 "iconSize": 24,
 "backgroundMode": "mica",
 "tintOpacity": 40,
 "position": "bottom-center",
 "showRunningApps": true,
 "items": [
 { "path": "C:\\\\...\\\\app.exe", "args": "", "label": "App" }
 ]
 },
 "topBar": {
 "backgroundMode": "tint",
 "tintOpacity": 40,
 "showSystemStats": true,
 "showGpuPerAdapter": true,
 "statusCatalogVersion": 5,
 "statusItems": [
 "trayMirror",
 "trayOverflow",
 "dateTime",
 "notifications",
 "controlCenter",
 "network",
 "battery"
 ]
 },
 "veil": {
 "enabled": false,
 "color": "#4a90d9",
 "opacity": 40,
 "hotKey": "Ctrl+F11"
 },
 "island": {
 "enabled": true,
 "persistence": "session",
 "retentionDays": 30,
 "maxItems": 200,
 "musicPresence": "persistent",
 "visualizer": true,
 "bluetoothNotice": true,
 "cameraMirror": false,
 "openWidgets": [],
 "hubTab": "clipboard",
 "shelfTab": true,
 "colorpickerTab": true,
 "prompterTab": true,
 "wallpaperTransition": "blurFade",
 "wallpaperRespectSystemAnimation": true,
 "wallpaperAccent": true,
 "lastHubWallpaper": ""
 },
 "plugins": {
 "enabled": []
 },
 "pinnedApps": [
 { "path": "C:\\\\...\\\\app.exe", "args": "", "label": "App" }
 ]
}
```

| Field | Meaning |
|------|--------|
| `schemaVersion` | Required; only `1`/`2` are accepted (`> 2` → `.bak` + default). **2** brought in the `monitors` block; the dock redesign **left the schema at 2 anyway** — a preference for reading without breakage via per-field defaults (`conventions.md:24`) |
| `theme` | `system` \| `light` \| `dark` (`AppThemePreference`). **Light/dark** also write Windows Personalization via `SystemThemeWriter`; **system** does not write Windows (WharfBar follows Windows). Pair wallpaper sync uses the resolved theme (see `WallpaperThemeSync`) |
| `language` | UI + formatting culture: `system` / `en` / `tr` (default `system`); applied at startup via `LocalizationService`. Soft field (`schemaVersion` stays 2). `system` uses `CultureInfo.InstalledUICulture` (`tr*` → Turkish, otherwise English). User strings come from `Resources/Strings.resx` + `Strings.tr.resx` (`{x:Static}` / `Strings.*` in C#). A change in Settings → General is saved immediately and applies after restart |
| `logoStyle` | `wharfbar` (default) \| `windows` — the top bar logo and the dock launch tile on every monitor. Soft string; `LogoStyles.Normalize`, unknown/missing → `wharfbar`. `schemaVersion` stays **2** (ADR-019) |
| `autostart` | `true` → `WharfBar.lnk` at Startup; `false` → the shortcut is deleted (`AutostartService`) |
| `dock.autoHide` | `true` → edge-band proximity + animated hide; `false` → always visible |
| `dock.iconSize` | DIP; 16–36 (default **24**). The old 48/72 and the previous default of 28 are pulled down to 24 on load |
| `dock.backgroundMode` | `mica` \| `transparent` \| `tint`; unknown → `mica`. Independent of the top bar. Mica is real DWM (`AllowsTransparency=false`). Tint/transparent is a layered window; switching rebuilds the dock |
| `dock.tintOpacity` | 0–100; `tint` only; default **40**. The fill is dark `#202020` / light `#FAFAFA` |
| `dock.surface` | A retired JSON field (`mica` \| `acrylic`). Pinned to `mica` on load; the choice goes through `backgroundMode` |
| `dock.items` | An ordered write list (Path/Args/Label). Taskbar pins are read-only in addition; “pin / unpin” (`sabitle` / `kaldır`) changes only this list. The key is the normalized full path |
| `dock.itemOrder` | Frozen left-group display order (normalized paths, taskbar pins included), written by the strip's drag-reorder so a taskbar-pinned tile can move too. The taskbar side stays read-only (ADR-005). Empty = taskbar order, then `dock.items` order; a settings-page row drag unfreezes that key; stale keys are ignored |
| `dock.position` | `bottom-left` \| `bottom-center` \| `bottom-right`; invalid/empty → `bottom-center`. A side dock is out of scope |
| `dock.magnification` | It stays in JSON, is clamped to 1.0–2.0 on load (default 1.15), and is **not applied**. The strip scale is 1 |
| `dock.showRunningApps` | Unpinned running apps + minimized windows |
| `topBar.backgroundMode` | `mica` \| `transparent` \| `tint`; unknown → `mica` (the default). A Mica ↔ translucent switch requires a TopBar recreate |
| `topBar.tintOpacity` | 0–100; alpha in tint mode; default **40**; clamped on load |
| `topBar.statusCatalogVersion` | Soft catalog version (independent of `schemaVersion` 2). Current **5**: a missing `controlCenter` is inserted before `volume`, a missing `brightness` after `volume`, a missing `media` after `brightness` (`Ensure*`; an old version is moved to 5 at startup — ADR-008/009) |
| `topBar.statusItems` | Visible status-item ids (left→right order). Unknown ids are dropped; empty/missing → `DefaultOrder`. The **default** does not include `volume` / `brightness` / `media` (ADR-011); every catalog item is listed on right-click and in the Top Bar (`Üst Çubuk`) setting, the saved list is not stripped, and a hidden item does not turn itself on. **Show all** (`Tümünü göster`) inserts the missing ones next to their catalog neighbor. Dragging on the bar changes the order with `MoveToInsertion`. `statusCatalogVersion` stays at 5. A soft field (`schemaVersion` 2) |
| `topBar.showSystemStats` | The `CPU x% · GPU y% · RAM z%` reading next to the brand logo in the left cluster (default **`true`**; soft bool — a missing field falls back to the default). If there is no GPU counter, the GPU section is not shown |
| `topBar.showGpuPerAdapter` | If there is more than one physical GPU, number the GPU sections (`GPU0 x% · GPU1 y%`); when off, a single combined segment (default **`true`**; soft bool). With a single GPU both modes look the same |
| `veil.enabled` | Overlay on/off (persistent; written by the QS tile + the shortcut + the page toggle). Default **`false`** |
| `veil.color` | Overlay color `#RRGGBB` (soft; invalid/missing → the `VeilNormalizer` default **`#4A90D9`**, normalized to lowercase; `#RGB` is accepted) |
| `veil.opacity` | 0–100 overlay strength; `ClampOpacity` on load (default **40**). Black + high opacity = brightness reduction (there is no separate control) |
| `veil.hotKey` | Global shortcut text (`Ctrl+F11`); empty = none. A soft field: if it is invalid, it is not registered and the string is kept (a warning on the page) |
| `island.enabled` | Island + clipboard listener on/off (default **`true`**). When `false`, the pill is not drawn, the `WM_CLIPBOARDUPDATE` listener is not set up, and `island.json` is not read; entries are left without touching the disk. Turning it off from the settings page also takes effect on the service via `IslandService.ApplyConfig` (ADR-010) |
| `island.persistence` | `"session"` \| `"persistent"` (default **`"session"`**). **A string, not an enum** — an enum-typed field threw `JsonException` on the value `"Persistent"` and reset the whole config. `IslandNormalizer.NormalizePersistence` is case-insensitive; an unresolvable value falls back to `"session"` (ADR-010) |
| `island.retentionDays` | How many days back are kept in persistent mode; clamped with `ClampRetentionDays` to 1–3650 on load (default **30**). Ignored in session mode (ADR-010) |
| `island.maxItems` | A hard cap on the entry count (independent of the day limit); clamped with `ClampMaxItems` to 10–5000 on load (default **200**). The panel also builds at most 200 rows (`IslandFlyout.MaxRenderedRows`); anything beyond that is reported with the note "N more entries (narrow with search)" (`N kayıt daha var (arama ile daralt)`) (ADR-010) |
| `island.musicPresence` | `"persistent"` (default) \| `"transient"`. How music sits on the pill (ADR-011). A soft string; `IslandNormalizer.NormalizeMusicPresence`, unresolvable → `persistent`. `schemaVersion` does not increase |
| `island.idleLabel` | `"clipboard"` (default, the pill says **Clipboard** (`Pano`)) \| `"lastClip"` (a preview of the last text). A soft string; `IslandNormalizer.NormalizeIdleLabel`, unresolvable → `clipboard`. `schemaVersion` does not increase (ADR-012) |
| `island.visualizer` | Five bars on the island pill while music is playing (default **`true`**). A gallery switch. Soft bool (ADR-015, ADR-017) |
| `island.colorMode` | `"album"` (default) \| `"system"` \| `"neutral"`. Who is tinted and the fallbacks: `IslandPalette.Tint`. A soft string; `IslandNormalizer.NormalizeColorMode`, unresolvable → `album`. `schemaVersion` stays **2** (ADR-018) |
| `island.bluetoothNotice` | The name of a newly connected paired device stays on the pill for a short time (default **`true`**). Soft bool (ADR-015) |
| `island.cameraMirror` | A camera preview on the pill. Default **`false`**; turned on from the gallery. If there is no permission, the text stays (ADR-015) |
| `island.openWidgets` | Widget ids that are open on the desktop: `clock`, `calendar`. Unknown ids are dropped on load. They do not enter the island pill (ADR-015) |
| `island.hubTab` | Last island hub page: `"clipboard"` (default) \| `"wallpaper"` \| `"music"` \| `"shelf"` \| `"prompter"` \| `"colorpicker"`. Soft string; `IslandNormalizer.NormalizeHubTab` / `IslandHub.Normalize`, unresolvable → `clipboard`. On open, `IslandHub.Resolve` falls back to Clipboard when Music is saved but no SMTC session exists; `ResolveHubTab` moves to the shelf when the latest pill face is Drop and adds a Clipboard fallback for a `shelf`/`prompter`/`colorpicker` tab whose config flag is off. Written on the primary bar when the user clicks a tab (`HubTabChanged`); `schemaVersion` stays **2** (ADR-010 addendum) |
| `island.prompterTab` | The island hub's **Prompter** (reading auto-scroll) tab visible/usable (default **`true`**). Soft bool; a saved `prompter` tab with this off resolves to Clipboard. `schemaVersion` stays **2** (island prompter phase 1/2) |
| `island.shelfTab` | The island hub's **Raf** (shelf) tab visible/usable (default **`true`**). Soft bool; hiding it also drops the Drop-face default to Clipboard. `schemaVersion` stays **2** (ADR-010 addendum) |
| `island.colorpickerTab` | The island hub's **Renk** (color picker) tab visible/usable (default **`true`**). Soft bool; a saved `colorpicker` tab with this off resolves to Clipboard. `schemaVersion` stays **2** (ADR-010 addendum) |
| `island.wallpaperTransition` | Post-apply animation: `"off"` \| `"fade"` \| `"blurFade"` (default **`blurFade`**). Soft string; `IslandNormalizer.NormalizeWallpaperTransition`, unresolvable → **`blurFade`**. Primary monitor only; overlay shows the **previous** image fading out over the SPI-applied desktop. `schemaVersion` stays **2** |
| `island.wallpaperRespectSystemAnimation` | When **`true`** (default), skip the overlay if `SystemParameters.ClientAreaAnimation` is false. When **`false`**, `fade`/`blurFade` run even when Windows animation effects are off. Soft bool; `schemaVersion` stays **2** |
| `island.wallpaperAccent` | After a successful island hub wallpaper apply, sync WharfBar accent (and best-effort Windows HKCU accent) from the image dominant color (default **`true`**). Soft bool; `schemaVersion` stays **2**. Does not alter SPI wallpaper apply or restore accent/wallpaper on exit |
| `island.lastHubWallpaper` | Full path of the last wallpaper applied through the island hub (default **`""`**). Soft string; normalized to empty when whitespace on load; not path-validated until sync. Drives light/dark **pair** auto-switch only for hub-applied wallpapers with a `-light`/`-dark` sibling; partner apply updates this field. `schemaVersion` stays **2** |
| `plugins.enabled` | Local manifest ids (`%AppData%/WharfBar/plugins/<klasör>/manifest.json`; `<klasör>` = folder). A soft list; `schemaVersion` stays **2**. Built-in ids do not live here. Registration is not drawing: no assembly is loaded, and top-bar and dock slots are not drawn in v1 (ADR-017) |
| `pinnedApps` | **Legacy.** Read only once, by `ConfigService.MigrateDockItems`, when `dock.items` is empty, and then **cleared**. The live taskbar `.lnk` scan was removed on 2026-10-04 (ADR-005 revision) |

If the file is missing on first run, the default is written with `DemoPinnedApps`. The save is atomic
(temp + replace). Broken JSON → `config.json.bak` + the default.
