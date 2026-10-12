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
    private static long _closedAt;

    private readonly AppConfig _config;
    private static readonly AppIconService Icons = new();
    private const int CatalogBatch = 16;
    private HwndSource? _source;
    private bool _acceptDismiss;
    private bool _closing;

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

            _closedAt = Environment.TickCount64;
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
        // already closed it. The same click must not open a second copy.
        if (Environment.TickCount64 - _closedAt < ReopenGuardMs)
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
            _ = window.Activate();
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
            Place();
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
            if (!CanCloseFromDeactivate(_acceptDismiss, _closing) || IsActive || OwnsOpenMenu())
            {
                return;
            }

            RequestClose();
        });
    }

    private void ApplyChrome()
    {
        SizeToContent = SizeToContent.Manual;
        Content = Build();
    }

    private UIElement Build()
    {
        var session = new LaunchpadSession
        {
            Config = _config,
            Apps = [],
            CatalogPending = true,
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
                GalleryWindow.Show(_config, widgets: true);
                RequestClose();
            },
            OpenPlugins = () =>
            {
                GalleryWindow.Show(_config, widgets: false);
                RequestClose();
            },
            OpenPath = OpenPath,
            Pinned = LaunchpadPins.FromDock(_config.Dock),
            Recent = [],
            LinkedPhone = LaunchpadPhoneLink.Read(),
            UseStyle = _ => { },
        };

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
            ? LaunchpadPhoneRow.Beside(card, phone, session.OpenPath)
            : card);
        stage.Children.Add(shadow);
        // The Start Menu walk and shell icons used to finish before Show, so the box
        // stayed invisible. Paint the shell first, then fill the list in small slices.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => LoadCatalog(session));
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
            IReadOnlyList<LaunchpadApp> apps;
            IReadOnlyList<LaunchpadRecentItem> recent;
            try
            {
                apps = LaunchpadCatalog.List();
                recent = LaunchpadRecent.List(24);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar launchpad catalog: {ex.Message}");
                apps = [];
                recent = [];
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => ApplyCatalog(session, apps, recent, 0));
        });
    }

    private void ApplyCatalog(
        LaunchpadSession session,
        IReadOnlyList<LaunchpadApp> apps,
        IReadOnlyList<LaunchpadRecentItem> recent,
        int shown)
    {
        if (_closing || session.Reload is null)
        {
            return;
        }

        int next = Math.Min(apps.Count, shown + CatalogBatch);
        session.Recent = recent;
        session.Apps = next == apps.Count ? apps : apps.Take(next).ToArray();
        session.CatalogPending = next < apps.Count;
        session.Reload();
        if (!session.CatalogPending)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => ApplyCatalog(session, apps, recent, next));
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
}
