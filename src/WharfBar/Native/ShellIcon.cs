using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Extracts a shell image for a classic filesystem path at a target pixel size.
/// Prefers <c>IShellItemImageFactory</c> (exact px, crisp at taskbar-like DIP sizes);
/// falls back to <c>SHGetFileInfo</c> HICON. Caller destroys the returned handle.
/// </summary>
internal static class ShellIcon
{
    private static readonly Guid ShellItemImageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    /// <summary>
    /// Tries to get a GDI bitmap (caller <see cref="NativeMethods.DeleteObject"/>)
    /// or an icon (caller <see cref="Destroy"/>). Exactly one of the outs is set on success.
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

        if (TryExtractBitmap(path, sizePx, out hBitmap))
        {
            return true;
        }

        return TryExtractIcon(path, sizePx, out hIcon);
    }

    private static bool TryExtractBitmap(string path, int sizePx, out IntPtr hBitmap)
    {
        hBitmap = IntPtr.Zero;
        IShellItemImageFactory? factory = null;

        try
        {
            Guid iid = ShellItemImageFactoryIid;
            int hr = NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (hr != NativeMethods.S_OK || factory is null)
            {
                return false;
            }

            var size = new SIZE { cx = sizePx, cy = sizePx };
            hr = factory.GetImage(
                size,
                NativeMethods.SIIGBF_ICONONLY | NativeMethods.SIIGBF_BIGGERSIZEOK,
                out hBitmap);

            return hr == NativeMethods.S_OK && hBitmap != IntPtr.Zero;
        }
        catch (COMException)
        {
            hBitmap = IntPtr.Zero;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            hBitmap = IntPtr.Zero;
            return false;
        }
        finally
        {
            if (factory is not null)
            {
                Marshal.ReleaseComObject(factory);
            }
        }
    }

    private static bool TryExtractIcon(string path, int sizePx, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;

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

    internal static void Destroy(IntPtr hIcon)
    {
        if (hIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}
