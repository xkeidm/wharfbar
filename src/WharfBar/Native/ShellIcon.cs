using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Extracts the same shell glyph the desktop shows for a classic filesystem path.
/// Prefers an exact-size <c>SHDefExtractIcon</c> from <c>SHGetFileInfo(SHGFI_ICONLOCATION)</c>
/// (jumbo resource, scaled down — never a stretched 16/32 bitmap), then
/// <c>SHGFI_SYSICONINDEX</c> + <c>IImageList</c> jumbo / extra-large. Caller destroys the handle.
/// </summary>
internal static class ShellIcon
{
    private static readonly Guid ImageListIid = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    /// <summary>
    /// Tries to get an icon (caller <see cref="Destroy"/>). <paramref name="hBitmap"/> stays zero;
    /// the HBITMAP factory path upscaled small bitmaps and ignored alpha.
    /// </summary>
    internal static bool TryExtract(
        string path,
        int sizePx,
        out IntPtr hBitmap,
        out IntPtr hIcon)
    {
        hBitmap = IntPtr.Zero;
        hIcon = IntPtr.Zero;

        if (string.IsNullOrWhiteSpace(path) || sizePx <= 0)
        {
            return false;
        }

        int px = Math.Clamp(sizePx, 16, 256);

        if (TryExtractLocated(path, px, out hIcon) && !WouldUpscale(IconPixelWidth(hIcon), px))
        {
            return true;
        }

        Destroy(hIcon);
        hIcon = IntPtr.Zero;

        int listId = ImageListForPixelSize(px);
        if (TryExtractImageList(path, listId, out hIcon) && !WouldUpscale(IconPixelWidth(hIcon), px))
        {
            return true;
        }

        Destroy(hIcon);
        hIcon = IntPtr.Zero;

        // Jumbo is 256 px. Use it whenever a smaller list would have to be stretched.
        if (listId != NativeMethods.SHIL_JUMBO
            && TryExtractImageList(path, NativeMethods.SHIL_JUMBO, out hIcon)
            && !WouldUpscale(IconPixelWidth(hIcon), px))
        {
            return true;
        }

        Destroy(hIcon);
        hIcon = IntPtr.Zero;

        return TryExtractFallbackIcon(path, px, out hIcon);
    }

    /// <summary>
    /// Image list whose native size is at least <paramref name="sizePx"/>, so a 16 or 32 px
    /// bitmap is never the source for a larger slot. 48 px requests use extra-large; above that, jumbo.
    /// </summary>
    internal static int ImageListForPixelSize(int sizePx)
    {
        if (sizePx <= 16)
        {
            return NativeMethods.SHIL_SMALL;
        }

        if (sizePx <= 48)
        {
            return NativeMethods.SHIL_EXTRALARGE;
        }

        return NativeMethods.SHIL_JUMBO;
    }

    /// <summary>
    /// True when <paramref name="iconWidthPx"/> is a 16/32 shell bitmap that would be stretched
    /// to <paramref name="requestedPx"/>. A 48 or 256 source is allowed (the view downscales it).
    /// </summary>
    internal static bool WouldUpscale(int iconWidthPx, int requestedPx) =>
        iconWidthPx > 0 && iconWidthPx < requestedPx && iconWidthPx <= 32;

    internal static void Destroy(IntPtr hIcon)
    {
        if (hIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static bool TryExtractLocated(string path, int sizePx, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        var info = new SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            NativeMethods.SHGFI_ICONLOCATION);

        if (result == IntPtr.Zero || string.IsNullOrWhiteSpace(info.szDisplayName))
        {
            return false;
        }

        int hr = NativeMethods.SHDefExtractIcon(
            info.szDisplayName,
            info.iIcon,
            0,
            out IntPtr large,
            out IntPtr small,
            (uint)sizePx);

        if (small != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(small);
        }

        if (hr != NativeMethods.S_OK || large == IntPtr.Zero)
        {
            Destroy(large);
            return false;
        }

        hIcon = large;
        return true;
    }

    private static bool TryExtractImageList(string path, int listId, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        var info = new SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            NativeMethods.SHGFI_SYSICONINDEX);

        // Non-zero is the system image-list handle. It is owned by the shell — do not destroy it.
        if (result == IntPtr.Zero)
        {
            return false;
        }

        Guid iid = ImageListIid;
        int hr = NativeMethods.SHGetImageList(listId, ref iid, out IImageList? list);
        if (hr != NativeMethods.S_OK || list is null)
        {
            return false;
        }

        try
        {
            hr = list.GetIcon(info.iIcon, (int)NativeMethods.ILD_TRANSPARENT, out hIcon);
            return hr == NativeMethods.S_OK && hIcon != IntPtr.Zero;
        }
        catch (COMException)
        {
            Destroy(hIcon);
            hIcon = IntPtr.Zero;
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(list);
        }
    }

    private static bool TryExtractFallbackIcon(string path, int sizePx, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;

        // Only when every large source failed. Callers that asked for more than 32 px may
        // still upscale this; it beats a gray placeholder.
        uint flags = NativeMethods.SHGFI_ICON
            | (sizePx <= 16 ? NativeMethods.SHGFI_SMALLICON : NativeMethods.SHGFI_LARGEICON);

        var info = new SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return false;
        }

        hIcon = info.hIcon;
        return true;
    }

    private static int IconPixelWidth(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero || !NativeMethods.GetIconInfo(hIcon, out ICONINFO info))
        {
            return 0;
        }

        try
        {
            IntPtr bits = info.hbmColor != IntPtr.Zero ? info.hbmColor : info.hbmMask;
            if (bits == IntPtr.Zero)
            {
                return 0;
            }

            int wrote = NativeMethods.GetObject(bits, Marshal.SizeOf<BITMAP>(), out BITMAP bmp);
            return wrote == 0 ? 0 : bmp.bmWidth;
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(info.hbmColor);
            }

            if (info.hbmMask != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(info.hbmMask);
            }
        }
    }
}
