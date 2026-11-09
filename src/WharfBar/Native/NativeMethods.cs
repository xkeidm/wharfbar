using System.Runtime.InteropServices;
using System.Text;

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

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;
}

/// <summary>
/// Union must be sized for <see cref="MOUSEINPUT"/> (largest member) so
/// <see cref="Marshal.SizeOf{T}"/> matches native INPUT (40 bytes on x64, 28 on x86).
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
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
    internal const uint ABM_GETSTATE = 0x00000004;
    internal const uint ABM_SETSTATE = 0x0000000A;

    internal const int ABS_AUTOHIDE = 0x00000001;
    internal const int ABS_ALWAYSONTOP = 0x00000002;

    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const int SW_MINIMIZE = 6;
    internal const int SW_SHOWNA = 8;
    internal const int SW_RESTORE = 9;

    internal const uint GW_OWNER = 4;

    /// <summary>Owner of a top-level popup. <c>SetWindowLongPtr</c> index, not <c>GWL_HWNDPARENT</c> (-8 is the same value).</summary>
    internal const int GWLP_HWNDPARENT = -8;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_LAYERED = 0x00080000;
    internal const uint LWA_ALPHA = 0x2;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_APPWINDOW = 0x00040000;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;

    internal const uint ABE_LEFT = 0;
    internal const uint ABE_TOP = 1;
    internal const uint ABE_RIGHT = 2;
    internal const uint ABE_BOTTOM = 3;

    internal const int ABN_STATECHANGE = 0x00000000;
    internal const int ABN_POSCHANGED = 0x00000001;
    internal const int ABN_FULLSCREENAPP = 0x00000002;
    internal const int ABN_WINDOWARRANGE = 0x00000003;

    internal const int WM_ACTIVATE = 0x0006;
    internal const int WM_ACTIVATEAPP = 0x001C;
    internal const int WM_SYSCOLORCHANGE = 0x0015;
    internal const int WM_SETTINGCHANGE = 0x001A;
    internal const int WM_WINDOWPOSCHANGING = 0x0046;
    internal const int WM_WINDOWPOSCHANGED = 0x0047;
    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_DPICHANGED = 0x02E0;
    internal const int WM_THEMECHANGED = 0x031A;
    internal const int WM_DWMCOLORIZATIONCOLORCHANGED = 0x0320;
    internal const uint WM_CLOSE = 0x0010;
    internal const uint WM_SYSCOMMAND = 0x0112;

    /// <summary>SC_RESTORE — restore a minimized/maximized window (Task Manager only honors syscommands).</summary>
    internal const IntPtr SC_RESTORE = 0xF120;

    /// <summary>SC_MINIMIZE — minimize via syscommand (Task Manager only honors syscommands).</summary>
    internal const IntPtr SC_MINIMIZE = 0xF020;

    internal static readonly IntPtr HWND_TOP = new(0);
    internal static readonly IntPtr HWND_BOTTOM = new(1);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);
    internal static readonly IntPtr HWND_BROADCAST = new(0xffff);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint SWP_HIDEWINDOW = 0x0080;

    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;
    internal const uint MDT_EFFECTIVE_DPI = 0;

    /// <summary>Primary-monitor work area (SPI). Pair with <see cref="SPI_SETWORKAREA"/>.</summary>
    internal const uint SPI_GETWORKAREA = 0x0030;

    /// <summary>
    /// Sets work area for the monitor containing the RECT. Used after <c>SW_HIDE</c> on
    /// <c>Shell_TrayWnd</c>, which does not release the taskbar AppBar reservation by itself.
    /// </summary>
    internal const uint SPI_SETWORKAREA = 0x002F;

    internal const uint SPI_SETDESKWALLPAPER = 0x0014;
    internal const uint SPI_GETDESKWALLPAPER = 0x0073;

    internal const uint SPIF_UPDATEINIFILE = 0x0001;
    internal const uint SPIF_SENDCHANGE = 0x0002;

    internal const int S_OK = 0;

    internal const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const uint DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>DWMWA_CLOAK — set composition cloak (13). Distinct from DWMWA_CLOAKED (14, get).</summary>
    internal const uint DWMWA_CLOAK = 13;

    /// <summary>DWMWA_CLOAKED — window hidden from composition (shell-cloaked apps, other virtual desktops).</summary>
    internal const uint DWMWA_CLOAKED = 14;

    /// <summary>Localized name the shell shows, in <c>szDisplayName</c>. No HICON to destroy.</summary>
    internal const uint SHGFI_DISPLAYNAME = 0x000000200;

    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_LARGEICON = 0x000000000;
    internal const uint SHGFI_SMALLICON = 0x000000001;

    /// <summary>Icon file path in <c>szDisplayName</c>, index in <c>iIcon</c>. No HICON to destroy.</summary>
    internal const uint SHGFI_ICONLOCATION = 0x000001000;

    /// <summary>System image-list index in <c>iIcon</c>. The returned handle is the shell list — do not destroy it.</summary>
    internal const uint SHGFI_SYSICONINDEX = 0x000004000;

    /// <summary>32×32 image list.</summary>
    internal const int SHIL_LARGE = 0;

    /// <summary>16×16 image list.</summary>
    internal const int SHIL_SMALL = 1;

    /// <summary>48×48 image list. Native size — do not upscale <see cref="SHIL_LARGE"/> instead.</summary>
    internal const int SHIL_EXTRALARGE = 2;

    /// <summary>256×256 image list. Desktop large/jumbo glyphs live here.</summary>
    internal const int SHIL_JUMBO = 4;

    /// <summary><c>IShellItemImageFactory.GetImage</c> — return an icon, not a thumbnail.</summary>
    internal const int SIIGBF_ICONONLY = 0x00000004;

    /// <summary>Allow a larger asset than requested when that yields a sharper downsample.</summary>
    internal const int SIIGBF_BIGGERSIZEOK = 0x00000001;

    internal const uint MF_STRING = 0x00000000;
    internal const uint MF_SEPARATOR = 0x00000800;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RETURNCMD = 0x0100;

    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_RBUTTONUP = 0x0205;
    internal const int WM_CONTEXTMENU = 0x007B;
    internal const int WM_COMMAND = 0x0111;

    internal const int WM_USER = 0x0400;
    internal const uint WM_LBUTTONDBLCLK = 0x0203;
    internal const uint WM_RBUTTONDOWN = 0x0204;
    internal const uint MK_RBUTTON = 0x0002;
    internal const uint MK_SHIFT = 0x0004;
    internal const uint MK_CONTROL = 0x0008;

    internal const uint TB_BUTTONCOUNT = (uint)WM_USER + 24;
    internal const uint TB_GETBUTTON = (uint)WM_USER + 23;
    internal const uint TB_GETITEMRECT = (uint)WM_USER + 29;
    internal const uint TB_GETIMAGELIST = (uint)WM_USER + 49;
    internal const uint TB_GETBUTTONTEXT = (uint)WM_USER + 75;
    internal const uint ILD_TRANSPARENT = 0x0001;
    internal const uint SMTO_NORMAL = 0x0000;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>
    /// Undocumented Progman message that creates a WorkerW behind the desktop icon host
    /// so a layered window can paint under icons. Used only for wallpaper transition hosting.
    /// </summary>
    internal const uint WM_SPAWN_WORKERW = 0x052C;
    internal const ushort VK_SHIFT = 0x10;
    internal const ushort VK_CONTROL = 0x11;

    /// <summary>Low word of tray callback lParam carries the mouse message.</summary>
    internal const int NIN_SELECT = 0x0400;
    internal const int NIN_KEYSELECT = 0x0401;

    internal const ushort VK_LWIN = 0x5B;
    internal const ushort VK_B = 0x42;
    internal const ushort VK_N = 0x4E;
    internal const ushort VK_P = 0x50;
    internal const ushort VK_RETURN = 0x0D;
    internal const ushort VK_SPACE = 0x20;
    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    internal const uint BM_CLICK = 0x00F5;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    // WM_LBUTTONUP already defined above for tray callbacks
    internal const uint MK_LBUTTON = 0x0001;
    internal const int ASFW_ANY = -1;

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

    /// <summary>
    /// The monitor a rect sits on — the same monitor <c>SPI_SETWORKAREA</c> writes. Pair with
    /// <see cref="GetMonitorInfo"/> to read back the work area of a specific monitor; the
    /// <c>SPI_GETWORKAREA</c> counterpart only ever answers for the primary one.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

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

    /// <summary>
    /// Shell image list for <paramref name="iImageList"/> (<see cref="SHIL_JUMBO"/> /
    /// <see cref="SHIL_EXTRALARGE"/>). Caller <see cref="Marshal.ReleaseComObject"/>s the list.
    /// </summary>
    [DllImport("shell32.dll", ExactSpelling = true)]
    internal static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    /// <summary>
    /// Extracts the shell icon resource at <paramref name="nIconSize"/> (low word = pixels).
    /// Picks the jumbo resource and scales down; does not stretch a 16/32 bitmap.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHDefExtractIcon(
        string pszIconFile,
        int iIndex,
        uint uFlags,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIconSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetObject(IntPtr h, int c, out BITMAP pv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    internal static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr ho);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("comctl32.dll")]
    internal static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    // Console close / Ctrl+C while attached to a console (e.g. `dotnet run`) — restore taskbar.
    internal const uint CTRL_C_EVENT = 0;
    internal const uint CTRL_BREAK_EVENT = 1;
    internal const uint CTRL_CLOSE_EVENT = 2;
    internal const uint CTRL_LOGOFF_EVENT = 5;
    internal const uint CTRL_SHUTDOWN_EVENT = 6;

    internal delegate bool ConsoleCtrlHandlerRoutine(uint dwCtrlType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerRoutine? handler, bool add);

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

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindowEx(
        IntPtr hwndParent,
        IntPtr hwndChildAfter,
        string? lpszClass,
        string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        IntPtr lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetDesktopWindow();

    /// <summary>
    /// Freezes painting of one window until called with <see cref="IntPtr.Zero"/>. Used around
    /// an app-bar state blip so the desktop icon host does not paint the intermediate work-area
    /// size. DWM may ignore it for composited application windows.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LockWindowUpdate(IntPtr hWndLock);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Non-blocking ShowWindow: safe to call on a hung target window.
    /// Used by the Show Desktop sliver (see docs/02-win32-appbar-dwm.md).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        ref RECT pvParam,
        uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        string? pvParam,
        uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(
        uint uiAction,
        uint uiParam,
        StringBuilder pvParam,
        uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsHungAppWindow(IntPtr hwnd);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        int dwFlags,
        System.Text.StringBuilder lpExeName,
        ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>PKEY_AppUserModel_ID — the AppUserModelID property of a .lnk shortcut (pid 5).</summary>
    internal static readonly Guid PKEY_AppUserModelId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    internal const int VT_LPWSTR = 31;
    internal const int VT_EMPTY = 0;
    internal const uint GPS_DEFAULT = 0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHGetPropertyStoreFromParsingName(
        string pszPath,
        IntPtr hwnd,
        uint flags,
        ref Guid riid,
        out IntPtr ppv);

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PROPVARIANT pvar);

    [DllImport("Shcore.dll", SetLastError = true)]
    internal static extern int GetDpiForMonitor(IntPtr hmonitor, uint dpiType, out uint dpiX, out uint dpiY);

    internal const int WS_POPUP = unchecked((int)0x80000000);
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WM_ERASEBKGND = 0x0014;

    internal delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASS
    {
        public uint style;
        public WndProc? lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string? lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    internal static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public int showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx;
    public int cy;
}

/// <summary>Shell item → sized HBITMAP (Windows 7+). Kept for callers that already hold a factory.</summary>
[ComImport]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig]
    int GetImage(SIZE size, int flags, out IntPtr phbm);
}

/// <summary>Shell image list. <see cref="GetIcon"/> is the 8th method; earlier slots stay so the vtable matches.</summary>
[ComImport]
[Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IImageList
{
    [PreserveSig]
    int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);

    [PreserveSig]
    int ReplaceIcon(int i, IntPtr hicon, ref int pi);

    [PreserveSig]
    int SetOverlayImage(int iImage, int iOverlay);

    [PreserveSig]
    int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);

    [PreserveSig]
    int AddMasked(IntPtr hbmImage, int crMask, ref int pi);

    [PreserveSig]
    int Draw(ref IMAGELISTDRAWPARAMS pimldp);

    [PreserveSig]
    int Remove(int i);

    /// <summary>Caller <see cref="NativeMethods.DestroyIcon"/>s <paramref name="picon"/>.</summary>
    [PreserveSig]
    int GetIcon(int i, int flags, out IntPtr picon);
}

[StructLayout(LayoutKind.Sequential)]
internal struct IMAGELISTDRAWPARAMS
{
    public int cbSize;
    public IntPtr himl;
    public int i;
    public IntPtr hdcDst;
    public int x;
    public int y;
    public int cx;
    public int cy;
    public int xBitmap;
    public int yBitmap;
    public int rgbBk;
    public int rgbFg;
    public int fStyle;
    public int dwRop;
    public int fState;
    public int Frame;
    public int crEffect;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    [MarshalAs(UnmanagedType.Bool)]
    public bool fIcon;
    public int xHotspot;
    public int yHotspot;
    public IntPtr hbmMask;
    public IntPtr hbmColor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAP
{
    public int bmType;
    public int bmWidth;
    public int bmHeight;
    public int bmWidthBytes;
    public ushort bmPlanes;
    public ushort bmBitsPixel;
    public IntPtr bmBits;
}

/// <summary>
/// PROPVARIANT — union; string values (VT_LPWSTR) hold <see cref="pwszVal"/> at offset 8.
/// After the value is taken it is released with <see cref="NativeMethods.PropVariantClear"/>.
/// </summary>
[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT
{
    [FieldOffset(0)] public ushort vt;
    [FieldOffset(2)] public ushort wReserved1;
    [FieldOffset(4)] public ushort wReserved2;
    [FieldOffset(6)] public ushort wReserved3;
    [FieldOffset(8)] public IntPtr pwszVal;
    [FieldOffset(8)] public IntPtr p;
}
