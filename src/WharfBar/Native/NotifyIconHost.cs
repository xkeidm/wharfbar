using System.Runtime.InteropServices;

namespace WharfBar.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NOTIFYICONDATA
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string szTip;
}

/// <summary>
/// Own-process tray icon via <c>Shell_NotifyIcon</c>. Not for reading other apps' tray icons.
/// </summary>
internal sealed class NotifyIconHost : IDisposable
{
    internal const uint TrayIconId = 1;
    internal const int TrayCallbackMessage = 0x0400 + 77; // WM_USER + 77

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private readonly IntPtr _hwnd;
    private IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly string _tip;
    private bool _added;
    private bool _disposed;

    internal NotifyIconHost(IntPtr hwnd, IntPtr icon, string tip, bool ownsIcon = true)
    {
        _hwnd = hwnd;
        _icon = icon;
        _ownsIcon = ownsIcon;
        _tip = tip.Length <= 127 ? tip : tip[..127];
        AddOrModify(NimAdd);
        _added = true;
    }

    internal void UpdateTip(string tip) => AddOrModify(NimModify, tip);

    /// <summary>NIM_ADD again after explorer restarts and drops the icon.</summary>
    internal void Readd()
    {
        if (_disposed)
        {
            return;
        }

        if (!AddOrModify(NimAdd))
        {
            AddOrModify(NimModify);
        }

        _added = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_added)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = TrayIconId,
            };
            _ = NativeMethods.Shell_NotifyIcon(NimDelete, ref data);
            _added = false;
        }

        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            _ = NativeMethods.DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    private bool AddOrModify(uint message, string? tip = null)
    {
        string shown = tip ?? _tip;
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
            szTip = shown.Length <= 127 ? shown : shown[..127],
        };

        bool ok = NativeMethods.Shell_NotifyIcon(message, ref data);
        if (!ok && message == NimAdd)
        {
            // Explorer may not be ready yet; TaskbarCreated calls Readd.
        }

        return ok;
    }
}
