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

        // DWM-cloaked windows are invisible to the user (shell-hidden apps, other
        // virtual desktops) yet still report WS_VISIBLE — they must not count as running.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
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
        if (pid == OwnProcessId)
        {
            return false;
        }

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        bool hasAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
        bool hasToolWindow = (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0;

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

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return NativeMethods.GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }
}
