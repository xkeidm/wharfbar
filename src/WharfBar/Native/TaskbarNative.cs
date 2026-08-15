namespace WharfBar.Native;

/// <summary>
/// Thin wrappers for primary-monitor taskbar hwnd + AppBar state.
/// UI must not call NativeMethods for tray hide/restore directly.
/// </summary>
internal static class TaskbarNative
{
    private const string TrayClass = "Shell_TrayWnd";
    private const string SecondaryTrayClass = "Shell_SecondaryTrayWnd";

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

    /// <summary>
    /// True when any primary or secondary taskbar HWND is visible. Explorer can re-show
    /// <c>Shell_TrayWnd</c> after AppBar <c>SETPOS</c> or auto-hide edge reveal.
    /// </summary>
    internal static bool IsAnyTrayVisible()
    {
        bool visible = false;
        ForEachTrayHwnd(hwnd =>
        {
            if (NativeMethods.IsWindowVisible(hwnd))
            {
                visible = true;
            }
        });
        return visible;
    }

    internal static void SetTrayVisible(bool visible)
    {
        IntPtr primary = FindTrayHwnd();
        if (primary != IntPtr.Zero)
        {
            if (visible)
            {
                ClearStealthVisuals(primary);
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: Shell_TrayWnd not found");
        }

        int shown = 0;
        ForEachTrayHwnd(hwnd =>
        {
            shown++;
            ApplyTrayVisibility(hwnd, visible);
            if (!visible)
            {
                ClearStealthVisuals(hwnd);
            }
        });

        if (shown == 0)
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: no Shell_TrayWnd / Shell_SecondaryTrayWnd");
        }
    }

    private static void ApplyTrayVisibility(IntPtr hwnd, bool visible)
    {
        if (visible)
        {
            // SW_SHOW (not SW_SHOWNA): after SW_HIDE, SHOWNA often leaves Shell_TrayWnd invisible.
            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            _ = NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_SHOWWINDOW
                    | NativeMethods.SWP_NOMOVE
                    | NativeMethods.SWP_NOSIZE
                    | NativeMethods.SWP_NOZORDER
                    | NativeMethods.SWP_NOACTIVATE);
            return;
        }

        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        _ = NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_HIDEWINDOW
                | NativeMethods.SWP_NOMOVE
                | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER
                | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// Primary via <c>FindWindow</c>, secondaries via <c>FindWindowEx</c>. Avoid
    /// <c>EnumWindows</c> on the UI thread — a hung top-level window stalls the callback
    /// and the AppBar shows the wait cursor.
    /// </summary>
    private static void ForEachTrayHwnd(Action<IntPtr> action)
    {
        IntPtr primary = FindTrayHwnd();
        if (primary != IntPtr.Zero)
        {
            action(primary);
        }

        IntPtr secondary = IntPtr.Zero;
        while (true)
        {
            secondary = NativeMethods.FindWindowEx(
                IntPtr.Zero,
                secondary,
                SecondaryTrayClass,
                null);
            if (secondary == IntPtr.Zero)
            {
                break;
            }

            action(secondary);
        }
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
    /// Expands the primary work-area bottom to the monitor edge (minus the optional dock
    /// reservation strip) while keeping top/left/right (top AppBar reservation).
    /// <c>ShowWindow(SW_HIDE)</c> alone leaves a phantom taskbar strip.
    /// </summary>
    /// <param name="reservedBottomPx">Strip reserved by the dock (0 = none).</param>
    /// <param name="broadcast">
    /// When true, writes with <c>SPIF_SENDCHANGE</c>. A broadcast makes the shell recompute
    /// appbar reservations and re-asserts the hidden taskbar's phantom strip, which then
    /// needs another expansion — an endless fight. Ongoing maintenance must be silent.
    /// </param>
    internal static bool TryExpandPrimaryWorkAreaBottom(int reservedBottomPx = 0, bool broadcast = false)
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

        int targetBottom = info.rcMonitor.Bottom - Math.Max(0, reservedBottomPx);
        if (!AppBarPlacement.NeedsBottomExpand(work.Bottom, targetBottom))
        {
            return true;
        }

        var expanded = new RECT
        {
            Left = work.Left,
            Top = work.Top,
            Right = work.Right,
            Bottom = targetBottom,
        };

        return NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_SETWORKAREA,
            0,
            ref expanded,
            broadcast ? NativeMethods.SPIF_SENDCHANGE : 0);
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
