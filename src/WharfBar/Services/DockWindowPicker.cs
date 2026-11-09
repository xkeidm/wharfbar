using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Selects the windows one dock tile lists: every top-level window of the tile's processes
/// whose center belongs to that dock's monitor. Windows are ordered open-on-the-desktop
/// first, minimized last, and the list is capped so the hover flyout stays a readable card
/// (the right-click "Windows" submenu uses the same list).
/// <para>
/// Pure: the rectangle whose center decides the monitor is injected, so a caller can place a
/// minimized window from its restored rectangle instead of its off-screen icon point. Lives
/// off the UI thread only as far as the cached window table does; there is no Win32 call here.
/// </para>
/// </summary>
internal static class DockWindowPicker
{
    /// <summary>Most windows one flyout card shows. Two rows of four at the flyout's card width.</summary>
    internal const int MaxWindows = 8;

    internal static List<AppWindowInfo> Collect(
        IReadOnlyList<AppWindowInfo> table,
        IReadOnlyList<uint> pids,
        DockMonitorBounds monitor,
        Func<AppWindowInfo, RECT?> centerRectOf,
        IntPtr exclude = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(pids);
        ArgumentNullException.ThrowIfNull(centerRectOf);

        var seen = new HashSet<IntPtr>();
        var open = new List<AppWindowInfo>();
        var minimized = new List<AppWindowInfo>();
        foreach (AppWindowInfo window in table)
        {
            if (window.Handle == IntPtr.Zero
                || window.Handle == exclude
                || !PidMatches(pids, window.Pid)
                || !seen.Add(window.Handle))
            {
                continue;
            }

            // An unmeasurable window is kept: losing a handle the user can otherwise activate
            // is worse than a preview that may land on the wrong monitor.
            if (centerRectOf(window) is { } rect
                && !DockMonitorWindowFilter.CenterLiesOn(Placed(window, rect), monitor))
            {
                continue;
            }

            (window.IsMinimized ? minimized : open).Add(window);
        }

        int openCount = Math.Min(open.Count, MaxWindows);
        var result = new List<AppWindowInfo>(MaxWindows);
        for (int i = 0; i < openCount; i++)
        {
            result.Add(open[i]);
        }

        int remaining = MaxWindows - result.Count;
        for (int i = 0; i < minimized.Count && i < remaining; i++)
        {
            result.Add(minimized[i]);
        }

        return result;
    }

    private static DockPlacedWindow Placed(AppWindowInfo window, RECT rect) =>
        new(window.Handle, window.Pid, window.IsMinimized, rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static bool PidMatches(IReadOnlyList<uint> pids, uint pid)
    {
        if (pid == 0)
        {
            return false;
        }

        for (int i = 0; i < pids.Count; i++)
        {
            if (pids[i] == pid)
            {
                return true;
            }
        }

        return false;
    }
}
