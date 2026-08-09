namespace WharfBar.Native;

/// <summary>
/// Thin wrappers for primary-monitor taskbar hwnd + AppBar state.
/// UI must not call NativeMethods for tray hide/restore directly.
/// </summary>
internal static class TaskbarNative
{
    private const string TrayClass = "Shell_TrayWnd";

    internal static IntPtr FindTrayHwnd() =>
        NativeMethods.FindWindow(TrayClass, null);

    internal static uint GetState()
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
        };
        return (uint)NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETSTATE, ref data);
    }

    internal static void SetState(uint state)
    {
        var data = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            lParam = (IntPtr)(int)state,
        };
        _ = NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETSTATE, ref data);
    }

    internal static bool IsTrayVisible()
    {
        IntPtr hwnd = FindTrayHwnd();
        return hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd);
    }

    internal static void SetTrayVisible(bool visible)
    {
        IntPtr hwnd = FindTrayHwnd();
        if (hwnd == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("WharfBar: Shell_TrayWnd not found");
            return;
        }

        // SW_SHOW (not SW_SHOWNA): after SW_HIDE, SHOWNA often leaves Shell_TrayWnd invisible.
        _ = NativeMethods.ShowWindow(
            hwnd,
            visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE);
    }
}
