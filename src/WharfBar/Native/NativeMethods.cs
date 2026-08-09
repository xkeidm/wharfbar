using System.Runtime.InteropServices;

namespace WharfBar.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;

    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal struct APPBARDATA
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uCallbackMessage;
    public uint uEdge;
    public RECT rc;
    public IntPtr lParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPOS
{
    public IntPtr hwnd;
    public IntPtr hwndInsertAfter;
    public int x;
    public int y;
    public int cx;
    public int cy;
    public uint flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MARGINS
{
    public int cxLeftWidth;
    public int cxRightWidth;
    public int cyTopHeight;
    public int cyBottomHeight;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEINFO
{
    public IntPtr hIcon;
    public int iIcon;
    public uint dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string szTypeName;
}

/// <summary>
/// Raw Win32 entry points. Kept internal; UI code goes through the wrappers in this namespace.
/// </summary>
internal static class NativeMethods
{
    internal const uint ABM_NEW = 0x00000000;
    internal const uint ABM_REMOVE = 0x00000001;
    internal const uint ABM_QUERYPOS = 0x00000002;
    internal const uint ABM_SETPOS = 0x00000003;
    internal const uint ABM_ACTIVATE = 0x00000006;
    internal const uint ABM_WINDOWPOSCHANGED = 0x00000009;

    internal const uint ABE_LEFT = 0;
    internal const uint ABE_TOP = 1;
    internal const uint ABE_RIGHT = 2;
    internal const uint ABE_BOTTOM = 3;

    internal const int ABN_STATECHANGE = 0x00000000;
    internal const int ABN_POSCHANGED = 0x00000001;
    internal const int ABN_FULLSCREENAPP = 0x00000002;
    internal const int ABN_WINDOWARRANGE = 0x00000003;

    internal const int WM_ACTIVATE = 0x0006;
    internal const int WM_SYSCOLORCHANGE = 0x0015;
    internal const int WM_SETTINGCHANGE = 0x001A;
    internal const int WM_WINDOWPOSCHANGING = 0x0046;
    internal const int WM_WINDOWPOSCHANGED = 0x0047;
    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_DPICHANGED = 0x02E0;
    internal const int WM_THEMECHANGED = 0x031A;
    internal const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;

    internal static readonly IntPtr HWND_TOP = new(0);
    internal static readonly IntPtr HWND_BOTTOM = new(1);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;

    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;

    internal const int S_OK = 0;

    internal const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;

    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_LARGEICON = 0x000000000;
    internal const uint SHGFI_SMALLICON = 0x000000001;

    internal const uint MF_STRING = 0x00000000;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RETURNCMD = 0x0100;

    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_CONTEXTMENU = 0x007B;
    internal const int WM_COMMAND = 0x0111;

    /// <summary>Low word of tray callback lParam carries the mouse message.</summary>
    internal const int NIN_SELECT = 0x0400;
    internal const int NIN_KEYSELECT = 0x0401;

    [DllImport("shell32.dll", SetLastError = false)]
    internal static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, uint dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmIsCompositionEnabled(out bool pfEnabled);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>Windows 10 1607+. Returns 0 when the window handle is not yet valid.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint TrackPopupMenu(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        int nReserved,
        IntPtr hWnd,
        IntPtr prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}
