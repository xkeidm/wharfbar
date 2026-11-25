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
    /// <summary>
    /// Upper bound for the icon cache. Every entry is a decoded, frozen bitmap, and without a bound a
    /// long-running Launchpad visit over a big start-menu tree would keep every glyph forever.
    /// </summary>
    internal const int MaxEntries = 256;

    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Insertion order for oldest-first eviction; only keys that were inserted are enqueued.</summary>
    private readonly ConcurrentQueue<string> _insertionOrder = new();
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
        if (_cache.TryGetValue(key, out ImageSource? cached))
        {
            return cached;
        }

        ImageSource loaded = Load(path, px);
        if (!_cache.TryAdd(key, loaded))
        {
            // Another thread cached the same key while this one was loading; both images are frozen
            // and identical, so the stored one wins (and the local one is simply dropped).
            return _cache.TryGetValue(key, out ImageSource? winner) ? winner : loaded;
        }

        _insertionOrder.Enqueue(key);
        Trim();
        return loaded;
    }

    /// <summary>Convenience — large shell icon (~32 px).</summary>
    internal ImageSource GetIcon(string path) => GetIcon(path, 32);

    /// <summary>Number of cached icons (used by the cache-bound test).</summary>
    internal int CacheCount => _cache.Count;

    /// <summary>
    /// Drops the oldest inserted entries until the cache is back within <see cref="MaxEntries"/>.
    /// Runs after every successful insert; the key just enqueued sits at the tail, so it is never the
    /// one evicted here.
    /// </summary>
    private void Trim()
    {
        while (_cache.Count > MaxEntries && _insertionOrder.TryDequeue(out string? oldest))
        {
            _cache.TryRemove(oldest, out _);
        }
    }

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
