using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WharfBar.Native;

/// <summary>
/// Captures real tray-icon pixels on Windows 11 25H2+ where the classic toolbar is gone.
/// <c>PrintWindow(PW_RENDERFULLCONTENT)</c> renders a DWM-cloaked taskbar's own content (it is
/// parked off-screen for UIA anyway), so the mirror gets real icons with no visible flash.
/// Pure crop math (<see cref="TryComputeCropRect"/>) is unit-tested.
/// </summary>
internal static class TrayCapture
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    /// <summary>
    /// Captures the real icon for a tray button: PrintWindow the (parked, cloaked) taskbar and
    /// crop the button's screen rect (converted to window coords). Returns false on any failure —
    /// the caller falls back to a letter glyph.
    /// </summary>
    internal static bool TryGetTrayIcon(IntPtr tray, RECT buttonRect, out ImageSource? icon)
    {
        icon = null;
        if (tray == IntPtr.Zero || !NativeMethods.GetWindowRect(tray, out RECT windowRect))
        {
            return false;
        }

        if (!TryCaptureWindow(tray, out BitmapSource? capture))
        {
            return false;
        }

        if (!TryComputeCropRect(
                windowRect,
                buttonRect,
                capture.PixelWidth,
                capture.PixelHeight,
                out Int32Rect crop))
        {
            return false;
        }

        var cropped = new CroppedBitmap(capture, crop);
        cropped.Freeze();
        icon = cropped;
        return true;
    }

    /// <summary>
    /// Pure: converts a button's screen-space rect into capture (window-space) coords and clamps
    /// it to the capture bounds. False when the result is empty or inputs are invalid.
    /// </summary>
    internal static bool TryComputeCropRect(
        RECT windowRect,
        RECT screenRect,
        int captureWidth,
        int captureHeight,
        out Int32Rect crop)
    {
        crop = default;
        if (windowRect.Width <= 0 || windowRect.Height <= 0
            || captureWidth <= 0 || captureHeight <= 0)
        {
            return false;
        }

        int x = screenRect.Left - windowRect.Left;
        int y = screenRect.Top - windowRect.Top;
        int w = screenRect.Right - screenRect.Left;
        int h = screenRect.Bottom - screenRect.Top;
        if (w <= 0 || h <= 0)
        {
            return false;
        }

        int cx = Math.Max(0, x);
        int cy = Math.Max(0, y);
        int right = Math.Min(x + w, captureWidth);
        int bottom = Math.Min(y + h, captureHeight);
        int cw = right - cx;
        int ch = bottom - cy;
        if (cw <= 0 || ch <= 0)
        {
            return false;
        }

        crop = new Int32Rect(cx, cy, cw, ch);
        return true;
    }

    private static bool TryCaptureWindow(IntPtr hwnd, out BitmapSource? bitmap)
    {
        bitmap = null;
        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        IntPtr dc = GetWindowDC(hwnd);
        if (dc == IntPtr.Zero)
        {
            return false;
        }

        IntPtr mem = IntPtr.Zero;
        IntPtr bmp = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(dc);
            bmp = CreateCompatibleBitmap(dc, rect.Width, rect.Height);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero)
            {
                return false;
            }

            old = SelectObject(mem, bmp);
            if (!PrintWindow(hwnd, mem, PW_RENDERFULLCONTENT))
            {
                return false;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                bmp,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            bitmap = source;
            return true;
        }
        finally
        {
            if (old != IntPtr.Zero)
            {
                _ = SelectObject(mem, old);
            }

            if (bmp != IntPtr.Zero)
            {
                _ = DeleteObject(bmp);
            }

            if (mem != IntPtr.Zero)
            {
                _ = DeleteDC(mem);
            }

            _ = ReleaseDC(hwnd, dc);
        }
    }
}
