using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// One layered launchpad. A centered card sits on a theme-tinted veil so the wallpaper shows through.
/// Closing this window from <see cref="OnDeactivated"/> while <see cref="Close"/>
/// is already running throws and takes the process down.
/// </summary>
internal partial class LaunchpadWindow : Window
{
    private const int ReopenGuardMs = 250;

    private static LaunchpadWindow? _open;
    private static long _dismissedAt;

    private readonly AppConfig _config;
    private static readonly AppIconService Icons = LaunchpadIcons.Shared;
    private HwndSource? _source;
    private LaunchpadSession? _session;
    private bool _acceptDismiss;
    private bool _closing;
    private bool _dismissed;

    private LaunchpadWindow(AppConfig config)
    {
        _config = config;
        // Transparent so the desktop wallpaper shows through the veil.
        AllowsTransparency = true;
        InitializeComponent();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
            {
                return;
            }

            // A non-empty search clears on the first Escape (handled by the face);
            // only the second Escape closes the pad.
            if (_session?.SearchText?.Invoke() is { Length: > 0 })
            {
                return;
            }

            e.Handled = true;
            RequestClose();
        };
        ApplyChrome();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, this))
            {
                _open = null;
            }

            if (_dismissed)
            {
                _dismissedAt = Environment.TickCount64;
            }
        };
    }

    /// <summary>
    /// Deactivate may close the pad only after the first frame, and never while
    /// a close is already in progress.
    /// </summary>
    internal static bool CanCloseFromDeactivate(bool acceptDismiss, bool closing) =>
        acceptDismiss && !closing;

    internal static void Toggle(AppConfig config)
    {
        if (_open is not null)
        {
            _open.RequestClose();
            return;
        }

        // A click on the dock tile deactivates this window first, which
        // already closed it. The same click must not open a second copy. Only an
        // auto-dismiss sets the timestamp, so a deliberate reopen after Esc or an
        // app launch is never swallowed.
        if (_dismissedAt != 0 && Environment.TickCount64 - _dismissedAt < ReopenGuardMs)
        {
            return;
        }

        Show(config);
    }

    private static void Show(AppConfig config)
    {
        LaunchpadWindow? window = null;
        try
        {
            window = new LaunchpadWindow(config);
            _open = window;
            window.Show();
            if (!window.Activate())
            {
                // The dock/top bar is topmost and may still hold the foreground; without
                // this the pad paints but keystrokes go to the previous app.
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                    _ = NativeMethods.SetForegroundWindow(hwnd);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launchpad: {ex}");
            try
            {
                window?.RequestClose();
            }
            catch (Exception closeEx)
            {
                Debug.WriteLine($"WharfBar launchpad close: {closeEx.Message}");
            }

            if (ReferenceEquals(_open, window))
            {
                _open = null;
            }
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = PresentationSource.FromVisual(this) as HwndSource;
        if (_source is null)
        {
            return;
        }

        if (_source.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        WindowOverlayStyles.ExcludeFromTaskSwitcher(_source.Handle);
        _source.AddHook(WndProc);
        ClearLayeredBackdrop();
        Place();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED or NativeMethods.WM_THEMECHANGED)
        {
            ClearLayeredBackdrop();
            // WM_DPICHANGED carries WPF's own suggested rect; let WPF finish first, then
            // re-place this full-monitor overlay or the pad snaps back to a partial rect.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Place);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// A Mica backdrop on this full-screen layered window is an opaque sheet and hides the wallpaper.
    /// Theme applies can attach it again, so clear it whenever the pad is placed.
    /// </summary>
    private void ClearLayeredBackdrop()
    {
        if (_source?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        if (_source?.Handle is { } hwnd && hwnd != IntPtr.Zero)
        {
            Dwm.SetSystemBackdrop(hwnd, DwmSystemBackdrop.None);
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Place();
        _acceptDismiss = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
        if (e.Cancel)
        {
            _closing = false;
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!CanCloseFromDeactivate(_acceptDismiss, _closing))
        {
            return;
        }

        // The power menu is a separate window. Closing here, inside the click
        // that opened it, is the crash: Close while Close is already running.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            if (!CanCloseFromDeactivate(_acceptDismiss, _closing) || IsActive)
            {
                return;
            }

            if (OwnsOpenMenu())
            {
                // A menu popup took the activation. Re-activate the pad once it closes,
                // otherwise the pad stays on screen while the keyboard goes elsewhere.
                foreach (ContextMenu menu in OpenMenus(this))
                {
                    menu.Closed -= OnOwnedMenuClosed;
                    menu.Closed += OnOwnedMenuClosed;
                }

                return;
            }

            _dismissed = true;
            RequestClose();
        });
    }

    private void OnOwnedMenuClosed(object? sender, EventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Closed -= OnOwnedMenuClosed;
        }

        if (_closing)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (!_closing && !IsActive)
            {
                _ = Activate();
            }
        });
    }

    private void ApplyChrome()
    {
        SizeToContent = SizeToContent.Manual;
        Content = Build();
    }

    private UIElement Build()
    {
        // A process-wide cache warmed at startup paints the list on the first frame.
        // Without it (cold start) the session waits for the background scan below.
        LaunchpadCatalogCache.Shared.TryGet(out LaunchpadCatalogSnapshot? snapshot);
        bool fresh = snapshot is not null
            && !LaunchpadCatalogCache.IsStale(snapshot, DateTimeOffset.UtcNow, LaunchpadCatalogCache.DefaultTtl);

        // The pad covers the primary screen; size the card from its DIPs so small or
        // high-DPI screens (e.g. 1366x768 at 150%) do not clip the box or the phone panel.
        double surfaceWidth = SystemParameters.PrimaryScreenWidth;
        double surfaceHeight = SystemParameters.PrimaryScreenHeight;
        double cardWidth = LaunchpadLayout.CardWidth(surfaceWidth);
        LaunchpadPhoneSnapshot? linkedPhone = LaunchpadLayout.PhoneFits(surfaceWidth, cardWidth)
            ? LaunchpadPhoneLink.Read()
            : null;

        var session = new LaunchpadSession
        {
            Config = _config,
            Apps = snapshot?.Apps ?? [],
            CatalogPending = snapshot is null,
            CardWidth = cardWidth,
            MaxCardHeight = LaunchpadLayout.CardHeight(surfaceHeight),
            Icon = app => Icons.GetIcon(app.ShortcutPath, 64),
            Open = OpenApp,
            Close = RequestClose,
            OpenWindowsSettings = () =>
            {
                QuickSettingsService.OpenSettingsUri("ms-settings:");
                RequestClose();
            },
            OpenWharfSettings = () =>
            {
                ((App)Application.Current).ShowSettings();
                RequestClose();
            },
            OpenWidgets = () =>
            {
                RequestClose();
                GalleryWindow.Show(_config, widgets: true);
            },
            OpenPlugins = () =>
            {
                RequestClose();
                GalleryWindow.Show(_config, widgets: false);
            },
            OpenPath = OpenPath,
            Pinned = LaunchpadPins.FromDock(_config.Dock),
            Recent = snapshot?.Recent ?? [],
            LinkedPhone = linkedPhone,
            UseStyle = _ => { },
        };
        _session = session;

        var stage = new Grid();
        stage.Loaded += (_, _) => stage.Background = Veil(stage);
        stage.MouseLeftButtonDown += (_, args) =>
        {
            if (ReferenceEquals(args.OriginalSource, stage))
            {
                RequestClose();
            }
        };

        var card = LaunchpadWin11Face.Create(session);
        if (card is FrameworkElement face)
        {
            face.HorizontalAlignment = HorizontalAlignment.Center;
            face.VerticalAlignment = VerticalAlignment.Center;
        }

        var shadow = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(18),
            RenderTransformOrigin = new Point(0.5, 0.5),
            Opacity = 0,
            Effect = new DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.38,
                Color = Colors.Black,
                RenderingBias = RenderingBias.Quality,
            },
        };
        var scale = new ScaleTransform(0.98, 0.98);
        var rise = new TranslateTransform(0, 18);
        var motion = new TransformGroup();
        motion.Children.Add(scale);
        motion.Children.Add(rise);
        shadow.RenderTransform = motion;
        shadow.Loaded += (_, _) => PlayOpen(shadow, scale, rise);
        shadow.Children.Add(session.LinkedPhone is { } phone
            ? LaunchpadPhoneRow.Beside(card, phone, session.OpenPath, session.MaxCardHeight)
            : card);
        stage.Children.Add(shadow);
        // The Start Menu walk and shell icons used to finish before Show, so the box
        // stayed invisible. Paint the shell first, then fill the list in small slices.
        // With a warm cache the pad paints the full list now; only a missing or stale
        // cache needs the background scan.
        if (!fresh)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => LoadCatalog(session));
        }

        return stage;
    }

    private void LoadCatalog(LaunchpadSession session)
    {
        if (_closing)
        {
            return;
        }

        Task.Run(() =>
        {
            // LoadSync also publishes the result, so the next open starts warm. It joins a
            // warm-up scan that is already running instead of scanning the Start Menu twice.
            LaunchpadCatalogSnapshot fresh = LaunchpadCatalogCache.Shared.LoadSync();
            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () =>
                {
                    if (_closing)
                    {
                        return;
                    }

                    // Paint the fresh list in one pass. Slicing it here made the already
                    // painted stale list collapse to 16 tiles and grow back in visible steps.
                    session.Recent = fresh.Recent;
                    session.Apps = fresh.Apps;
                    session.CatalogPending = false;
                    session.Reload?.Invoke();
                });
        });
    }

    private static void PlayOpen(Grid card, ScaleTransform scale, TranslateTransform rise)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(280));
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.98, 1, duration) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.98, 1, duration) { EasingFunction = ease });
        rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, duration) { EasingFunction = ease });
    }

    private static Brush Veil(FrameworkElement source)
    {
        Color basis = Color.FromRgb(28, 28, 32);
        if (source.TryFindResource("SolidBackgroundFillColorBaseBrush") is SolidColorBrush theme)
        {
            basis = theme.Color;
        }

        basis.A = 88;
        return new SolidColorBrush(basis);
    }

    private void OpenApp(LaunchpadApp app)
    {
        try
        {
            Process.Start(new ProcessStartInfo(app.ShortcutPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launchpad: {ex.Message}");
            return;
        }

        RequestClose();
    }

    private void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launchpad: {ex.Message}");
            return;
        }

        RequestClose();
    }

    private void RequestClose()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        Close();
    }

    private void Place()
    {
        if (_source is null)
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        ClearLayeredBackdrop();
        RECT monitor = Display.GetPrimaryMonitorBounds(hwnd);
        if (monitor.Width <= 0 || monitor.Height <= 0)
        {
            return;
        }

        uint dpi = Display.GetDpi(hwnd);
        Width = monitor.Width * 96d / dpi;
        Height = monitor.Height * 96d / dpi;
        _ = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            monitor.Left,
            monitor.Top,
            monitor.Width,
            monitor.Height,
            0);
    }

    private bool OwnsOpenMenu()
    {
        return OwnsOpenMenu(this);

        static bool OwnsOpenMenu(DependencyObject node)
        {
            if (node is FrameworkElement element && element.ContextMenu is { IsOpen: true })
            {
                return true;
            }

            foreach (object child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is DependencyObject next && OwnsOpenMenu(next))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static List<ContextMenu> OpenMenus(DependencyObject node)
    {
        var menus = new List<ContextMenu>();
        Collect(node, menus);
        return menus;

        static void Collect(DependencyObject current, List<ContextMenu> found)
        {
            if (current is FrameworkElement element && element.ContextMenu is { IsOpen: true } menu)
            {
                found.Add(menu);
            }

            foreach (object child in LogicalTreeHelper.GetChildren(current))
            {
                if (child is DependencyObject next)
                {
                    Collect(next, found);
                }
            }
        }
    }
}
