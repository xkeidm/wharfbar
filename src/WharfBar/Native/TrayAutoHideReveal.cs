using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Brings the parked auto-hide taskbar on screen the way a user does — by hovering the bottom
/// edge — instead of <c>ABM_SETSTATE(0)</c>. An auto-hide reveal never reserves a work area, so
/// no window moves or resizes; the state flip did both on every mirrored click (diag log:
/// tray at <c>y=1032</c> during each click = strip reserved). Also hosts the window checks that
/// gate the rare remaining state blip. Documented user32/dwmapi only.
/// </summary>
internal static class TrayAutoHideReveal
{
    /// <summary>Explorer parks an auto-hide taskbar with this many pixels left on screen.</summary>
    internal const int ParkedSliverPx = 2;

    private const int PollMs = 30;
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const uint DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>Pure: the tray sits in the parked sliver (auto-hide, hidden).</summary>
    internal static bool IsParked(RECT tray, RECT monitor) =>
        tray.Height > 0 && tray.Top >= monitor.Bottom - ParkedSliverPx - 1;

    /// <summary>Pure: the tray is fully on screen at the bottom edge (revealed or not auto-hide).</summary>
    internal static bool IsFullyOnScreen(RECT tray, RECT monitor) =>
        tray.Height > 0 && tray.Top <= monitor.Bottom - tray.Height && tray.Bottom <= monitor.Bottom;

    /// <summary>
    /// Current fully-on-screen state of <c>Shell_TrayWnd</c>, for the empty-read evidence rule:
    /// <see cref="IsParkedNow"/> false is not proof that the tray was readable — the rect read can
    /// fail and explorer also passes through a mid slide-out state, where the peek is open but no
    /// button was ever looked at. False whenever the rect or monitor cannot be read.
    /// </summary>
    internal static bool IsFullyOnScreenNow(IntPtr tray)
    {
        if (tray == IntPtr.Zero
            || !NativeMethods.GetWindowRect(tray, out RECT rect)
            || !TryGetMonitor(tray, out RECT monitor))
        {
            return false;
        }

        return IsFullyOnScreen(rect, monitor);
    }

    /// <summary>
    /// Current parked state of <c>Shell_TrayWnd</c>, for the quiet mirror read: a parked tray
    /// shows only its 2 px sliver and explorer virtualizes the XAML buttons, so the read has to
    /// hover-reveal it first (see <see cref="TrayRevealPolicy"/>). False when the window rect or
    /// the monitor cannot be read.
    /// </summary>
    internal static bool IsParkedNow(IntPtr tray)
    {
        if (tray == IntPtr.Zero
            || !NativeMethods.GetWindowRect(tray, out RECT rect)
            || !TryGetMonitor(tray, out RECT monitor))
        {
            return false;
        }

        return IsParked(rect, monitor);
    }

    /// <summary>Pure: a window a work-area change would move/resize (maximized, snapped, or
    /// any normal frame reaching into the bottom strip the taskbar would reserve).</summary>
    internal static bool IsAffectedByBottomReservation(RECT window, RECT monitor, int stripPx, bool maximized)
    {
        if (maximized)
        {
            return true;
        }

        return window.Bottom > monitor.Bottom - stripPx
            && window.Top < monitor.Bottom
            && window.Right > monitor.Left
            && window.Left < monitor.Right;
    }

    /// <summary>
    /// Hovers the bottom edge at <paramref name="x"/> and waits until explorer has slid the
    /// tray fully on screen. True immediately when the tray is not parked. The caller restores
    /// the cursor. <paramref name="onCursorParked"/> is invoked with the physical point the
    /// pointer was parked on, so the caller's cursor lease can restore it only while it is still
    /// there. False on timeout — the caller decides whether a state flip is worth it.
    /// </summary>
    internal static bool TryReveal(
        IntPtr tray,
        int x,
        int timeoutMs,
        Action<int, int>? onCursorParked,
        out int elapsedMs)
    {
        elapsedMs = 0;
        if (tray == IntPtr.Zero
            || !NativeMethods.GetWindowRect(tray, out RECT rect)
            || !TryGetMonitor(tray, out RECT monitor))
        {
            return false;
        }

        if (!IsParked(rect, monitor))
        {
            return IsFullyOnScreen(rect, monitor);
        }

        int edgeX = Math.Clamp(x, monitor.Left + 1, monitor.Right - 2);
        int edgeY = monitor.Bottom - 1;
        onCursorParked?.Invoke(edgeX, edgeY);
        SendInputMouse.MoveTo(edgeX, edgeY);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Thread.Sleep(PollMs);
            if (NativeMethods.GetWindowRect(tray, out rect) && IsFullyOnScreen(rect, monitor))
            {
                elapsedMs = (int)sw.ElapsedMilliseconds;
                return true;
            }
        }

        elapsedMs = (int)sw.ElapsedMilliseconds;
        return false;
    }

    /// <summary>Time since the last user keyboard/mouse input (synthetic input counts too).</summary>
    internal static TimeSpan UserIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        uint now = (uint)Environment.TickCount;
        return TimeSpan.FromMilliseconds(unchecked(now - info.dwTime));
    }

    /// <summary>
    /// True when any normal app window on the primary monitor would visibly move or resize if the
    /// taskbar reserved its strip (a maximized window, or a framed window reaching into it).
    /// Overlays, tool windows, cloaked windows and our own windows do not count.
    /// </summary>
    internal static bool HasWindowAffectedByWorkAreaChange(int stripPx)
    {
        IntPtr tray = TaskbarNative.FindTrayHwnd();
        if (!TryGetMonitor(tray != IntPtr.Zero ? tray : NativeMethods.GetDesktopWindow(), out RECT monitor))
        {
            return true;
        }

        uint ownPid = (uint)Environment.ProcessId;
        bool affected = false;
        NativeMethods.EnumWindows((hwnd, lParam) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            {
                return true;
            }

            int style = NativeMethods.GetWindowLong(hwnd, GWL_STYLE);
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            bool framed = (style & WS_THICKFRAME) != 0 || (style & WS_CAPTION) == WS_CAPTION;
            if (!framed
                || (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0
                || (exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
            {
                return true;
            }

            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid)
            {
                return true;
            }

            if (NativeMethods.DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0)
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(hwnd, out RECT rect)
                || NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY)
                    != NativeMethods.MonitorFromWindow(tray, NativeMethods.MONITOR_DEFAULTTOPRIMARY))
            {
                return true;
            }

            if (IsAffectedByBottomReservation(rect, monitor, stripPx, NativeMethods.IsZoomed(hwnd)))
            {
                affected = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return affected;
    }

    private static bool TryGetMonitor(IntPtr anchor, out RECT monitor)
    {
        monitor = default;
        IntPtr handle = NativeMethods.MonitorFromWindow(anchor, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (handle == IntPtr.Zero || !NativeMethods.GetMonitorInfo(handle, ref info))
        {
            return false;
        }

        monitor = info.rcMonitor;
        return true;
    }
}
