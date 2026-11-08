# WharfBar — Agent Index

**WharfBar** is a Windows desktop shell inspired by Windows, macOS, and Linux desktops: a top bar and a dock.

## Stack

- **.NET 8** + **WPF** + **WPF-UI** (Fluent / Mica)
- **Win32 P/Invoke** (`SHAppBarMessage`, DWM, `SHGetFileInfo`, etc.)
- Target: `net8.0-windows10.0.19041.0`, Per-Monitor V2 DPI

## Canonical source

**`.agent/` is the source of truth for AI.** Cursor `.cursor/rules/*.mdc` files are short pointers only; do not copy the detail into Cursor rules.

## Read before coding

1. [`.agent/project.md`](.agent/project.md) — project summary
2. [`.agent/boundaries.md`](.agent/boundaries.md) — do / ask / never
3. [`.agent/conventions.md`](.agent/conventions.md) — code and documentation rules
4. [`docs/00-overview.md`](docs/00-overview.md) — purpose, goals, non-goals
5. [`docs/01-architecture.md`](docs/01-architecture.md) — planned structure
6. Relevant technical document:
   - Win32 / AppBar / DWM → [`docs/02-win32-appbar-dwm.md`](docs/02-win32-appbar-dwm.md)
   - Development order → [`docs/03-development-order.md`](docs/03-development-order.md)
   - Gotchas → [`docs/04-gotchas.md`](docs/04-gotchas.md)
7. Context: [`.agent/context/stack.md`](.agent/context/stack.md), [`.agent/context/roadmap.md`](.agent/context/roadmap.md)
   (completed-phase history for agents; **public / what’s-next backlog** → [GitHub Milestones](https://github.com/xkeidm/wharfbar/milestones))
8. UI tour (top bar, dock, settings): [`.agent/screens/README.md`](.agent/screens/README.md)

## Build / run

Code root: **`src/WharfBar/`**, solution file at the repo root (`WharfBar.sln`).

```bash
dotnet build WharfBar.sln
dotnet run --project src/WharfBar
dotnet test                       # tests/WharfBar.Tests (xUnit)
```

## Code map

- `src/WharfBar/Models/` — config + data models (`AppConfig`, `PinnedApp`, `DockAppItem`)
- `src/WharfBar/Native/` — Win32 P/Invoke (`AppBar`, `Dwm`, `ShellIcon`, `TaskbarNative`, …)
- `src/WharfBar/Services/` — logic (`ConfigService`, `StatusService`, `TrayIconService`, `DockAppsComposer`, …)
- `src/WharfBar/Views/` — windows (`TopBarWindow`, `DockWindow`, `SettingsWindow`, `AboutWindow`, flyouts)
- `src/WharfBar/Views/Pages/` — settings pages (`General`, `Appearance`, `Dock`, `TopBar`, `About`)

> **Warning:** Running the application reserves an AppBar region along the top edge. Force-killing
> the process (`Stop-Process -Force`, detaching the debugger) skips the `ABM_REMOVE` path;
> the **taskbar** is now brought back by the watchdog child process within ~0.5 s, but
> the reserved AppBar strip stays stuck — the fix for that is restarting `explorer.exe`.
> Close the window the normal way.

The machine may have only the .NET 9 SDK installed; that is fine, it can build the
`net8.0-windows10.0.19041.0` target. **Do not pin to 8.x with `global.json`** — if the 8.x SDK
is missing, the build breaks.

## Boundaries (summary)

- **Never:** On exit, forget AppBar unregister (`ABM_REMOVE`) or taskbar restore; do not use Mica together with `AllowsTransparency=true`; do not read foreign tray icons via memory or an undocumented path.
- **Do (ADR-005):** While WharfBar is running, hide/auto-hide the taskbar; on exit/crash, restore the previous state. Dock: on the left, `User Pinned` is **read-only** (including closed pins) + `dock.items`; on the right, unpinned running apps; one box per application. The pin folder is not written; strip reordering persists only to `dock.itemOrder`. Magnification ceiling is 1.15×. Tray: hybrid (native overflow button; no full mirror).
- **v1:** Primary monitor only; UWP/Store icons are limited. Quick settings (Wi-Fi/BT/project) work through real APIs (wlanapi, WinRT Radio, SetDisplayConfig); the top bar network tooltip stays on the .NET network API heuristic.
- Detail: [`.agent/boundaries.md`](.agent/boundaries.md)

## Documentation updates

When behavior or architecture changes, update the matching `docs/` and `.agent/` files in the same change. Do not bloat the Cursor rules.

Committed docs, code comments, and UI copy defaults are **English-only**. Turkish exists only in
`src/WharfBar/Resources/Strings.tr.resx` and `README.tr.md`. See [`.agent/conventions.md`](.agent/conventions.md).
