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

    internal ImageSource GetIcon(string path)
    {
        string key = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        return _cache.GetOrAdd(key, Load);
    }

    private ImageSource Load(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)
            && ShellIcon.TryExtract(path, out IntPtr hIcon))
        {
            try
            {
                ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                ShellIcon.Destroy(hIcon);
            }
        }

        return Fallback();
    }

    private ImageSource Fallback()
    {
        if (_fallback is not null)
        {
            return _fallback;
        }

        // Simple 32×32 gray tile — enough to show a missing pin without crashing.
        var bmp = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), null, new Rect(0, 0, 32, 32));
        }

        bmp.Render(dv);
        bmp.Freeze();
        _fallback = bmp;
        return _fallback;
    }
}
