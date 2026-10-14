# Stack

Priority: low resource use > Fluent native look > simplicity. WPF was chosen for RAM/startup cost
compared with Flutter/Electron (rationale: [`../../docs/01-architecture.md`](../../docs/01-architecture.md)).

## Runtime / UI

| Piece | Choice | Note |
|-------|--------|------|
| Framework |.NET 8 | `net8.0-windows10.0.19041.0` |
| UI | WPF | `UseWPF` — no embedded Chromium/Skia |
| Design system | WPF-UI | Fluent; backdrop starts here |
| DPI | Per-Monitor V2 | `app.manifest` |
| Unsafe | Off (default) | Not required for basic P/Invoke |
| License | MIT | WPF-UI is MIT too |

## Native (P/Invoke)

| API | Use |
|-----|-----|
| `SHAppBarMessage` | Top bar AppBar |
| `DwmSetWindowAttribute` / `DwmExtendFrameIntoClientArea` | Fallback visuals |
| `SHGetFileInfo` | Dock icons |
| `Shell_NotifyIcon` | WharfBar's own NotifyIcon (`TrayIconService` / Quit menu) |

##.NET APIs (simulation / system)

- Clock / culture
- `NetworkInformation` (connection)
- Power / battery APIs
- `Process.Start` / shell execute (launcher)

## Persistence / OS integration

- Config: JSON under `%AppData%/WharfBar/` (`config.json`; local plugin folder `plugins/`)
- Autostart: **Startup folder** shortcut
- Single-instance: named **Mutex**

## Dock code (2026-10-04 redesign)

| File | Role |
|------|------|
| `Services/DockMagnification.cs` + `Services/DockStripLayout.cs` | Pure magnification/layout math (no WPF types, tested) |
| `Services/DockItemStore.cs` | Independent pinned list (`dock.items`; a single `App` instance) |
| `Services/WindowCensus.cs` | One window census for every dock (3 s, pid path cache) |
| `Services/MinimizedWindowsSource.cs` | Minimized-window table (`WindowCensus` snapshot) |
| `Services/DockStripBuilder.cs` | Section-separator slots (1 DIP) |
| `Services/DockPlateMetrics.cs` | Plate geometry (button, gap, padding, height/width) |
| `Views/Controls/DockStrip.cs` | Strip `Panel` — live scale 1; the math is in `DockStripLayout` |
| `Views/Controls/DockItemCell.xaml` | Single cell: icon + running indicator \| separator |

## Consciously out of scope (v1)

- UWP ApplicationModel / AppxManifest icon path
- Multi-monitor AppBar
- Reading/writing taskbar **pins** (the dock's only list is its own `dock.items` —
  ADR-005 revision). **Hiding/restoring** the taskbar is in scope (ADR-005 §3).
- macOS global menu / Apple brand elements
- Flutter / Electron (resource cost)
