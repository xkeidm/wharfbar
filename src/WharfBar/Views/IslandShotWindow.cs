using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WharfBar.Native;

namespace WharfBar.Views;

/// <summary>
/// The island pill's own top-level window. Screen capture can drop this HWND
/// without dropping the rest of the top bar.
/// </summary>
internal sealed class IslandShotWindow : Window
{
    private readonly Grid _host = new();

    internal IslandShotWindow(Window owner)
    {
        Owner = owner;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = _host;
        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);
            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_NOACTIVATE);
        };
    }

    internal Grid Host => _host;

    internal void Place(int x, int y, int widthPx, int heightPx)
    {
        if (!IsVisible)
        {
            Show();
        }

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            x,
            y,
            Math.Max(1, widthPx),
            Math.Max(1, heightPx),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        WindowCapture.SetExcludedFromCapture(hwnd, true);
    }

    internal void Conceal()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        WindowCapture.SetExcludedFromCapture(hwnd, false);
        Hide();
    }
}
