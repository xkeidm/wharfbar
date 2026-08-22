using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Image = System.Windows.Controls.Image;
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

    private readonly TopBarEngine _engine;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly ConfigService _config;
    private readonly VeilService _veil;
    private AppBar? _appBar;
    private WindowAppearance? _appearance;
    private HwndSource? _source;
    private uint _taskbarCreatedMessage;
    private bool _isApplyingPlacement;
    private bool _placementQueued;
    private bool _pendingAppBarReregister;
    private RECT _lastApprovedPlacement;
    private bool _haveLastApprovedPlacement;
    private bool _isFullScreenAppActive;
    private bool _isShutdownHooked;
    private bool _isRecreateClose;
    private ExitEventHandler? _onAppExit;
    private SessionEndingCancelEventHandler? _onSessionEnding;
    private DispatcherUnhandledExceptionEventHandler? _onDispatcherUnhandled;
    private EventHandler? _onProcessExit;
    private UnhandledExceptionEventHandler? _onUnhandledException;
    private QuickSettingsPage _pendingQuickPage = QuickSettingsPage.Main;
    private Dictionary<string, FrameworkElement>? _statusElements;
    private bool _batteryPresent = false;
    private bool _volumeAvailable = true;
    private bool _mediaActive;
    private bool _mediaClosePending;
    private bool _trayMirrorActive;
    private readonly Dictionary<string, Image> _trayMirrorImages = new(StringComparer.Ordinal);
    private DispatcherTimer? _tintSaveTimer;
    private int _pendingTintOpacity;

    /// <summary>The monitor this bar is attached to (physical-pixel geometry).</summary>
    internal MonitorInfo Monitor { get; }

    internal TopBarWindow(TopBarEngine engine, ConfigService config, TaskbarVisibilityService taskbar, VeilService veil, MonitorInfo monitor)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _veil = veil ?? throw new ArgumentNullException(nameof(veil));
        Monitor = monitor;

        // Must be set before InitializeComponent — WPF locks AllowsTransparency after creation.
        AllowsTransparency = TopBarBackgroundModes.NeedsTransparency(
            _config.Current.TopBar?.BackgroundMode ?? TopBarBackgroundModes.Mica);
        InitializeComponent();

        _statusElements = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            [TopBarStatusItemIds.TrayMirror] = TrayMirrorHost,
            [TopBarStatusItemIds.TrayOverflow] = TrayOverflowButton,
            [TopBarStatusItemIds.DateTime] = DateTimeButton,
            [TopBarStatusItemIds.Notifications] = NotificationsButton,
            [TopBarStatusItemIds.ControlCenter] = ControlCenterButton,
            [TopBarStatusItemIds.Volume] = VolumeButton,
            [TopBarStatusItemIds.Brightness] = BrightnessButton,
            [TopBarStatusItemIds.Media] = MediaButton,
            [TopBarStatusItemIds.Network] = NetworkButton,
            [TopBarStatusItemIds.Battery] = BatteryGroup,
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

        WindowOverlayStyles.ExcludeFromTaskSwitcher(_source.Handle);

        _source.AddHook(WndProc);

        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        _appBar = new AppBar(
            _source.Handle,
            NativeMethods.RegisterWindowMessage(AppBarCallbackName),
            NativeMethods.ABE_TOP);

        HookShutdownPaths();

        _appearance = new WindowAppearance(this, _source.Handle, Backdrop, Corners);
        WindowAppearance.MainWindowChromeClobbered += OnMainWindowChromeClobbered;
        ApplyBackgroundAppearance();

        _engine.Status.Changed += OnStatusChanged;
        ApplyStatus(_engine.Status.Snapshot);

        _engine.Volume.Changed += OnVolumeChanged;
        ApplyVolume(_engine.Volume.Snapshot);

        QuickSettingsHost.Attach(
            _engine.Volume,
            _engine.Mixer,
            _engine.QuickSettings,
            _engine.Wifi,
            _engine.Bluetooth,
            _engine.Projection);
        BrightnessHost.Attach(_engine.MonitorBrightness);

        _engine.Media.Changed += OnMediaChanged;
        _engine.Media.SessionsChanged += OnMediaSessionsChanged;
        MediaHost.Attach(_engine.Media);

        if (_engine.Media.HasActiveSession)
        {
            // The engine already holds the session (created before this window exists), so no
            // event fires after subscription on create/recreate — apply the initial state so a
            // rebuilt window shows the same media button as the pre-recreate one.
            OnMediaChanged(this, EventArgs.Empty);
        }

        // System tray mirrors only on the primary bar (ADR-009): secondary bars keep the
        // tray section collapsed — _trayMirrorActive stays false there.
        if (Monitor.IsPrimary)
        {
            _engine.TrayMirror.Changed += OnTrayMirrorChanged;
            ApplyTrayMirror(_engine.TrayMirror.Items);
        }

        // The shell binds an AppBar to the monitor the window occupies at ABM_NEW time.
        // Move onto the target monitor first so a secondary bar reserves its own edge.
        RECT home = Monitor.Bounds;
        NativeMethods.SetWindowPos(
            _source.Handle,
            IntPtr.Zero,
            home.Left,
            home.Top,
            home.Width,
            1,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        _appBar.Register();
        ApplyPlacement();

        TrayOverflowButton.PreviewMouseLeftButtonUp += OnTrayOverflowClick;
        AppMenuButton.PreviewMouseLeftButtonUp += OnAppMenuClick;
        ControlCenterButton.PreviewMouseLeftButtonUp += OnControlCenterClick;
        NetworkButton.PreviewMouseLeftButtonUp += OnNetworkClick;
        VolumeButton.PreviewMouseLeftButtonUp += OnVolumeClick;
        BrightnessButton.PreviewMouseLeftButtonUp += OnBrightnessClick;
        MediaButton.PreviewMouseLeftButtonUp += OnMediaClick;
        DateTimeButton.PreviewMouseLeftButtonUp += OnDateTimeOrNotificationsClick;
        NotificationsButton.PreviewMouseLeftButtonUp += OnDateTimeOrNotificationsClick;

        PreviewMouseDown += OnTopBarPreviewMouseDown;
        PreviewMouseRightButtonDown += OnTopBarPreviewMouseDown;
        PreviewKeyDown += OnTopBarPreviewKeyDown;

        // ESC while focus is inside a flyout popup window (e.g. the Wi-Fi password box)
        // routes through the popup's visual tree, not the topbar window.
        QuickSettingsHost.KeyDown += OnFlyoutHostKeyDown;
        BrightnessHost.KeyDown += OnFlyoutHostKeyDown;
        MediaHost.KeyDown += OnFlyoutHostKeyDown;
    }

    /// <summary>
    /// Flyoutlar açıkken topbar'a yapılan tıklamaları kapatma animasyonuyla kapatır;
    /// flyout'u yeniden hedefleyen çapa butonları (QS anchors / brightness / media) ve
    /// popup içeriğine yapılan tıklamalar hariç tutulur — popup içeriği pencerenin görsel
    /// ağacında olduğundan tıklamalar buraya kadar tünel açar; onlar kendi tüketir.
    /// </summary>
    private void OnTopBarPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!QuickSettingsPopup.IsOpen && !BrightnessPopup.IsOpen && !MediaPopup.IsOpen)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject hit)
        {
            if (IsWithin(hit, QuickSettingsHost)
                || IsWithin(hit, BrightnessHost)
                || IsWithin(hit, MediaHost))
            {
                return;
            }

            if (IsWithin(hit, ControlCenterButton)
                || IsWithin(hit, VolumeButton)
                || IsWithin(hit, NetworkButton)
                || IsWithin(hit, BrightnessButton)
                || IsWithin(hit, MediaButton))
            {
                return;
            }
        }

        CloseFlyouts();
    }

    private static bool IsWithin(DependencyObject? node, DependencyObject? target)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, target))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    private void OnTopBarPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        CloseFlyouts();
    }

    private void OnFlyoutHostKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        CloseFlyouts();
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

    /// <summary>
    /// WPF-UI popup'ı gölge payı için ContextMenu'ye 30px'lik effect margin uygular ve popup
    /// penceresi (content+60 genişliğinde) hedefin sol kenarına hizalanır; buton ekranın en
    /// solunda olduğundan pencere ekran kenarına clamp edilir ve görünen menü sağa kayar.
    /// Sol marjı sıfırlayarak pencerenin buton hizasında durmasını ve menünün ikonun sol
    /// kenarına yapışmasını sağlıyoruz; sağ/alt gölge payı korunur.
    /// </summary>
    private void OnAppMenuLoaded(object sender, RoutedEventArgs e)
    {
        ((ContextMenu)sender).Margin = new Thickness(0, 30, 30, 30);
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void OnOpenWindowsSettingsClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:");

    private void OnOpenProfileClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:yourinfo");

    private void OnOpenAboutPcClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:about");

    private void OnLockClick(object sender, RoutedEventArgs e) => PowerActions.TryLock();

    private void OnSleepClick(object sender, RoutedEventArgs e) => PowerActions.TrySleep();

    private void OnShutdownClick(object sender, RoutedEventArgs e) => PowerActions.TryShutdown();

    private void OnRestartClick(object sender, RoutedEventArgs e) => PowerActions.TryRestart();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).ShowSettings();
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
            new Action(() => _engine.TrayOverflow.TryOpen()));
    }

    private void OnVolumeClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleQuickSettings(QuickSettingsPage.Mixer, VolumeButton);
    }

    private void OnControlCenterClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleQuickSettings(QuickSettingsPage.Main, ControlCenterButton);
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
            CloseQuickSettings();
            return;
        }

        CancelPendingClose(QuickSettingsHost, ref _qsClosePending);
        _pendingQuickPage = page;
        QuickSettingsPopup.PlacementTarget = anchor;
        QuickSettingsHost.ShowPage(page);
        QuickSettingsPopup.IsOpen = true;
    }

    private void OnQuickSettingsOpened(object sender, EventArgs e)
    {
        CancelPendingClose(QuickSettingsHost, ref _qsClosePending);
        QuickSettingsHost.OnFlyoutOpened(_pendingQuickPage);
        AnimateFlyoutOpen(QuickSettingsHost);
    }

    private void OnBrightnessPopupOpened(object sender, EventArgs e)
    {
        CancelPendingClose(BrightnessHost, ref _brightnessClosePending);
        BrightnessHost.OnFlyoutOpened();
        AnimateFlyoutOpen(BrightnessHost);
    }

    /// <summary>
    /// Win11-style flyout entrance: content slides down from under the bar while fading in.
    /// The animation restarts cleanly on every open (prior clocks are cleared first).
    /// </summary>
    private static void AnimateFlyoutOpen(FrameworkElement content)
    {
        var transform = content.RenderTransform as TranslateTransform ?? new TranslateTransform(0, -FlyoutSlideDip);
        content.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        transform.Y = -FlyoutSlideDip;
        content.Opacity = 0;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(
            -FlyoutSlideDip,
            0,
            TimeSpan.FromMilliseconds(FlyoutSlideMs))
        {
            EasingFunction = ease,
        });
        content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(
            0,
            1,
            TimeSpan.FromMilliseconds(FlyoutFadeMs))
        {
            EasingFunction = ease,
        });
    }

    /// <summary>
    /// Flyout kapanışı: açılışın tam tersi — içerik yukarı (barın altına) doğru kayarak
    /// kaybolur, açılıştan bir tık hızlıdır. Animasyon bitince popup kapanır.
    /// Bekleyen kapanış, popup yeniden açılırsa <see cref="CancelPendingClose"/> ile iptal edilir
    /// (animasyon clock'ları temizlenince Completed tetiklenmez, yeni popup kapanmaz).
    /// </summary>
    private static void AnimateFlyoutClose(FrameworkElement content, Action onDone)
    {
        var transform = content.RenderTransform as TranslateTransform ?? new TranslateTransform();
        content.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        content.BeginAnimation(UIElement.OpacityProperty, null);

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var slide = new DoubleAnimation(
            0,
            -FlyoutSlideDip,
            TimeSpan.FromMilliseconds(FlyoutCloseSlideMs))
        {
            EasingFunction = ease,
        };
        slide.Completed += (_, _) => onDone();
        transform.BeginAnimation(TranslateTransform.YProperty, slide);
        content.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(
            1,
            0,
            TimeSpan.FromMilliseconds(FlyoutCloseFadeMs))
        {
            EasingFunction = ease,
        });
    }

    private static void CancelPendingClose(FrameworkElement content, ref bool pending)
    {
        pending = false;
        content.BeginAnimation(UIElement.OpacityProperty, null);
        if (content.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = 0;
        }

        content.Opacity = 1;
    }

    private const double FlyoutSlideDip = 10;
    private const double FlyoutSlideMs = 170;
    private const double FlyoutFadeMs = 140;
    private const double FlyoutCloseSlideMs = 110;
    private const double FlyoutCloseFadeMs = 90;

    private bool _qsClosePending;
    private bool _brightnessClosePending;

    private void CloseQuickSettings(bool animate = true)
    {
        if (!QuickSettingsPopup.IsOpen)
        {
            _qsClosePending = false;
            return;
        }

        if (animate && !_qsClosePending)
        {
            _qsClosePending = true;
            AnimateFlyoutClose(QuickSettingsHost, () =>
            {
                _qsClosePending = false;
                QuickSettingsPopup.IsOpen = false;
            });
        }
        else
        {
            CancelPendingClose(QuickSettingsHost, ref _qsClosePending);
            QuickSettingsPopup.IsOpen = false;
        }
    }

    private void CloseBrightness(bool animate = true)
    {
        if (!BrightnessPopup.IsOpen)
        {
            _brightnessClosePending = false;
            return;
        }

        if (animate && !_brightnessClosePending)
        {
            _brightnessClosePending = true;
            AnimateFlyoutClose(BrightnessHost, () =>
            {
                _brightnessClosePending = false;
                BrightnessPopup.IsOpen = false;
            });
        }
        else
        {
            CancelPendingClose(BrightnessHost, ref _brightnessClosePending);
            BrightnessPopup.IsOpen = false;
        }
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
            new Action(() => _engine.Notifications.TryOpen()));
    }

    private void OnBrightnessClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleBrightnessFlyout();
    }

    private void ToggleBrightnessFlyout()
    {
        if (BrightnessPopup.IsOpen)
        {
            CloseBrightness();
            return;
        }

        CancelPendingClose(BrightnessHost, ref _brightnessClosePending);
        CloseFlyouts();
        BrightnessHost.Refresh();
        BrightnessPopup.IsOpen = true;
    }

    private void OnMediaClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleMediaFlyout();
    }

    private void ToggleMediaFlyout()
    {
        if (MediaPopup.IsOpen)
        {
            CloseMedia();
            return;
        }

        CancelPendingClose(MediaHost, ref _mediaClosePending);
        CloseFlyouts();
        MediaPopup.IsOpen = true;
    }

    private void CloseMedia(bool animate = true)
    {
        if (!MediaPopup.IsOpen)
        {
            _mediaClosePending = false;
            return;
        }

        if (animate && !_mediaClosePending)
        {
            _mediaClosePending = true;
            AnimateFlyoutClose(MediaHost, () =>
            {
                _mediaClosePending = false;
                MediaPopup.IsOpen = false;
            });
        }
        else
        {
            CancelPendingClose(MediaHost, ref _mediaClosePending);
            MediaPopup.IsOpen = false;
        }
    }

    private void OnMediaPopupOpened(object sender, EventArgs e)
    {
        CancelPendingClose(MediaHost, ref _mediaClosePending);
        MediaHost.OnFlyoutOpened();
        AnimateFlyoutOpen(MediaHost);
    }

    private void CloseFlyouts(bool animate = true)
    {
        CloseQuickSettings(animate);
        CloseBrightness(animate);
        CloseMedia(animate);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_tintSaveTimer is { IsEnabled: true }
            && TopBarBackgroundModes.NormalizeMode(_config.Current.TopBar?.BackgroundMode)
               == TopBarBackgroundModes.Tint)
        {
            _tintSaveTimer.Stop();
            PersistBackground(TopBarBackgroundModes.Tint, _pendingTintOpacity);
        }

        _tintSaveTimer = null;

        if (_isRecreateClose)
        {
            // AppBar already removed in PrepareForRecreate; dispose services here so AbortRecreate
            // can still recover a prepared-but-failed swap. Never restore taskbar on this path.
            DisposeOwnedServices();
            base.OnClosing(e);
            return;
        }

        CloseFlyouts(animate: false);
        DisposeOwnedServices();
        ReleaseShellState(); // taskbar.Restore + AppBar.Remove
        base.OnClosing(e);
    }

    /// <summary>
    /// Tear down for Mica ↔ translucent window recreate. AppBar remove only — no taskbar restore.
    /// Services stay alive until <see cref="OnClosing"/> so <see cref="AbortRecreate"/> can recover.
    /// </summary>
    internal void PrepareForRecreate()
    {
        _isRecreateClose = true;
        UnhookShutdownPaths();
        CloseFlyouts(animate: false);
        _appBar?.Remove();
    }

    /// <summary>
    /// Re-registers AppBar after a failed recreate. Never restores the Windows taskbar.
    /// </summary>
    internal void AbortRecreate()
    {
        _isRecreateClose = false;
        if (_source is not null)
        {
            // Previous AppBar instance is permanently removed; allocate a fresh one.
            _appBar = new AppBar(
                _source.Handle,
                NativeMethods.RegisterWindowMessage(AppBarCallbackName),
                NativeMethods.ABE_TOP);
            _appBar.Register();
            ApplyPlacement();
        }

        HookShutdownPaths();
    }

    private void DisposeOwnedServices()
    {
        WindowAppearance.MainWindowChromeClobbered -= OnMainWindowChromeClobbered;
        QuickSettingsHost.Detach();
        MediaHost.Detach();
        BrightnessHost.Detach();
        _engine.Status.Changed -= OnStatusChanged;
        _engine.Volume.Changed -= OnVolumeChanged;
        _engine.Media.Changed -= OnMediaChanged;
        _engine.Media.SessionsChanged -= OnMediaSessionsChanged;
        if (Monitor.IsPrimary)
        {
            _engine.TrayMirror.Changed -= OnTrayMirrorChanged;
        }
    }

    private void OnMainWindowChromeClobbered()
    {
        // Dock (or any secondary WindowAppearance.Apply) rewrites MainWindow via WPF-UI
        // RestoreContentBackground — re-assert translucent/mica without recreating the AppBar.
        // Only the current MainWindow reacts: every bar subscribes to the static event, and
        // a secondary re-applying here would raise the event again (mutual recursion).
        if (!ReferenceEquals(Application.Current?.MainWindow, this))
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyBackgroundAppearance();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(ApplyBackgroundAppearance);
        }
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
        else if (ReferenceEquals(QuickSettingsPopup.PlacementTarget, VolumeButton)
            || ReferenceEquals(QuickSettingsPopup.PlacementTarget, ControlCenterButton))
        {
            CloseQuickSettings(animate: false);
        }

        ApplyStatusItemLayout();
    }

    private void OnStatusChanged(object? sender, EventArgs e) => ApplyStatus(_engine.Status.Snapshot);

    private void OnVolumeChanged(object? sender, EventArgs e) => ApplyVolume(_engine.Volume.Snapshot);

    private void OnTrayMirrorChanged(object? sender, EventArgs e) => ApplyTrayMirror(_engine.TrayMirror.Items);

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        _mediaActive = _engine.Media.HasActiveSession;
        SetPlainToolTip(MediaButton, BuildMediaTooltip(_engine.Media.Snapshot));
        ApplyStatusItemLayout();
    }

    private void OnMediaSessionsChanged(object? sender, EventArgs e)
    {
        _mediaActive = _engine.Media.HasActiveSession;
        ApplyStatusItemLayout();
    }

    private static string BuildMediaTooltip(MediaSnapshot snapshot)
    {
        if (!snapshot.HasSession)
        {
            return "Çalan medya";
        }

        if (string.IsNullOrEmpty(snapshot.Title))
        {
            return snapshot.AppName;
        }

        return string.IsNullOrEmpty(snapshot.Artist)
            ? snapshot.Title
            : $"{snapshot.Title} – {snapshot.Artist}";
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
                TopBarStatusItemIds.TrayMirror => _trayMirrorActive,
                TopBarStatusItemIds.Battery => _batteryPresent,
                TopBarStatusItemIds.Volume => _volumeAvailable,
                TopBarStatusItemIds.Media => _mediaActive,
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

        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = "Arka plan",
            IsEnabled = false,
        });

        string mode = TopBarBackgroundModes.NormalizeMode(_config.Current.TopBar.BackgroundMode);
        int opacity = TopBarBackgroundModes.ClampOpacity(_config.Current.TopBar.TintOpacity);

        var radios = new List<System.Windows.Controls.MenuItem>(3);
        radios.Add(CreateBackgroundRadio("Mica", TopBarBackgroundModes.Mica, mode));
        radios.Add(CreateBackgroundRadio("Transparent", TopBarBackgroundModes.Transparent, mode));
        radios.Add(CreateBackgroundRadio("Tint", TopBarBackgroundModes.Tint, mode));

        var opacityHost = new System.Windows.Controls.MenuItem
        {
            StaysOpenOnClick = true,
            IsEnabled = mode == TopBarBackgroundModes.Tint,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = $"Opaklık {opacity}%",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Width = 88,
        });
        var label = (System.Windows.Controls.TextBlock)row.Children[0];
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Width = 120,
            Value = opacity,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, args) =>
        {
            int v = (int)Math.Round(args.NewValue);
            label.Text = $"Opaklık {v}%";
            ScheduleTintOpacitySave(v);
        };
        row.Children.Add(slider);
        opacityHost.Header = row;

        foreach (System.Windows.Controls.MenuItem radio in radios)
        {
            string radioMode = (string)radio.Tag!;
            radio.Click += (_, _) =>
            {
                SetBackgroundMode(radioMode);
                // Recreate destroys this menu; otherwise keep radios exclusive + slider enable in sync.
                SyncBackgroundMenuUi(
                    radios,
                    opacityHost,
                    TopBarBackgroundModes.NormalizeMode(_config.Current.TopBar.BackgroundMode));
            };
            menu.Items.Add(radio);
        }

        menu.Items.Add(opacityHost);

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

    private static System.Windows.Controls.MenuItem CreateBackgroundRadio(
        string header,
        string mode,
        string current)
    {
        return new System.Windows.Controls.MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = current == mode,
            StaysOpenOnClick = true,
            Tag = mode,
        };
    }

    private static void SyncBackgroundMenuUi(
        IReadOnlyList<System.Windows.Controls.MenuItem> radios,
        System.Windows.Controls.MenuItem opacityHost,
        string mode)
    {
        foreach (System.Windows.Controls.MenuItem radio in radios)
        {
            radio.IsChecked = string.Equals(radio.Tag as string, mode, StringComparison.Ordinal);
        }

        opacityHost.IsEnabled = mode == TopBarBackgroundModes.Tint;
    }

    private static string StatusItemHeader(string id) => id switch
    {
        TopBarStatusItemIds.TrayMirror => "Tepsi simgeleri",
        TopBarStatusItemIds.TrayOverflow => "Gizli simgeler",
        TopBarStatusItemIds.DateTime => "Tarih ve saat",
        TopBarStatusItemIds.Notifications => "Bildirimler",
        TopBarStatusItemIds.ControlCenter => "Hızlı ayarlar",
        TopBarStatusItemIds.Volume => "Ses",
        TopBarStatusItemIds.Network => "Ağ",
        TopBarStatusItemIds.Battery => "Pil",
        TopBarStatusItemIds.Media => "Medya",
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

    private void ApplyBackgroundAppearance()
    {
        if (_appearance is null)
        {
            return;
        }

        TopBarConfig top = _config.Current.TopBar ?? new TopBarConfig();
        string mode = TopBarBackgroundModes.NormalizeMode(top.BackgroundMode);
        switch (mode)
        {
            case TopBarBackgroundModes.Transparent:
                _appearance.ApplyTranslucent(null);
                break;
            case TopBarBackgroundModes.Tint:
                _appearance.ApplyTranslucent(TopBarBackgroundModes.ClampOpacity(top.TintOpacity));
                break;
            default:
                _appearance.Apply();
                break;
        }
    }

    /// <summary>
    /// Re-applies all config-driven visuals. Called by settings pages after config is saved.
    /// </summary>
    internal void ApplyConfigChanges()
    {
        ApplyStatusItemLayout();
        ApplyBackgroundAppearance();
    }

    private void PersistBackground(string mode, int? opacity = null)
    {
        AppConfig cfg = _config.Current;
        cfg.TopBar ??= new TopBarConfig();
        cfg.TopBar.BackgroundMode = TopBarBackgroundModes.NormalizeMode(mode);
        if (opacity is not null)
        {
            cfg.TopBar.TintOpacity = TopBarBackgroundModes.ClampOpacity(opacity.Value);
        }

        _config.Save(cfg);
    }

    private void SetBackgroundMode(string mode)
    {
        _tintSaveTimer?.Stop();
        string normalized = TopBarBackgroundModes.NormalizeMode(mode);
        bool needTx = TopBarBackgroundModes.NeedsTransparency(normalized);
        PersistBackground(normalized);

        if (needTx != AllowsTransparency)
        {
            ((App)Application.Current).RecreateTopBar();
            return;
        }

        ApplyBackgroundAppearance();
    }

    private void SetTintOpacity(int opacity)
    {
        PersistBackground(TopBarBackgroundModes.Tint, opacity);
        if (AllowsTransparency
            && TopBarBackgroundModes.NormalizeMode(_config.Current.TopBar.BackgroundMode)
               == TopBarBackgroundModes.Tint)
        {
            _appearance?.ApplyTranslucent(TopBarBackgroundModes.ClampOpacity(opacity));
        }
    }

    private void ScheduleTintOpacitySave(int opacity)
    {
        _pendingTintOpacity = TopBarBackgroundModes.ClampOpacity(opacity);
        // Live preview immediately; debounce disk Save.
        if (AllowsTransparency
            && TopBarBackgroundModes.NormalizeMode(_config.Current.TopBar.BackgroundMode)
               == TopBarBackgroundModes.Tint)
        {
            _appearance?.ApplyTranslucent(_pendingTintOpacity);
        }

        if (_tintSaveTimer is null)
        {
            _tintSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _tintSaveTimer.Tick += (_, _) =>
            {
                _tintSaveTimer.Stop();
                if (TopBarBackgroundModes.NormalizeMode(_config.Current.TopBar?.BackgroundMode)
                    == TopBarBackgroundModes.Tint)
                {
                    PersistBackground(TopBarBackgroundModes.Tint, _pendingTintOpacity);
                }
            };
        }

        _tintSaveTimer.Stop();
        _tintSaveTimer.Start();
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
            FontFamily = SystemVersion.TextFontFamily,
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

        _onAppExit = (_, _) => ReleaseShellState();
        _onSessionEnding = (_, _) => ReleaseShellState();
        _onDispatcherUnhandled = (_, _) => ReleaseShellState();
        _onProcessExit = (_, _) => ReleaseShellState();
        _onUnhandledException = (_, _) => ReleaseShellState();

        if (Application.Current is { } app)
        {
            app.Exit += _onAppExit;
            app.SessionEnding += _onSessionEnding;
            app.DispatcherUnhandledException += _onDispatcherUnhandled;
        }

        AppDomain.CurrentDomain.ProcessExit += _onProcessExit;
        AppDomain.CurrentDomain.UnhandledException += _onUnhandledException;
    }

    private void UnhookShutdownPaths()
    {
        if (!_isShutdownHooked)
        {
            return;
        }

        if (Application.Current is { } app)
        {
            if (_onAppExit is not null)
            {
                app.Exit -= _onAppExit;
            }

            if (_onSessionEnding is not null)
            {
                app.SessionEnding -= _onSessionEnding;
            }

            if (_onDispatcherUnhandled is not null)
            {
                app.DispatcherUnhandledException -= _onDispatcherUnhandled;
            }
        }

        if (_onProcessExit is not null)
        {
            AppDomain.CurrentDomain.ProcessExit -= _onProcessExit;
        }

        if (_onUnhandledException is not null)
        {
            AppDomain.CurrentDomain.UnhandledException -= _onUnhandledException;
        }

        _onAppExit = null;
        _onSessionEnding = null;
        _onDispatcherUnhandled = null;
        _onProcessExit = null;
        _onUnhandledException = null;
        _isShutdownHooked = false;
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
            RECT monitor = Display.GetMonitorBounds(hwnd);

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

            RECT approved = _appBar.Query(desired);
            bool needSetPos = AppBarPlacement.NeedsShellSetPos(
                _haveLastApprovedPlacement,
                _lastApprovedPlacement,
                approved);
            if (needSetPos)
            {
                approved = _appBar.CommitPos(approved);
            }

            bool placementChanged = !_haveLastApprovedPlacement
                || !AppBarPlacement.RectsEqual(_lastApprovedPlacement, approved);
            // Commit before SetWindowPos so WM_WINDOWPOSCHANGING clamp uses this rect
            // (WPF otherwise shoves the bar into the work area when the phantom strip returns).
            _lastApprovedPlacement = approved;
            _haveLastApprovedPlacement = true;

            bool windowMatches = NativeMethods.GetWindowRect(hwnd, out RECT current)
                && AppBarPlacement.RectsEqual(current, approved);
            if (needSetPos || !windowMatches)
            {
                NativeMethods.SetWindowPos(
                    hwnd,
                    _isFullScreenAppActive ? NativeMethods.HWND_BOTTOM : NativeMethods.HWND_TOPMOST,
                    approved.Left,
                    approved.Top,
                    approved.Width,
                    approved.Height,
                    NativeMethods.SWP_NOACTIVATE);
            }

            // ABM_SETPOS can restore the phantom taskbar strip; keep-hidden re-hides
            // Shell_TrayWnd. Do not ReapplyHide here — hide+SETPOS → ABN_POSCHANGED loops
            // stall the UI thread (wait cursor on the bar) and snap the dock back off-screen.
            // Skip SETPOS when QUERYPOS matches the last commit or explorer's hide/show
            // of Shell_TrayWnd retriggers ABN_POSCHANGED forever (wait cursor, dead clicks).
            _taskbar.EnsureWorkAreaExpanded();

            if (placementChanged)
            {
                WorkAreaChanged.Notify();
            }
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
            QueueApplyPlacement(reRegister: true);
            _engine.TrayMirror.Reenumerate();
            ((App)Application.Current).Monitors?.Refresh();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_DISPLAYCHANGE:
                QueueApplyPlacement();
                _ = Dispatcher.BeginInvoke(new Action(() => _engine.MonitorBrightness.RefreshMonitors()));
                break;

            case NativeMethods.WM_WINDOWPOSCHANGED:
                if (_source is not null
                    && NativeMethods.GetWindowRect(_source.Handle, out RECT current)
                    && AppBarPlacement.ShouldNotifyShellWindowPosChanged(
                        _isApplyingPlacement,
                        _haveLastApprovedPlacement,
                        current,
                        _lastApprovedPlacement))
                {
                    _appBar?.NotifyWindowPosChanged();
                }

                break;

            case NativeMethods.WM_ACTIVATE:
                _appBar?.NotifyActivated();
                break;

            case NativeMethods.WM_ACTIVATEAPP:
                // App losing activation (desktop/other-app/taskbar click, Alt+Tab): close
                // flyouts with their close animation instead of the instant popup teardown.
                if (wParam == IntPtr.Zero)
                {
                    CloseFlyouts();
                }

                break;

            case NativeMethods.WM_WINDOWPOSCHANGING:
                ClampToApprovedPlacement(lParam);
                break;

            case NativeMethods.WM_THEMECHANGED:
            case NativeMethods.WM_SYSCOLORCHANGE:
            case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                ApplyBackgroundAppearance();
                break;

            case NativeMethods.WM_SETTINGCHANGE:
                if (IsImmersiveColorSet(lParam))
                {
                    ApplyBackgroundAppearance();
                }

                break;
        }

        return IntPtr.Zero;
    }

    private void ClampToApprovedPlacement(IntPtr lParam)
    {
        if (_source is null || lParam == IntPtr.Zero)
        {
            return;
        }

        WINDOWPOS pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if (_haveLastApprovedPlacement
            && NativeMethods.GetWindowRect(_source.Handle, out RECT current)
            && AppBarPlacement.TryAlignToRect(
                pos.x,
                pos.y,
                pos.cx,
                pos.cy,
                pos.flags,
                current.Left,
                current.Top,
                current.Width,
                current.Height,
                _lastApprovedPlacement,
                out int nx,
                out int ny,
                out int ncx,
                out int ncy,
                out uint nflags))
        {
            pos.x = nx;
            pos.y = ny;
            pos.cx = ncx;
            pos.cy = ncy;
            pos.flags = nflags;
            Marshal.StructureToPtr(pos, lParam, false);
            return;
        }

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

    /// <summary>
    /// Defers shell AppBar work off explorer's SendMessage. SHAppBarMessage inside
    /// ABN_POSCHANGED deadlocks (explorer waits for us, we wait for explorer) and the
    /// bar shows the wait cursor with dead clicks.
    /// </summary>
    internal void QueueApplyPlacement(bool reRegister = false)
    {
        if (reRegister)
        {
            _pendingAppBarReregister = true;
        }

        if (_placementQueued)
        {
            return;
        }

        _placementQueued = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _placementQueued = false;
            if (_pendingAppBarReregister)
            {
                _pendingAppBarReregister = false;
                _appBar?.ReRegister();
                _taskbar.ReapplyHide();
            }

            ApplyPlacement();
        });
    }

    private void HandleAppBarNotification(AppBarNotification notification, IntPtr lParam)
    {
        switch (notification)
        {
            case AppBarNotification.PosChanged:
            case AppBarNotification.StateChange:
                QueueApplyPlacement();
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

    private void ApplyTrayMirror(IReadOnlyList<TrayMirrorItem> items)
    {
        _trayMirrorActive = items.Count > 0;

        var nextKeys = new HashSet<string>(items.Select(static i => i.Key), StringComparer.Ordinal);
        var stale = _trayMirrorImages.Keys.Where(k => !nextKeys.Contains(k)).ToList();
        foreach (string key in stale)
        {
            TrayMirrorItems.Items.Remove(_trayMirrorImages[key]);
            _trayMirrorImages.Remove(key);
        }

        foreach (TrayMirrorItem item in items)
        {
            if (_trayMirrorImages.TryGetValue(item.Key, out Image? existing))
            {
                // Icons can change in place (badges) — refresh Source on every poll.
                existing.Source = item.Icon;
                existing.Tag = item;
                SetPlainToolTip(existing, item.Tooltip);
            }
            else
            {
                var image = new Image
                {
                    Source = item.Icon,
                    Tag = item,
                    Width = 16,
                    Height = 16,
                    // Real tray captures can be 16–24px; scale into the fixed box (up/down).
                    Stretch = Stretch.Uniform,
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(3, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                image.MouseDown += OnTrayMirrorItemMouseDown;
                image.MouseRightButtonUp += OnTrayMirrorItemRightUp;
                SetPlainToolTip(image, item.Tooltip);
                _trayMirrorImages[item.Key] = image;
                TrayMirrorItems.Items.Add(image);
            }
        }

        ApplyStatusItemLayout();
    }

    private void OnTrayMirrorItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image { Tag: TrayMirrorItem item })
        {
            return;
        }

        e.Handled = true;
        // Çift tık: ikinci fiziksel tıklamada ClickCount==2 (MouseLeftButtonDown UIElement'te
        // yok; MouseDown üzerinden ClickCount ile ayrıştırılır). Sıra: down/up, down/up, dblclk/up.
        if (e.ChangedButton == MouseButton.Left && e.ClickCount >= 2)
        {
            ForwardTrayClick(item, TrayClick.DoubleLeft);
        }
        else
        {
            ForwardTrayClick(item, e.ChangedButton == MouseButton.Right ? TrayClick.Right : TrayClick.Left);
        }
    }

    private void OnTrayMirrorItemRightUp(object sender, MouseButtonEventArgs e)
    {
        // Blocks the status-strip context menu when the click landed on a tray icon.
        if (sender is Image { Tag: TrayMirrorItem })
        {
            e.Handled = true;
        }
    }

    private void ForwardTrayClick(TrayMirrorItem item, TrayClick kind)
    {
        // Routes by source: classic toolbar (TB_*) or UIA fallback (25H2+).
        _engine.TrayMirror.Click(item, kind);
    }
}
