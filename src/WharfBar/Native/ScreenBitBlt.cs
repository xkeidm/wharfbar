using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// GDI screen capture at physical (device) pixel coordinates: a small BitBlt into a
/// screen-compatible bitmap, read back as a 32bpp top-down DIB via <c>GetDIBits</c>.
/// The app is Per-Monitor V2 aware, so the coordinates given here are already physical
/// pixels — nothing is rescaled. No undocumented surface; only the documented BitBlt/DIB set.
/// Every DC, bitmap, and selection is released in the caller's finally blocks.
/// </summary>
internal static class ScreenBitBlt
{
    private const uint SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

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

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest,
        int x,
        int y,
        int width,
        int height,
        IntPtr hdcSrc,
        int x1,
        int y1,
        uint rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr hBitmap,
        uint startScan,
        uint scanLines,
        byte[]? bits,
        ref BITMAPINFO bitmapInfo,
        uint usage);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    /// <summary>Virtual screen bounds in physical pixels (all monitors).</summary>
    internal static (int X, int Y, int Width, int Height) VirtualScreenBounds() => (
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>
    /// Captures a <paramref name="size"/>-by-<paramref name="size"/> box whose top-left sits at
    /// physical screen coordinates (<paramref name="x"/>, <paramref name="y"/>). On success
    /// <paramref name="bgra"/> holds <c>size*size*4</c> bytes, rows top-down, bytes B-G-R-X
    /// (<paramref name="stride"/> = 4*size). False when any GDI step failed — never throws.
    /// </summary>
    internal static bool TryCapture(int x, int y, int size, out byte[] bgra, out int stride)
    {
        bgra = null!;
        stride = 0;
        if (size <= 0)
        {
            return false;
        }

        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
        {
            return false;
        }

        IntPtr mem = IntPtr.Zero;
        IntPtr bmp = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(screen);
            bmp = CreateCompatibleBitmap(screen, size, size);
            if (mem == IntPtr.Zero || bmp == IntPtr.Zero)
            {
                return false;
            }

            old = SelectObject(mem, bmp);
            if (!BitBlt(mem, 0, 0, size, size, screen, x, y, SRCCOPY))
            {
                return false;
            }

            // GetDIBits requires the bitmap NOT to be selected into the DC it is given.
            _ = SelectObject(mem, old);
            old = IntPtr.Zero;

            stride = size * 4;
            bgra = new byte[stride * size];

            // Negative height = top-down DIB: row 0 first, no bottom-up flip afterwards.
            var info = new BITMAPINFO
            {
                BmiHeader = new BITMAPINFOHEADER
                {
                    BiSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    BiWidth = size,
                    BiHeight = -size,
                    BiPlanes = 1,
                    BiBitCount = 32,
                    BiCompression = BI_RGB,
                },
            };

            int lines = GetDIBits(mem, bmp, 0, (uint)size, bgra, ref info, DIB_RGB_COLORS);
            if (lines != size)
            {
                bgra = null!;
                stride = 0;
                return false;
            }

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

            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint BiSize;
        public int BiWidth;
        public int BiHeight;
        public ushort BiPlanes;
        public ushort BiBitCount;
        public uint BiCompression;
        public uint BiSizeImage;
        public int BiXPelsPerMeter;
        public int BiYPelsPerMeter;
        public uint BiClrUsed;
        public uint BiClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER BmiHeader;

        // 32bpp BI_RGB needs no palette; one slot keeps the struct well-formed.
        public uint BmiColors;
    }
}
