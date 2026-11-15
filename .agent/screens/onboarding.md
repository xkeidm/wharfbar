# First-run setup (onboarding)

`Views/OnboardingWindow.xaml(.cs)` — shown by `App.ShowOnboarding()` on a **fresh install**
(`config.onboardingCompleted` false) and again from Settings → General → **Run setup again**
(`OnboardingWindow(replay: true)`, final button "Done"). It runs **modally over the already running
shell**, so the real top bar, dock and desktop behind the window preview every choice; the wizard
itself is only the rail + header + footer, one `UserControl` per step lives in `Views/Onboarding/`.

Window: FluentWindow, Mica backdrop (**never** `AllowsTransparency` + Mica), 1080×720, Mica/theme
follows the app through `AppThemeWatcher`; size is clamped into the current work area so the title
bar never lands under the top-bar AppBar. Left rail: brand mark + version, numbered steps with icon
badges that turn into accent checks, the active step gets an accent bar; rail items jump to a step.
Footer: **Skip setup** (left), "Step N of 7", **Back**, **Continue** (primary; "Start WharfBar" on
the fresh-install last step, "Done" in replay). Enter advances, Esc does nothing (the title-bar X is
the same as Skip: the current settings are kept and the wizard closes).

Step changes are Windows-style page transitions (`Services/OnboardingStepMotion`): the whole pane —
title, subtitle and body — slides in from the **right** on Continue/Next and from the **left** on
Back, travelling 32 DIP and fading in over 220 ms (`CubicEase` ease-out, the same rhythm as the
quick-settings page change). The clip sits on the **static** host (`Grid Grid.Column="1"
ClipToBounds`), not on the moved pane, so the parked page is cut at the rail instead of dragging its
clip along; with `ClientAreaAnimation` off the step is swapped in place with no slide.

Timing (render harness at 1080×720): one step change costs 5–17 ms (fresh `UserControl` + layout) and
the 220 ms slide renders at ~7 ms/frame with at most one or two dropped frames. The wallpaper gallery
was the outlier — it used to decode ~20 photos inline (450–600 ms) — and is now background work.

| # | Step | What it does |
|---|------|--------------|
| 1 | **Welcome** | Brand hero + version, what WharfBar adds (top bar, dock, customization), the taskbar note ("it hides the Windows taskbar while running; quit from the tray to bring it back") and "nothing here is final — rerun from Settings". No config. |
| 2 | **Look and feel** | Three flat theme cards (System / Light / Dark: icon + label + check), accent source (Windows accent vs fixed theme accent), logo style with real `BrandLogo.BarGlyph` previews. Applies live through `AppearanceApplier` (same pipeline as Settings → Appearance, including Windows personalization on light/dark). |
| 3 | **Top bar** | Status-item toggle rows in the Windows tray-corner order (tray icons → hidden icons → volume → brightness → network → battery → quick settings → clock → notifications; volume and brightness are on by default, `media` is the only hidden one), system-stats and per-GPU toggles, backdrop mode (Mica / transparent / tint) with the tint-only opacity slider; a mode that changes `WS_EX_LAYERED` rebuilds the real bar (`RecreateTopBar`). |
| 4 | **Dock** | Position tiles (bottom-center / bottom-left / bottom-right / left edge), icon size slider 16–36 DIP, dock backdrop + tint-only opacity (rebuild vs. repaint like the top bar), auto-hide and show-running-apps toggles. |
| 5 | **Wallpaper** | Bundled + user thumbnails from `WallpaperCatalog`; a click applies the Windows wallpaper immediately through a step-owned `WallpaperService` (accent sync intentionally off during setup), selection ring + current-wallpaper line + open-folder button. Tiles paint instantly from a shared frozen thumbnail cache; a cold gallery decodes in the background (two at a time, never inline in the constructor) and the wizard prewarms it after opening. |
| 6 | **How to use** | Drawn mini shell (top bar with brand, island pill, status glyphs and clock; a desktop window; the dock plate) plus four cards: top bar (logo menu, island, status strip), dock (pins/running, click to minimize, right-click menu), Launchpad & shortcuts (Windows key, Win+chords, quit restores the taskbar), Settings (where it is, this setup can be replayed). |
| 7 | **Finish** | Read-back summary (theme, visible bar item count, dock position), "WharfBar is already running behind this window", start-with-Windows toggle and display language (restart note), plus the "change everything later in Settings" hint. |

Every change is **saved immediately** by the step (`ConfigService.Save`) and applied to the live
shell — there is no Apply button and no cancel/reset. A fresh step instance is created on every
visit, so jumping back after another step always shows the current config.

Completion marker: `config.onboardingCompleted` (soft field, `schemaVersion` unchanged). Skipping,
closing or finishing sets it; quitting the app mid-setup does not, so the next launch continues.
A config written before the field existed, or one reset after corruption, counts as completed —
setup is a first-install experience, not an upgrade nag. `WharfBar.exe --setup` forces the wizard.

Screenshot/QA harness (git-ignored): `.superpowers/scratch/onboarding-render` renders all seven
steps in light/dark and en/tr without starting the shell (`App.SuppressShellStartup`), and validates
every `Symbol="…"` value against `SymbolRegular` (an invalid glyph only fails at XAML load).

Related: `Services/AppearanceApplier.cs` (shared apply pipeline), `Services/WallpaperService` +
`WallpaperCatalog` (wallpaper step), `.agent/screens/settings.md` (where the replay button lives).
