using System.Diagnostics;
using System.Text;

namespace WharfBar.Native;

/// <summary>
/// Opens the real Windows notification-area overflow without reading foreign tray icon data.
/// Strategies use only window show/click + synthetic focus keys (ADR-005 hybrid tray).
/// </summary>
internal static class TrayOverflowNative
{
    private static readonly string[] OverflowWindowClasses =
    [
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
    ];

    internal static bool IsOverflowVisible()
    {
        foreach (string className in OverflowWindowClasses)
        {
            IntPtr hwnd = NativeMethods.FindWindow(className, null);
            if (hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to surface the overflow flyout. Caller must ensure <c>Shell_TrayWnd</c> is
    /// visible first — Win+B / chevron clicks are no-ops while the tray is SW_HIDE.
    /// </summary>
    internal static bool TryOpen()
    {
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        IntPtr tray = TaskbarNative.FindTrayHwnd();
        if (tray != IntPtr.Zero)
        {
            _ = NativeMethods.SetForegroundWindow(tray);
        }

        if (TryShowExistingOverflowWindow())
        {
            return true;
        }

        if (TryClickTrayOverflowButton(tray))
        {
            // Win10 often creates the overflow HWND only after the chevron click.
            Thread.Sleep(120);
            if (IsOverflowVisible() || TryShowExistingOverflowWindow())
            {
                return true;
            }

            // Click itself is still a success even if the class name differs on this build.
            return true;
        }

        return TryWinBThenActivate();
    }

    private static bool TryShowExistingOverflowWindow()
    {
        foreach (string className in OverflowWindowClasses)
        {
            IntPtr hwnd = NativeMethods.FindWindow(className, null);
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            {
                continue;
            }

            _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            _ = NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            _ = NativeMethods.SetForegroundWindow(hwnd);

            if (NativeMethods.IsWindowVisible(hwnd))
            {
                Debug.WriteLine($"WharfBar: showed overflow window ({className})");
                return true;
            }
        }

        return false;
    }

    private static bool TryClickTrayOverflowButton(IntPtr tray)
    {
        if (tray == IntPtr.Zero)
        {
            return false;
        }

        IntPtr notify = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify == IntPtr.Zero)
        {
            return false;
        }

        // The overflow chevron is typically the last Button under TrayNotifyWnd (Win10).
        // Walk children; prefer a small square-ish button near the end of the z-order walk.
        IntPtr best = IntPtr.Zero;
        IntPtr child = IntPtr.Zero;
        while (true)
        {
            child = NativeMethods.FindWindowEx(notify, child, "Button", null);
            if (child == IntPtr.Zero)
            {
                break;
            }

            best = child;
        }

        if (best == IntPtr.Zero)
        {
            // Some builds nest the button one level deeper.
            IntPtr pager = NativeMethods.FindWindowEx(notify, IntPtr.Zero, "SysPager", null);
            if (pager != IntPtr.Zero)
            {
                child = IntPtr.Zero;
                while (true)
                {
                    child = NativeMethods.FindWindowEx(notify, child, null, null);
                    if (child == IntPtr.Zero)
                    {
                        break;
                    }

                    if (IsButtonClass(child))
                    {
                        best = child;
                    }
                }
            }
        }

        if (best == IntPtr.Zero)
        {
            return false;
        }

        // BM_CLICK is the least invasive; fall back to posted mouse clicks at center.
        _ = NativeMethods.SendMessage(best, NativeMethods.BM_CLICK, IntPtr.Zero, IntPtr.Zero);

        if (!NativeMethods.GetWindowRect(best, out RECT rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            Debug.WriteLine("WharfBar: BM_CLICK sent to tray overflow button");
            return true;
        }

        int x = (rect.Left + rect.Right) / 2 - rect.Left;
        int y = (rect.Top + rect.Bottom) / 2 - rect.Top;
        IntPtr lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
        _ = NativeMethods.PostMessage(best, NativeMethods.WM_LBUTTONDOWN, (IntPtr)NativeMethods.MK_LBUTTON, lParam);
        _ = NativeMethods.PostMessage(best, (uint)NativeMethods.WM_LBUTTONUP, IntPtr.Zero, lParam);
        Debug.WriteLine("WharfBar: clicked tray overflow button");
        return true;
    }

    private static bool IsButtonClass(IntPtr hwnd)
    {
        var sb = new StringBuilder(64);
        return NativeMethods.GetClassName(hwnd, sb, sb.Capacity) > 0
            && string.Equals(sb.ToString(), "Button", StringComparison.Ordinal);
    }

    private static bool TryWinBThenActivate()
    {
        if (!SendInputKeys.TrySendWinB())
        {
            Debug.WriteLine("WharfBar: Win+B SendInput failed");
            return false;
        }

        // Win+B focuses the notification area; Space/Enter activates the overflow chevron
        // when it is the focused item (common after a fresh tray show).
        Thread.Sleep(80);
        _ = SendInputKeys.TrySendSpace();
        Thread.Sleep(40);
        if (IsOverflowVisible())
        {
            return true;
        }

        _ = SendInputKeys.TrySendReturn();
        Thread.Sleep(80);
        return IsOverflowVisible() || TaskbarNative.IsTrayVisible();
    }
}
