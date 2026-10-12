using System.Runtime.InteropServices;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>One top-level window plus the rectangle used to decide which monitor owns it.</summary>
internal readonly record struct DockPlacedWindow(
    IntPtr Handle,
    uint Pid,
    bool IsMinimized,
    int Left,
    int Top,
    int Right,
    int Bottom);

/// <summary>Monitor rectangle in the same physical pixels as <see cref="DockPlacedWindow"/>.</summary>
internal readonly record struct DockMonitorBounds(int Left, int Top, int Right, int Bottom)
{
    internal static DockMonitorBounds From(RECT rect) =>
        new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}

/// <summary>
/// Keeps badge counts, the running line, and thumbnail membership to windows whose center
/// lies on one dock's monitor. The pin list is not rewritten; an unpinned app with no
/// window here is the only tile that drops off.
/// </summary>
internal static class DockMonitorWindowFilter
{
    /// <summary>
    /// A minimized window's <c>GetWindowRect</c> is often the iconic point near
    /// (-32000, -32000). That center matches no monitor, so the tile vanished.
    /// The restored <c>rcNormalPosition</c> is the monitor the window belongs to.
    /// </summary>
    internal static RECT RectForCenter(bool minimized, RECT measured, RECT restored, bool hasRestored)
    {
        if (minimized && hasRestored && restored.Width > 0 && restored.Height > 0)
        {
            return restored;
        }

        return measured;
    }

    internal static bool CenterLiesOn(DockPlacedWindow window, DockMonitorBounds monitor)
    {
        long width = (long)window.Right - window.Left;
        long height = (long)window.Bottom - window.Top;
        long cx = window.Left + (width / 2);
        long cy = window.Top + (height / 2);
        return cx >= monitor.Left && cx < monitor.Right
            && cy >= monitor.Top && cy < monitor.Bottom;
    }

    internal static IReadOnlyList<DockAppItem> Apply(
        IReadOnlyList<DockAppItem> items,
        IReadOnlyList<DockPlacedWindow> windows,
        DockMonitorBounds monitor)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(windows);

        var next = new List<DockAppItem>(items.Count);
        foreach (DockAppItem item in items)
        {
            if (item.Kind is DockTileKind.Downloads or DockTileKind.Trash or DockTileKind.Launchpad)
            {
                next.Add(item);
                continue;
            }

            var local = new List<DockPlacedWindow>();
            for (int i = 0; i < windows.Count; i++)
            {
                DockPlacedWindow window = windows[i];
                if (!BelongsTo(item, window) || !CenterLiesOn(window, monitor))
                {
                    continue;
                }

                local.Add(window);
            }

            if (local.Count == 0)
            {
                if (!item.IsPinned && item.Kind == DockTileKind.App)
                {
                    continue;
                }

                next.Add(item with
                {
                    WindowCount = item.IsRunning ? 0 : item.WindowCount,
                    IsOpenOnDesktop = false,
                    LocalPresence = item.IsRunning
                        ? DockLocalPresence.Absent
                        : DockLocalPresence.Unscoped,
                });
                continue;
            }

            bool open = false;
            DockPlacedWindow best = local[0];
            for (int i = 0; i < local.Count; i++)
            {
                if (!local[i].IsMinimized)
                {
                    open = true;
                    best = local[i];
                    break;
                }
            }

            next.Add(item with
            {
                WindowCount = local.Count,
                IsOpenOnDesktop = open,
                LocalPresence = open ? DockLocalPresence.Open : DockLocalPresence.Minimized,
                MainWindowHandle = best.Handle,
            });
        }

        return next;
    }

    /// <summary>
    /// Live measure. An empty window table, or a table whose rectangles cannot be read,
    /// leaves the strip unscoped so the first scan does not blank every capsule.
    /// </summary>
    internal static IReadOnlyList<DockAppItem> ApplyMeasured(
        IReadOnlyList<DockAppItem> items,
        IReadOnlyList<AppWindowInfo> table,
        DockMonitorBounds monitor)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (table is null || table.Count == 0)
        {
            return items;
        }

        var placed = new List<DockPlacedWindow>(table.Count);
        for (int i = 0; i < table.Count; i++)
        {
            AppWindowInfo window = table[i];
            if (window.Handle == IntPtr.Zero)
            {
                continue;
            }

            bool measured = NativeMethods.GetWindowRect(window.Handle, out RECT rect);
            RECT normal = default;
            bool restored = window.IsMinimized && TryNormalRect(window.Handle, out normal);
            if (!measured && !restored)
            {
                continue;
            }

            RECT used = RectForCenter(window.IsMinimized, rect, normal, restored);
            placed.Add(new DockPlacedWindow(
                window.Handle,
                window.Pid,
                window.IsMinimized,
                used.Left,
                used.Top,
                used.Right,
                used.Bottom));
        }

        if (placed.Count == 0)
        {
            return items;
        }

        return Apply(items, placed, monitor);
    }

    private static bool TryNormalRect(IntPtr hwnd, out RECT normal)
    {
        normal = default;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(hwnd, ref placement))
        {
            return false;
        }

        normal = placement.rcNormalPosition;
        return normal.Width > 0 && normal.Height > 0;
    }

    internal static bool BelongsTo(DockAppItem item, DockPlacedWindow window)
    {
        if (window.Handle != IntPtr.Zero && window.Handle == item.MainWindowHandle)
        {
            return true;
        }

        if (window.Pid == 0 || item.ProcessIds is null)
        {
            return false;
        }

        for (int i = 0; i < item.ProcessIds.Count; i++)
        {
            if (item.ProcessIds[i] == window.Pid)
            {
                return true;
            }
        }

        return false;
    }
}
