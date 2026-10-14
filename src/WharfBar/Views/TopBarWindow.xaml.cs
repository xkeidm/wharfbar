using System.Globalization;
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
using WharfBar.Resources;
using WharfBar.Services;
using Image = System.Windows.Controls.Image;
using Wpf.Ui.Controls;

namespace WharfBar.Views;

/// <summary>
/// The top bar across the primary monitor.
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
    private bool _clockClosePending;
    private StatsHoverCard? _statsHover;
    private bool _trayMirrorActive;
    private readonly Dictionary<string, Border> _trayMirrorImages = new(StringComparer.Ordinal);
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
        StatusStrip.PreviewMouseLeftButtonDown += OnStatusStripDragDown;
        StatusStrip.PreviewMouseMove += OnStatusStripDragMove;
        StatusStrip.PreviewMouseLeftButtonUp += OnStatusStripDragUp;
        StatusStrip.PreviewMouseRightButtonDown += OnStatusStripDragCancelRight;
        StatusStrip.LostMouseCapture += OnStatusStripDragLostCapture;
        _statsHover = new StatsHoverCard();
        SystemStatsText.ToolTip = new ToolTip { Content = _statsHover };
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

        _engine.SystemStats.Changed += OnSystemStatsChanged;
        ApplySystemStatsVisibility();
        ApplySystemStats(_engine.SystemStats.Snapshot);

        _engine.Volume.Changed += OnVolumeChanged;
        ApplyVolume(_engine.Volume.Snapshot);

        QuickSettingsHost.Attach(
            _engine.Volume,
            _engine.Mixer,
            _engine.QuickSettings,
            _engine.Wifi,
            _engine.Bluetooth,
            _engine.Projection,
            _engine.NetworkDetails);
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

        AttachIsland();

        // System tray mirrors only on the primary bar (ADR-009): secondary bars keep the
        // tray section collapsed — _trayMirrorActive stays false there.
        if (Monitor.IsPrimary)
        {
            _engine.TrayMirror.Changed += OnTrayMirrorChanged;
            ApplyTrayMirror(_engine.TrayMirror.Items);
            AttachTrayOverflowBox();
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
        DateTimeButton.PreviewMouseLeftButtonUp += OnDateTimeClick;
        NotificationsButton.PreviewMouseLeftButtonUp += OnNotificationsClick;
        ClockPanelHost.OpenCenterRequested += OnClockPanelOpenCenter;

        PreviewMouseDown += OnTopBarPreviewMouseDown;
        PreviewMouseRightButtonDown += OnTopBarPreviewMouseDown;
        PreviewKeyDown += OnTopBarPreviewKeyDown;

        // ESC while focus is inside a flyout popup window (e.g. the Wi-Fi password box)
        // routes through the popup's visual tree, not the topbar window.
        QuickSettingsHost.KeyDown += OnFlyoutHostKeyDown;
        BrightnessHost.KeyDown += OnFlyoutHostKeyDown;
        MediaHost.KeyDown += OnFlyoutHostKeyDown;
        ClockPanelHost.KeyDown += OnFlyoutHostKeyDown;
    }

    /// <summary>
    /// Flyoutlar açıkken topbar'a yapılan tıklamaları kapatma animasyonuyla kapatır;
    /// flyout'u yeniden hedefleyen çapa butonları (QS anchors / brightness / media) ve
    /// popup içeriğine yapılan tıklamalar hariç tutulur — popup içeriği pencerenin görsel
    /// ağacında olduğundan tıklamalar buraya kadar tünel açar; onlar kendi tüketir.
    /// </summary>
    private void OnTopBarPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!QuickSettingsPopup.IsOpen && !BrightnessPopup.IsOpen && !MediaPopup.IsOpen
            && !IslandPopup.IsOpen && !ClockPopup.IsOpen && !TrayOverflowPopup.IsOpen)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject hit)
        {
            if (IsWithin(hit, QuickSettingsHost)
                || IsWithin(hit, BrightnessHost)
                || IsWithin(hit, MediaHost)
                || IsWithin(hit, IslandHost)
                || IsWithin(hit, ClockPanelHost)
                || IsWithin(hit, TrayOverflowHost))
            {
                return;
            }

            if (IsWithin(hit, ControlCenterButton)
                || IsWithin(hit, VolumeButton)
                || IsWithin(hit, NetworkButton)
                || IsWithin(hit, BrightnessButton)
                || IsWithin(hit, MediaButton)
                || IsWithin(hit, IslandPill)
                || IsWithin(hit, DateTimeButton)
                || IsWithin(hit, TrayOverflowButton))
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
        if (_statusDragActive)
        {
            EndStatusDragVisuals();
            return;
        }

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
        if (AppMenu.IsOpen)
        {
            AppMenu.IsOpen = false;
            return;
        }

        // Open after this click finishes. Opening in the same mouse-up lets the
        // click land outside the menu and dismiss it immediately.
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            AppMenu.PlacementTarget = AppMenuButton;
            AppMenu.Placement = PlacementMode.Bottom;
            AppMenu.IsOpen = true;
        });
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
        if (!Monitor.IsPrimary)
        {
            CloseFlyouts();
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() => _engine.TrayOverflow.TryOpen()));
            return;
        }

        ToggleTrayOverflowBox();
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

        CloseIsland();
        CloseBrightness();
        CloseMedia();
        CloseClockPanel();
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
        if (_pendingQuickPage == QuickSettingsPage.Mixer)
        {
            NoteIslandVolume();
        }
    }

    private void OnBrightnessPopupOpened(object sender, EventArgs e)
    {
        CancelPendingClose(BrightnessHost, ref _brightnessClosePending);
        BrightnessHost.OnFlyoutOpened();
        AnimateFlyoutOpen(BrightnessHost);
        NoteIslandBrightness(apply: true);
    }

    /// <summary>
    /// Win11-style flyout entrance: content slides down from under the bar while fading in.
    /// The island uses a shorter rise plus a slight scale so the panel feels attached to the chip.
    /// The animation restarts cleanly on every open (prior clocks are cleared first).
    /// </summary>
    private static void AnimateFlyoutOpen(FrameworkElement content, bool fromChip = false)
    {
        if (fromChip)
        {
            AnimateChipFlyout(content, opening: true, onDone: null, fromScaleX: 0.96, fromScaleY: 0.96);
            return;
        }

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
    private static void AnimateFlyoutClose(FrameworkElement content, Action onDone, bool fromChip = false)
    {
        if (fromChip)
        {
            AnimateChipFlyout(content, opening: false, onDone, fromScaleX: 0.96, fromScaleY: 0.96);
            return;
        }

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

    /// <summary>
    /// Grows from the top center with a fade. Horizontal scale starts at
    /// <paramref name="fromScaleX"/>, vertical scale at <paramref name="fromScaleY"/>.
    /// The top edge does not rise. Close reverses and is faster.
    /// </summary>
    /// <param name="fadeTarget">
    /// Opacity host when it must not be <paramref name="content"/>. The island scales an
    /// inner surface and fades the popup child: a scale on the popup child moves WPF's
    /// placement corners.
    /// </param>
    /// <param name="motion">When set, replaces the default open/close duration. Island passes zero for reduced motion.</param>
    private static void AnimateChipFlyout(
        FrameworkElement content,
        bool opening,
        Action? onDone,
        double fromScaleX,
        double fromScaleY,
        FrameworkElement? fadeTarget = null,
        TimeSpan? motion = null)
    {
        FrameworkElement fade = fadeTarget ?? content;
        content.RenderTransformOrigin = new Point(0.5, 0);
        (ScaleTransform scale, TranslateTransform move) = ChipMotion(content);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        move.BeginAnimation(TranslateTransform.YProperty, null);
        move.Y = 0;
        fade.BeginAnimation(UIElement.OpacityProperty, null);

        double startX = opening ? fromScaleX : 1;
        double endX = opening ? 1 : fromScaleX;
        double startY = opening ? fromScaleY : 1;
        double endY = opening ? 1 : fromScaleY;
        double fromOpacity = opening ? 0 : 1;
        double toOpacity = opening ? 1 : 0;
        var duration = motion ?? TimeSpan.FromMilliseconds(opening ? ChipFlyoutOpenMs : ChipFlyoutCloseMs);
        var ease = new CubicEase { EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn };

        scale.ScaleX = startX;
        scale.ScaleY = startY;
        fade.Opacity = fromOpacity;

        var scaleX = new DoubleAnimation(startX, endX, duration) { EasingFunction = ease };
        if (onDone is not null)
        {
            scaleX.Completed += (_, _) => onDone();
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(startY, endY, duration)
        {
            EasingFunction = ease,
        });
        fade.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromOpacity, toOpacity, duration)
        {
            EasingFunction = ease,
        });
    }

    private static (ScaleTransform Scale, TranslateTransform Move) ChipMotion(FrameworkElement content)
    {
        if (content.RenderTransform is TransformGroup group
            && group.Children.Count >= 2
            && group.Children[0] is ScaleTransform scale
            && group.Children[1] is TranslateTransform move)
        {
            return (scale, move);
        }

        var createdScale = new ScaleTransform(1, 1);
        var createdMove = new TranslateTransform(0, 0);
        var created = new TransformGroup();
        created.Children.Add(createdScale);
        created.Children.Add(createdMove);
        content.RenderTransform = created;
        return (createdScale, createdMove);
    }

    private static void CancelPendingClose(FrameworkElement content, ref bool pending)
    {
        pending = false;
        content.BeginAnimation(UIElement.OpacityProperty, null);
        if (content.RenderTransform is TransformGroup group)
        {
            foreach (Transform child in group.Children)
            {
                if (child is ScaleTransform scale)
                {
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = 1;
                    scale.ScaleY = 1;
                }
                else if (child is TranslateTransform move)
                {
                    move.BeginAnimation(TranslateTransform.YProperty, null);
                    move.Y = 0;
                }
            }
        }
        else if (content.RenderTransform is TranslateTransform transform)
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
    private const double ChipFlyoutOpenMs = 200;
    private const double ChipFlyoutCloseMs = 140;

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

    private void OnDateTimeClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleClockPanel();
    }

    private void OnNotificationsClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseFlyouts();
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _engine.Notifications.TryOpen()));
    }

    private void OnClockPanelOpenCenter(object? sender, EventArgs e)
    {
        CloseClockPanel(animate: false);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() => _engine.Notifications.TryOpen()));
    }

    private void ToggleClockPanel()
    {
        if (ClockPopup.IsOpen)
        {
            CloseClockPanel();
            return;
        }

        CancelPendingClose(ClockPanelHost, ref _clockClosePending);
        CloseFlyouts();
        ClockPopup.PlacementTarget = DateTimeButton;
        ClockPopup.IsOpen = true;
    }

    private void CloseClockPanel(bool animate = true)
    {
        if (!ClockPopup.IsOpen)
        {
            _clockClosePending = false;
            return;
        }

        if (animate && !_clockClosePending)
        {
            _clockClosePending = true;
            AnimateFlyoutClose(ClockPanelHost, () =>
            {
                _clockClosePending = false;
                ClockPopup.IsOpen = false;
            });
        }
        else
        {
            CancelPendingClose(ClockPanelHost, ref _clockClosePending);
            ClockPopup.IsOpen = false;
        }
    }

    private void OnClockPopupOpened(object sender, EventArgs e)
    {
        CancelPendingClose(ClockPanelHost, ref _clockClosePending);
        ClockPanelHost.OnFlyoutOpened();
        AnimateFlyoutOpen(ClockPanelHost);
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
        BrightnessPopup.PlacementTarget = BrightnessButton;
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

    /// <summary>
    /// Island servisi. Adı bilerek <c>Island</c>, <c>IslandService</c> değil: C#'ta bir özelliğin
    /// adı tip adıyla aynı olursa sınıf gövdesindeki o ad özelliğe bağlanır ve aynı sınıftaki
    /// tip referansları bozulur. Engine servisi Dispose sonrası null yapar, o yüzden null
    /// kontrolü yazılı kalıyor.
    /// </summary>
    private IslandService? Island => _engine.Island;

    private void OnIslandKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseIsland();
            e.Handled = true;
            return;
        }

        // Ok/Enter/Delete yalnız panelde tüketilir; dışarıda bırakılırsa arama kutusu da
        // ok tuşlarını yer. Handled yalnız gerçekten tüketilen tuşta yazılır: e.Handled
        // false'a sıfırlamak, bu handler'ın bir üst sınıftan gelmiş işlenmiş bir olayı
        // yeniden açmasına yol açardı.
        if (IslandHost.HandleNavigationKey(e.Key))
        {
            e.Handled = true;
        }
    }

    private void CloseFlyouts(bool animate = true)
    {
        CloseIsland(animate);
        CloseQuickSettings(animate);
        CloseBrightness(animate);
        CloseMedia(animate);
        CloseClockPanel(animate);
        CloseTrayOverflowBox(animate);
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
        DetachIsland();
        _engine.Status.Changed -= OnStatusChanged;
        _engine.SystemStats.Changed -= OnSystemStatsChanged;
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
        NoteIslandBattery(apply: true);
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
            CloseQuickSettings(animate: false);
        }

        ApplyStatusItemLayout();
    }

    private void OnStatusChanged(object? sender, EventArgs e) => ApplyStatus(_engine.Status.Snapshot);

    private void OnSystemStatsChanged(object? sender, EventArgs e)
        => ApplySystemStats(_engine.SystemStats.Snapshot);

    private void ApplySystemStats(SystemStatsSnapshot snapshot)
    {
        bool showGpuPerAdapter = _config.Current.TopBar?.ShowGpuPerAdapter != false;
        string text = snapshot.DisplayText(showGpuPerAdapter);
        SystemStatsText.Text = text;
        Brush spark = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
        _statsHover?.Apply(snapshot, text, spark);
    }

    private void ApplySystemStatsVisibility()
    {
        bool show = _config.Current.TopBar?.ShowSystemStats != false;
        SystemStatsText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _engine.SystemStats.SetSampling(show);
    }

    private void OnVolumeChanged(object? sender, EventArgs e)
    {
        ApplyVolume(_engine.Volume.Snapshot);
        NoteIslandVolume();
    }

    private void OnTrayMirrorChanged(object? sender, EventArgs e) => ApplyTrayMirror(_engine.TrayMirror.Items);

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        _mediaActive = _engine.Media.HasActiveSession;
        SetPlainToolTip(MediaButton, BuildMediaTooltip(_engine.Media.Snapshot));
        ApplyStatusItemLayout();
        NoteIslandMedia(apply: true);
    }

    private void OnMediaSessionsChanged(object? sender, EventArgs e)
    {
        _mediaActive = _engine.Media.HasActiveSession;
        ApplyStatusItemLayout();
        NoteIslandMedia(apply: true);
    }

    private static string BuildMediaTooltip(MediaSnapshot snapshot)
    {
        if (!snapshot.HasSession)
        {
            return Strings.TopBar_NowPlaying;
        }

        if (string.IsNullOrEmpty(snapshot.Title))
        {
            return snapshot.AppName;
        }

        string text = string.IsNullOrEmpty(snapshot.Artist)
            ? snapshot.Title
            : $"{snapshot.Title} – {snapshot.Artist}";
        string? clock = snapshot.TimelineText();
        return clock is null ? text : $"{text} · {clock}";
    }

    private void ApplyStatusItemLayout()
    {
        if (_statusElements is null)
        {
            return;
        }

        IReadOnlyList<string> order = TopBarStatusItemIds.Normalize(
            _config.Current.TopBar?.StatusItems);

        // A tray poll hits this often. Rebuilding the strip mid-drag would throw away the
        // frozen slot measure the insertion line is anchored to.
        if (!StatusStripMatches(order))
        {
            StatusStrip.Children.Clear();
            foreach (string id in order)
            {
                if (_statusElements.TryGetValue(id, out FrameworkElement? el))
                {
                    StatusStrip.Children.Add(el);
                }
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

        if (Monitor.IsPrimary && _presence is not null)
        {
            ApplyIslandFace(_presence.View);
        }

        if (_statusDragActive
            && _statusDragId is string dragging
            && _statusElements.TryGetValue(dragging, out FrameworkElement? dragged)
            && dragged.Visibility != Visibility.Visible)
        {
            EndStatusDragVisuals();
        }
    }

    private bool StatusStripMatches(IReadOnlyList<string> order)
    {
        if (StatusStrip.Children.Count != order.Count)
        {
            return false;
        }

        for (int i = 0; i < order.Count; i++)
        {
            if (StatusStrip.Children[i] is not FrameworkElement element
                || !TryGetIdForElement(element, out string id)
                || !string.Equals(id, order[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

        foreach (string id in TopBarStatusItemIds.CatalogOrder)
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
            Header = Strings.TopBar_Background,
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
            Text = string.Format(Strings.Culture ?? CultureInfo.CurrentUICulture, Strings.TopBar_OpacityFormat, opacity),
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
            label.Text = string.Format(Strings.Culture ?? CultureInfo.CurrentUICulture, Strings.TopBar_OpacityFormat, v);
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
                Header = Strings.TopBar_MoveLeft,
                IsEnabled = index > 0,
                Tag = hitId,
            };
            moveLeft.Click += OnMoveStatusItemLeft;
            var moveRight = new System.Windows.Controls.MenuItem
            {
                Header = Strings.TopBar_MoveRight,
                IsEnabled = index >= 0 && index < current.Count - 1,
                Tag = hitId,
            };
            moveRight.Click += OnMoveStatusItemRight;
            menu.Items.Add(moveLeft);
            menu.Items.Add(moveRight);
            menu.Items.Add(new Separator());
        }

        bool missing = false;
        foreach (string id in TopBarStatusItemIds.CatalogOrder)
        {
            if (!visible.Contains(id))
            {
                missing = true;
                break;
            }
        }

        var showAll = new System.Windows.Controls.MenuItem
        {
            Header = Strings.TopBar_ShowAll,
            IsEnabled = missing,
        };
        showAll.Click += OnShowAllStatusItems;
        menu.Items.Add(showAll);

        var reset = new System.Windows.Controls.MenuItem { Header = Strings.TopBar_ResetDefault };
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
        TopBarStatusItemIds.TrayMirror => Strings.TopBar_TrayIcons,
        TopBarStatusItemIds.TrayOverflow => Strings.TopBar_TrayOverflow,
        TopBarStatusItemIds.DateTime => Strings.TopBar_DateTime,
        TopBarStatusItemIds.Notifications => Strings.TopBar_Notifications,
        TopBarStatusItemIds.ControlCenter => Strings.TopBar_ControlCenter,
        TopBarStatusItemIds.Volume => Strings.TopBar_Volume,
        TopBarStatusItemIds.Brightness => Strings.TopBar_Brightness,
        TopBarStatusItemIds.Network => Strings.TopBar_Network,
        TopBarStatusItemIds.Battery => Strings.TopBar_Battery,
        TopBarStatusItemIds.Media => Strings.TopBar_Media,
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
                items = TopBarStatusItemIds.InsertInCatalogOrder(items, id);
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

    private void OnShowAllStatusItems(object sender, RoutedEventArgs e)
    {
        PersistStatusItems(TopBarStatusItemIds.ShowAll(_config.Current.TopBar.StatusItems));
    }

    private void PersistStatusItems(List<string> items)
    {
        AppConfig cfg = _config.Current;
        cfg.TopBar ??= new TopBarConfig();
        List<string> normalized = TopBarStatusItemIds.Normalize(items);
        if (cfg.TopBar.StatusItems is { } existing && existing.SequenceEqual(normalized))
        {
            return;
        }

        cfg.TopBar.StatusItems = normalized;
        _config.Save(cfg);
        if (Application.Current is App app)
        {
            app.ApplyTopBarChanges();
        }
        else
        {
            ApplyStatusItemLayout();
        }
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
        ApplySystemStatsVisibility();
        ApplySystemStats(_engine.SystemStats.Snapshot);
        ApplyBackgroundAppearance();

        // Ayar sayfası IslandService.ApplyConfig(newConfig) çağırmak ZORUNDADIR: ada
        // görünürlüğü servisin Enabled alanına, canlı liste de Changed aboneliğine bağlı;
        // ikisi de ayar kaydından sonra bayat kalır. Bu satır yalnız görünürlüğü tazeler.
        UpdateIslandPill();
        SyncBluetoothWatch();
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
            // Fresh shell: re-arm the SPI_SETWORKAREA circuit breaker in case this explorer
            // build accepts the API again.
            WorkAreaExpandPolicy.Shared.Reset();
            QueueApplyPlacement(reRegister: true);
            _engine.TrayMirror.Reenumerate();
            if (Application.Current is App app)
            {
                app.ReaddTrayIcon();
                app.Monitors?.Refresh();
            }
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
                if (_pillParked)
                {
                    Dispatcher.BeginInvoke(PlaceShot, DispatcherPriority.Render);
                }

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
        _haveTraySnapshot = true;
        _trayMirrorSnapshot = items;
        if (_trayOverflowDragActive)
        {
            _trayOverflowUiStale = true;
            return;
        }

        _trayOverflowUiStale = false;

        IReadOnlyList<string>? box = _config.Current.TopBar?.TrayOverflowIcons;
        var visible = new List<TrayMirrorItem>(items.Count);
        foreach (TrayMirrorItem candidate in items)
        {
            if (!TrayOverflowMembership.IsInBox(box, candidate.Key))
            {
                visible.Add(candidate);
            }
        }

        _trayMirrorActive = visible.Count > 0;

        var nextKeys = new HashSet<string>(visible.Select(static i => i.Key), StringComparer.Ordinal);
        var stale = _trayMirrorImages.Keys.Where(k => !nextKeys.Contains(k)).ToList();
        foreach (string key in stale)
        {
            TrayMirrorItems.Items.Remove(_trayMirrorImages[key]);
            _trayMirrorImages.Remove(key);
        }

        foreach (TrayMirrorItem item in visible)
        {
            if (_trayMirrorImages.TryGetValue(item.Key, out Border? existing))
            {
                // Icons can change in place (badges) — refresh Source on every poll.
                if (existing.Child is Image existingImage)
                {
                    existingImage.Source = item.Icon;
                }

                existing.Tag = item;
                SetPlainToolTip(existing, item.Tooltip);
            }
            else
            {
                var image = new Image
                {
                    Source = item.Icon,
                    Width = 16,
                    Height = 16,
                    // Real tray captures can be 16–24px; scale into the fixed box (up/down).
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                // Hit-friendly host: the bare 16×16 image is a tiny target inside a 30px bar
                // (misclicks land on neighbors or the gap). A transparent, padded Border —
                // same pattern as the other status buttons — gives a ~30×28 hit area with
                // the icon centered. Hover highlight makes the real target visible; without
                // it users aim at the 16px glyph and cannot tell when they are on target.
                var hoverBrush = new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
                hoverBrush.Freeze();
                var host = new Border
                {
                    Background = Brushes.Transparent,
                    Width = 30,
                    Height = 28,
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = image,
                    Tag = item,
                };
                host.MouseEnter += (_, _) => host.Background = hoverBrush;
                host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
                host.MouseDown += OnTrayMirrorItemMouseDown;
                host.MouseMove += OnTrayMirrorItemMouseMove;
                host.MouseLeftButtonUp += OnTrayMirrorItemMouseLeftButtonUp;
                // Tunnel marks the release handled before StatusStrip's bubble opens WharfBar's
                // own menu. The bubble handler is the same method and only swallows.
                host.PreviewMouseRightButtonUp += OnTrayMirrorItemRightUp;
                host.MouseRightButtonUp += OnTrayMirrorItemRightUp;
                SetPlainToolTip(host, item.Tooltip);
                _trayMirrorImages[item.Key] = host;
                TrayMirrorItems.Items.Add(host);
            }
        }

        for (int i = 0; i < visible.Count; i++)
        {
            if (!_trayMirrorImages.TryGetValue(visible[i].Key, out Border? host))
            {
                continue;
            }

            int current = TrayMirrorItems.Items.IndexOf(host);
            if (current >= 0 && current != i)
            {
                TrayMirrorItems.Items.RemoveAt(current);
                TrayMirrorItems.Items.Insert(i, host);
            }
        }

        TrayOverflowHost.SetItems(ParkedTrayIcons(items, box));
        ApplyStatusItemLayout();
    }

    private void OnTrayMirrorItemRightUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: TrayMirrorItem item } host)
        {
            return;
        }

        // Preview (tünel) ve bubble aynı metodu paylaşır. İletim yalnız tünelde: bubble ikinci
        // bir sağ tık basar ve menüyü kapatır. Handled, StatusStrip'in kendi menüsünü keser.
        bool forward = e.RoutedEvent == UIElement.PreviewMouseRightButtonUpEvent;
        e.Handled = true;
        if (!forward)
        {
            return;
        }

        if (Mouse.Captured != null)
        {
            Mouse.Capture(null);
        }

        ForwardTrayClick(item, TrayClick.Right, host);
    }

    private void ForwardTrayClick(TrayMirrorItem item, TrayClick kind, FrameworkElement source)
    {
        // Routes by source: classic toolbar (TB_*) or UIA fallback (25H2+). The icon's
        // on-screen rect (physical px) lets the UIA path re-anchor the app's popup below it.
        Rect anchor = Rect.Empty;
        try
        {
            var topLeft = source.PointToScreen(new Point(0, 0));
            var bottomRight = source.PointToScreen(new Point(source.ActualWidth, source.ActualHeight));
            anchor = new Rect(topLeft, bottomRight);
        }
        catch
        {
            // Not yet rendered / no presentation source — re-anchor is skipped downstream.
        }

        _engine.TrayMirror.Click(item, kind, anchor);
    }
}
