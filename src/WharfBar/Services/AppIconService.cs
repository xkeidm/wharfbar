using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed class AppIconService
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private ImageSource? _fallback;

    /// <summary>
    /// Loads the shell glyph for <paramref name="sizePx"/> device pixels (DPI-aware).
    /// The bitmap is jumbo or exact-size — never a 16/32 px icon stretched in WPF.
    /// Cache key includes size so 100%/150%/200% strips do not share one bitmap.
    /// </summary>
    internal ImageSource GetIcon(string path, int sizePx)
    {
        int px = Math.Clamp(sizePx, 16, 256);
        string key = string.IsNullOrWhiteSpace(path) ? $"?\0{px}" : $"{path}\0{px}";
        return _cache.GetOrAdd(key, _ => Load(path, px));
    }

    /// <summary>Convenience — large shell icon (~32 px).</summary>
    internal ImageSource GetIcon(string path) => GetIcon(path, 32);

    private ImageSource Load(string path, int sizePx)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)
            && ShellIcon.TryExtract(path, sizePx, out IntPtr hBitmap, out IntPtr hIcon))
        {
            try
            {
                if (hBitmap != IntPtr.Zero)
                {
                    ImageSource fromBmp = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    fromBmp.Freeze();
                    return fromBmp;
                }

                ImageSource fromIcon = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                fromIcon.Freeze();
                return fromIcon;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(hBitmap);
                }

                ShellIcon.Destroy(hIcon);
            }
        }

        return Fallback(sizePx);
    }

    private ImageSource Fallback(int sizePx)
    {
        // One shared tile is fine; it is only a missing-pin placeholder.
        if (_fallback is not null)
        {
            return _fallback;
        }

        int tile = Math.Clamp(sizePx, 16, 48);
        var bmp = new RenderTargetBitmap(tile, tile, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                null,
                new Rect(0, 0, tile, tile));
        }

        bmp.Render(dv);
        bmp.Freeze();
        _fallback = bmp;
        return _fallback;
    }
}
