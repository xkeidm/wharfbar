using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WharfBar.Native;

/// <summary>
/// Static preview frame for the dock's hover flyout.
/// <para>
/// DWM live thumbnails cannot be used here: <c>DwmRegisterThumbnail</c> rejects both a layered
/// window and a child window as the destination (<c>E_INVALIDARG</c>), and the flyout is a
/// layered popup whose preview surface would be a child HWND. One <c>PrintWindow</c> frame
/// (<see cref="DockWindowSnapshot"/>) drawn as a plain WPF image gives the same picture with no
/// airspace, so the whole card stays clickable in WPF.
/// </para>
/// </summary>
internal static class DockWindowPreview
{
    /// <summary>
    /// One frozen frame of <paramref name="hwnd"/>, or null when the capture fails or is blank.
    /// Runs off the UI thread (the caller captures on a worker); the result is frozen and safe
    /// to hand to the UI.
    /// </summary>
    internal static BitmapSource? TryCapture(IntPtr hwnd) => DockWindowSnapshot.TryCopy(hwnd);

    /// <summary>
    /// Scale factor that fits a frame inside the preview box. Never upscales — a small window
    /// keeps its own pixels and the card's <c>Stretch</c> does the rest.
    /// </summary>
    internal static double FitScale(int widthPx, int heightPx, int maxWidthPx, int maxHeightPx)
    {
        if (widthPx <= 0 || heightPx <= 0 || maxWidthPx <= 0 || maxHeightPx <= 0)
        {
            return 0d;
        }

        double scale = Math.Min((double)maxWidthPx / widthPx, (double)maxHeightPx / heightPx);
        return scale >= 1d ? 1d : scale;
    }

    /// <summary>
    /// Downscaled copy (UI thread). The full-size frame is released here, so a flyout showing
    /// eight windows does not pin eight full-window bitmaps while it stays open.
    /// </summary>
    internal static BitmapSource ScaleToFit(BitmapSource frame, int maxWidthPx, int maxHeightPx)
    {
        ArgumentNullException.ThrowIfNull(frame);
        double scale = FitScale(frame.PixelWidth, frame.PixelHeight, maxWidthPx, maxHeightPx);
        if (scale <= 0d || scale >= 1d)
        {
            return frame;
        }

        var scaled = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
        var copy = new WriteableBitmap(scaled);
        copy.Freeze();
        return copy;
    }
}
