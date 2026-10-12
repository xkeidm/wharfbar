using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary><c>FOLDERID_Downloads</c> via <c>SHGetKnownFolderPath</c>. Not written into config.</summary>
internal static class DockKnownFolder
{
    internal static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    internal static string? TryGetDownloads()
    {
        Guid id = Downloads;
        int hr = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out IntPtr path);
        if (hr != 0 || path == IntPtr.Zero)
        {
            Debug.WriteLine($"WharfBar FOLDERID_Downloads failed: 0x{hr:X8}");
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);
}
