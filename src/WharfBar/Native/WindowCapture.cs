using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// <c>SetWindowDisplayAffinity</c> — drops the given window from screen capture.
/// In the last-entry preview this call targets the pill's own window; the top bar stays.
/// </summary>
internal static class WindowCapture
{
    internal const uint WDA_NONE = 0;

    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    internal static void SetExcludedFromCapture(IntPtr hwnd, bool exclude)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        uint affinity = exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE;
        if (!SetWindowDisplayAffinity(hwnd, affinity))
        {
            Debug.WriteLine(
                $"[WharfBar] SetWindowDisplayAffinity({affinity:X}) failed: {Marshal.GetLastWin32Error()}");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
