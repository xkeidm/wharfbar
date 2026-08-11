using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace WharfBar.Native;

internal sealed class TrayIconItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public ImageSource? IconSource { get; init; }
}

/// <summary>
/// Enumerates third-party notification-area icons via UI Automation (Win11 22H2+).
/// Classic <c>ToolbarWindow32</c> + <c>ReadProcessMemory</c> no longer works — the tray is XAML.
/// Requires a short taskbar peek so UIA can see <c>NotifyItemIcon</c> buttons.
/// </summary>
internal static class TrayIconNative
{
    private const string NotifyItemAutomationId = "NotifyItemIcon";
    private const string SelfTrayName = "WharfBar";

    private const uint SRCCOPY = 0x00CC0020;
    private const int InputMouse = 0;
    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint MouseEventFAbsolute = 0x8000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;

    /// <summary>
    /// Caller must ensure <c>Shell_TrayWnd</c> is visible (peek) before calling.
    /// Must run on an STA thread (WPF UI dispatcher is fine).
    /// </summary>
    /// <param name="captureIcons">
    /// When false (stealth / off-screen peek), skip <c>BitBlt</c> — bounds are not on-screen.
    /// </param>
    internal static List<TrayIconItem> GetSystemTrayIcons(bool captureIcons = true)
    {
        var items = new List<TrayIconItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            // Give XAML tray a beat after ShowWindow.
            Thread.Sleep(captureIcons ? 80 : 50);

            AutomationElement? taskbar = AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));

            if (taskbar is null)
            {
                Debug.WriteLine("WharfBar: UIA Shell_TrayWnd not found (is tray peeked?)");
                return items;
            }

            AutomationElementCollection buttons = taskbar.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, NotifyItemAutomationId));

            foreach (AutomationElement button in buttons)
            {
                string? title;
                Rect bounds;
                try
                {
                    title = button.Current.Name;
                    bounds = button.Current.BoundingRectangle;
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                title = title.Trim();
                if (string.Equals(title, SelfTrayName, StringComparison.OrdinalIgnoreCase)
                    || title.StartsWith(SelfTrayName + " ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Stable-ish key: first line of tooltip (volume % etc. still updates list).
                string id = FirstLine(title);
                if (!seen.Add(id))
                {
                    continue;
                }

                ImageSource? icon = captureIcons ? CaptureIcon(bounds) : null;

                items.Add(new TrayIconItem
                {
                    Id = id,
                    Title = title,
                    IconSource = icon,
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: UIA tray scan failed — {ex.Message}");
        }

        return items;
    }

    internal static void ForwardClick(TrayIconItem item, bool isRightClick)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Id))
        {
            return;
        }

        try
        {
            Thread.Sleep(60);

            AutomationElement? taskbar = AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
            if (taskbar is null)
            {
                return;
            }

            AutomationElement? match = FindButtonById(taskbar, item.Id);
            if (match is null)
            {
                Debug.WriteLine($"WharfBar: tray UIA button not found for '{item.Id}'");
                return;
            }

            Rect bounds = match.Current.BoundingRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0 || double.IsInfinity(bounds.X))
            {
                return;
            }

            if (!isRightClick
                && match.TryGetCurrentPattern(InvokePattern.Pattern, out object? patternObj)
                && patternObj is InvokePattern invoke)
            {
                invoke.Invoke();
                return;
            }

            ClickScreenPoint(
                bounds.X + (bounds.Width / 2.0),
                bounds.Y + (bounds.Height / 2.0),
                isRightClick);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: tray UIA click failed — {ex.Message}");
        }
    }

    private static AutomationElement? FindButtonById(AutomationElement taskbar, string id)
    {
        AutomationElementCollection buttons = taskbar.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, NotifyItemAutomationId));

        foreach (AutomationElement button in buttons)
        {
            try
            {
                string name = button.Current.Name?.Trim() ?? string.Empty;
                if (string.Equals(FirstLine(name), id, StringComparison.Ordinal))
                {
                    return button;
                }
            }
            catch (ElementNotAvailableException)
            {
                // skip
            }
        }

        return null;
    }

    private static string FirstLine(string title)
    {
        int nl = title.IndexOfAny(['\r', '\n']);
        return nl >= 0 ? title[..nl].Trim() : title;
    }

    private static ImageSource? CaptureIcon(Rect bounds)
    {
        if (bounds.Width < 2 || bounds.Height < 2
            || double.IsNaN(bounds.X) || double.IsInfinity(bounds.X)
            || bounds.X < -10000)
        {
            return null;
        }

        int x = (int)Math.Round(bounds.X);
        int y = (int)Math.Round(bounds.Y);
        int w = Math.Clamp((int)Math.Round(bounds.Width), 2, 96);
        int h = Math.Clamp((int)Math.Round(bounds.Height), 2, 96);

        // Prefer a centered square (tray buttons are often taller than wide).
        int side = Math.Min(w, h);
        int srcX = x + ((w - side) / 2);
        int srcY = y + ((h - side) / 2);

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = CreateCompatibleBitmap(screenDc, side, side);
        if (memDc == IntPtr.Zero || hBitmap == IntPtr.Zero)
        {
            if (hBitmap != IntPtr.Zero)
            {
                _ = NativeMethods.DeleteObject(hBitmap);
            }

            if (memDc != IntPtr.Zero)
            {
                _ = DeleteDC(memDc);
            }

            _ = ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        IntPtr old = SelectObject(memDc, hBitmap);
        try
        {
            if (!BitBlt(memDc, 0, 0, side, side, screenDc, srcX, srcY, SRCCOPY))
            {
                return null;
            }

            BitmapSource bitmap = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // BitBlt captures the whole NotifyItemIcon chrome (dark tile + white
            // separators). Punch that out so only the glyph sits on the top bar.
            return StripTrayButtonChrome(bitmap);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: tray icon capture failed — {ex.Message}");
            return null;
        }
        finally
        {
            _ = SelectObject(memDc, old);
            _ = NativeMethods.DeleteObject(hBitmap);
            _ = DeleteDC(memDc);
            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Makes Win11 tray-button fill transparent via corner flood-fill, then crops to the glyph.
    /// Falls back to a center crop (keeps chrome) if stripping would leave an empty image.
    /// </summary>
    private static ImageSource StripTrayButtonChrome(BitmapSource source)
    {
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        if (width < 4 || height < 4)
        {
            bgra.Freeze();
            return bgra;
        }

        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        // Work on a copy so we can fall back to the original capture.
        byte[] stripped = (byte[])pixels.Clone();
        const int fillTolerance = 22;
        FloodClearFromCorner(stripped, stride, width, height, 0, 0, fillTolerance);
        FloodClearFromCorner(stripped, stride, width, height, width - 1, 0, fillTolerance);
        FloodClearFromCorner(stripped, stride, width, height, 0, height - 1, fillTolerance);
        FloodClearFromCorner(stripped, stride, width, height, width - 1, height - 1, fillTolerance);

        // Edge hairlines that survive flood (thin white separators).
        bool fillIsDark = IsDark(ReadPixel(pixels, stride, 0, 0));
        ClearEdgeSeparators(stripped, stride, width, height, fillIsDark);

        int opaque = CountOpaque(stripped, stride, width, height);
        // Too aggressive / monochrome glyph wiped → keep a center crop of the raw capture.
        if (opaque < Math.Max(24, (width * height) / 40))
        {
            return CenterCrop(pixels, stride, width, height, bgra.DpiX, bgra.DpiY);
        }

        if (!TryContentBounds(stripped, stride, width, height, out int left, out int top, out int right, out int bottom))
        {
            return CenterCrop(pixels, stride, width, height, bgra.DpiX, bgra.DpiY);
        }

        return CropPixels(stripped, stride, width, height, left, top, right, bottom, bgra.DpiX, bgra.DpiY, pad: 1);
    }

    private static void FloodClearFromCorner(
        byte[] pixels,
        int stride,
        int width,
        int height,
        int startX,
        int startY,
        int tolerance)
    {
        Color fill = ReadPixel(pixels, stride, startX, startY);
        var stack = new Stack<(int X, int Y)>();
        var seen = new bool[width * height];
        stack.Push((startX, startY));

        while (stack.Count > 0)
        {
            (int x, int y) = stack.Pop();
            if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            {
                continue;
            }

            int idx = (y * width) + x;
            if (seen[idx])
            {
                continue;
            }

            seen[idx] = true;
            int i = (y * stride) + (x * 4);
            if (pixels[i + 3] < 16)
            {
                continue;
            }

            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            if (ColorDistance(r, g, b, fill) > tolerance)
            {
                continue;
            }

            pixels[i + 3] = 0;
            stack.Push((x + 1, y));
            stack.Push((x - 1, y));
            stack.Push((x, y + 1));
            stack.Push((x, y - 1));
        }
    }

    private static void ClearEdgeSeparators(byte[] pixels, int stride, int width, int height, bool fillIsDark)
    {
        const int tolerance = 20;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool nearEdge = x <= 1 || y <= 1 || x >= width - 2 || y >= height - 2;
                if (!nearEdge)
                {
                    continue;
                }

                int i = (y * stride) + (x * 4);
                if (pixels[i + 3] < 16)
                {
                    continue;
                }

                if (IsHairlineSeparator(pixels[i + 2], pixels[i + 1], pixels[i], tolerance, fillIsDark))
                {
                    pixels[i + 3] = 0;
                }
            }
        }
    }

    private static int CountOpaque(byte[] pixels, int stride, int width, int height)
    {
        int n = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] >= 16)
                {
                    n++;
                }
            }
        }

        return n;
    }

    private static bool IsDark(Color c) => ((c.R + c.G + c.B) / 3) < 128;

    private static ImageSource CenterCrop(
        byte[] pixels,
        int stride,
        int width,
        int height,
        double dpiX,
        double dpiY)
    {
        int inset = Math.Max(2, Math.Min(width, height) / 5);
        int left = inset;
        int top = inset;
        int right = width - 1 - inset;
        int bottom = height - 1 - inset;
        if (right <= left || bottom <= top)
        {
            left = 0;
            top = 0;
            right = width - 1;
            bottom = height - 1;
        }

        return CropPixels(pixels, stride, width, height, left, top, right, bottom, dpiX, dpiY, pad: 0);
    }

    private static ImageSource CropPixels(
        byte[] pixels,
        int stride,
        int width,
        int height,
        int left,
        int top,
        int right,
        int bottom,
        double dpiX,
        double dpiY,
        int pad)
    {
        left = Math.Max(0, left - pad);
        top = Math.Max(0, top - pad);
        right = Math.Min(width - 1, right + pad);
        bottom = Math.Min(height - 1, bottom + pad);

        int cropW = right - left + 1;
        int cropH = bottom - top + 1;
        int cropStride = cropW * 4;
        byte[] cropped = new byte[cropStride * cropH];
        for (int y = 0; y < cropH; y++)
        {
            Buffer.BlockCopy(
                pixels,
                ((top + y) * stride) + (left * 4),
                cropped,
                y * cropStride,
                cropStride);
        }

        var result = BitmapSource.Create(
            cropW,
            cropH,
            dpiX,
            dpiY,
            PixelFormats.Bgra32,
            null,
            cropped,
            cropStride);
        result.Freeze();
        return result;
    }

    private static Color ReadPixel(byte[] pixels, int stride, int x, int y)
    {
        int i = (y * stride) + (x * 4);
        return Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private static int ColorDistance(byte r, byte g, byte b, Color other)
    {
        int dr = r - other.R;
        int dg = g - other.G;
        int db = b - other.B;
        return (int)Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static bool IsHairlineSeparator(byte r, byte g, byte b, int tolerance, bool fillIsDark)
    {
        // On dark tray tiles separators are near-white; on light tiles they are near-black.
        // Avoid punching mid-tone glyph pixels that happen to sit on the crop edge.
        if (fillIsDark)
        {
            return r >= 255 - tolerance && g >= 255 - tolerance && b >= 255 - tolerance;
        }

        return r <= tolerance && g <= tolerance && b <= tolerance;
    }

    private static bool TryContentBounds(
        byte[] pixels,
        int stride,
        int width,
        int height,
        out int left,
        out int top,
        out int right,
        out int bottom)
    {
        left = width;
        top = height;
        right = -1;
        bottom = -1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[(y * stride) + (x * 4) + 3] < 16)
                {
                    continue;
                }

                if (x < left)
                {
                    left = x;
                }

                if (y < top)
                {
                    top = y;
                }

                if (x > right)
                {
                    right = x;
                }

                if (y > bottom)
                {
                    bottom = y;
                }
            }
        }

        return right >= left && bottom >= top;
    }

    private static void ClickScreenPoint(double screenX, double screenY, bool rightClick)
    {
        int cx = GetSystemMetrics(SmCxScreen);
        int cy = GetSystemMetrics(SmCyScreen);
        if (cx <= 0 || cy <= 0)
        {
            return;
        }

        int absX = (int)Math.Round(screenX * 65535.0 / Math.Max(cx - 1, 1));
        int absY = (int)Math.Round(screenY * 65535.0 / Math.Max(cy - 1, 1));

        uint down = rightClick ? MouseEventFRightDown : MouseEventFLeftDown;
        uint up = rightClick ? MouseEventFRightUp : MouseEventFLeftUp;

        INPUT[] inputs =
        [
            new INPUT
            {
                type = InputMouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absX,
                        dy = absY,
                        dwFlags = MouseEventFMove | MouseEventFAbsolute,
                    },
                },
            },
            new INPUT
            {
                type = InputMouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absX,
                        dy = absY,
                        dwFlags = down | MouseEventFAbsolute,
                    },
                },
            },
            new INPUT
            {
                type = InputMouse,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absX,
                        dy = absY,
                        dwFlags = up | MouseEventFAbsolute,
                    },
                },
            },
        ];

        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
