using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WharfBar.Native;
using WharfBar.Services;
using Wpf.Ui.Controls;

namespace WharfBar.Views;

/// <summary>
/// The macOS-style menu bar across the top of the primary monitor.
/// Owns the AppBar registration and every path that must release it.
/// </summary>
public partial class TopBarWindow : Window
{
    private const double BarHeightDip = 30d;
    private const string AppBarCallbackName = "WharfBarAppBarMessage";

    /// <summary>
    /// Mica rather than Acrylic: the AppBar reserves the strip, so in practice only the wallpaper
    /// sits behind the bar and Acrylic's live blur would cost more for the same result.
    /// </summary>
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    /// <summary>Square corners on a full-width edge-anchored bar — see ADR-004.</summary>
    private const DwmCornerPreference Corners = DwmCornerPreference.DoNotRound;

    private AppBar? _appBar;
    private WindowAppearance? _appearance;
    private HwndSource? _source;
    private uint _taskbarCreatedMessage;
    private bool _isApplyingPlacement;
    private bool _isFullScreenAppActive;
    private bool _isShutdownHooked;

    public TopBarWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source is null)
        {
            return;
        }

        _source.AddHook(WndProc);

        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _appBar = new AppBar(
            _source.Handle,
            NativeMethods.RegisterWindowMessage(AppBarCallbackName),
            NativeMethods.ABE_TOP);

        HookShutdownPaths();

        _appearance = new WindowAppearance(this, _source.Handle, Backdrop, Corners);
        _appearance.Apply();

        _appBar.Register();
        ApplyPlacement();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ReleaseAppBar();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WndProc);
        _source = null;
        base.OnClosed(e);
    }

    /// <summary>
    /// Every route out of the process must reach <see cref="AppBar.Remove"/>, otherwise the
    /// reserved work area survives us. Removal is idempotent, so overlapping hooks are fine.
    /// </summary>
    private void HookShutdownPaths()
    {
        if (_isShutdownHooked)
        {
            return;
        }

        _isShutdownHooked = true;

        if (Application.Current is { } app)
        {
            app.Exit += (_, _) => ReleaseAppBar();
            app.SessionEnding += (_, _) => ReleaseAppBar();
            app.DispatcherUnhandledException += (_, _) => ReleaseAppBar();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseAppBar();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseAppBar();
    }

    private void ReleaseAppBar() => _appBar?.Remove();

    /// <summary>
    /// Recomputes the bar rect in physical pixels for the current monitor DPI and asks the
    /// shell to approve it, then moves the window there with SetWindowPos. WPF's DIP-based
    /// Left/Top/Width/Height are deliberately unused: they assume the primary monitor's scale.
    /// </summary>
    private void ApplyPlacement()
    {
        if (_source is null || _appBar is null || _isApplyingPlacement)
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _isApplyingPlacement = true;
        try
        {
            uint dpi = Display.GetDpi(hwnd);
            int heightPx = Display.DipToPixels(BarHeightDip, dpi);
            RECT monitor = Display.GetPrimaryMonitorBounds(hwnd);

            if (monitor.Width <= 0 || heightPx <= 0)
            {
                return;
            }

            var desired = new RECT
            {
                Left = monitor.Left,
                Top = monitor.Top,
                Right = monitor.Right,
                Bottom = monitor.Top + heightPx,
            };

            RECT approved = _appBar.Reserve(desired);

            NativeMethods.SetWindowPos(
                hwnd,
                _isFullScreenAppActive ? NativeMethods.HWND_BOTTOM : NativeMethods.HWND_TOPMOST,
                approved.Left,
                approved.Top,
                approved.Width,
                approved.Height,
                NativeMethods.SWP_NOACTIVATE);

            WorkAreaChanged.Notify();
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_appBar is not null && _appBar.TryGetNotification(msg, wParam, out AppBarNotification notification))
        {
            HandleAppBarNotification(notification, lParam);
            handled = true;
            return IntPtr.Zero;
        }

        if (_taskbarCreatedMessage != 0 && (uint)msg == _taskbarCreatedMessage)
        {
            // explorer.exe restarted and forgot us; claim the work area again.
            _appBar?.ReRegister();
            ApplyPlacement();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_DISPLAYCHANGE:
                // Let WPF and DefWindowProc finish their own repositioning first, then override it.
                Dispatcher.BeginInvoke(new Action(ApplyPlacement));
                break;

            case NativeMethods.WM_WINDOWPOSCHANGED:
                // Skip our own SetWindowPos: notifying the shell about a move we just made
                // invites an ABN_POSCHANGED echo and a move/notify ping-pong.
                if (!_isApplyingPlacement)
                {
                    _appBar?.NotifyWindowPosChanged();
                }

                break;

            case NativeMethods.WM_ACTIVATE:
                _appBar?.NotifyActivated();
                break;

            case NativeMethods.WM_WINDOWPOSCHANGING:
                ClampHeight(lParam);
                break;

            case NativeMethods.WM_THEMECHANGED:
            case NativeMethods.WM_SYSCOLORCHANGE:
            case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                _appearance?.Refresh();
                break;

            case NativeMethods.WM_SETTINGCHANGE:
                if (IsImmersiveColorSet(lParam))
                {
                    _appearance?.Refresh();
                }

                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Keeping a captioned frame (or WindowChrome that leaves WS_CAPTION) also brings the
    /// minimum track height for captioned windows (~39 px), which DefWindowProc will re-impose
    /// even if we shrink cy first. With WindowStyle=None that floor is gone; this clamp still
    /// blocks any other resize source (WPF layout, DPI churn) from growing the strip.
    /// </summary>
    private void ClampHeight(IntPtr lParam)
    {
        if (_source is null || lParam == IntPtr.Zero)
        {
            return;
        }

        WINDOWPOS pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if ((pos.flags & NativeMethods.SWP_NOSIZE) != 0)
        {
            return;
        }

        int heightPx = Display.DipToPixels(BarHeightDip, Display.GetDpi(_source.Handle));
        if (heightPx <= 0 || pos.cy == heightPx)
        {
            return;
        }

        pos.cy = heightPx;
        Marshal.StructureToPtr(pos, lParam, false);
    }

    /// <summary>
    /// Light/dark switches arrive as a WM_SETTINGCHANGE whose lParam names the changed area;
    /// every other setting change also lands here, so the name has to be checked.
    /// </summary>
    private static bool IsImmersiveColorSet(IntPtr lParam)
    {
        return lParam != IntPtr.Zero
            && string.Equals(Marshal.PtrToStringUni(lParam), "ImmersiveColorSet", StringComparison.Ordinal);
    }

    private void HandleAppBarNotification(AppBarNotification notification, IntPtr lParam)
    {
        switch (notification)
        {
            case AppBarNotification.PosChanged:
            case AppBarNotification.StateChange:
                ApplyPlacement();
                break;

            case AppBarNotification.FullScreenApp:
                SetFullScreenAppActive(lParam != IntPtr.Zero);
                break;

            case AppBarNotification.WindowArrange:
                break;
        }
    }

    /// <summary>
    /// A fullscreen app must not be covered by the bar, so we drop out of the topmost band
    /// while one is in the foreground and climb back afterwards.
    /// </summary>
    private void SetFullScreenAppActive(bool isActive)
    {
        if (_isFullScreenAppActive == isActive || _source is null)
        {
            return;
        }

        _isFullScreenAppActive = isActive;

        NativeMethods.SetWindowPos(
            _source.Handle,
            isActive ? NativeMethods.HWND_BOTTOM : NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        FullscreenState.Instance.Set(isActive);
    }
}
