using System.Text;

namespace WharfBar.Native;

internal static class DesktopWallpaper
{
    internal static bool TrySet(string path) =>
        NativeMethods.SystemParametersInfo(
            NativeMethods.SPI_SETDESKWALLPAPER,
            0,
            path,
            NativeMethods.SPIF_UPDATEINIFILE | NativeMethods.SPIF_SENDCHANGE);

    internal static bool TryGet(out string path)
    {
        var sb = new StringBuilder(512);
        if (!NativeMethods.SystemParametersInfo(
                NativeMethods.SPI_GETDESKWALLPAPER,
                (uint)sb.Capacity,
                sb,
                0))
        {
            path = string.Empty;
            return false;
        }

        path = sb.ToString();
        return true;
    }
}
