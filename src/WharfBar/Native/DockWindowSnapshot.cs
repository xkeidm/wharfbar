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
            if (TrayCapture.IsBlankCapture(source, new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight)))
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
