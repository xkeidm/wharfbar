# Conventions

## Language

- Canonical docs and `README.md` are English.
- The only extra translation is `README.tr.md` (Turkish). Do not add other language READMEs.
- API, type, and config names stay English.
- Code identifiers stay English. Code comments and XML doc comments are English.
- Committed docs, code comments, and UI copy defaults are English-only; Turkish exists only in
  `src/WharfBar/Resources/Strings.tr.resx` and `README.tr.md`.
- Commit messages may be English or Turkish, short and why-focused.
- `.cursor` rules stay short pointers; do not paste long guides into them.
- **In-app UI:** English + Turkish via `Resources/Strings.resx` + `Strings.tr.resx`; preference is soft top-level `config.language` (`system` \| `en` \| `tr`, default `system`). Applies at startup (`LocalizationService`); change requires restart. This is separate from docs/README language rules above.

## Documentation

- When behavior changes, `docs/` and `.agent/` are updated together.
- Do not paste long guides into Cursor `.mdc` files; keep them as pointers.
- Notes in Downloads or on an external path are not a source; move the content into `docs/`.

## Code (target, after the scaffold)

- C#: `PascalCase` types/members, private fields `_camelCase`; nullable enabled.
- Folders: `Views/` (or `UI/`), `Native/`, `Models/`, `Services/` — **no folder named `Windows/`**.
- P/Invoke: thin wrappers under `Native/`; do not call raw DllImport from the UI.
- Win32 structs (`APPBARDATA` and the like) are `internal` inside `Native/`; the service layer translates them into domain models.
- A new Win32 call: add the signature to `docs/02-win32-appbar-dwm.md`; do not swallow the error return / `GetLastWin32Error` silently (at least a debug log).
- WPF-UI backdrop first; DWM is the fallback.
- Theme colours: a brush read in code is a **snapshot** of the current WPF-UI theme dictionary. Anything
  code-built uses `DynamicResource` / `SetResourceReference` (never `TryFindResource(...)` assigned to
  `Foreground`/`Background`/`Fill`), otherwise it keeps the old palette after a light/dark/accent switch.
  Window-level theme reaction goes through `AppThemePreference` / `AppThemeWatcher`, not WPF-UI's
  `SystemThemeWatcher` (which applies the raw system theme). Detail: `docs/04-gotchas.md`.
- `schemaVersion` is a required field on config models; prefer reading without breaking, using per-field defaults.
- The AppBar lifecycle lives in one place (register/unregister/re-register); `ABM_NEW` / `ABM_REMOVE` stay symmetric.
- `unsafe` / `AllowUnsafeBlocks` only when there is a real need.
- Meaningful names; no unnecessary abstraction.

## Test / verification (v1 manual)

- AppBar: does a maximized window stay out from under the top bar?
- Exit: does the work area come back?
- After a crash simulation, the Explorer fix is intentional
- Mutex: does a second instance fail to open?
- Position on a DPI change
- Dock: does hover fill the button, and does the window stay inside the plate?
- Dock: does drag-to-reorder work, and does it not launch the app?
- Dock: is a drop from Explorer accepted (if not, is it added from the settings list)?
- Dock: with **auto-hide off**, drag a file from Explorer onto the transparent band beside the plate
  (about 30 px beyond the plate) — does the dock add it? The drop region's real boundary was never measured;
  the root `Grid` comment in `DockWindow.xaml` does not claim it either. If it does not add the file, the next step
  is to decide to shrink the region, not to rewrite the comment.
- Dock: at the 16/36 icon-size extremes, does the plate stay tight to the icons? Does the Mica ↔ transparent transition recreate the window?
- Dock: do taskbar pins not affect WharfBar, and does WharfBar not affect the taskbar?
  (ADR-005 revision: there must be no write or read in either direction)
- Show Desktop sliver: click 1 — every open window minimizes; click 2 — the same set comes back
  (a window closed in between is skipped); a window the user minimized themselves is neither
  minimized again nor restored.

## Versioning

- SemVer; current public line: **`1.0.0-beta`**.
- During beta: `1.0.0-beta.N` → optional `1.0.0-rc.N` → first stable **`1.0.0`** (drops Beta in the app UI).
- After stable: normal PATCH / MINOR / MAJOR.
- About reads **`InformationalVersion`** from `WharfBar.csproj`; `AssemblyVersion` stays numeric.
- User-facing notes go in root [`CHANGELOG.md`](../CHANGELOG.md); security process in [`SECURITY.md`](../SECURITY.md). Detail for contributors: [`CONTRIBUTING.md`](../CONTRIBUTING.md).

## Git

- Commit only when the user asks.
- Do not commit secret files / `.env`.
