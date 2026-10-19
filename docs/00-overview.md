# 00 — Overview

## Purpose

WharfBar is a Windows desktop shell inspired by Windows, macOS, and Linux desktops. It adds a **top bar** and a **dock**:

- **Top bar:** Clock and status indicators; it reserves screen space with an AppBar.
- **Dock:** Applications on its own list; a click starts the process or focuses the window;
  a centered plate (Mica / transparent / tinted).

The visual language must be **Windows Fluent** (Mica, the system theme). Apple branding and macOS icon language are not used.

## Priority order (when they conflict)

1. **Low resource use** — decisions that grow the RAM/CPU footprint are not accepted silently.
   When idle, WharfBar does not step in front of the desktop: window census is a single pass across all docks (3 s),
   stats are 4 s and off the UI thread (the bar is not refreshed if the visible text has not changed;
   sampling stops if stats are off), the tray mirror is 4 s (icon pixels are not pulled every round),
   the hidden-dock cursor is 200 ms, progress scanning is 20 s when there is no tray button, and the Bluetooth connection
   notification is 10 s. Detail: the idle-load section of [`01-architecture.md`](01-architecture.md).
2. **A native look on Windows** — Fluent Design; not a “macOS clone”
3. **Simplicity** — a personal project; a working skeleton first, polish later

It started for personal use; as it matures, opening it from private → public is planned (MIT).

## Goals (v1)

| Goal | Note |
|-------|-----|
| Top AppBar | `ABE_TOP`, work-area reservation |
| Multiple monitors | Per-monitor top bar / dock on and off; each bar is an AppBar on its own monitor |
| Dock overlay | Topmost; an AppBar is not required (ADR-001) |
| Pinned / running apps | Left: `User Pinned\TaskBar` **read-only** (including closed pins) + `dock.items`; right: unpinned running apps (`EnumWindows`/AUMID); `SHGetFileInfo`. **Nothing is written** to the taskbar pin folder (ADR-005, 2026-10-07). Hide/restore is a separate discipline |
| Tray hybrid + mirror | Status simulation + the native overflow button; a third-party icon mirror via documented comctl32 `TB_*` messages (ADR-009); hidden icons stay in the native overflow |
| Taskbar | Hide/auto-hide while WharfBar is running; restore the previous state on exit (ADR-005) |
| Config | JSON, `%AppData%/WharfBar/config.json` |
| Autostart | Startup-folder shortcut |
| DPI | Per-Monitor V2 (`app.manifest`) |
| Single-instance | Named Mutex |
| Fullscreen | Hide the bar/dock in a fullscreen application (hit-test / state) |
| Resilience | `ABM_REMOVE` on exit + taskbar restore; re-register / hide when explorer restarts |

## Non-goals (v1)

- UWP / Store package icons (the AppxManifest path)
- Reading system / third-party tray icons with `ReadProcessMemory` / injection / undocumented
  NotifyIcon enumeration. (ADR-009: a read-only mirror + click forward on the top bar via documented comctl32 `TB_*` messages
  **is a goal**; hidden/overflow icons are out of scope.)
- **Permanently** breaking the taskbar (leaving it hidden / broken even while WharfBar is closed).
  Hide/auto-hide at runtime + restore **is a goal** (ADR-005); it is not a blanket ban.
- A macOS-style **global application menu** (File/Edit/View) — on Windows each window carries its own menu; the top bar is only clock / status / controls
- Apple / macOS brand elements (name, logo, icon language)
- A theme store and cloud sync
- Downloading a remote plugin or loading an assembly. A local `manifest.json` catalog
  (on/off, slot registration) is in v1 scope (ADR-017);
  top-bar and dock slots are not drawn
- Embedded render engines such as Electron / Flutter (resource priority; see architecture)

## Success criteria (v1)

- Maximized windows do not go under the top bar (every active monitor).
- When the application closes cleanly, the reserved area comes back; the taskbar returns to its previous state (ADR-005).
- The dock shows its own list and running applications; list order is the config order and
  is independent of the taskbar.
- Top bar: third-party tray icons are mirrored (click forward). Chosen ones sit in a horizontal
  pill as the icon alone; the chevron at the end of the pill opens Windows' hidden
  area with `Win+B`.
- Top bar: the right-edge Show Desktop sliver puts every open window away on the first click
  and restores exactly the recorded set (skipping windows closed in the meantime) on the second;
  windows minimized by the user themselves are never touched.
- A second instance does not start (Mutex).
- On a DPI / display change the top bar position does not break (the basic handlers).

## License

MIT (WPF-UI is MIT as well; there is no compatibility issue).

## Source note

The first technical notes (Downloads / the project note) were moved into the structured `docs/` + `.agent/` files.
External notes are not the source; the current truth is this tree. Early notes assumed the dock was also an AppBar,
a `/Windows` folder, and `AllowUnsafeBlocks=true` — those are treated as **invalid** by the ADR-001 / ADR-002 /
`Views/` decision.
