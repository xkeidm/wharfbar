using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WharfBar.Models;
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
    private readonly ConfigService _config;
    private readonly TrayOverflowInvoker _trayOverflow;
    private readonly NotificationCenterInvoker _notifications = new();
    private AppBar? _appBar;
    private WindowAppearance? _appearance;
    private StatusService? _status;
    private VolumeService? _volume;
    private AudioMixerService? _mixer;
    private QuickSettingsService? _quick;
    private HwndSource? _source;
    private uint _taskbarCreatedMessage;
    private bool _isApplyingPlacement;
    private bool _isFullScreenAppActive;
    private bool _isShutdownHooked;
    private QuickSettingsPage _pendingQuickPage = QuickSettingsPage.Main;
    private Dictionary<string, FrameworkElement>? _statusElements;
    private bool _batteryPresent = false;
    private bool _volumeAvailable = true;

    internal TopBarWindow(TaskbarVisibilityService taskbar, ConfigService config)
    {
        _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _trayOverflow = new TrayOverflowInvoker(_taskbar);
        InitializeComponent();

        _statusElements = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            [TopBarStatusItemIds.TrayOverflow] = TrayOverflowButton,
            [TopBarStatusItemIds.Network] = NetworkButton,
            [TopBarStatusItemIds.Battery] = BatteryGroup,
            [TopBarStatusItemIds.Volume] = VolumeButton,
            [TopBarStatusItemIds.DateTime] = DateTimeButton,
            [TopBarStatusItemIds.Notifications] = NotificationsButton,
        };
        ApplyStatusItemLayout();
        StatusStrip.MouseRightButtonUp += OnStatusStripRightClick;
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

        _mixer = new AudioMixerService(_volume);
        _quick = new QuickSettingsService();
        QuickSettingsHost.Attach(_volume, _mixer, _quick);

        _appBar.Register();
        ApplyPlacement();

        TrayOverflowButton.PreviewMouseLeftButtonUp += OnTrayOverflowClick;
        AppMenuButton.PreviewMouseLeftButtonUp += OnAppMenuClick;
        NetworkButton.PreviewMouseLeftButtonUp += OnNetworkClick;
        VolumeButton.PreviewMouseLeftButtonUp += OnVolumeClick;
        DateTimeButton.PreviewMouseLeftButtonUp += OnDateTimeOrNotificationsClick;
        NotificationsButton.PreviewMouseLeftButtonUp += OnDateTimeOrNotificationsClick;
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
        ToggleQuickSettings(QuickSettingsPage.Mixer, VolumeButton);
    }

    private void OnNetworkClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleQuickSettings(QuickSettingsPage.Network, NetworkButton);
    }

    private void ToggleQuickSettings(QuickSettingsPage page, UIElement anchor)
    {
        if (QuickSettingsPopup.IsOpen
            && ReferenceEquals(QuickSettingsPopup.PlacementTarget, anchor)
            && _pendingQuickPage == page)
        {
            QuickSettingsPopup.IsOpen = false;
            return;
        }

        _pendingQuickPage = page;
        QuickSettingsPopup.PlacementTarget = anchor;
        QuickSettingsHost.ShowPage(page);
        QuickSettingsPopup.IsOpen = true;
    }

    private void OnQuickSettingsOpened(object sender, EventArgs e)
    {
        QuickSettingsHost.OnFlyoutOpened(_pendingQuickPage);
    }

    private void OnQuickSettingsClosed(object sender, EventArgs e)
    {
        QuickSettingsHost.OnFlyoutClosed();
    }

    private void OnDateTimeOrNotificationsClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseFlyouts();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _notifications.TryOpen()));
    }

    private void CloseFlyouts()
    {
        QuickSettingsPopup.IsOpen = false;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        CloseFlyouts();
        QuickSettingsHost.Detach();
        _status?.Dispose();
        _status = null;
        _mixer?.Dispose();
        _mixer = null;
        _quick?.Dispose();
        _quick = null;
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
        DateText.Text = snapshot.DateText;
        SetPlainToolTip(DateTimeButton, snapshot.ClockTooltip);

        NetworkGlyph.Text = snapshot.NetworkKind switch
        {
            NetworkKind.WiFi => "\uE701",
            NetworkKind.Ethernet => "\uE839",
            NetworkKind.Other when snapshot.IsNetworkOnline => "\uE774",
            _ => "\uE704",
        };
        SetPlainToolTip(NetworkButton, snapshot.NetworkTooltip);

        _batteryPresent = snapshot.IsBatteryPresent;
        if (snapshot.IsBatteryPresent)
        {
            BatteryText.Text = snapshot.BatteryText;
            SetPlainToolTip(BatteryGroup, snapshot.BatteryTooltip);
            BatteryGlyph.Text = ResolveBatteryGlyph(snapshot);
        }

        ApplyStatusItemLayout();
    }

    private void ApplyVolume(VolumeSnapshot snapshot)
    {
        _volumeAvailable = snapshot.IsAvailable;
        if (snapshot.IsAvailable)
        {
            VolumeGlyph.Text = snapshot.IsMuted || snapshot.Percent == 0
                ? "\uE74F" // Mute
                : snapshot.Percent < 33
                    ? "\uE992" // Volume0 / low
                    : snapshot.Percent < 66
                        ? "\uE993"
                        : "\uE767"; // Volume3

            string tip = snapshot.IsMuted ? "Ses (sessiz)" : $"Ses %{snapshot.Percent}";
            SetPlainToolTip(VolumeButton, tip);
        }
        else if (ReferenceEquals(QuickSettingsPopup.PlacementTarget, VolumeButton))
        {
            QuickSettingsPopup.IsOpen = false;
        }

        ApplyStatusItemLayout();
    }

    private void ApplyStatusItemLayout()
    {
        if (_statusElements is null)
        {
            return;
        }

        IReadOnlyList<string> order = TopBarStatusItemIds.Normalize(
            _config.Current.TopBar?.StatusItems);

        StatusStrip.Children.Clear();
        foreach (string id in order)
        {
            if (_statusElements.TryGetValue(id, out FrameworkElement? el))
            {
                StatusStrip.Children.Add(el);
            }
        }

        foreach (string id in order)
        {
            FrameworkElement el = _statusElements[id];
            bool show = id switch
            {
                TopBarStatusItemIds.Battery => _batteryPresent,
                TopBarStatusItemIds.Volume => _volumeAvailable,
                _ => true,
            };
            el.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnStatusStripRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        FrameworkElement? hit = FindStatusElement(e.OriginalSource as DependencyObject);
        ContextMenu menu = BuildStatusItemsMenu(hit);
        menu.PlacementTarget = StatusStrip;
        menu.IsOpen = true;
    }

    private FrameworkElement? FindStatusElement(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement fe
                && _statusElements is not null
                && _statusElements.Values.Contains(fe))
            {
                return fe;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private ContextMenu BuildStatusItemsMenu(FrameworkElement? hitItem)
    {
        var menu = new ContextMenu();
        List<string> current = TopBarStatusItemIds.Normalize(_config.Current.TopBar.StatusItems);
        var visible = new HashSet<string>(current, StringComparer.Ordinal);

        foreach (string id in TopBarStatusItemIds.DefaultOrder)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = StatusItemHeader(id),
                IsCheckable = true,
                IsChecked = visible.Contains(id),
                Tag = id,
            };
            item.Click += OnToggleStatusItem;
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        if (hitItem is not null
            && TryGetIdForElement(hitItem, out string hitId)
            && visible.Contains(hitId))
        {
            int index = current.IndexOf(hitId);
            var moveLeft = new System.Windows.Controls.MenuItem
            {
                Header = "Sola taşı",
                IsEnabled = index > 0,
                Tag = hitId,
            };
            moveLeft.Click += OnMoveStatusItemLeft;
            var moveRight = new System.Windows.Controls.MenuItem
            {
                Header = "Sağa taşı",
                IsEnabled = index >= 0 && index < current.Count - 1,
                Tag = hitId,
            };
            moveRight.Click += OnMoveStatusItemRight;
            menu.Items.Add(moveLeft);
            menu.Items.Add(moveRight);
            menu.Items.Add(new Separator());
        }

        var reset = new System.Windows.Controls.MenuItem { Header = "Varsayılana sıfırla" };
        reset.Click += OnResetStatusItems;
        menu.Items.Add(reset);
        return menu;
    }

    private static string StatusItemHeader(string id) => id switch
    {
        TopBarStatusItemIds.TrayOverflow => "Gizli simgeler",
        TopBarStatusItemIds.Network => "Ağ",
        TopBarStatusItemIds.Battery => "Pil",
        TopBarStatusItemIds.Volume => "Ses",
        TopBarStatusItemIds.DateTime => "Tarih ve saat",
        TopBarStatusItemIds.Notifications => "Bildirimler",
        _ => id,
    };

    private bool TryGetIdForElement(FrameworkElement el, out string id)
    {
        if (_statusElements is not null)
        {
            foreach (KeyValuePair<string, FrameworkElement> pair in _statusElements)
            {
                if (ReferenceEquals(pair.Value, el))
                {
                    id = pair.Key;
                    return true;
                }
            }
        }

        id = string.Empty;
        return false;
    }

    private void OnToggleStatusItem(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string id, IsChecked: bool isChecked })
        {
            return;
        }

        List<string> items = TopBarStatusItemIds.Normalize(_config.Current.TopBar.StatusItems);
        if (isChecked)
        {
            if (!items.Contains(id, StringComparer.Ordinal))
            {
                items = InsertByDefaultOrder(items, id);
            }
        }
        else
        {
            items.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
            if (items.Count == 0)
            {
                items.Add(TopBarStatusItemIds.DateTime);
            }
        }

        PersistStatusItems(items);
    }

    private static List<string> InsertByDefaultOrder(List<string> current, string id)
    {
        var set = new HashSet<string>(current, StringComparer.Ordinal) { id };
        return TopBarStatusItemIds.DefaultOrder.Where(set.Contains).ToList();
    }

    private void OnMoveStatusItemLeft(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string id })
        {
            return;
        }

        List<string> items = TopBarStatusItemIds.Normalize(_config.Current.TopBar.StatusItems);
        int i = items.IndexOf(id);
        if (i <= 0)
        {
            return;
        }

        (items[i - 1], items[i]) = (items[i], items[i - 1]);
        PersistStatusItems(items);
    }

    private void OnMoveStatusItemRight(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string id })
        {
            return;
        }

        List<string> items = TopBarStatusItemIds.Normalize(_config.Current.TopBar.StatusItems);
        int i = items.IndexOf(id);
        if (i < 0 || i >= items.Count - 1)
        {
            return;
        }

        (items[i + 1], items[i]) = (items[i], items[i + 1]);
        PersistStatusItems(items);
    }

    private void OnResetStatusItems(object sender, RoutedEventArgs e)
    {
        PersistStatusItems(TopBarStatusItemIds.DefaultOrder.ToList());
    }

    private void PersistStatusItems(List<string> items)
    {
        AppConfig cfg = _config.Current;
        cfg.TopBar ??= new TopBarConfig();
        cfg.TopBar.StatusItems = TopBarStatusItemIds.Normalize(items);
        _config.Save(cfg);
        ApplyStatusItemLayout();
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
