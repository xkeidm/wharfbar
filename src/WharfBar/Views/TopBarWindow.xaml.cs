using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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

    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;
    private const DwmCornerPreference Corners = DwmCornerPreference.DoNotRound;

    private readonly TaskbarVisibilityService _taskbar;
    private readonly TrayOverflowInvoker _trayOverflow;
    private readonly NotificationCenterInvoker _notifications = new();
    private AppBar? _appBar;
    private WindowAppearance? _appearance;
    private StatusService? _status;
    private VolumeService? _volume;
    private HwndSource? _source;
    private uint _taskbarCreatedMessage;
    private bool _isApplyingPlacement;
    private bool _isFullScreenAppActive;
    private bool _isShutdownHooked;
    private bool _volumeSliderUpdating;

    internal TopBarWindow(TaskbarVisibilityService taskbar)
    {
        _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
        _trayOverflow = new TrayOverflowInvoker(_taskbar);
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

        _status = new StatusService();
        _status.Changed += (_, _) => ApplyStatus(_status.Snapshot);
        ApplyStatus(_status.Snapshot);

        _volume = new VolumeService();
        _volume.Changed += (_, _) => ApplyVolume(_volume.Snapshot);
        ApplyVolume(_volume.Snapshot);

        _appBar.Register();
        ApplyPlacement();

        TrayOverflowButton.PreviewMouseLeftButtonUp += OnTrayOverflowClick;
        AppMenuButton.PreviewMouseLeftButtonUp += OnAppMenuClick;
        VolumeButton.PreviewMouseLeftButtonUp += OnVolumeClick;
        DateButton.PreviewMouseLeftButtonUp += OnDateTimeClick;
        ClockButton.PreviewMouseLeftButtonUp += OnDateTimeClick;
        NotificationsButton.PreviewMouseLeftButtonUp += OnNotificationsClick;
    }

    private void OnAppMenuClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseFlyouts();
        if (AppMenuButton.ContextMenu is not { } menu)
        {
            return;
        }

        menu.PlacementTarget = AppMenuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        Application.Current?.Shutdown();
    }

    private void OnTrayOverflowClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseFlyouts();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _trayOverflow.TryOpen()));
    }

    private void OnVolumeClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        DateTimePopup.IsOpen = false;
        if (_volume is null || !_volume.Snapshot.IsAvailable)
        {
            return;
        }

        ApplyVolume(_volume.Snapshot);
        VolumePopup.PlacementTarget = VolumeButton;
        VolumePopup.IsOpen = !VolumePopup.IsOpen;
    }

    private void OnDateTimeClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        VolumePopup.IsOpen = false;
        if (_status is not null)
        {
            FlyoutClockText.Text = _status.Snapshot.ClockText;
            FlyoutDateText.Text = _status.Snapshot.DateTooltip;
            FlyoutCalendar.SelectedDate = DateTime.Today;
            FlyoutCalendar.DisplayDate = DateTime.Today;
        }

        DateTimePopup.PlacementTarget = sender as UIElement ?? ClockButton;
        DateTimePopup.IsOpen = !DateTimePopup.IsOpen;
    }

    private void OnNotificationsClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseFlyouts();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _notifications.TryOpen()));
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        _volume?.ToggleMute();
    }

    private void OnVolumeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volumeSliderUpdating || _volume is null)
        {
            return;
        }

        _volume.SetPercent((int)Math.Round(e.NewValue));
    }

    private void CloseFlyouts()
    {
        VolumePopup.IsOpen = false;
        DateTimePopup.IsOpen = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CloseFlyouts();
        _status?.Dispose();
        _status = null;
        _volume?.Dispose();
        _volume = null;

        ReleaseShellState();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WndProc);
        _source = null;
        base.OnClosed(e);
    }

    private void ApplyStatus(StatusSnapshot snapshot)
    {
        ClockText.Text = snapshot.ClockText;
        SetPlainToolTip(ClockButton, snapshot.ClockTooltip);

        DateText.Text = snapshot.DateText;
        SetPlainToolTip(DateButton, snapshot.DateTooltip);

        if (DateTimePopup.IsOpen)
        {
            FlyoutClockText.Text = snapshot.ClockText;
            FlyoutDateText.Text = snapshot.DateTooltip;
        }

        NetworkGlyph.Text = snapshot.NetworkKind switch
        {
            NetworkKind.WiFi => "\uE701",
            NetworkKind.Ethernet => "\uE839",
            NetworkKind.Other when snapshot.IsNetworkOnline => "\uE774",
            _ => "\uE704",
        };
        SetPlainToolTip(NetworkButton, snapshot.NetworkTooltip);

        if (!snapshot.IsBatteryPresent)
        {
            BatteryGroup.Visibility = Visibility.Collapsed;
            return;
        }

        BatteryGroup.Visibility = Visibility.Visible;
        BatteryText.Text = snapshot.BatteryText;
        SetPlainToolTip(BatteryGroup, snapshot.BatteryTooltip);
        BatteryGlyph.Text = ResolveBatteryGlyph(snapshot);
    }

    private void ApplyVolume(VolumeSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            VolumeButton.Visibility = Visibility.Collapsed;
            VolumePopup.IsOpen = false;
            return;
        }

        VolumeButton.Visibility = Visibility.Visible;
        VolumeGlyph.Text = snapshot.IsMuted || snapshot.Percent == 0
            ? "\uE74F" // Mute
            : snapshot.Percent < 33
                ? "\uE992" // Volume0 / low
                : snapshot.Percent < 66
                    ? "\uE993"
                    : "\uE767"; // Volume3

        string tip = snapshot.IsMuted ? "Ses (sessiz)" : $"Ses %{snapshot.Percent}";
        SetPlainToolTip(VolumeButton, tip);

        _volumeSliderUpdating = true;
        try
        {
            VolumeSlider.Value = snapshot.Percent;
            VolumePercentText.Text = $"{snapshot.Percent}%";
            MuteButton.Content = snapshot.IsMuted ? "\uE74F" : "\uE767";
            MuteButton.FontFamily = new FontFamily("Segoe MDL2 Assets");
            MuteButton.ToolTip = new ToolTip
            {
                Content = snapshot.IsMuted ? "Sesi aç" : "Sessiz",
                FontFamily = new FontFamily("Segoe UI"),
            };
        }
        finally
        {
            _volumeSliderUpdating = false;
        }
    }

    private static void SetPlainToolTip(FrameworkElement element, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            element.ToolTip = null;
            return;
        }

        element.ToolTip = new ToolTip
        {
            Content = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
        };
    }

    private static string ResolveBatteryGlyph(StatusSnapshot snapshot)
    {
        if (snapshot.IsBatteryCharging)
        {
            return "\uE83E";
        }

        return snapshot.BatteryPercent switch
        {
            <= 5 => "\uE850",
            <= 25 => "\uE851",
            <= 50 => "\uE852",
            <= 75 => "\uE853",
            _ => "\uE83F",
        };
    }

    private void HookShutdownPaths()
    {
        if (_isShutdownHooked)
        {
            return;
        }

        _isShutdownHooked = true;

        if (Application.Current is { } app)
        {
            app.Exit += (_, _) => ReleaseShellState();
            app.SessionEnding += (_, _) => ReleaseShellState();
            app.DispatcherUnhandledException += (_, _) => ReleaseShellState();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseShellState();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseShellState();
    }

    private void ReleaseShellState()
    {
        _taskbar.Restore();
        _appBar?.Remove();
    }

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
            _appBar?.ReRegister();
            _taskbar.ReapplyHide();
            ApplyPlacement();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(new Action(ApplyPlacement));
                break;

            case NativeMethods.WM_WINDOWPOSCHANGED:
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
