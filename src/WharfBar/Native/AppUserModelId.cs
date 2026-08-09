using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class AppUserModelId
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr hWnd, string lpString);

    internal static bool TryGet(IntPtr hwnd, out string aumid)
    {
        aumid = string.Empty;
        IntPtr p = GetProp(hwnd, "ApplicationUserModelID");
        if (p == IntPtr.Zero)
        {
            return false;
        }

        aumid = Marshal.PtrToStringUni(p) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(aumid);
    }
}
