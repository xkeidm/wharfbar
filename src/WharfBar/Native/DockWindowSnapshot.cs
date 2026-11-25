using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WharfBar.Native;

/// <summary>
/// One <c>PrintWindow</c> of a window at the moment it starts to minimize.
/// A failed or blank capture returns null so the dock skips the animation.
/// </summary>
internal static class DockWindowSnapshot
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>Rows inspected by <see cref="IsBlankCaptureSampled"/> (spread evenly over the frame).</summary>
    private const int SampleRows = 64;

    internal static BitmapSource? TryCopy(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.GetWindowRect(hwnd, out RECT rect))
        {
            return null;
        }

        if (rect.Width <= 0 || rect.Height <= 0 || rect.Width > 8192 || rect.Height > 8192)
        {
            return null;
        }

        IntPtr dc = GetWindowDC(hwnd);
        if (dc == IntPtr.Zero)
        {
            Debug.WriteLine($"WharfBar minimize capture: GetWindowDC failed {Marshal.GetLastWin32Error()}");
            return null;
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
                Debug.WriteLine($"WharfBar minimize capture: bitmap alloc failed {Marshal.GetLastWin32Error()}");
                return null;
            }

            old = SelectObject(mem, bmp);
            if (!PrintWindow(hwnd, mem, PW_RENDERFULLCONTENT))
            {
                Debug.WriteLine($"WharfBar PrintWindow failed: {Marshal.GetLastWin32Error()}");
                return null;
            }

            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                bmp,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            if (IsBlankCaptureSampled(source))
            {
                Debug.WriteLine("WharfBar minimize capture was blank");
                return null;
            }

            return source;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar minimize capture failed: {ex.Message}");
            return null;
        }
        finally
        {
            if (old != IntPtr.Zero && mem != IntPtr.Zero)
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

    /// <summary>
    /// Blank check for a whole window frame. <see cref="TrayCapture.IsBlankCapture"/> would copy the
    /// entire frame just to test uniformity (a 1080p BGRA copy is ~8 MB of LOH per minimize); this
    /// variant copies only <see cref="SampleRows"/> evenly spaced rows and compares their pixels, so
    /// an unpainted, fully transparent or single-color frame is still rejected while a painted window
    /// with content on any sampled row is accepted. A content band that fits entirely between two
    /// sampled rows would be misread as blank — the same trade-off as a coarse preview, but a real
    /// window paints rows across its whole height (title bar, background, controls).
    /// </summary>
    internal static bool IsBlankCaptureSampled(BitmapSource source)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        int bytesPerPixel = Math.Max(1, (source.Format.BitsPerPixel + 7) / 8);
        var row = new byte[width * bytesPerPixel];
        int rowStep = Math.Max(1, height / SampleRows);
        long reference = -1;
        for (int y = 0; y < height; y += rowStep)
        {
            if (!IsUniformRow(source, y, width, row, bytesPerPixel, ref reference))
            {
                return false;
            }
        }

        // The evenly spaced pass can stop above the bottom edge, and a failed paint often shows up
        // as a black strip there; inspect the last row too when the loop did not already cover it.
        int last = height - 1;
        if (last % rowStep != 0
            && !IsUniformRow(source, last, width, row, bytesPerPixel, ref reference))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Copies one row and compares every pixel in it (and across rows, through
    /// <paramref name="reference"/>) against the first sampled pixel.
    /// </summary>
    private static bool IsUniformRow(
        BitmapSource source,
        int y,
        int width,
        byte[] row,
        int bytesPerPixel,
        ref long reference)
    {
        source.CopyPixels(new Int32Rect(0, y, width, 1), row, width * bytesPerPixel, 0);
        for (int i = 0; i + bytesPerPixel <= row.Length; i += bytesPerPixel)
        {
            long pixel = 0;
            for (int b = 0; b < bytesPerPixel; b++)
            {
                pixel = (pixel << 8) | row[i + b];
            }

            if (reference < 0)
            {
                reference = pixel;
            }
            else if (pixel != reference)
            {
                return false;
            }
        }

        return true;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
}
