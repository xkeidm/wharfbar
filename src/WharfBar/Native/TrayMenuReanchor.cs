using System.Text;

namespace WharfBar.Native;

/// <summary>
/// Re-anchors the popup window (Win32 context menu or app-owned flyout) that a tray app opens in
/// response to a mirrored synthetic click. The physical click lands on the stealth real taskbar
/// at the screen bottom edge, so each app anchors its menu by its own strategy — icon rect at the
/// bottom, restored cursor, screen corner — anywhere but next to the icon the user actually
/// clicked in the top bar. After the click, this watcher watches for a newly created popup near
/// the clicked button and moves it below the mirror icon. Documented APIs only.
/// <para>
/// Matching is style-based, never pid-based: the 25H2 XAML tray buttons all report explorer's pid,
/// so process identity cannot distinguish the target popup from unrelated shell windows. A pure
/// geometric test is not enough either — the 2026-11-09 diag log had app flyouts and ordinary
/// framed app windows in the same bottom-edge rect band (a flyout was moved to y=33 and a normal
/// app frame could follow it). The Win32 menu class <c>#32768</c> is always the clicked app's own
/// menu; any other class must be a real popup (WS_POPUP set, no WS_CAPTION/WS_THICKFRAME frame)
/// that overlaps the click zone, so a normal framed window is never moved.
/// </para>
/// <para>
/// One watch at a time: a process-wide gate serializes the discovery watches, and every moved
/// window is claimed by the watch that moved it, so a newer watch supersedes an older reassert
/// loop instead of both moving the same popup. The diag log showed two watchers moving the same
/// <c>#32768</c> menu to 1375,33 and 1345,33 in turns (30–60 ms apart) until both move budgets
/// ran out.
/// </para>
/// </summary>
internal static class TrayMenuReanchor
{
    /// <summary>Win32 standard popup-menu window class (TrackPopupMenu).</summary>
    internal const string MenuClassName = "#32768";

    /// <summary>GWL_STYLE index for <see cref="NativeMethods.GetWindowLong"/>.</summary>
    internal const int WindowStyleIndex = -16;

    /// <summary>WS_CAPTION = WS_BORDER | WS_DLGFRAME: a titled window is never a bare popup.</summary>
    internal const int WindowCaptionStyle = 0x00C00000;

    /// <summary>WS_THICKFRAME: a resizable frame is the signature of a normal app window.</summary>
    internal const int WindowThickFrameStyle = 0x00040000;

    private const int GapPx = 4;
    private const int MinCandidateWidth = 100;
    private const int MinCandidateHeight = 60;

    /// <summary>
    /// App flyouts anchor near the clicked tray button's real slot on the parked taskbar; the
    /// zone is that slot expanded sideways/upward. A window elsewhere belongs to the app's
    /// normal UI or an unrelated shell popup, not to this click.
    /// </summary>
    private const int ZoneSideMarginPx = 320;
    private const int ZoneBottomBandPx = 460;

    /// <summary>Full-screen overlays (IME hosts, vendor overlays) are never popups.</summary>
    private const double FullScreenAreaFraction = 0.9;

    /// <summary>
    /// How long a click watches for its popup. Cold owners (Phone Link, first click after
    /// start) open theirs well past 1 s; the old caller passed a 1 s budget and timed out.
    /// </summary>
    internal const int WatchTimeoutMs = 2500;
    private const int WatchPollMs = 40;

    /// <summary>
    /// After the move, some owners (EarTrumpet, XAML flyouts) re-position their own window once
    /// their show animation or layout pass finishes. Watch that long and put it back.
    /// </summary>
    internal const int ReassertWindowMs = 1500;
    private const int ReassertPollMs = 50;
    internal const int ReassertMaxMoves = 4;
    internal const int ReassertTolerancePx = 8;

    /// <summary>
    /// The process-wide single-watcher gate: at most one discovery watch runs at a time and a moved
    /// window has exactly one owning watch. Static on purpose — overlapping clicks all come through
    /// the same entry points.
    /// </summary>
    private static readonly ReanchorWatchGate WatchGate = new();

    /// <summary>
    /// Pure: did the owner move the popup away from where it was re-anchored? A few pixels of
    /// drift (DPI rounding, shadow frame) is not worth a fight.
    /// </summary>
    internal static bool ShouldReassert(RECT current, POINT target, int tolerancePx) =>
        current.Width > 0
        && (Math.Abs(current.Left - target.X) > tolerancePx || Math.Abs(current.Top - target.Y) > tolerancePx);

    /// <summary>
    /// Pure: may the bounded reassert loop keep running? It stops as soon as the window closed,
    /// as soon as another (newer) watch has claimed the same hwnd — never fight another watcher
    /// for one popup — or when the move/time budget is spent.
    /// </summary>
    internal static bool ShouldContinueReassert(bool windowAlive, bool ownsWindow, int moves, long elapsedMs) =>
        windowAlive
        && ownsWindow
        && moves < ReassertMaxMoves
        && elapsedMs < ReassertWindowMs;

    /// <summary>True when the two screen rectangles intersect (edges touching do not count).</summary>
    internal static bool Overlaps(RECT rect, RECT other) =>
        rect.Left < other.Right
        && rect.Right > other.Left
        && rect.Top < other.Bottom
        && rect.Bottom > other.Top;

    /// <summary>
    /// The screen region a click-triggered popup can plausibly appear in: the clicked button's
    /// real rect expanded sideways and upward from the monitor's bottom edge.
    /// </summary>
    internal static RECT ComputeClickZone(RECT buttonRect, RECT monitorBounds)
    {
        return new RECT
        {
            Left = buttonRect.Left - ZoneSideMarginPx,
            Right = buttonRect.Right + ZoneSideMarginPx,
            Top = monitorBounds.Bottom - ZoneBottomBandPx,
            Bottom = monitorBounds.Bottom,
        };
    }

    /// <summary>
    /// Pure: is this pair of window styles a real popup rather than a normal app window? A bare
    /// popup menu/flyout has WS_POPUP and no caption or resizable frame; WS_EX_APPWINDOW marks a
    /// taskbar window, which a click-triggered popup never is.
    /// </summary>
    internal static bool IsRealPopupStyle(int style, int exStyle) =>
        (style & NativeMethods.WS_POPUP) != 0
        && (style & WindowCaptionStyle) != WindowCaptionStyle
        && (style & WindowThickFrameStyle) == 0
        && (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0;

    /// <summary>
    /// Pure: may this newly appeared window be the popup to re-anchor? Read everything from
    /// <see cref="CandidateWindow"/> so the rule is unit-testable without Win32.
    /// <list type="bullet">
    /// <item>Cloaked, hung, invisible and own-process windows are never the clicked popup.</item>
    /// <item>Full-screen overlays (IME hosts, vendor overlays, borderless games) are rejected.</item>
    /// <item>
    /// The Win32 menu class <c>#32768</c> is accepted regardless of style and position: the owner
    /// anchors its own TrackPopupMenu anywhere on the screen.
    /// </item>
    /// <item>
    /// Any other class must be a real popup (<see cref="IsRealPopupStyle"/>), big enough for a
    /// flyout, and intersect the click zone. A normal framed app window is rejected even when it
    /// happens to overlap the zone — the 2026-11-09 diag log moved such a window to y=33.
    /// </item>
    /// </list>
    /// </summary>
    internal static bool IsReanchorCandidate(
        CandidateWindow window,
        uint ownPid,
        RECT monitorBounds,
        RECT clickZone)
    {
        if (!window.Visible || window.Hung || window.Cloaked || window.ProcessId == ownPid)
        {
            return false;
        }

        long monitorArea = (long)monitorBounds.Width * monitorBounds.Height;
        if (monitorArea > 0
            && (long)window.Rect.Width * window.Rect.Height >= monitorArea * FullScreenAreaFraction)
        {
            return false;
        }

        if (string.Equals(window.ClassName, MenuClassName, StringComparison.Ordinal))
        {
            return true;
        }

        if (window.Rect.Width < MinCandidateWidth || window.Rect.Height < MinCandidateHeight)
        {
            return false;
        }

        return IsRealPopupStyle(window.Style, window.ExStyle) && Overlaps(window.Rect, clickZone);
    }

    /// <summary>
    /// Position that centers <paramref name="menuRect"/> horizontally under the anchor (mirror
    /// icon rect) and places its top just below, clamped into the work area.
    /// </summary>
    internal static POINT ComputeAnchoredPosition(RECT menuRect, RECT anchor, RECT workArea)
    {
        int menuWidth = menuRect.Width;
        int menuHeight = menuRect.Height;
        int x = ((anchor.Left + anchor.Right) / 2) - (menuWidth / 2);
        int y = anchor.Bottom + GapPx;
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - menuWidth));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - menuHeight));
        return new POINT { X = x, Y = y };
    }

    /// <summary>
    /// Hwnds visible right now. Call this before the synthetic click so a menu that the
    /// owner creates synchronously is not already in the "existing" set when the watch starts.
    /// Cloaked windows are left out: XAML/UWP flyouts are often pre-created visible-but-cloaked
    /// and only uncloak on the click, so snapshotting them hid the very popup to move.
    /// </summary>
    internal static HashSet<IntPtr> CaptureVisibleWindows()
    {
        var existing = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd) && !IsCloaked(hwnd))
            {
                existing.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);
        return existing;
    }

    /// <summary>
    /// Watches up to <see cref="WatchTimeoutMs"/> for a new popup near
    /// <paramref name="buttonRect"/> and moves it below <paramref name="anchor"/>. Runs on a
    /// background thread; bounded, so no cancellation is required. No-op when no candidate
    /// appears (menu already closed, shell-hosted flyout elsewhere, …).
    /// Pass a pre-click <see cref="CaptureVisibleWindows"/> snapshot so a menu created by the
    /// click is not treated as already open.
    /// </summary>
    internal static bool TryReanchor(RECT buttonRect, RECT anchor) =>
        TryReanchor(buttonRect, anchor, CaptureVisibleWindows(), WatchTimeoutMs);

    internal static bool TryReanchor(RECT buttonRect, RECT anchor, HashSet<IntPtr> existing) =>
        TryReanchor(buttonRect, anchor, existing, WatchTimeoutMs);

    internal static bool TryReanchor(RECT buttonRect, RECT anchor, HashSet<IntPtr> existing, int timeoutMs)
    {
        if (buttonRect.Width <= 0 || anchor.Width <= 0 || existing is null)
        {
            return false;
        }

        if (!TryGetPrimaryFrame(out RECT monitorBounds, out RECT workArea))
        {
            return false;
        }

        // Single-watcher gate: while another click's discovery watch is still looking for its
        // popup, a second watch would compete for the same new windows (and their reassert loops
        // would move one menu back and forth). The second click keeps its physical click but loses
        // the re-anchor; that is the safer trade.
        long watchId = WatchGate.TryBeginWatch();
        if (watchId == 0)
        {
            TrayPeekDiagnostics.LogNote("reanchor skipped: another watch is running");
            return false;
        }

        try
        {
            RECT clickZone = ComputeClickZone(buttonRect, monitorBounds);
            uint ownPid = (uint)Environment.ProcessId;
            int waitMs = timeoutMs > 0 ? timeoutMs : WatchTimeoutMs;
            var claimedByOthers = new HashSet<IntPtr>();
            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            while (DateTime.UtcNow < deadline)
            {
                if (TryFindAndMoveNewPopup(
                        watchId, ownPid, existing, claimedByOthers,
                        clickZone, monitorBounds, workArea, anchor))
                {
                    return true;
                }

                Thread.Sleep(WatchPollMs);
            }

            TrayPeekDiagnostics.LogNote("reanchor timeout (no candidate)");
            return false;
        }
        finally
        {
            WatchGate.EndWatch(watchId);
        }
    }

    private static bool TryFindAndMoveNewPopup(
        long watchId,
        uint ownPid,
        HashSet<IntPtr> existing,
        HashSet<IntPtr> claimedByOthers,
        RECT clickZone,
        RECT monitorBounds,
        RECT workArea,
        RECT anchor)
    {
        IntPtr found = IntPtr.Zero;
        CandidateWindow foundWindow = default;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (existing.Contains(hwnd) || claimedByOthers.Contains(hwnd))
            {
                return true;
            }

            // Cheap filters before crossing the Win32 boundary for class/style strings; the pure
            // classifier below repeats them so the rules stay unit-testable.
            bool visible = NativeMethods.IsWindowVisible(hwnd);
            if (!visible || NativeMethods.IsHungAppWindow(hwnd))
            {
                return true;
            }

            if (!TryDescribeWindow(hwnd, visible, out CandidateWindow window)
                || !IsReanchorCandidate(window, ownPid, monitorBounds, clickZone))
            {
                return true;
            }

            found = hwnd;
            foundWindow = window;
            return false;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            return false;
        }

        // Claim the window before moving it. A claim released by an older watch's finished
        // reassert loop means the window is free again; a claim held by a newer watch means that
        // click's anchor wins — never fight over one popup.
        if (!WatchGate.TryClaimWindow(found, watchId))
        {
            claimedByOthers.Add(found);
            TrayPeekDiagnostics.LogNote(
                $"reanchor skip hwnd={found} class={foundWindow.ClassName}: owned by another watch");
            return false;
        }

        POINT pos = ComputeAnchoredPosition(foundWindow.Rect, anchor, workArea);
        // The click path no longer shows and hides the tray (clicks are UIA-driven and cursor-free),
        // so a popup owned by Shell_TrayWnd is not torn down at the end of the click any more: the
        // owner is left alone. SWP_NOZORDER keeps the popup's own topmost z-order, which is above
        // the top bar.
        uint flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER;

        // A newer watch may have stolen the claim between the two checks; moving now would undo
        // its move, so drop this one instead.
        if (!WatchGate.OwnsWindow(found, watchId))
        {
            WatchGate.ReleaseWindow(found, watchId);
            claimedByOthers.Add(found);
            TrayPeekDiagnostics.LogNote(
                $"reanchor skip hwnd={found} class={foundWindow.ClassName}: another watch took it");
            return false;
        }

        if (!NativeMethods.SetWindowPos(found, IntPtr.Zero, pos.X, pos.Y, 0, 0, flags))
        {
            WatchGate.ReleaseWindow(found, watchId);
            TrayPeekDiagnostics.LogNote($"reanchor move failed hwnd={found} class={foundWindow.ClassName}");
            return false;
        }

        TrayPeekDiagnostics.LogNote(
            $"reanchor moved hwnd={found} class={foundWindow.ClassName} rect={foundWindow.Rect.Left},{foundWindow.Rect.Top},{foundWindow.Rect.Right},{foundWindow.Rect.Bottom} to {pos.X},{pos.Y}");
        _ = Task.Run(() => ReassertPosition(
            found, watchId, pos, anchor, workArea, flags | NativeMethods.SWP_NOZORDER));
        return true;
    }

    /// <summary>
    /// Bounded follow-up: puts the popup back when its owner re-positions it after our move.
    /// Stops when the window closes, when another (newer) watch claims the same hwnd, or after
    /// <see cref="ReassertMaxMoves"/> / <see cref="ReassertWindowMs"/>. A size change (layout
    /// pass) re-centres against the new size. The hwnd claim is held for the whole loop and
    /// released on every exit path.
    /// </summary>
    private static void ReassertPosition(
        IntPtr hwnd,
        long watchId,
        POINT target,
        RECT anchor,
        RECT workArea,
        uint flags)
    {
        int moves = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            while (ShouldContinueReassert(
                       windowAlive: NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd),
                       ownsWindow: WatchGate.OwnsWindow(hwnd, watchId),
                       moves: moves,
                       elapsedMs: sw.ElapsedMilliseconds))
            {
                Thread.Sleep(ReassertPollMs);
                if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.GetWindowRect(hwnd, out RECT rect))
                {
                    return;
                }

                POINT wanted = ComputeAnchoredPosition(rect, anchor, workArea);
                if (!ShouldReassert(rect, wanted, ReassertTolerancePx))
                {
                    target = wanted;
                    continue;
                }

                moves++;
                _ = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, wanted.X, wanted.Y, 0, 0, flags);
                TrayPeekDiagnostics.LogNote(
                    $"reanchor reassert #{moves} hwnd={hwnd} from {rect.Left},{rect.Top} to {wanted.X},{wanted.Y} (was {target.X},{target.Y})");
                target = wanted;
            }

            if (!WatchGate.OwnsWindow(hwnd, watchId))
            {
                TrayPeekDiagnostics.LogNote(
                    $"reanchor reassert stop hwnd={hwnd}: another watch owns it");
            }
        }
        finally
        {
            WatchGate.ReleaseWindow(hwnd, watchId);
        }
    }

    private static bool TryDescribeWindow(IntPtr hwnd, bool visible, out CandidateWindow window)
    {
        window = default;
        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect))
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        var className = new StringBuilder(256);
        _ = NativeMethods.GetClassName(hwnd, className, className.Capacity);
        window = new CandidateWindow(
            ClassName: className.ToString(),
            Style: NativeMethods.GetWindowLong(hwnd, WindowStyleIndex),
            ExStyle: NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE),
            ProcessId: pid,
            Visible: visible,
            Cloaked: IsCloaked(hwnd),
            Hung: false,
            Rect: rect);
        return true;
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int value, sizeof(int)) == 0
        && value != 0;

    private static bool TryGetPrimaryFrame(out RECT monitorBounds, out RECT workArea)
    {
        monitorBounds = default;
        workArea = default;
        IntPtr tray = TaskbarNative.FindTrayHwnd();
        if (tray == IntPtr.Zero)
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(tray, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        monitorBounds = info.rcMonitor;
        workArea = TaskbarNative.TryGetPrimaryWorkArea(out RECT work) ? work : info.rcWork;
        return true;
    }
}

/// <summary>
/// Pure snapshot of one top-level window: everything <see cref="TrayMenuReanchor.IsReanchorCandidate"/>
/// needs, so the rule can be exercised without Win32. <see cref="Rect"/> is in physical screen pixels.
/// </summary>
internal readonly record struct CandidateWindow(
    string ClassName,
    int Style,
    int ExStyle,
    uint ProcessId,
    bool Visible,
    bool Cloaked,
    bool Hung,
    RECT Rect);

/// <summary>
/// Process-wide single-watcher gate for the tray popup re-anchor.
/// <list type="bullet">
/// <item>At most one discovery watch runs at a time: a concurrent <c>TryReanchor</c> gets no
/// watch id and must not search for a popup.</item>
/// <item>A moved hwnd belongs to exactly one watch. A newer watch supersedes the claim (the
/// newest click's anchor wins), and the older watch's reassert loop stops on its next check.
/// </item>
/// </list>
/// The class is pure in-memory state (no Win32), so the concurrency rules are directly testable;
/// the app keeps one static instance for the process lifetime.
/// </summary>
internal sealed class ReanchorWatchGate
{
    private readonly object _sync = new();
    private readonly Dictionary<IntPtr, long> _windowOwners = new();
    private long _activeWatchId;
    private long _lastWatchId;

    /// <summary>
    /// Claims the single discovery slot. Returns a non-zero watch id, or <c>0</c> when another
    /// watch is already looking for its popup.
    /// </summary>
    internal long TryBeginWatch()
    {
        lock (_sync)
        {
            if (_activeWatchId != 0)
            {
                return 0;
            }

            _activeWatchId = ++_lastWatchId;
            return _activeWatchId;
        }
    }

    /// <summary>True while <paramref name="watchId"/> owns the discovery slot.</summary>
    internal bool IsWatching(long watchId)
    {
        lock (_sync)
        {
            return watchId != 0 && _activeWatchId == watchId;
        }
    }

    /// <summary>
    /// Frees the discovery slot. A stale (superseded) watch id never frees the slot of the watch
    /// that replaced it — the click session and its watch are not strictly nested.
    /// </summary>
    internal void EndWatch(long watchId)
    {
        lock (_sync)
        {
            if (watchId != 0 && _activeWatchId == watchId)
            {
                _activeWatchId = 0;
            }
        }
    }

    /// <summary>
    /// Claims a window for <paramref name="watchId"/> before moving it. A newer watch supersedes
    /// an older claim; an older watch never takes a window from a newer one. Re-claiming your own
    /// window is a no-op success.
    /// </summary>
    internal bool TryClaimWindow(IntPtr hwnd, long watchId)
    {
        lock (_sync)
        {
            if (watchId == 0)
            {
                return false;
            }

            if (_windowOwners.TryGetValue(hwnd, out long owner))
            {
                if (owner == watchId)
                {
                    return true;
                }

                if (owner > watchId)
                {
                    return false;
                }
            }

            _windowOwners[hwnd] = watchId;
            return true;
        }
    }

    /// <summary>True when this watch still owns the window and may move/reassert it.</summary>
    internal bool OwnsWindow(IntPtr hwnd, long watchId)
    {
        lock (_sync)
        {
            return watchId != 0
                && _windowOwners.TryGetValue(hwnd, out long owner)
                && owner == watchId;
        }
    }

    /// <summary>
    /// Releases a window claim. Only the owning watch can release it: an older reassert loop that
    /// finishes late must not hand the window to another watch's logic.
    /// </summary>
    internal void ReleaseWindow(IntPtr hwnd, long watchId)
    {
        lock (_sync)
        {
            if (_windowOwners.TryGetValue(hwnd, out long owner) && owner == watchId)
            {
                _windowOwners.Remove(hwnd);
            }
        }
    }

    /// <summary>Number of window claims still held (0 once every watch and reassert loop ended).</summary>
    internal int HeldWindowCount
    {
        get
        {
            lock (_sync)
            {
                return _windowOwners.Count;
            }
        }
    }
}
