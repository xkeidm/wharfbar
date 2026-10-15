using System.Windows.Media.Imaging;

namespace WharfBar.Services;

/// <summary>
/// Pixel fingerprint of a captured tray icon (FNV-1a 64 over size + raw pixels). A refresh whose
/// crop hashes the same as the cached icon reuses the cached <c>ImageSource</c>, so an unchanged
/// icon never reaches the UI. Pure; a 24×24 BGRA crop is ~2 KB of hashing.
/// </summary>
internal static class TrayIconSignature
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    internal static ulong Compute(ReadOnlySpan<byte> pixels, int width, int height)
    {
        ulong hash = OffsetBasis;
        hash = MixInt(hash, width);
        hash = MixInt(hash, height);
        foreach (byte b in pixels)
        {
            hash ^= b;
            hash *= Prime;
        }

        return hash;
    }

    internal static ulong Of(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int bytesPerPixel = Math.Max(1, (source.Format.BitsPerPixel + 7) / 8);
        int stride = source.PixelWidth * bytesPerPixel;
        var buffer = new byte[stride * source.PixelHeight];
        source.CopyPixels(buffer, stride, 0);
        return Compute(buffer, source.PixelWidth, source.PixelHeight);
    }

    private static ulong MixInt(ulong hash, int value)
    {
        for (int shift = 0; shift < 32; shift += 8)
        {
            hash ^= (byte)(value >> shift);
            hash *= Prime;
        }

        return hash;
    }
}
