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
    /// One <c>PrintWindow</c> of the tray, shared by every button crop of a refresh (it used to
    /// be one full-taskbar capture per icon). False when the tray is not shown: PrintWindow then
    /// returns TRUE with an unpainted DIB, so trying is pure cost.
    /// </summary>
    internal static bool TryCaptureFrame(IntPtr tray, out TrayCaptureFrame? frame)
    {
        frame = null;
        if (tray == IntPtr.Zero
            || !NativeMethods.IsWindowVisible(tray)
            || !NativeMethods.GetWindowRect(tray, out RECT windowRect))
        {
            return false;
        }

        if (!TryCaptureWindow(tray, out BitmapSource? capture) || capture is null)
        {
            return false;
        }

        frame = new TrayCaptureFrame(capture, windowRect);
        return true;
    }

    /// <summary>
    /// Crops a button's screen rect out of <paramref name="frame"/> into a standalone bitmap (the
    /// full-taskbar frame is not kept alive by the icon). Returns the raw pixels too so the
    /// caller can fingerprint them. False for an invalid rect or a glyph-less crop — the caller
    /// keeps its cached icon or falls back to a letter glyph.
    /// </summary>
    internal static bool TryCropIcon(TrayCaptureFrame frame, RECT buttonRect, out TrayIconCrop? crop)
    {
        crop = null;
        BitmapSource capture = frame.Bitmap;
        if (!TryComputeCropRect(
                frame.WindowRect,
                buttonRect,
                capture.PixelWidth,
                capture.PixelHeight,
                out Int32Rect region))
        {
            return false;
        }

        int bytesPerPixel = Math.Max(1, capture.Format.BitsPerPixel / 8);
        if (bytesPerPixel != 4)
        {
            int rawStride = region.Width * bytesPerPixel;
            var raw = new byte[rawStride * region.Height];
            capture.CopyPixels(region, raw, rawStride, 0);
            if (IsBlankPixels(raw, bytesPerPixel))
            {
                return false;
            }

            BitmapSource plain = BitmapSource.Create(
                region.Width, region.Height, capture.DpiX, capture.DpiY, capture.Format, capture.Palette, raw, rawStride);
            plain.Freeze();
            crop = new TrayIconCrop(plain, raw);
            return true;
        }

        // Copy the icon rect plus a ring around it: the ring is plain taskbar paint (button
        // padding) and gives the background colour to key out.
        Int32Rect outer = InflateClamped(region, TrayIconMatte.RingPx, capture.PixelWidth, capture.PixelHeight);
        int outerStride = outer.Width * 4;
        var outerPixels = new byte[outerStride * outer.Height];
        capture.CopyPixels(outer, outerPixels, outerStride, 0);
        SplitRing(outerPixels, outer, region, out byte[] pixels, out List<BgrColor> ring);
        if (IsBlankPixels(pixels, 4))
        {
            return false;
        }

        if (ring.Count < 4)
        {
            ring = BorderSamples(pixels, region.Width, region.Height);
        }

        byte[] keyed = TrayIconMatte.RemoveBackground(pixels, TrayIconMatte.MedianColor(ring));
        if (!TrayIconMatte.HasVisiblePixels(keyed))
        {
            return false;
        }

        BitmapSource icon = BitmapSource.Create(
            region.Width,
            region.Height,
            capture.DpiX,
            capture.DpiY,
            PixelFormats.Bgra32,
            null,
            keyed,
            region.Width * 4);
        icon.Freeze();
        crop = new TrayIconCrop(icon, keyed);
        return true;
    }

    /// <summary>Pure: <paramref name="rect"/> grown by <paramref name="by"/> on every side, clamped to the capture.</summary>
    internal static Int32Rect InflateClamped(Int32Rect rect, int by, int width, int height)
    {
        int left = Math.Max(0, rect.X - by);
        int top = Math.Max(0, rect.Y - by);
        int right = Math.Min(width, rect.X + rect.Width + by);
        int bottom = Math.Min(height, rect.Y + rect.Height + by);
        return new Int32Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>Pure: splits an outer BGRA buffer into the inner rect's pixels and the ring colours.</summary>
    internal static void SplitRing(byte[] outerPixels, Int32Rect outer, Int32Rect inner, out byte[] innerPixels, out List<BgrColor> ring)
    {
        innerPixels = new byte[inner.Width * inner.Height * 4];
        ring = new List<BgrColor>();
        for (int y = 0; y < outer.Height; y++)
        {
            int absY = outer.Y + y;
            for (int x = 0; x < outer.Width; x++)
            {
                int absX = outer.X + x;
                int src = ((y * outer.Width) + x) * 4;
                bool inside = absX >= inner.X && absX < inner.X + inner.Width
                    && absY >= inner.Y && absY < inner.Y + inner.Height;
                if (inside)
                {
                    int dst = (((absY - inner.Y) * inner.Width) + (absX - inner.X)) * 4;
                    Buffer.BlockCopy(outerPixels, src, innerPixels, dst, 4);
                }
                else
                {
                    ring.Add(new BgrColor(outerPixels[src], outerPixels[src + 1], outerPixels[src + 2]));
                }
            }
        }
    }

    private static List<BgrColor> BorderSamples(byte[] pixels, int width, int height)
    {
        var samples = new List<BgrColor>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x != 0 && y != 0 && x != width - 1 && y != height - 1)
                {
                    continue;
                }

                int i = ((y * width) + x) * 4;
                samples.Add(new BgrColor(pixels[i], pixels[i + 1], pixels[i + 2]));
            }
        }

        return samples;
    }

    /// <summary>
    /// True when the region carries no glyph: fully transparent, or a single solid fill across
    /// every pixel (a black DIB, an unpainted background, a bare button plate). Pure and
    /// unit-tested — see <c>TrayCaptureTests</c>.
    /// </summary>
    internal static bool IsBlankCapture(BitmapSource source, Int32Rect region)
    {
        if (region.Width <= 0 || region.Height <= 0
            || region.X < 0 || region.Y < 0
            || region.X + region.Width > source.PixelWidth
            || region.Y + region.Height > source.PixelHeight)
        {
            return true;
        }

        int bytesPerPixel = Math.Max(1, source.Format.BitsPerPixel / 8);
        int stride = region.Width * bytesPerPixel;
        var buffer = new byte[stride * region.Height];
        source.CopyPixels(region, buffer, stride, 0);
        return IsBlankPixels(buffer, bytesPerPixel);
    }

    private static bool IsBlankPixels(byte[] buffer, int bytesPerPixel)
    {
        if (buffer.Length == 0)
        {
            return true;
        }

        if (bytesPerPixel < 4)
        {
            // No alpha channel to inspect: a uniform fill is blank, anything else is content.
            for (int i = bytesPerPixel; i < buffer.Length; i++)
            {
                if (buffer[i] != buffer[0])
                {
                    return false;
                }
            }

            return true;
        }

        int first = 0;
        for (int i = 0; i < buffer.Length; i += 4)
        {
            // A single transparent pixel does not make the whole crop blank: the crop is blank only
            // when it is FULLY transparent or a single-color fill (the definition in the doc). Transparent
            // pixels enter the comparison too — if all are 0 it is correctly caught as a "single fill".
            int pixel = (buffer[i + 3] << 24) | (buffer[i + 2] << 16) | (buffer[i + 1] << 8) | buffer[i];
            if (i == 0)
            {
                first = pixel;
                continue;
            }

            if (pixel != first)
            {
                return false;
            }
        }

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

/// <summary>One tray capture plus the window rect it was taken at (crop origin).</summary>
internal sealed record TrayCaptureFrame(BitmapSource Bitmap, RECT WindowRect);

/// <summary>A cropped tray icon and its raw pixels (for <c>TrayIconSignature</c>).</summary>
internal sealed record TrayIconCrop(BitmapSource Icon, byte[] Pixels);
