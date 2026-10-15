using System.Text;

namespace WharfBar.Native;

/// <summary>
/// Re-anchors the popup window (Win32 context menu or app-owned flyout) that a tray app opens in
/// response to a mirrored synthetic click. The physical click lands on the stealth real taskbar
/// at the screen bottom edge, so each app anchors its menu by its own strategy — icon rect at the
/// bottom, restored cursor, screen corner — anywhere but next to the icon the user actually
/// clicked in the top bar. After the click, this watcher watches for a newly created popup near
/// the clicked button and moves it below the mirror icon. Documented APIs only.
/// Matching is geometric, not pid-based: the 25H2 XAML tray buttons all report explorer's pid,
/// so process identity cannot distinguish the target popup from unrelated shell windows.
/// </summary>
internal static class TrayMenuReanchor
{
    /// <summary>Win32 standard popup-menu window class (TrackPopupMenu).</summary>
    internal const string MenuClassName = "#32768";

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
    private const int ReassertWindowMs = 1500;
    private const int ReassertPollMs = 50;
    private const int ReassertMaxMoves = 4;
    internal const int ReassertTolerancePx = 8;

    /// <summary>
    /// Pure: did the owner move the popup away from where it was re-anchored? A few pixels of
    /// drift (DPI rounding, shadow frame) is not worth a fight.
    /// </summary>
    internal static bool ShouldReassert(RECT current, POINT target, int tolerancePx) =>
        current.Width > 0
        && (Math.Abs(current.Left - target.X) > tolerancePx || Math.Abs(current.Top - target.Y) > tolerancePx);

    /// <summary>
    /// A popup owned by <c>Shell_TrayWnd</c> is destroyed when the tray is hidden at the end of
    /// a click. Detach only that owner — app-owned flyouts already survive the hide.
    /// </summary>
    internal static bool ShouldDetachPopupFromTray(IntPtr owner, IntPtr tray) =>
        tray != IntPtr.Zero && owner == tray;

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
    /// True when a newly appeared window is the popup to re-anchor: the Win32 menu class always
    /// qualifies (it can land anywhere, e.g. screen top); other window classes qualify only when
    /// big enough and inside the click zone. Full-screen overlays are rejected first.
    /// </summary>
    internal static bool IsMenuCandidate(
        string className,
        int width,
        int height,
        bool cloaked,
        RECT windowRect,
        RECT monitorBounds,
        RECT clickZone)
    {
        if (cloaked)
        {
            return false;
        }

        if (width < MinCandidateWidth || height < MinCandidateHeight)
        {
            return false;
        }

        long monitorArea = (long)monitorBounds.Width * monitorBounds.Height;
        if (monitorArea > 0 && (long)width * height >= monitorArea * FullScreenAreaFraction)
        {
            return false;
        }

        if (string.Equals(className, MenuClassName, StringComparison.Ordinal))
        {
            return true;
        }

        return windowRect.Left < clickZone.Right
            && windowRect.Right > clickZone.Left
            && windowRect.Top < clickZone.Bottom
            && windowRect.Bottom > clickZone.Top;
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

        RECT clickZone = ComputeClickZone(buttonRect, monitorBounds);
        uint ownPid = (uint)Environment.ProcessId;
        int waitMs = timeoutMs > 0 ? timeoutMs : WatchTimeoutMs;

        var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
        while (DateTime.UtcNow < deadline)
        {
            if (TryFindAndMoveNewPopup(ownPid, existing, clickZone, monitorBounds, workArea, anchor))
            {
                return true;
            }

            Thread.Sleep(WatchPollMs);
        }

        TrayPeekDiagnostics.LogNote("reanchor timeout (no candidate)");
        return false;
    }

    private static bool TryFindAndMoveNewPopup(
        uint ownPid,
        HashSet<IntPtr> existing,
        RECT clickZone,
        RECT monitorBounds,
        RECT workArea,
        RECT anchor)
    {
        IntPtr found = IntPtr.Zero;
        RECT foundRect = default;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (existing.Contains(hwnd)
                || !NativeMethods.IsWindowVisible(hwnd)
                || NativeMethods.IsHungAppWindow(hwnd))
            {
                return true;
            }

            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid)
            {
                return true;
            }

            bool cloaked = IsCloaked(hwnd);
            if (cloaked || !NativeMethods.GetWindowRect(hwnd, out RECT rect))
            {
                return true;
            }

            var className = new StringBuilder(256);
            _ = NativeMethods.GetClassName(hwnd, className, className.Capacity);
            if (!IsMenuCandidate(
                    className.ToString(), rect.Width, rect.Height, cloaked,
                    rect, monitorBounds, clickZone))
            {
                return true;
            }

            found = hwnd;
            foundRect = rect;
            return false;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero)
        {
            return false;
        }

        POINT pos = ComputeAnchoredPosition(foundRect, anchor, workArea);
        IntPtr tray = TaskbarNative.FindTrayHwnd();
        IntPtr owner = NativeMethods.GetWindow(found, NativeMethods.GW_OWNER);
        uint flags = NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE;
        if (ShouldDetachPopupFromTray(owner, tray))
        {
            // Owned popups hide with their owner. Drop the tray owner before the peek ends
            // or the menu vanishes with Shell_TrayWnd. App-owned flyouts are left alone.
            // HWND_TOP (no SWP_NOZORDER) keeps the detached menu above the top bar.
            IntPtr previous = NativeMethods.SetWindowLongPtr(found, NativeMethods.GWLP_HWNDPARENT, IntPtr.Zero);
            if (previous == IntPtr.Zero && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 0)
            {
                TrayPeekDiagnostics.LogNote(
                    $"reanchor detach failed hwnd={found} err={System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
            }
        }
        else
        {
            flags |= NativeMethods.SWP_NOZORDER;
        }

        _ = NativeMethods.SetWindowPos(
            found,
            IntPtr.Zero,
            pos.X,
            pos.Y,
            0,
            0,
            flags);
        string className = ClassNameOf(found);
        TrayPeekDiagnostics.LogNote(
            $"reanchor moved hwnd={found} class={className} rect={foundRect.Left},{foundRect.Top},{foundRect.Right},{foundRect.Bottom} to {pos.X},{pos.Y}");
        _ = Task.Run(() => ReassertPosition(found, pos, anchor, workArea, flags | NativeMethods.SWP_NOZORDER));
        return true;
    }

    /// <summary>
    /// Bounded follow-up: puts the popup back when its owner re-positions it after our move.
    /// Stops when the window closes, the window expires, or after <see cref="ReassertMaxMoves"/>.
    /// A size change (layout pass) re-centres against the new size.
    /// </summary>
    private static void ReassertPosition(IntPtr hwnd, POINT target, RECT anchor, RECT workArea, uint flags)
    {
        int moves = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ReassertWindowMs && moves < ReassertMaxMoves)
        {
            Thread.Sleep(ReassertPollMs);
            if (!NativeMethods.IsWindowVisible(hwnd) || !NativeMethods.GetWindowRect(hwnd, out RECT rect))
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
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int value, sizeof(int)) == 0
        && value != 0;

    private static string ClassNameOf(IntPtr hwnd)
    {
        var className = new StringBuilder(256);
        _ = NativeMethods.GetClassName(hwnd, className, className.Capacity);
        return className.ToString();
    }

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
