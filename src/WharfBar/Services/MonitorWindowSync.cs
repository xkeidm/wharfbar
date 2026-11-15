using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Decides whether a window's captured monitor entry has to be re-pointed at the live one.
/// The top bar and the dock capture <see cref="MonitorInfo"/> at construction and read
/// <c>Monitor.Bounds</c> for placement, the dock's AppBar reservation and the per-monitor window
/// filter — after a resolution / arrangement / DPI change the stale copy puts them on the old
/// geometry until the window is recreated.
/// </summary>
internal static class MonitorWindowSync
{
    /// <summary>
    /// True when <paramref name="live"/> describes the same display as <paramref name="captured"/>
    /// with different geometry, DPI or primary flag. A different display returns false: that window
    /// belongs to the old monitor and is reconciled by closing it, not by re-pointing it.
    /// </summary>
    internal static bool ShouldRepoint(MonitorInfo captured, MonitorInfo live)
    {
        if (!string.Equals(captured.DeviceId, live.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !AppBarPlacement.RectsEqual(captured.Bounds, live.Bounds)
            || !AppBarPlacement.RectsEqual(captured.WorkArea, live.WorkArea)
            || captured.Dpi != live.Dpi
            || captured.IsPrimary != live.IsPrimary;
    }
}
