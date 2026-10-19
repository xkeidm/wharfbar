using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Show Desktop sliver support: one z-order pass over the windows a user would expect
/// the sliver to put away, plus non-blocking minimize/restore via <see cref="NativeMethods.ShowWindowAsync"/>.
/// <para>
/// The pass shares the census's candidate rules (visible, not hung, not cloaked, no
/// owner, tool windows out) but keeps <c>ApplicationFrameWindow</c> in: a UWP app's
/// main frame is a real window here, not a GA (guest app) shell. <see cref="WindowEnumeration"/>
/// excludes that class for the dock's app list; a drift in one filter must re-verify the other.
/// Must not run on the WPF UI thread — <c>EnumWindows</c> stalls on a hung HWND (same note).
/// </para>
/// </summary>
internal static class ShowDesktopNative
{
    private static readonly uint OwnProcessId = (uint)Environment.ProcessId;

    private static readonly HashSet<string> ExcludedClassNames = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Progman",
        "WorkerW",
    };

    /// <summary>
    /// Visible, non-minimized user windows in z-order (top first). Minimized windows
    /// stay out: the user minimized them themselves, so the toggle must not restore them.
    /// </summary>
    internal static IReadOnlyList<IntPtr> GetMinimizableWindows()
    {
        var results = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (IsMinimizable(hwnd))
            {
                results.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }

    internal static void Minimize(IntPtr hwnd) =>
        Run(hwnd, NativeMethods.SW_MINIMIZE, "minimize");

    internal static void Restore(IntPtr hwnd) =>
        Run(hwnd, NativeMethods.SW_RESTORE, "restore");

    private static void Run(IntPtr hwnd, int command, string step)
    {
        if (!NativeMethods.ShowWindowAsync(hwnd, command))
        {
            // The handle may have been closed between collection and use.
            Debug.WriteLine($"WharfBar show-desktop {step} failed: hwnd={hwnd} win32error={Marshal.GetLastWin32Error()}");
        }
    }

    private static bool IsMinimizable(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindowVisible(hwnd)
            || NativeMethods.IsIconic(hwnd)
            || NativeMethods.IsHungAppWindow(hwnd))
        {
            return false;
        }

        // Cloaked windows still report WS_VISIBLE; another virtual desktop stays out.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && cloaked != 0)
        {
            return false;
        }

        if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        var className = new System.Text.StringBuilder(256);
        if (NativeMethods.GetClassName(hwnd, className, className.Capacity) > 0
            && ExcludedClassNames.Contains(className.ToString()))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == OwnProcessId)
        {
            return false;
        }

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        bool hasAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
        if (!hasAppWindow && (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }

        if (!hasAppWindow && NativeMethods.GetWindowText(hwnd, className, className.Capacity) <= 0)
        {
            return false;
        }

        return true;
    }
}
