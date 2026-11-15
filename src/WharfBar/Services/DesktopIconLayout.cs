using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>Work area of one monitor at capture time (physical pixels, virtual-screen coordinates).</summary>
internal readonly record struct WorkAreaRect(int Left, int Top, int Right, int Bottom)
{
    internal int Width => Right - Left;

    internal int Height => Bottom - Top;

    internal bool Contains(int x, int y) => x >= Left && x <= Right && y >= Top && y <= Bottom;

    /// <summary>Squared distance from the point to the nearest pixel of the rect (0 inside).</summary>
    internal long DistanceSquared(int x, int y)
    {
        int dx = x < Left ? Left - x : (x > Right ? x - Right : 0);
        int dy = y < Top ? Top - y : (y > Bottom ? y - Bottom : 0);
        return (long)dx * dx + (long)dy * dy;
    }
}

/// <summary>A monitor and its work area at capture time.</summary>
internal readonly record struct MonitorWorkArea(string DeviceName, WorkAreaRect WorkArea);

/// <summary>An icon's stable desktop-grid cell on one monitor.</summary>
internal readonly record struct DesktopIconCell(string Name, string MonitorDeviceName, int Column, int Row);

/// <summary>Grid origin reference for one monitor: the top-left icon at capture time.</summary>
internal readonly record struct DesktopIconAnchor(string MonitorDeviceName, int X, int Y);

/// <summary>
/// The desktop icon layout as the user left it, expressed as monitor + grid cell instead of raw
/// pixels. Windows 11 rescales the desktop grid when the work area changes (25H2 re-expresses the
/// whole layout: the vertical pitch and the top edge move), so raw pixels cannot survive a
/// work-area change — the cell pattern can. A snapshot is immutable and work-area independent:
/// it stores the monitor work areas and pitch it was captured with, so it can be re-projected onto
/// the live work areas later.
/// </summary>
internal sealed class DesktopIconLayoutSnapshot
{
    private const int SnapTolerancePx = 2;
    private const double MinGridConsistency = 0.8;

    private readonly Dictionary<string, DesktopIconCell> _cellByName;
    private readonly Dictionary<string, WorkAreaRect> _workAreaByName;
    private readonly Dictionary<string, DesktopIconAnchor> _anchorByName;

    private DesktopIconLayoutSnapshot(
        IReadOnlyList<MonitorWorkArea> monitors,
        IReadOnlyList<DesktopIconCell> cells,
        IReadOnlyDictionary<string, DesktopIconPoint> positions,
        IReadOnlyDictionary<string, DesktopIconAnchor> anchors,
        int pitchX,
        int pitchY)
    {
        Monitors = monitors;
        Cells = cells;
        Positions = positions;
        Anchors = anchors;
        PitchX = pitchX;
        PitchY = pitchY;
        _cellByName = new Dictionary<string, DesktopIconCell>(StringComparer.OrdinalIgnoreCase);
        foreach (DesktopIconCell cell in cells)
        {
            _cellByName[cell.Name] = cell;
        }

        _workAreaByName = new Dictionary<string, WorkAreaRect>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorWorkArea monitor in monitors)
        {
            _workAreaByName[monitor.DeviceName] = monitor.WorkArea;
        }

        _anchorByName = new Dictionary<string, DesktopIconAnchor>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, DesktopIconAnchor> pair in anchors)
        {
            _anchorByName[pair.Key] = pair.Value;
        }
    }

    internal IReadOnlyList<MonitorWorkArea> Monitors { get; }

    internal IReadOnlyList<DesktopIconCell> Cells { get; }

    /// <summary>Exact captured positions (raw view pixels), keyed by icon name.</summary>
    internal IReadOnlyDictionary<string, DesktopIconPoint> Positions { get; }

    /// <summary>Per-monitor grid origin (the top-left icon).</summary>
    internal IReadOnlyDictionary<string, DesktopIconAnchor> Anchors { get; }

    internal int PitchX { get; }

    internal int PitchY { get; }

    internal int Count => Cells.Count;

    /// <summary>
    /// Builds a snapshot, or null when the layout cannot be expressed on a grid (fewer than two
    /// icons, no monitors, auto-arranged / free-positioned icons). Refusing is deliberate: the
    /// guard must never invent a layout it cannot reproduce.
    /// </summary>
    internal static DesktopIconLayoutSnapshot? Create(
        IReadOnlyList<DesktopIconPoint> items,
        IReadOnlyList<MonitorWorkArea> monitors,
        int pitchX,
        int pitchY)
    {
        if (items.Count == 0 || monitors.Count == 0)
        {
            return null;
        }

        if (pitchX <= 0)
        {
            pitchX = DerivePitch(items.Select(item => item.X), 0);
        }

        if (pitchY <= 0)
        {
            pitchY = DerivePitch(items.Select(item => item.Y), 0);
        }

        if (pitchX <= 0 || pitchY <= 0)
        {
            return null;
        }

        var anchors = new Dictionary<string, DesktopIconAnchor>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, List<DesktopIconPoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (DesktopIconPoint item in items)
        {
            string deviceName = MonitorForPoint(monitors, item);
            if (deviceName.Length == 0)
            {
                continue;
            }

            if (!groups.TryGetValue(deviceName, out List<DesktopIconPoint>? group))
            {
                group = new List<DesktopIconPoint>();
                groups[deviceName] = group;
            }

            group.Add(item);
        }

        var cells = new List<DesktopIconCell>(items.Count);
        var positions = new Dictionary<string, DesktopIconPoint>(items.Count, StringComparer.OrdinalIgnoreCase);
        int gridAligned = 0;
        foreach (KeyValuePair<string, List<DesktopIconPoint>> pair in groups)
        {
            DesktopIconPoint anchor = pair.Value
                .OrderBy(item => item.Y)
                .ThenBy(item => item.X)
                .First();
            anchors[pair.Key] = new DesktopIconAnchor(pair.Key, anchor.X, anchor.Y);
            foreach (DesktopIconPoint item in pair.Value)
            {
                int column = (int)Math.Round((item.X - anchor.X) / (double)pitchX, MidpointRounding.AwayFromZero);
                int row = (int)Math.Round((item.Y - anchor.Y) / (double)pitchY, MidpointRounding.AwayFromZero);
                cells.Add(new DesktopIconCell(item.Name, pair.Key, column, row));
                positions[item.Name] = item;

                int expectedX = anchor.X + column * pitchX;
                int expectedY = anchor.Y + row * pitchY;
                if (Math.Abs(expectedX - item.X) <= SnapTolerancePx
                    && Math.Abs(expectedY - item.Y) <= SnapTolerancePx)
                {
                    gridAligned++;
                }
            }
        }

        if (cells.Count == 0)
        {
            return null;
        }

        // Free-positioned layouts ("Align icons to grid" off) cannot be reproduced on the shell's
        // grid — a re-projection would move icons that the user deliberately placed between cells.
        if (gridAligned < cells.Count * MinGridConsistency)
        {
            return null;
        }

        return new DesktopIconLayoutSnapshot(monitors, cells, positions, anchors, pitchX, pitchY);
    }

    internal bool TryGetCell(string name, out DesktopIconCell cell) => _cellByName.TryGetValue(name, out cell);

    internal bool TryGetWorkArea(string deviceName, out WorkAreaRect workArea) =>
        _workAreaByName.TryGetValue(deviceName, out workArea);

    internal bool TryGetAnchor(string deviceName, out DesktopIconAnchor anchor) =>
        _anchorByName.TryGetValue(deviceName, out anchor);

    private static string MonitorForPoint(IReadOnlyList<MonitorWorkArea> monitors, DesktopIconPoint point)
    {
        foreach (MonitorWorkArea monitor in monitors)
        {
            if (monitor.WorkArea.Contains(point.X, point.Y))
            {
                return monitor.DeviceName;
            }
        }

        // Icons can sit a pixel outside the work area (or on a monitor whose work area was just
        // shrunk): fall back to the physically nearest one.
        MonitorWorkArea nearest = default;
        long best = long.MaxValue;
        foreach (MonitorWorkArea monitor in monitors)
        {
            long distance = monitor.WorkArea.DistanceSquared(point.X, point.Y);
            if (distance < best)
            {
                best = distance;
                nearest = monitor;
            }
        }

        return nearest.DeviceName ?? string.Empty;
    }

    private static int DerivePitch(IEnumerable<int> coordinates, int fallback)
    {
        int[] values = coordinates.Distinct().OrderBy(value => value).ToArray();
        if (values.Length < 2)
        {
            return fallback;
        }

        var histogram = new Dictionary<int, int>();
        for (int i = 1; i < values.Length; i++)
        {
            int delta = values[i] - values[i - 1];
            if (delta <= 0)
            {
                continue;
            }

            histogram[delta] = histogram.GetValueOrDefault(delta) + 1;
        }

        return histogram.Count == 0
            ? fallback
            : histogram.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).First().Key;
    }
}

/// <summary>
/// Projects a captured cell layout onto the live work areas. Pure and shell-free so the rules are
/// unit-testable.
/// </summary>
internal static class DesktopIconLayoutMap
{
    /// <summary>
    /// Target positions for every named icon that matches the snapshot, using the live monitor work
    /// areas and icon pitch. The per-monitor top-left anchor follows its work area edge, and every
    /// icon keeps its (column, row) cell, so the user's arrangement survives a pitch/offset change.
    /// </summary>
    internal static List<DesktopIconPoint> Map(
        DesktopIconLayoutSnapshot snapshot,
        IReadOnlyList<MonitorWorkArea> currentMonitors,
        int pitchX,
        int pitchY,
        IReadOnlyCollection<string>? onlyNames = null)
    {
        var result = new List<DesktopIconPoint>(snapshot.Count);
        if (pitchX <= 0)
        {
            pitchX = snapshot.PitchX;
        }

        if (pitchY <= 0)
        {
            pitchY = snapshot.PitchY;
        }

        var currentByName = new Dictionary<string, WorkAreaRect>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorWorkArea monitor in currentMonitors)
        {
            currentByName[monitor.DeviceName] = monitor.WorkArea;
        }

        HashSet<string>? filter = onlyNames is null
            ? null
            : new HashSet<string>(onlyNames, StringComparer.OrdinalIgnoreCase);

        foreach (DesktopIconCell cell in snapshot.Cells)
        {
            if (filter is not null && !filter.Contains(cell.Name))
            {
                continue;
            }

            if (!snapshot.TryGetWorkArea(cell.MonitorDeviceName, out WorkAreaRect capturedWork)
                || !snapshot.TryGetAnchor(cell.MonitorDeviceName, out DesktopIconAnchor anchor)
                || !currentByName.TryGetValue(cell.MonitorDeviceName, out WorkAreaRect liveWork))
            {
                // The monitor is gone (unplugged) — leave its icons to the shell.
                continue;
            }

            int leftDelta = liveWork.Left - capturedWork.Left;
            int topDelta = liveWork.Top - capturedWork.Top;
            result.Add(new DesktopIconPoint(
                cell.Name,
                anchor.X + leftDelta + cell.Column * pitchX,
                anchor.Y + topDelta + cell.Row * pitchY));
        }

        return result;
    }
}
