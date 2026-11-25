using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Holds a minimize <c>PrintWindow</c> frame so dock restore can reverse the cue.
/// </summary>
/// <remarks>
/// Frames are stored downscaled to <see cref="MaxStoredWidth"/> pixels wide: the flyout preview draws
/// them at ~176 DIP and the restore cue only animates one while it shrinks/fades, so a full-resolution
/// frame is not worth pinning. (The minimize cue itself plays the freshly captured full frame before
/// this cache is read.) A 1080p frame is ~8.3 MB of pixels and up to 32 of them would keep ~265 MB.
/// Entries are evicted oldest-first on both the entry-count and the pixel-byte budget.
/// Every stored frame is frozen (the captured frame arrives frozen; a downscaled frame is
/// materialized and frozen here), so <see cref="TryGet"/> can hand it to any UI context.
/// </remarks>
internal sealed class DockRestoreSnapshotCache
{
    internal const int MaxEntries = 32;

    /// <summary>Stored frames are never wider than this; a frame already this small is kept as it is.</summary>
    internal const int MaxStoredWidth = 480;

    /// <summary>Pixel-byte budget for every stored (downscaled) frame together.</summary>
    internal const long MaxBytes = 48L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<nint, Entry> _byHwnd = new();
    private readonly LinkedList<nint> _order = new();
    private long _totalBytes;

    internal int Count
    {
        get
        {
            lock (_gate)
            {
                return _byHwnd.Count;
            }
        }
    }

    internal void Put(IntPtr hwnd, BitmapSource shot, RECT restorePx)
    {
        if (hwnd == IntPtr.Zero || shot is null)
        {
            return;
        }

        lock (_gate)
        {
            BitmapSource stored;
            try
            {
                stored = Downscale(shot);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                // A resample failure must not break the minimize path: keep the captured frame (the
                // pre-downscale behavior) and only lose the memory saving for this entry.
                System.Diagnostics.Debug.WriteLine($"WharfBar snapshot downscale failed: {ex.Message}");
                stored = shot;
            }

            nint key = hwnd;
            if (_byHwnd.Remove(key, out Entry? previous))
            {
                _totalBytes -= previous.Bytes;
                _order.Remove(key);
            }

            var entry = new Entry(stored, restorePx, PixelBytes(stored));
            _byHwnd[key] = entry;
            _order.AddLast(key);
            _totalBytes += entry.Bytes;

            // Count cap and byte budget both evict oldest-first. The entry just inserted sits at the
            // end of the order list, so it is never the one dropped by this loop.
            while (_byHwnd.Count > 0
                   && _order.First is not null
                   && _order.First.Value != key
                   && (_byHwnd.Count > MaxEntries || _totalBytes > MaxBytes))
            {
                nint oldest = _order.First.Value;
                _order.RemoveFirst();
                if (_byHwnd.Remove(oldest, out Entry? evicted))
                {
                    _totalBytes -= evicted.Bytes;
                }
            }
        }
    }

    internal bool TryGet(IntPtr hwnd, out BitmapSource shot, out RECT restorePx)
    {
        shot = null!;
        restorePx = default;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        lock (_gate)
        {
            if (!_byHwnd.TryGetValue(hwnd, out Entry? entry))
            {
                return false;
            }

            shot = entry.Shot;
            restorePx = entry.RestorePx;
            return true;
        }
    }

    internal void Remove(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        lock (_gate)
        {
            nint key = hwnd;
            if (_byHwnd.Remove(key, out Entry? entry))
            {
                _totalBytes -= entry.Bytes;
                _order.Remove(key);
            }
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _byHwnd.Clear();
            _order.Clear();
            _totalBytes = 0;
        }
    }

    /// <summary>
    /// Materializes a downscale of <paramref name="shot"/>. A bare <see cref="TransformedBitmap"/> is a
    /// deferred view that keeps the full-resolution source alive, so the scaled pixels are copied into
    /// a standalone frozen bitmap; only then can the original frame be collected.
    /// </summary>
    private static BitmapSource Downscale(BitmapSource shot)
    {
        if (shot.PixelWidth <= 0 || shot.PixelHeight <= 0 || shot.PixelWidth <= MaxStoredWidth)
        {
            return shot;
        }

        double scale = (double)MaxStoredWidth / shot.PixelWidth;
        BitmapSource scaled = new TransformedBitmap(shot, new ScaleTransform(scale, scale));
        if (scaled.PixelWidth > MaxStoredWidth)
        {
            // Rounding can push the transformed width one pixel over; correct it so the stored
            // frame stays inside the documented width bound.
            double correction = (double)MaxStoredWidth / scaled.PixelWidth;
            scaled = new TransformedBitmap(scaled, new ScaleTransform(correction, correction));
        }

        int width = scaled.PixelWidth;
        int height = scaled.PixelHeight;
        if (width < 1 || height < 1)
        {
            return shot;
        }

        int bitsPerPixel = scaled.Format.BitsPerPixel;
        if (bitsPerPixel % 8 != 0)
        {
            // Sub-byte pixel formats do not appear in window captures; keep the frame untouched
            // rather than copy it with a guessed stride.
            return shot;
        }

        int bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
        int stride = width * bytesPerPixel;
        var pixels = new byte[stride * height];
        scaled.CopyPixels(pixels, stride, 0);
        BitmapSource frozen = BitmapSource.Create(
            width,
            height,
            shot.DpiX,
            shot.DpiY,
            scaled.Format,
            scaled.Palette,
            pixels,
            stride);
        frozen.Freeze();
        return frozen;
    }

    private static long PixelBytes(BitmapSource bitmap)
        => (long)bitmap.PixelWidth * bitmap.PixelHeight * Math.Max(1, (bitmap.Format.BitsPerPixel + 7) / 8);

    private sealed record Entry(BitmapSource Shot, RECT RestorePx, long Bytes);
}
