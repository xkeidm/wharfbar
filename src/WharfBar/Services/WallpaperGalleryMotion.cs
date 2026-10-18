using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace WharfBar.Services;

/// <summary>
/// Motion + layout constants for the island hub wallpaper gallery (preview + strip design):
/// preview crossfade, eased strip scrolling, thumb metrics, and keyboard neighbor indexing.
/// </summary>
internal static class WallpaperGalleryMotion
{
    /// <summary>Preview crossfade duration when a strip thumb is picked.</summary>
    internal const int PreviewCrossfadeMs = 160;

    /// <summary>Eased scroll duration for one arrow / wheel page.</summary>
    internal const int StripScrollMs = 260;

    internal const double ThumbWidthDip = 70;
    internal const double ThumbHeightDip = 52;
    internal const double ThumbGapDip = 8;

    /// <summary>How far one arrow click moves the strip: about four thumbs.</summary>
    internal const double ScrollStepDip = 4d * (ThumbWidthDip + ThumbGapDip);

    /// <summary>How far one mouse wheel notch (over a strip) moves: half a page.</summary>
    internal const double WheelStepDip = ScrollStepDip * 0.5;

    internal static TimeSpan PreviewCrossfade => TimeSpan.FromMilliseconds(PreviewCrossfadeMs);

    internal static TimeSpan StripScroll => TimeSpan.FromMilliseconds(StripScrollMs);

    internal static IEasingFunction CreateEase() => new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>
    /// Wheel policy for one horizontal strip: capture the wheel only when the strip can
    /// actually move in that direction (wheel up = scroll left); otherwise fall through so
    /// the outer vertical list still scrolls. Target is clamped to the strip bounds.
    /// </summary>
    internal static (bool Handles, double Target) WheelTarget(double scrollableWidth, double offset, double delta)
    {
        if (scrollableWidth <= 0)
        {
            return (false, offset);
        }

        double direction = delta > 0 ? -1 : 1;
        bool canMove = direction < 0 ? offset > 0 : offset < scrollableWidth - 0.001;
        if (!canMove)
        {
            return (false, offset);
        }

        return (true, Math.Clamp(offset + direction * WheelStepDip, 0, scrollableWidth));
    }

    /// <summary>Bitmap decode width for a given display width: 2× for crispness, capped.</summary>
    internal static int DecodeWidth(double displayWidth) =>
        Math.Clamp((int)Math.Ceiling(displayWidth * 2d), 64, 1024);

    /// <summary>
    /// Index of the neighbor in <paramref name="direction"/> (-1 / +1), clamped to the
    /// section bounds without wrapping. An unselected index (-1) starts from the edge
    /// nearest the press. Returns -1 when there is nothing to select.
    /// </summary>
    internal static int NeighborIndex(int count, int index, int direction)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (index < 0)
        {
            return direction < 0 ? count - 1 : 0;
        }

        int next = index + direction;
        return Math.Clamp(next, 0, count - 1);
    }
}
