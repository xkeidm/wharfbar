using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Turns a cover bitmap into the final tint color. The extraction runs on a 32x32 copy
/// (microseconds) and is cached by a weak bitmap reference, so repeated calls for the same track are
/// free without pinning a cover the media session has already let go.
/// A gray or very dark cover still resolves to its representative average color, so the island
/// follows the album instead of always falling back to the accent (null = no usable image color).
/// UI thread only.
/// </summary>
internal static class IslandTintSource
{
    /// <summary>
    /// Last cover the tint was computed for. Weak on purpose: the decoded artwork belongs to the
    /// media session and a static strong reference would keep the previous (large) cover alive after
    /// the track changed. <see cref="_lastColor"/> alone is all a repeat lookup needs.
    /// </summary>
    private static WeakReference<ImageSource>? _lastSource;
    private static IslandRgb? _lastColor;

    internal static Color? Resolve(string? mode, ImageSource? thumbnail, bool musicPlaying)
    {
        IslandRgb? album = musicPlaying ? FromThumbnail(thumbnail) : null;
        IslandRgb? tint = IslandPalette.Tint(mode, album, SystemAccent(), musicPlaying);
        return tint is IslandRgb c ? Color.FromRgb(c.R, c.G, c.B) : null;
    }

    private static IslandRgb? FromThumbnail(ImageSource? source)
    {
        if (source is null)
        {
            return null;
        }

        if (_lastSource is not null
            && _lastSource.TryGetTarget(out ImageSource? last)
            && ReferenceEquals(last, source))
        {
            return _lastColor;
        }

        _lastSource = new WeakReference<ImageSource>(source);
        _lastColor = Compute(source);
        return _lastColor;
    }

    private static IslandRgb? Compute(ImageSource source)
    {
        if (source is not BitmapSource bitmap || bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1)
        {
            return null;
        }

        try
        {
            int size = IslandPalette.SampleSize;
            var scaled = new TransformedBitmap(
                bitmap,
                new ScaleTransform((double)size / bitmap.PixelWidth, (double)size / bitmap.PixelHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            var buffer = new byte[width * height * 4];
            converted.CopyPixels(buffer, width * 4, 0);
            return IslandPalette.Dominant(buffer);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[island] tint: {ex.Message}");
            return null;
        }
    }

    private static IslandRgb SystemAccent()
    {
        if (Application.Current?.TryFindResource("SystemAccentColor") is Color c)
        {
            return new IslandRgb(c.R, c.G, c.B);
        }

        return new IslandRgb(0x00, 0x78, 0xD4);
    }
}
