using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Extracts a shell icon for a classic filesystem path. Caller must <see cref="Destroy"/>.
/// </summary>
internal static class ShellIcon
{
    internal static bool TryExtract(string path, out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var info = new SHFILEINFO();
        IntPtr result = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);

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
