# Faz 8 — Dock Auto-Hide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete `dock.autoHide`: edge-band proximity show/hide with slide animation, click-through while hidden, and clear fullscreen interaction — without changing pin/running UI.

**Architecture:** `DockAutoHideController` owns poll + state machine; `DockWindow` owns `SetWindowPos` animation and placement; `DockHitTest` toggles `WS_EX_TRANSPARENT` via `NativeMethods`. No AppBar on dock (ADR-001). No `AllowsTransparency` (Mica).

**Tech Stack:** .NET 8 WPF, existing `Native/` P/Invoke, WPF `DispatcherTimer` + optional `DoubleAnimation` on a helper dependency property or CompositionTarget/manual tick for Y.

**Spec:** [`docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md`](../specs/2026-08-10-dock-auto-hide-design.md)

## Global Constraints

- Dock remains overlay (no `SHAppBarMessage`); primary monitor only
- Never pair `AllowsTransparency=true` with Mica
- `dock.autoHide` already in config; no schema bump
- Edge band **8 DIP**; hide debounce **500 ms**; show debounce **0 ms**; anim **~200 ms** ease-out
- Hidden Y = `monitor.Bottom` (fully below screen); visible Y = existing `ApplyPlacement`
- Fullscreen: keep `Visibility=Collapsed` path; stop auto-hide timer; on exit → Visible + interactive then resume rules
- Repo has **no unit test project** — verify with `dotnet build WharfBar.sln` + manual checklist in spec
- **Do not git commit** unless the user later asks (repo convention: commit only on request)
- Human docs Turkish; code identifiers English
- Update `docs/` + `.agent/` in the same change when behavior lands
- Raw P/Invoke stays `internal` in `Native/`

---

## File map

| File | Role |
|------|------|
| `src/WharfBar/Native/NativeMethods.cs` | `WS_EX_TRANSPARENT`, `SetWindowLong` |
| `src/WharfBar/Native/DockHitTest.cs` | Real interactive / passthrough |
| `src/WharfBar/Services/DockAutoHideController.cs` | State machine + cursor poll |
| `src/WharfBar/Views/DockWindow.xaml.cs` | Wire controller, animate Y, hit-test, fullscreen |
| `docs/03-development-order.md` | Faz 8 checklist |
| `docs/01-architecture.md` | Stub → live notes |
| `docs/02-win32-appbar-dwm.md` | §i short note |
| `.agent/project.md`, `.agent/context/roadmap.md` | Roadmap P0 |

---

### Task 1: `WS_EX_TRANSPARENT` + `DockHitTest`

**Files:**
- Modify: `src/WharfBar/Native/NativeMethods.cs`
- Modify: `src/WharfBar/Native/DockHitTest.cs`

**Interfaces:**
- Produces: `NativeMethods.WS_EX_TRANSPARENT = 0x00000020`; `SetWindowLong(IntPtr, int, int)`; `DockHitTest.ApplyInteractive` / `ApplyPassthrough` actually toggle the bit

- [ ] **Step 1: Add constants + SetWindowLong**

In `NativeMethods.cs` next to existing `WS_EX_*`:

```csharp
internal const int WS_EX_TRANSPARENT = 0x00000020;
```

Near `GetWindowLong`:

```csharp
[DllImport("user32.dll", SetLastError = true)]
internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
```

(Project targets 64-bit-capable `net8.0-windows`; existing code already uses `GetWindowLong` returning `int` — match that pattern for consistency with `WindowEnumeration`.)

- [ ] **Step 2: Implement `DockHitTest`**

Replace stubs:

```csharp
namespace WharfBar.Native;

/// <summary>
/// Edge-band auto-hide hit-testing: clear or set WS_EX_TRANSPARENT for click-through.
/// </summary>
internal static class DockHitTest
{
    internal static void ApplyInteractive(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        int cleared = ex & ~NativeMethods.WS_EX_TRANSPARENT;
        if (cleared != ex)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, cleared);
        }
    }

    internal static void ApplyPassthrough(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        int with = ex | NativeMethods.WS_EX_TRANSPARENT;
        if (with != ex)
        {
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, with);
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln --no-restore` (or with restore if needed)  
Expected: 0 errors

- [ ] **Step 4: Do not commit** — leave working tree dirty; report files changed

---

### Task 2: `DockAutoHideController` state machine

**Files:**
- Modify: `src/WharfBar/Services/DockAutoHideController.cs`

**Interfaces:**
- Consumes: `NativeMethods.GetCursorPos`, `POINT`, caller-supplied monitor bottom band + dock rect (physical px)
- Produces:
  - `enum DockAutoHideVisualState { Visible, Showing, Hiding, Hidden }`
  - `event EventHandler? Changed`
  - `DockAutoHideVisualState State { get; }`
  - `bool Enabled { get; }`
  - `void Start(Func<EdgeProbeContext> probeFactory)` / `void Stop()`
  - `void NotifyFullscreen(bool isFullscreen)`
  - `void NotifyPointerOverDock(bool over)` — optional if rect probe covers dock; prefer rect-only
  - Constants: `EdgeBandDip = 8`, `HideDelayMs = 500`, `PollMs = 50`

`EdgeProbeContext` (same file or nested):

```csharp
internal readonly record struct EdgeProbeContext(
    int MonitorLeft,
    int MonitorTop,
    int MonitorRight,
    int MonitorBottom,
    int DockLeft,
    int DockTop,
    int DockRight,
    int DockBottom,
    uint Dpi);
```

Logic:

1. If `!Enabled` or fullscreen → do not transition to Hidden; if was polling, ignore hide requests.
2. Each poll: `GetCursorPos`; if fail, return.
3. Cursor in edge band: `Y >= MonitorBottom - DipToPixels(8, Dpi)` and `X` in `[MonitorLeft, MonitorRight)` → request Show.
4. Cursor in dock rect (when Visible/Showing/Hiding mid-slide use **visible** dock rect from probe — DockWindow should report the *logical* interactive rect = visible placement, not off-screen rect while animating) → stay Visible; cancel hide timer.
5. Else → start/restart hide debounce; after 500 ms → request Hide.
6. `RequestShow` / `RequestHide` only change state to Showing/Hiding if not already there; raise `Changed`.
7. `NotifyAnimationCompleted(DockAutoHideVisualState resulting)` — DockWindow calls when slide finishes → Visible or Hidden.

Use `System.Windows.Threading.DispatcherTimer` constructed with `Dispatcher.CurrentDispatcher` passed in ctor (DockWindow UI thread).

Skeleton:

```csharp
namespace WharfBar.Services;

internal enum DockAutoHideVisualState
{
    Visible,
    Showing,
    Hiding,
    Hidden,
}

internal readonly record struct EdgeProbeContext(
    int MonitorLeft,
    int MonitorTop,
    int MonitorRight,
    int MonitorBottom,
    int DockLeft,
    int DockTop,
    int DockRight,
    int DockBottom,
    uint Dpi);

/// <summary>
/// Proximity auto-hide: polls cursor against bottom edge band + dock rect.
/// </summary>
internal sealed class DockAutoHideController : IDisposable
{
    internal const double EdgeBandDip = 8d;
    internal const int HideDelayMs = 500;
    internal const int PollMs = 50;

    private readonly bool _enabled;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _hideTimer;
    private Func<EdgeProbeContext>? _probe;
    private bool _fullscreen;
    private bool _disposed;

    internal DockAutoHideController(bool enabled, Dispatcher dispatcher)
    {
        _enabled = enabled;
        State = DockAutoHideVisualState.Visible;
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(PollMs),
        };
        _pollTimer.Tick += OnPollTick;
        _hideTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(HideDelayMs),
        };
        _hideTimer.Tick += OnHideTimerTick;
    }

    internal bool Enabled => _enabled;
    internal DockAutoHideVisualState State { get; private set; }
    internal event EventHandler? Changed;

    internal void Start(Func<EdgeProbeContext> probe)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        if (!_enabled || _fullscreen)
        {
            return;
        }

        _pollTimer.Start();
    }

    internal void Stop()
    {
        _pollTimer.Stop();
        _hideTimer.Stop();
    }

    internal void NotifyFullscreen(bool isFullscreen)
    {
        _fullscreen = isFullscreen;
        if (isFullscreen)
        {
            Stop();
            return;
        }

        // Caller shows dock; reset to Visible then Start again.
        SetState(DockAutoHideVisualState.Visible);
        if (_enabled && _probe is not null)
        {
            _pollTimer.Start();
        }
    }

    internal void NotifyAnimationCompleted()
    {
        if (State == DockAutoHideVisualState.Showing)
        {
            SetState(DockAutoHideVisualState.Visible);
        }
        else if (State == DockAutoHideVisualState.Hiding)
        {
            SetState(DockAutoHideVisualState.Hidden);
        }
    }

    // OnPollTick / OnHideTimerTick / SetState / Dispose — implement per spec
}
```

Implement full poll helpers with `Display.DipToPixels(EdgeBandDip, dpi)` for band height. Cancel `_hideTimer` when pointer in band or dock. When requesting Show from Hidden/Hiding, `SetState(Showing)`. When hide fires, `SetState(Hiding)`.

If `!_enabled`: `State` stays `Visible`; `Start` is no-op for timer.

- [ ] **Step 1: Replace stub file with full controller** (above + complete private methods)
- [ ] **Step 2: Build** — fix ctor call sites temporarily if DockWindow still uses old ctor (`new DockAutoHideController(bool)` → will fix in Task 3; for Task 2 only, keep a compatibility overload `DockAutoHideController(bool enabled) : this(enabled, Dispatcher.CurrentDispatcher)` so build still works)
- [ ] **Step 3: Do not commit**

---

### Task 3: Wire `DockWindow` animation + hit-test + fullscreen

**Files:**
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs`

**Interfaces:**
- Consumes: Task 1–2 APIs
- Produces: working auto-hide UX per spec checklist

- [ ] **Step 1: Construction**

```csharp
_autoHide = new DockAutoHideController(config.Dock.AutoHide, Dispatcher);
```

Subscribe `_autoHide.Changed += OnAutoHideChanged` in `OnSourceInitialized` after hwnd ready; call `_autoHide.Start(BuildEdgeProbe)` when `Enabled`.

`BuildEdgeProbe`:

```csharp
private EdgeProbeContext BuildEdgeProbe()
{
    IntPtr hwnd = _source!.Handle;
    uint dpi = Display.GetDpi(hwnd);
    RECT monitor = Display.GetPrimaryMonitorBounds(hwnd);
    // Logical visible dock rect (not current off-screen rect):
    int heightPx = Display.DipToPixels(_dockHeightDip, dpi);
    int widthPx = /* same measure as ApplyPlacement */;
    int x = monitor.Left + (monitor.Width - widthPx) / 2;
    int yVisible = monitor.Bottom - heightPx; // BottomGapDip == 0
    return new EdgeProbeContext(
        monitor.Left, monitor.Top, monitor.Right, monitor.Bottom,
        x, yVisible, x + widthPx, yVisible + heightPx,
        dpi);
}
```

- [ ] **Step 2: `OnAutoHideChanged`**

- `Showing` / `Hiding`: start Y animation toward visible or `monitor.Bottom`
- On completed: `_autoHide.NotifyAnimationCompleted()`; then if `Hidden` → `DockHitTest.ApplyPassthrough(hwnd)`; if `Visible` → `ApplyInteractive`
- Entering `Showing` from `Hidden`: `ApplyInteractive` **before** or as animation starts so user can click when mostly visible (spec: interactive when visible/showing — apply Interactive at start of Showing)

- [ ] **Step 3: Animate Y without AllowsTransparency**

Preferred approach: `DispatcherTimer` ~15ms or WPF `DoubleAnimation` on a private `DependencyProperty` `AnimY` with callback calling `SetWindowPos` (NOMOVE cleared). Duration 200ms, `QuadraticEase EaseOut`.

Cancel in-flight animation on DPI/work-area change; snap to target for current state.

- [ ] **Step 4: `ApplyPlacement` integration**

When `_autoHide.Enabled` and state is `Hidden`/`Hiding`, after computing visible geometry still apply **hidden** Y if state is Hidden (instant), or don’t fight mid-animation. Simplest: if `State is Hidden`, place at `monitor.Bottom`; if `Visible`, normal Y; if animating, skip full ApplyPlacement except width/x updates.

- [ ] **Step 5: Fullscreen**

In `OnFullscreenChanged`:

```csharp
if (FullscreenState.Instance.IsFullscreen)
{
    _isFullscreenHidden = true;
    _autoHide.NotifyFullscreen(true);
    Visibility = Visibility.Collapsed;
    return;
}

_isFullscreenHidden = false;
Visibility = Visibility.Visible;
DockHitTest.ApplyInteractive(_source.Handle);
_autoHide.NotifyFullscreen(false);
// existing Topmost restore + ApplyPlacement
```

- [ ] **Step 6: Dispose**

`OnClosed`: `_autoHide.Changed -= ...`; `_autoHide.Dispose()` / `Stop()`.

- [ ] **Step 7: Build** `dotnet build WharfBar.sln` — 0 errors
- [ ] **Step 8: Do not commit**

---

### Task 4: Docs + agent context

**Files:**
- Modify: `docs/03-development-order.md` — Faz 8 checkboxes → done notes
- Modify: `docs/01-architecture.md` — DockHitTest / DockAutoHideController descriptions
- Modify: `docs/02-win32-appbar-dwm.md` — §i one short paragraph that Faz 8 uses `WS_EX_TRANSPARENT` while hidden
- Modify: `.agent/project.md` — remove “auto-hide stub” wording; point to this spec
- Modify: `.agent/context/roadmap.md` — Faz 8 done (kod); manuel doğrulama bekliyor

- [ ] **Step 1: Apply doc edits** (Turkish prose; keep API names English)
- [ ] **Step 2: Do not commit**

---

### Task 5: Controller verification gate

**Files:** none (verification only)

- [ ] **Step 1:** `dotnet build WharfBar.sln` — confirm 0 errors / 0 warnings preferred
- [ ] **Step 2:** Report summary for human manual QA (spec checklist items 1–7)
- [ ] **Step 3:** Do not run the app headlessly in a way that leaves AppBar stuck; if agent runs app, close via normal Quit

---

## Manual QA (human after Task 5)

1. `autoHide: true` — edge show / delay hide  
2. Pointer on dock — stays  
3. Hidden click-through  
4. Visible launch/focus  
5. `autoHide: false` — always on  
6. Fullscreen hide/restore  
7. Maximize + Topmost edge show  

Then tell controller: look OK / tweak X / next phase.
