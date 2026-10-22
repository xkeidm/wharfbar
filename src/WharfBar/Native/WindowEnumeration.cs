using System.Text;

namespace WharfBar.Native;

internal static class WindowEnumeration
{
    private static readonly HashSet<string> ExcludedClassNames = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Progman",
        "WorkerW",
        "ApplicationFrameWindow",
    };

    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    /// <summary>
    /// Top-level visible windows for the dock running-apps list.
    /// Must not run on the WPF UI thread — <c>EnumWindows</c> stalls on a hung HWND
    /// and the AppBar then shows the wait cursor.
    /// </summary>
    internal static IReadOnlyList<(IntPtr Hwnd, string Title, uint Pid)> GetTopLevelCandidates()
    {
        var results = new List<(IntPtr Hwnd, string Title, uint Pid)>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsCandidate(hwnd, out string title, out uint pid))
            {
                results.Add((hwnd, title, pid));
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }

    private static bool IsCandidate(IntPtr hwnd, out string title, out uint pid)
    {
        title = string.Empty;
        pid = 0;

        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsHungAppWindow(hwnd))
        {
            return false;
        }

        // Cloaked windows still report WS_VISIBLE. Another virtual desktop stays out.
        // A minimized window is often cloaked too; it is still a running app and must
        // stay on the dock (the monitor filter places it from its restored rect).
        bool iconic = NativeMethods.IsIconic(hwnd);
        if (!iconic
            && NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
        {
            return false;
        }

        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        var className = new StringBuilder(256);
        if (NativeMethods.GetClassName(hwnd, className, className.Capacity) > 0
            && ExcludedClassNames.Contains(className.ToString()))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out pid);
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        bool hasAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
        bool hasToolWindow = (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0;

        // Own-process chrome (top bar, dock, launchpad, veils…) stays out. The process's
        // taskbar windows still surface: SettingsWindow is the only ShowInTaskbar=true
        // window of the process, and the dock hosts it like any running app.
        if (pid == OwnProcessId && !IsTaskbarWindow(hasAppWindow, hasToolWindow))
        {
            return false;
        }

        title = GetWindowTitle(hwnd);

        if (hasToolWindow && !hasAppWindow)
        {
            return false;
        }

        if (!hasAppWindow && string.IsNullOrEmpty(title))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether an ex-style set carries a taskbar button: <c>WS_EX_APPWINDOW</c> without
    /// <c>WS_EX_TOOLWINDOW</c>. Pure core the window census uses to decide if an
    /// own-process window may surface (Settings does; the top bar never does).
    /// </summary>
    internal static bool IsTaskbarWindow(bool hasAppWindow, bool hasToolWindow) =>
        hasAppWindow && !hasToolWindow;

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return NativeMethods.GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }
}
