using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Puts the launchpad box against the dock plate and keeps it on the monitor.
/// </summary>
internal static class LaunchpadBoxPlacement
{
    internal static (int X, int Y) Anchor(
        RECT monitor,
        RECT dock,
        int boxWidth,
        int boxHeight,
        int gap,
        bool dockOnLeft)
    {
        int inset = Math.Max(0, gap);
        int width = Math.Max(1, boxWidth);
        int height = Math.Max(1, boxHeight);

        int x;
        int y;
        if (dockOnLeft)
        {
            x = dock.Right + inset;
            y = dock.Bottom - height;
        }
        else
        {
            int center = dock.Left + (dock.Width / 2);
            x = center - (width / 2);
            y = dock.Top - inset - height;
        }

        int minX = monitor.Left + inset;
        int maxX = monitor.Right - inset - width;
        int minY = monitor.Top + inset;
        int maxY = dockOnLeft
            ? monitor.Bottom - inset - height
            : dock.Top - inset - height;

        if (maxX < minX)
        {
            x = minX;
        }
        else
        {
            x = Math.Clamp(x, minX, maxX);
        }

        if (maxY < minY)
        {
            y = minY;
        }
        else
        {
            y = Math.Clamp(y, minY, maxY);
        }

        return (x, y);
    }
}
