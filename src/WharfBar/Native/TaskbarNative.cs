namespace WharfBar.Native;

/// <summary>
/// Thin wrappers for primary-monitor taskbar hwnd + AppBar state.
/// UI must not call NativeMethods for tray hide/restore directly.
/// </summary>
internal static class TaskbarNative
{
    private const string TrayClass = "Shell_TrayWnd";

    /// <summary>DWMWA_CLOAK — hide from composition while the HWND stays "shown" for UIA.</summary>
    private const uint DwmwaCloak = 13;

    private static RECT _savedTrayRect;
    private static bool _haveSavedTrayRect;
    private static bool _trayParkedOffscreen;
    private static bool _trayCloakedForStealth;

    internal static IntPtr FindTrayHwnd() =>
        NativeMethods.FindWindow(TrayClass, null);

    internal static uint GetState()
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        };
        return (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
    }

    internal static void SetState(uint state)
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            lParam = (IntPtr)(int)state,
        };
        _ = NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
    }

    internal static bool IsTrayVisible()
    {
        IntPtr hwnd = FindTrayHwnd();
        return hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);
    }

    internal static void SetTrayVisible(bool visible)
    {
        IntPtr hwnd = FindTrayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: Shell_TrayWnd not found");
            return;
        }

        if (visible)
        {
            ClearStealthVisuals(hwnd);
            // SW_SHOW (not SW_SHOWNA): after SW_HIDE, SHOWNA often leaves Shell_TrayWnd invisible.
            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            return;
        }

        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        ClearStealthVisuals(hwnd);
    }

    /// <summary>
    /// Shows <c>Shell_TrayWnd</c> for UIA without a visible flash: DWM cloak + park below
    /// the primary monitor, then <c>SW_SHOW</c>. Pair with <see cref="SetTrayVisible"/>(false).
    /// </summary>
    internal static void SetTrayVisibleStealth()
    {
        IntPtr hwnd = FindTrayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: Shell_TrayWnd not found (stealth)");
            return;
        }

        // Cloak first so even a momentary shell snap to the bottom edge is not painted.
        if (TrySetCloaked(hwnd, cloaked: true))
        {
            _trayCloakedForStealth = true;
        }

        if (NativeMethods.GetWindowRect(hwnd, out RECT rect) && rect.Width > 0 && rect.Height > 0)
        {
            if (!_trayParkedOffscreen)
            {
                _savedTrayRect = rect;
                _haveSavedTrayRect = true;
            }

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            var info = new MONITORINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
            };
            if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                _ = NativeMethods.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    info.rcMonitor.Left,
                    info.rcMonitor.Bottom + 8,
                    rect.Width,
                    rect.Height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                _trayParkedOffscreen = true;
            }
        }

        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
    }

    /// <summary>Moves a stealth-parked tray back on-screen and uncloaks (peek upgrade).</summary>
    internal static void UnparkTrayOnScreen()
    {
        IntPtr hwnd = FindTrayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        ClearStealthVisuals(hwnd);
        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
    }

    private static void ClearStealthVisuals(IntPtr hwnd)
    {
        if (_trayParkedOffscreen && _haveSavedTrayRect)
        {
            _ = NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                _savedTrayRect.Left,
                _savedTrayRect.Top,
                _savedTrayRect.Width,
                _savedTrayRect.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            _trayParkedOffscreen = false;
        }

        if (_trayCloakedForStealth)
        {
            _ = TrySetCloaked(hwnd, cloaked: false);
            _trayCloakedForStealth = false;
        }
    }

    private static bool TrySetCloaked(IntPtr hwnd, bool cloaked)
    {
        int value = cloaked ? 1 : 0;
        return NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaCloak, ref value, sizeof(int))
            == NativeMethods.S_OK;
    }

    /// <summary>
    /// Primary monitor work area via <c>SPI_GETWORKAREA</c> (physical pixels).
    /// </summary>
    internal static bool TryGetPrimaryWorkArea(out RECT work)
    {
        work = default;
        return NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_GETWORKAREA,
            0,
            ref work,
            0);
    }

    /// <summary>
    /// Expands the primary work-area bottom to the monitor edge while keeping top/left/right
    /// (top AppBar reservation). <c>ShowWindow(SW_HIDE)</c> alone leaves a phantom taskbar strip.
    /// </summary>
    internal static bool TryExpandPrimaryWorkAreaBottom()
    {
        IntPtr tray = FindTrayHwnd();
        IntPtr anchor = tray != IntPtr.Zero ? tray : NativeMethods.GetDesktopWindow();

        IntPtr monitor = NativeMethods.MonitorFromWindow(anchor, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        if (!TryGetPrimaryWorkArea(out RECT work) || work.Width <= 0 || work.Height <= 0)
        {
            work = info.rcWork;
        }

        if (work.Bottom >= info.rcMonitor.Bottom)
        {
            return true;
        }

        var expanded = new RECT
        {
            Left = work.Left,
            Top = work.Top,
            Right = work.Right,
            Bottom = info.rcMonitor.Bottom,
        };

        return NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_SETWORKAREA,
            0,
            ref expanded,
            NativeMethods.SPIF_SENDCHANGE);
    }

    /// <summary>Restores a previously captured primary work area (exit / crash restore).</summary>
    internal static bool TrySetPrimaryWorkArea(RECT work)
    {
        if (work.Width <= 0 || work.Height <= 0)
        {
            return false;
        }

        return NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_SETWORKAREA,
            0,
            ref work,
            NativeMethods.SPIF_SENDCHANGE);
    }
}
