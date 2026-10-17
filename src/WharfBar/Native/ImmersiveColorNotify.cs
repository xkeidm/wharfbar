using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class ImmersiveColorNotify
{
    internal static void Broadcast()
    {
        IntPtr lParam = Marshal.StringToHGlobalUni("ImmersiveColorSet");
        try
        {
            _ = NativeMethods.SendMessageTimeout(
                NativeMethods.HWND_BROADCAST,
                (uint)NativeMethods.WM_SETTINGCHANGE,
                IntPtr.Zero,
                lParam,
                NativeMethods.SMTO_ABORTIFHUNG,
                1000,
                out _);
        }
        finally
        {
            Marshal.FreeHGlobal(lParam);
        }
    }
}
