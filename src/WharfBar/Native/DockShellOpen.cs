using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>Opens a file or <c>shell:</c> folder with the registered verb. Nothing is executed by WharfBar.</summary>
internal static class DockShellOpen
{
    private const int SW_SHOWNORMAL = 1;

    internal static bool TryOpen(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // A null verb is the registered default, not a hard-coded "open".
        IntPtr result = ShellExecuteW(IntPtr.Zero, null, path, null, null, SW_SHOWNORMAL);
        if (result.ToInt64() <= 32)
        {
            Debug.WriteLine($"WharfBar ShellExecute failed for {path}: {result.ToInt64()}");
            return false;
        }

        return true;
    }

    internal static bool TryOpenRecycleBin() => TryOpen("shell:RecycleBinFolder");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr ShellExecuteW(
        IntPtr hwnd,
        string? lpOperation,
        string lpFile,
        string? lpParameters,
        string? lpDirectory,
        int nShowCmd);
}
