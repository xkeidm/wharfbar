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

    private const int WatchTimeoutMs = 2000;
    private const int WatchPollMs = 40;

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
    /// Watches up to <see cref="WatchTimeoutMs"/> for a new popup near
    /// <paramref name="buttonRect"/> and moves it below <paramref name="anchor"/>. Runs on a
    /// background thread; bounded, so no cancellation is required. No-op when no candidate
    /// appears (menu already closed, shell-hosted flyout elsewhere, …).
    /// </summary>
    internal static void TryReanchor(RECT buttonRect, RECT anchor)
    {
        if (buttonRect.Width <= 0 || anchor.Width <= 0)
        {
            return;
        }

        if (!TryGetPrimaryFrame(out RECT monitorBounds, out RECT workArea))
        {
            return;
        }

        RECT clickZone = ComputeClickZone(buttonRect, monitorBounds);
        uint ownPid = (uint)Environment.ProcessId;

        var existing = new HashSet<IntPtr>();
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                existing.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        var deadline = DateTime.UtcNow.AddMilliseconds(WatchTimeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (TryFindAndMoveNewPopup(ownPid, existing, clickZone, monitorBounds, workArea, anchor))
            {
                return;
            }

            Thread.Sleep(WatchPollMs);
        }

        TrayPeekDiagnostics.LogNote("reanchor timeout (no candidate)");
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

            bool cloaked = NativeMethods.DwmGetWindowAttribute(
                    hwnd, NativeMethods.DWMWA_CLOAKED, out int cloakedValue, sizeof(int)) == 0
                && cloakedValue != 0;
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
        _ = NativeMethods.SetWindowPos(
            found,
            IntPtr.Zero,
            pos.X,
            pos.Y,
            0,
            0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        TrayPeekDiagnostics.LogNote($"reanchor moved hwnd={found} rect={foundRect.Left},{foundRect.Top},{foundRect.Right},{foundRect.Bottom} to {pos.X},{pos.Y}");
        return true;
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
