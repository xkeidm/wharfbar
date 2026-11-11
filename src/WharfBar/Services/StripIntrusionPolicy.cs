using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Pure decision for <see cref="TopBarStripGuard"/>: does a window's rectangle intrude on the strip
/// an AppBar reserved at the top of its monitor, and to which y must the window move?
///
/// <para>Windows has no API that forbids placing a window in a reserved strip: the AppBar contract
/// shapes the <b>work area</b>, so maximize / snap / new-window placement respect the bar, but a
/// manual drag can always put a window there. The guard therefore pushes such a window back below
/// the bar; this class only decides <i>whether</i> and <i>where</i>.</para>
/// </summary>
internal static class StripIntrusionPolicy
{
    /// <summary>
    /// New top (physical pixels) that clears the reserved strip, or null when the window is already
    /// clear or must be left alone: fullscreen windows own their monitor (the bar hides for them
    /// anyway via <c>ABN_FULLSCREENAPP</c>), and a monitor without a top AppBar has no strip.
    /// </summary>
    internal static int? ClampTop(RECT window, RECT monitor, RECT workArea)
    {
        if (workArea.Top <= monitor.Top)
        {
            return null; // No reserved top strip on this monitor.
        }

        if (window.Bottom <= monitor.Top)
        {
            return null; // Entirely above the screen: nothing is drawn in the strip.
        }

        if (IsFullscreen(window, monitor))
        {
            return null;
        }

        return window.Top >= workArea.Top ? null : workArea.Top;
    }

    /// <summary>True when the window covers its whole monitor (a fullscreen app, not a normal window).</summary>
    internal static bool IsFullscreen(RECT window, RECT monitor)
        => window.Top <= monitor.Top
           && window.Bottom >= monitor.Bottom
           && window.Left <= monitor.Left
           && window.Right >= monitor.Right;
}
