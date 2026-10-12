using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WharfBar.Native;

/// <summary><c>SHLoadIndirectString</c> for a package logo resource. A failed lookup is not a path.</summary>
internal static class PackageIconNative
{
    internal static bool TryLoadIndirectString(string? source, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var buffer = new StringBuilder(1024);
        int hr = SHLoadIndirectString(source, buffer, buffer.Capacity, IntPtr.Zero);
        if (hr != 0 || buffer.Length == 0)
        {
            Debug.WriteLine($"WharfBar SHLoadIndirectString failed: 0x{hr:X8}");
            return false;
        }

        resolved = buffer.ToString();
        return true;
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(
        string pszSource,
        StringBuilder pszOutBuf,
        int cchOutBuf,
        IntPtr ppvReserved);
}
