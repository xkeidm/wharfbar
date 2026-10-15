using System.Windows;

namespace WharfBar.Services;

/// <summary>
/// Whether a minimize/restore snapshot may play. Reduced motion
/// (<c>SystemParameters.ClientAreaAnimation</c> false) and a failed capture both skip it.
/// </summary>
internal static class DockMinimizeMotion
{
    internal const int DurationMs = 200;
    internal const double CueScaleMin = 0.12;

    internal static bool ShouldPlay(bool clientAreaAnimation, bool captureSucceeded) =>
        clientAreaAnimation && captureSucceeded;

    /// <summary>Popup top-left when the cue is parked on the icon (minimize end / restore start).</summary>
    internal static Point MinimizeEndOffset(Point iconDip, Rect windowDip) =>
        new(iconDip.X - (windowDip.Width * 0.06), iconDip.Y - (windowDip.Height * 0.06));

    /// <summary>Same geometry as <see cref="MinimizeEndOffset"/> — restore grows from here.</summary>
    internal static Point RestoreStartOffset(Point iconDip, Rect windowDip) =>
        MinimizeEndOffset(iconDip, windowDip);
}
