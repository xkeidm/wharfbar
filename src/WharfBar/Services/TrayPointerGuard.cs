using System.Diagnostics;
using System.Runtime.InteropServices;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Keeps the physical pointer where the user clicked a mirrored tray icon while the menu owner
/// opens its menu. Measured on 25H2: Epic, RustDesk, Monitorian, AMD and NVIDIA all call
/// <c>SetCursorPos</c> to their real tray button at the screen's bottom edge 0–20 ms after the
/// mirror's Menu-key gesture. Undoing that warp afterwards is not enough — NVIDIA's menu belongs
/// to a SYSTEM-integrity window (<c>NVDisplay.Container</c>), and while it is in the foreground a
/// medium-integrity process can neither call <c>SetCursorPos</c> nor inject input, so the undo
/// silently failed and the pointer stayed at the bottom edge.
/// <para>
/// The guard therefore <b>prevents</b> the visible warp: just before the gesture (after the tray
/// button has taken keyboard focus — a foreground change releases an earlier clip) it confines
/// the cursor with <c>ClipCursor</c> to a few pixels around the click point, so the owner's own
/// <c>SetCursorPos</c> is clamped in place. The clip is released after
/// <see cref="ClipHoldMs"/>; the system also releases it if the owning process exits (verified on
/// 25H2), so a crash cannot leave the cursor confined.
/// </para>
/// <para>
/// A short escape watcher stays for the rest of the click: a warp that still lands in the tray
/// anchor band (clip unavailable, release race) is put back and the result is logged honestly —
/// a refused <c>SetCursorPos</c> is not reported as a restore.
/// </para>
/// </summary>
internal sealed class TrayPointerGuard : IDisposable
{
    /// <summary>Half-size of the clip box around the click point, in physical px.</summary>
    internal const int ClipMarginPx = 6;

    /// <summary>
    /// How long the clip stays armed after a gesture. Every measured owner warps within 20 ms of
    /// the Menu key; a long hold would start fighting the user's own mouse movement.
    /// </summary>
    internal const int ClipHoldMs = 150;

    /// <summary>How long the escape watcher runs for one click.</summary>
    internal const int WatchMs = 600;

    private const int PollMs = 3;
    private const int RestoreDeadZonePx = 2;
    private const int RestoreAttempts = 3;
    private const int RestoreRetryMs = 25;

    /// <summary>A displacement this small is the clamped warp, not the user moving the mouse.</summary>
    private const int ClampedWarpTolerancePx = ClipMarginPx + 4;

    private readonly object _sync = new();
    private readonly POINT _home;
    private readonly RECT _trayButton;
    private readonly RECT _monitor;
    private readonly Timer _clipTimer;
    private readonly Thread _watcher;
    private bool _clipActive;
    private bool _disposed;

    private TrayPointerGuard(POINT home, RECT trayButton, RECT monitor)
    {
        _home = home;
        _trayButton = trayButton;
        _monitor = monitor;
        _clipTimer = new Timer(_ => ReleaseClip(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new Thread(Watch)
        {
            IsBackground = true,
            Name = "WharfBar.TrayPointerGuard",
        };
        _watcher.Start();
    }

    /// <summary>
    /// Starts the escape watcher for one click. <paramref name="trayButton"/> is the real tray
    /// button's rect and <paramref name="monitor"/> the primary monitor's bounds (physical px).
    /// </summary>
    internal static TrayPointerGuard Start(POINT home, RECT trayButton, RECT monitor) =>
        new(home, trayButton, monitor);

    /// <summary>
    /// Confines the cursor to a tiny box around the click and releases it after
    /// <see cref="ClipHoldMs"/>. Call right before the gesture that opens the owner's menu; safe
    /// to call again for a follow-up gesture. A refused clip only loses the prevention, never the
    /// click.
    /// </summary>
    internal void ArmClip()
    {
        // Never yank a pointer that the user already moved away from the click point (the click
        // repair path can replay the gesture seconds later): without this, ClipCursor would pull
        // the cursor back into the box. Skipping the clip only loses the prevention.
        if (!NativeMethods.GetCursorPos(out POINT now) || MovedFrom(now, _home, ClipMarginPx))
        {
            TrayPeekDiagnostics.LogNote("cursor clip skipped: pointer left the click point");
            return;
        }

        RECT box = ComputeClipBox(now, _monitor, ClipMarginPx);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (!NativeMethods.ClipCursor(ref box))
            {
                TrayPeekDiagnostics.LogNote($"cursor clip failed err={Marshal.GetLastWin32Error()}");
                return;
            }

            _clipActive = true;
            _clipTimer.Change(ClipHoldMs, Timeout.Infinite);
        }
    }

    /// <summary>Releases the clip now (idempotent).</summary>
    internal void ReleaseClip()
    {
        lock (_sync)
        {
            if (!_clipActive)
            {
                return;
            }

            _clipActive = false;
            _ = NativeMethods.ReleaseCursorClip(IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        ReleaseClip();
        _clipTimer.Dispose();
        _watcher.Join(200);
        ReleaseClip();
    }

    /// <summary>
    /// Pure: the clip box around <paramref name="home"/> clamped inside the monitor. The box is
    /// one pixel at minimum so it stays valid when the click sits exactly on a monitor edge.
    /// </summary>
    internal static RECT ComputeClipBox(POINT home, RECT monitor, int marginPx)
    {
        int margin = Math.Max(0, marginPx);
        if (monitor.Width <= 0 || monitor.Height <= 0)
        {
            return new RECT
            {
                Left = home.X - margin,
                Top = home.Y - margin,
                Right = home.X + margin + 1,
                Bottom = home.Y + margin + 1,
            };
        }

        int left = Math.Max(monitor.Left, home.X - margin);
        int top = Math.Max(monitor.Top, home.Y - margin);
        int right = Math.Min(monitor.Right, home.X + margin + 1);
        int bottom = Math.Min(monitor.Bottom, home.Y + margin + 1);
        if (right <= left)
        {
            right = Math.Min(monitor.Right, left + 1);
        }

        if (bottom <= top)
        {
            bottom = Math.Min(monitor.Bottom, top + 1);
        }

        return new RECT { Left = left, Top = top, Right = right, Bottom = bottom };
    }

    /// <summary>Pure: the pointer left the click point by more than a couple of pixels.</summary>
    internal static bool MovedFrom(POINT now, POINT home, int tolerancePx) =>
        Math.Abs(now.X - home.X) > tolerancePx || Math.Abs(now.Y - home.Y) > tolerancePx;

    /// <summary>
    /// Pure: a position this close to the click is the owner's warp clamped by the clip box, not
    /// the user moving the pointer; only that one is safe to put back.
    /// </summary>
    internal static bool IsClampedWarp(POINT now, POINT home) =>
        Math.Abs(now.X - home.X) <= ClampedWarpTolerancePx
        && Math.Abs(now.Y - home.Y) <= ClampedWarpTolerancePx;

    private void Watch()
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < WatchMs)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }
            }

            if (NativeMethods.GetCursorPos(out POINT now) && MovedFrom(now, _home, RestoreDeadZonePx))
            {
                if (TrayCursorWarp.ShouldRestore(now, _trayButton, _monitor))
                {
                    Restore(now);
                }
                else if (IsClampedWarp(now, _home))
                {
                    // The warp was clamped inside the clip box: it never reached the tray edge,
                    // but the pointer may sit a few pixels off the click point. Put it back while
                    // the foreground still allows it (harmless if it refuses).
                    RestoreClamped(now);
                }

                return;
            }

            Thread.Sleep(PollMs);
        }
    }

    private void Restore(POINT now)
    {
        bool restored = false;
        for (int attempt = 0; attempt < RestoreAttempts && !restored; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(RestoreRetryMs);
            }

            restored = NativeMethods.SetCursorPos(_home.X, _home.Y);
        }

        TrayPeekDiagnostics.LogNote(restored
            ? $"cursor warp undone {now.X},{now.Y} -> {_home.X},{_home.Y}"
            : $"cursor warp NOT undone at {now.X},{now.Y}: foreground blocks SetCursorPos");
    }

    private void RestoreClamped(POINT now)
    {
        if (NativeMethods.SetCursorPos(_home.X, _home.Y))
        {
            TrayPeekDiagnostics.LogNote($"cursor clamped warp undone {now.X},{now.Y} -> {_home.X},{_home.Y}");
        }
        else
        {
            TrayPeekDiagnostics.LogNote($"cursor clamped warp left at {now.X},{now.Y} (within {ClampedWarpTolerancePx}px of the click)");
        }
    }
}
