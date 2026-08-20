using System.Windows.Interop;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// A hidden message-only window (HWND_MESSAGE) that receives broadcast messages such as
/// WM_DISPLAYCHANGE / WM_SETTINGCHANGE without touching the visible window set. Created on the
/// UI thread (STA) so the WPF dispatcher can pump its messages.
/// </summary>
internal sealed class MonitorMessageWindow : IDisposable
{
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _disposed;

    internal event EventHandler? DisplaySettingsChanged;
    internal event EventHandler? SettingsChanged;

    internal MonitorMessageWindow()
    {
        var parameters = new HwndSourceParameters("WharfBarMonitorWindow")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_DISPLAYCHANGE:
                DisplaySettingsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case NativeMethods.WM_SETTINGCHANGE:
                SettingsChanged?.Invoke(this, EventArgs.Empty);
                break;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
