using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WharfBar.Services;

internal static class WallpaperAccent
{
    internal static IslandRgb? TryFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            BitmapSource? bitmap = TryDecode(path);
            if (bitmap is null || bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1)
            {
                return null;
            }

            int size = IslandPalette.SampleSize;
            var scaled = new TransformedBitmap(
                bitmap,
                new ScaleTransform((double)size / bitmap.PixelWidth, (double)size / bitmap.PixelHeight));
            var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            var buffer = new byte[width * height * 4];
            converted.CopyPixels(buffer, width * 4, 0);
            return FromDominant(IslandPalette.Dominant(buffer));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static BitmapSource? TryDecode(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
    }

    internal static IslandRgb? FromDominant(IslandRgb? dominant)
    {
        if (dominant is not IslandRgb c)
        {
            return null;
        }

        double L = IslandPalette.Luminance(c);
        if (L < IslandPalette.MinLuminance || L > IslandPalette.MaxLuminance)
        {
            return null;
        }

        return c;
    }

    internal static bool ShouldApply(bool enabled, IslandRgb? color)
        => enabled && color is not null;
}
