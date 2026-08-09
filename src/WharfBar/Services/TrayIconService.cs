using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Owns WharfBar's tray icon and quit menu via <c>Shell_NotifyIcon</c>.
/// This is our icon only — it does not read Wi-Fi / battery / volume tray icons.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const uint QuitCommandId = 1;

    private readonly HwndSource _source;
    private NotifyIconHost? _notifyIcon;
    private bool _disposed;

    internal TrayIconService()
    {
        var parameters = new HwndSourceParameters("WharfBarTray")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            // Message-only window: no desktop presence, receives tray callbacks.
            ParentWindow = new IntPtr(-3),
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        IntPtr icon = LoadTrayIcon(out bool ownsIcon);
        _notifyIcon = new NotifyIconHost(_source.Handle, icon, "WharfBar", ownsIcon);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon?.Dispose();
        _notifyIcon = null;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NotifyIconHost.TrayCallbackMessage)
        {
            int mouseMsg = lParam.ToInt32() & 0xFFFF;
            if (mouseMsg is NativeMethods.WM_RBUTTONUP
                or NativeMethods.WM_CONTEXTMENU
                or NativeMethods.WM_LBUTTONUP
                or NativeMethods.NIN_SELECT
                or NativeMethods.NIN_KEYSELECT)
            {
                ShowQuitMenu(hwnd);
                handled = true;
            }

            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private static void ShowQuitMenu(IntPtr hwnd)
    {
        if (!NativeMethods.GetCursorPos(out POINT point))
        {
            return;
        }

        IntPtr menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, (UIntPtr)QuitCommandId, "Quit WharfBar");

            // Required so the menu dismisses correctly when clicking away.
            _ = NativeMethods.SetForegroundWindow(hwnd);

            uint selected = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                point.X,
                point.Y,
                0,
                hwnd,
                IntPtr.Zero);

            if (selected == QuitCommandId)
            {
                Application.Current?.Shutdown();
            }
        }
        finally
        {
            _ = NativeMethods.DestroyMenu(menu);
        }
    }

    private static IntPtr LoadTrayIcon(out bool ownsIcon)
    {
        string? path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            var info = new SHFILEINFO();
            IntPtr result = NativeMethods.SHGetFileInfo(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

            if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                ownsIcon = true;
                return info.hIcon;
            }
        }

        // Stock IDI_APPLICATION — shared handle, must not DestroyIcon.
        ownsIcon = false;
        return NativeMethods.LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }
}
