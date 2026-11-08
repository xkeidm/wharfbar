# Theme ↔ Windows + wallpaper pair sync

**Date:** 2026-10-17  
**Status:** Implemented  
**Product:** WharfBar

> Canonical committed design for this feature.

## Goal

Make Appearance theme behave like Windows Personalization for light/dark, and when the current hub-applied wallpaper is a light/dark pair, switch the desktop wallpaper to the matching variant when the resolved theme changes.

## Decisions (locked)

| Topic | Choice |
|-------|--------|
| Windows write | **Light/Dark** write Windows theme; **System** does not touch Windows (WharfBar follows Windows) |
| Wallpaper auto-switch | Only when the path was applied via the island hub **and** a `-light`/`-dark` sibling exists |
| Exit restore | No restore of Windows theme (same policy as accent writer) |
| Gallery UI | Unchanged — still two thumbnails; pairing is backend-only |
| Approach | Extend existing Appearance + thin writers/helpers (not a full ThemeSyncService) |

## Current state

- `config.theme` is `system` \| `light` \| `dark` via `AppThemePreference` + WPF-UI.
- Appearance page already exposes the combo; it only updates WharfBar.
- Bundled wallpapers already use `*-light` / `*-dark` names (see `Wallpapers/README.md`).
- Hub apply goes through `WallpaperService.TryApply` (SPI + optional transition/accent).
- `SystemAccentWriter` is the pattern for best-effort HKCU + `ImmersiveColorSet` broadcast.

## Design

### 1. Windows theme write

New: `Native/SystemThemeWriter.cs` (and any tiny pure helper if useful for tests).

When Appearance sets theme to **light** or **dark**:

1. Update `AppConfig.Theme` + `AppThemePreference` + `ApplicationThemeManager.Apply` (existing).
2. Best-effort write:
   - `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`
   - `AppsUseLightTheme` = `1` (light) or `0` (dark)
   - `SystemUsesLightTheme` = same value (Windows “Choose your mode”, not Custom apps≠shell)
3. Broadcast `WM_SETTINGCHANGE` with `ImmersiveColorSet` (reuse the same SendMessageTimeout pattern as `SystemAccentWriter`).
4. Failure is silent; callers must not depend on success.

When theme is **system**: skip the registry write entirely.

No capture/restore on WharfBar exit.

### 2. Wallpaper pairing

New pure helper: `Services/WallpaperPair.cs` (unit-tested).

Convention (same directory, same extension):

- `name-light.ext` ↔ `name-dark.ext`
- Case-insensitive; only the final `-light` / `-dark` suffix before the extension counts.
- Solids / `turkey-*` / unpaired brand PNGs: no partner → no-op.

API sketch:

```csharp
// Returns partner full path when the sibling file exists; otherwise null.
internal static string? TryResolvePartner(string path, bool wantDark);
```

Also expose a way to know if a path is already the correct variant for `wantDark` (so sync can no-op when already matching).

### 3. Hub memory

Soft field on `IslandConfig`:

- `lastHubWallpaper` (string, default `""`)
- Normalized on load: empty if whitespace; path not validated until sync time (`WallpaperService.IsAllowedPath`).

On successful hub `TryApply`, persist the applied full path into `lastHubWallpaper` and save config.

Partner auto-apply also updates `lastHubWallpaper` to the partner path.

### 4. Sync trigger: `WallpaperThemeSync`

New small service (or static coordinator) that:

1. Reads `island.lastHubWallpaper`.
2. If empty / not allowed → return.
3. Resolves desired dark/light from `AppThemePreference.Resolve()`. If the resolved theme is **HighContrast**, skip sync (no wallpaper flip).
4. If current path already matches desired variant → return.
5. `TryResolvePartner` → if null → return.
6. `WallpaperService.TryApply(partner)` (transition + accent policies unchanged).

Call sites:

| Event | Action |
|-------|--------|
| Appearance `OnThemeChanged` | After WharfBar (+ optional Windows) apply → sync |
| `theme == system` and bars/dock receive `ImmersiveColorSet` | Re-resolve app theme (existing Refresh path) → sync |
| `theme` is fixed light/dark | External Windows theme changes do **not** drive wallpaper sync |

Avoid double-apply loops: sync only applies when the partner path differs from `lastHubWallpaper` / current desktop path; Windows write from Appearance already broadcasts ImmersiveColorSet — when mode is light/dark, ImmersiveColorSet handlers must not treat that as a wallpaper re-sync source beyond the explicit Appearance call (fixed mode ignores external).

### 5. UI copy

Update Appearance theme description (EN + TR):

- Light/Dark also change the Windows personalization theme.
- System leaves Windows alone; WharfBar follows Windows.

No new settings toggle.

### 6. Docs

Update in the same implementation change:

- `docs/01-architecture.md` — theme write + `lastHubWallpaper` + pair sync
- `docs/02-win32-appbar-dwm.md` — Personalize registry keys + broadcast
- `.agent/project.md` — short product note
- `src/WharfBar/Wallpapers/README.md` — pairing rule for light/dark auto-switch

## Out of scope

- Collapsing gallery tiles into one card per pair
- Restoring Windows theme on exit
- Windows Custom mode (apps theme ≠ system chrome)
- Auto-switch for wallpapers not applied through the island hub
- Per-monitor wallpapers
- New NuGet packages

## Testing

- `WallpaperPair` unit tests: pair exists, missing sibling, wrong suffix, extension preserved, already-correct variant.
- Sync policy tests (pure): empty last path, unpaired path, fixed theme ignores “external” flag, system mode allows follow.
- Manual: Appearance light ↔ dark flips Windows Settings → Personalization → Colors; with a pier/alpine pair applied from hub, desktop image swaps; applying a solid then changing theme leaves wallpaper alone; System mode + Windows Settings theme change updates WharfBar and pair wallpaper.

## Success criteria

1. Light/Dark in Appearance updates WharfBar **and** Windows light/dark mode.
2. System does not write Windows theme keys.
3. Hub-applied light/dark pair follows resolved theme; unpaired / non-hub wallpapers do not.
4. Existing SPI transition and wallpaper-accent behavior still apply on partner switch.
