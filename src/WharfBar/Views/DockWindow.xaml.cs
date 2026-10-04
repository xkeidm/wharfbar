using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using WharfBar.Views.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using MenuItem = Wpf.Ui.Controls.MenuItem;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace WharfBar.Views;

/// <summary>
/// macOS-style dock overlay anchored to the primary monitor work-area bottom.
/// Not an AppBar — placement uses physical-pixel SetWindowPos only.
/// </summary>
public partial class DockWindow : Window
{
    /// <summary>Vertical padding around icons (DIP).</summary>
    private const double VerticalPaddingDip = 6d;

    /// <summary>Horizontal inset inside the dock surface (DIP).</summary>
    private const double HorizontalPaddingDip = 12d;

    /// <summary>Gap between adjacent icons (DIP). Wider than early 4 DIP — less cramped.</summary>
    private const double IconGapDip = 12d;

    /// <summary>Height of the W11-style running pill (DIP).</summary>
    private const double RunningIndicatorSizeDip = 4d;

    /// <summary>Gap between icon bottom and the running pill (DIP).</summary>
    private const double RunningIndicatorGapDip = 2d;

    /// <summary>
    /// Gap between dock window bottom and the primary monitor's bottom edge (DIP).
    /// Kept at 0 so the strip sits on the screen edge (work-area bottom often leaves a taskbar-sized hole
    /// while Shell_TrayWnd is hidden).
    /// </summary>
    private const double BottomGapDip = 0d;

    private const int AutoHideAnimMs = 200;
    private const double MinWidthDip = 180d;

    /// <summary>Magnification enter/exit durations (ms).</summary>
    private const int PhaseInMs = 130;

    private const int PhaseOutMs = 180;

    /// <summary>Cursor catch-up frame interval (ms).</summary>
    private const int CursorFrameMs = 16;

    /// <summary>Mica translucency over the theme color (0–255 alpha).</summary>
    private const byte MicaDarkAlpha = 0x99;

    private const byte MicaLightAlpha = 0xD9;

    /// <summary>Acrylic surface translucency (0–255 alpha over the theme color).</summary>
    private const byte AcrylicDarkAlpha = 0xCC;

    private const byte AcrylicLightAlpha = 0xE6;

    private readonly AppConfig _config;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly DockStrip _strip;
    private double _iconSizeDip;
    private readonly DockAutoHideController _autoHide;
    private HwndSource? _source;
    private readonly AppIconService _icons = new();
    private readonly ProcessLauncherService _launcher = new();
    private ConfigService _configService = null!;
    private DockItemStore? _itemStore;
    private RunningAppsSource? _runningSource;
    private MinimizedWindowsSource? _minimizedSource;
    private DockAppsComposer? _composer;
    private WindowActivationService? _activation;
    private readonly DispatcherTimer _cursorTimer;
    private double _cursorTarget;
    private long _lastCursorFrameMs;
    private bool _isFullscreenHidden;
    private bool _isApplyingPlacement;
    private bool _isAnimatingY;
    private int _lastPlacementX;
    private int _lastPlacementWidthPx;
    private int _lastPlacementHeightPx;
    private int _lastVisibleY;
    private int _lastHiddenY;
    private bool _haveSlideRange;

    private static readonly DependencyProperty AnimYOffsetProperty =
        DependencyProperty.Register(
            nameof(AnimYOffset),
            typeof(double),
            typeof(DockWindow),
            new PropertyMetadata(0d, OnAnimYOffsetChanged));

    private double AnimYOffset
    {
        get => (double)GetValue(AnimYOffsetProperty);
        set => SetValue(AnimYOffsetProperty, value);
    }

    private static void OnAnimYOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DockWindow)d).SetWindowY((int)Math.Round((double)e.NewValue));
    }

    internal DockWindow(AppConfig config, TaskbarVisibilityService taskbar, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(taskbar);

        Monitor = monitor;
        InitializeComponent();
        _strip = Strip;

        _config = config;
        _taskbar = taskbar;
        _iconSizeDip = Math.Clamp(
            config.Dock.IconSize <= 0 ? DockConfig.DefaultIconSize : config.Dock.IconSize,
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);
        _autoHide = new DockAutoHideController(config.Dock.AutoHide, Dispatcher);

        _cursorTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(CursorFrameMs),
        };
        _cursorTimer.Tick += OnCursorFrame;

        ApplyStripGeometry();

        // Keep XAML padding in sync with the height budget used by SetWindowPos.
        Surface.Padding = new Thickness(
            HorizontalPaddingDip,
            VerticalPaddingDip,
            HorizontalPaddingDip,
            VerticalPaddingDip);
    }

    /// <summary>The monitor this dock window is anchored to.</summary>
    internal MonitorInfo Monitor { get; }

    /// <summary>
    /// Re-applies config-driven settings (icon size, auto-hide).
    /// </summary>
    internal void ApplyConfigChanges()
    {
        ApplyIconSizeFromConfig();
        ApplySurfaceFromConfig();

        bool wasEnabled = _autoHide.Enabled;
        _autoHide.SetEnabled(_config.Dock.AutoHide);

        if (_source is null)
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (_config.Dock.AutoHide)
        {
            _autoHide.Start(BuildEdgeProbe);
            if (!wasEnabled && TryGetPlacementGeometry(out PlacementGeometry geo))
            {
                SetWindowRect(geo.X, geo.HiddenY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyPassthrough(hwnd);
                _autoHide.AssumeHidden();
            }
            else if (wasEnabled)
            {
                // Icon size / height may have changed while auto-hide stays on.
                ApplyPlacement();
            }
        }
        else
        {
            CancelYAnimation();
            DockHitTest.ApplyInteractive(hwnd);
            ApplyPlacement();
        }

        ApplyWorkAreaReservation();

        // Fullscreen + auto-hide toggle: if auto-hide turned OFF while a fullscreen app is
        // active, the dock was collapsed for the fullscreen — re-show it (it now reserves).
        if (_isFullscreenHidden && !_config.Dock.AutoHide)
        {
            OnFullscreenChanged(this, EventArgs.Empty);
        }
    }

    private void ApplyIconSizeFromConfig()
    {
        double next = Math.Clamp(
            _config.Dock.IconSize <= 0 ? DockConfig.DefaultIconSize : _config.Dock.IconSize,
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);

        if (Math.Abs(next - _iconSizeDip) < 0.01)
        {
            return;
        }

        _iconSizeDip = next;

        Surface.Padding = new Thickness(
            HorizontalPaddingDip,
            VerticalPaddingDip,
            HorizontalPaddingDip,
            VerticalPaddingDip);

        // Before RebuildStrip: SetRows bakes slot widths from the strip's current IconSize,
        // so pushing the new metrics afterwards would leave the layout one icon size stale.
        ApplyStripGeometry();

        if (_composer is not null)
        {
            RebuildStrip();
        }
    }

    /// <summary>
    /// The dock surface is drawn by WPF (layered window), so DWM backdrops are off the table
    /// (docs/04-gotchas.md:7). Mica is a translucent theme tint; Acrylic is denser. Both follow
    /// the system light/dark preference.
    /// </summary>
    private void ApplySurfaceFromConfig()
    {
        bool acrylic = string.Equals(_config.Dock.Surface, DockSurfaceStyles.Acrylic, StringComparison.OrdinalIgnoreCase);
        bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        byte baseRgb = dark ? (byte)0x20 : (byte)0xFA;
        byte alpha = acrylic
            ? (dark ? AcrylicDarkAlpha : AcrylicLightAlpha)
            : (dark ? MicaDarkAlpha : MicaLightAlpha);

        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseRgb, baseRgb, baseRgb));
        brush.Freeze();
        Surface.Background = brush;
    }

    /// <summary>
    /// Lazily resolved config service for services that need to persist (the dock item
    /// store). <see cref="App.Config"/> is created during app startup, after the first dock
    /// window is constructed.
    /// </summary>
    private ConfigService WharfBarConfigService
    {
        get
        {
            if (_configService is null)
            {
                _configService = (ConfigService)((App)Application.Current).Config!;
            }

            return _configService;
        }
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

        IntPtr hwnd = _source.Handle;
        WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);

        // Layered window: keep the composition surface clear so only the strip is visible.
        _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        ApplySurfaceFromConfig();

        DockHitTest.ApplyInteractive(hwnd);

        FullscreenState.Instance.Changed += OnFullscreenChanged;
        WorkAreaChanged.Raised += OnWorkAreaChanged;

        _itemStore = new DockItemStore(WharfBarConfigService);
        _runningSource = new RunningAppsSource(Dispatcher);
        _minimizedSource = new MinimizedWindowsSource(Dispatcher);
        _composer = new DockAppsComposer(_itemStore, _runningSource, _minimizedSource, _config.Dock);
        _activation = new WindowActivationService();
        _composer.Changed += OnComposerChanged;

        ApplyStripGeometry();
        Strip.MouseMove += OnStripMouseMove;
        Strip.MouseLeave += OnStripMouseLeave;

        RebuildStrip();

        _autoHide.Changed += OnAutoHideChanged;
        if (_autoHide.Enabled)
        {
            if (TryGetPlacementGeometry(out PlacementGeometry geo))
            {
                SetWindowRect(geo.X, geo.HiddenY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyPassthrough(hwnd);
                _autoHide.AssumeHidden();
            }

            _autoHide.Start(BuildEdgeProbe);
        }

        ApplyWorkAreaReservation();

        if (FullscreenState.Instance.IsFullscreen)
        {
            OnFullscreenChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Reserves the dock strip at the target monitor's work-area bottom when the dock is
    /// always visible (auto-hide off), so maximized apps stop at the dock's top edge. In
    /// auto-hide mode nothing is reserved and maximized apps cover the full bottom.
    /// </summary>
    private void ApplyWorkAreaReservation()
    {
        if (_source is null || _autoHide.Enabled || !TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            _taskbar.SetDockReservation(Monitor, 0);
            return;
        }

        uint dpi = Display.GetDpi(_source.Handle);
        int bottomGapPx = Display.DipToPixels(BottomGapDip, dpi);
        _taskbar.SetDockReservation(Monitor, geo.HeightPx + bottomGapPx);
    }

    protected override void OnClosed(EventArgs e)
    {
        FullscreenState.Instance.Changed -= OnFullscreenChanged;
        WorkAreaChanged.Raised -= OnWorkAreaChanged;

        if (_composer is not null)
        {
            _composer.Changed -= OnComposerChanged;
            _composer.Dispose();
            _composer = null;
        }

        _itemStore = null;
        _runningSource?.Dispose();
        _runningSource = null;
        _minimizedSource?.Dispose();
        _minimizedSource = null;
        _activation = null;

        _autoHide.Changed -= OnAutoHideChanged;
        _autoHide.Dispose();

        Strip.MouseMove -= OnStripMouseMove;
        Strip.MouseLeave -= OnStripMouseLeave;
        _cursorTimer.Tick -= OnCursorFrame;
        _cursorTimer.Stop();

        _source?.RemoveHook(WndProc);
        _source = null;

        base.OnClosed(e);
    }

    private void OnComposerChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RebuildStrip);

    private void OnStripMouseMove(object sender, MouseEventArgs e)
    {
        _cursorTarget = Strip.HitTestCursor(e.GetPosition(Strip).X);

        if (!_cursorTimer.IsEnabled)
        {
            _lastCursorFrameMs = Environment.TickCount64;
            _cursorTimer.Start();
        }

        AnimatePhase(1d, PhaseInMs);
    }

    private void OnStripMouseLeave(object sender, MouseEventArgs e)
    {
        _cursorTimer.Stop();
        AnimatePhase(0d, PhaseOutMs);
    }

    private void OnCursorFrame(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = now - _lastCursorFrameMs;
        _lastCursorFrameMs = now;

        double next = DockMagnification.Eased(Strip.CursorPosition, _cursorTarget, dt);
        if (Math.Abs(next - _cursorTarget) < 0.001d)
        {
            Strip.CursorPosition = _cursorTarget;
            _cursorTimer.Stop();
            return;
        }

        Strip.CursorPosition = next;
    }

    private void AnimatePhase(double to, int milliseconds)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Strip.BeginAnimation(DockStrip.PhaseProperty, animation);
    }

    private void RebuildStrip()
    {
        IReadOnlyList<DockAppItem> items = _composer!.Items;
        IReadOnlyList<DockStripRow> rows = DockStripBuilder.Build(items);
        int iconPx = ResolveIconPixelSize();

        var viewModels = new List<DockItemViewModel>(rows.Count);
        foreach (DockStripRow row in rows)
        {
            viewModels.Add(row.IsSeparator
                ? DockItemViewModel.ForSeparator()
                : DockItemViewModel.ForItem(row.Item!, _icons.GetIcon(row.Item!.Path, iconPx)));
        }

        _strip.SetRows(viewModels);

        // One block, on purpose: a cell reused from the previous rebuild must always get a
        // fresh tooltip and menu, otherwise a stale menu leaks onto a different app.
        foreach (DockItemCell cell in _strip.Cells)
        {
            DockItemViewModel? row = cell.Row;
            if (row is null || row.IsSeparator)
            {
                continue;
            }

            cell.ToolTip = CreateDockTip(row.Tooltip, cell);
            cell.ContextMenu = BuildContextMenuForItem(row.Item!);
        }

        ApplyPlacement();
    }

    private static ToolTip CreateDockTip(string text, UIElement target) => new()
    {
        Content = text,
        Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
        PlacementTarget = target,
        CustomPopupPlacementCallback = CenterAboveTarget,
    };

    private static System.Windows.Controls.Primitives.CustomPopupPlacement[] CenterAboveTarget(
        Size tipSize, Size targetSize, Point offset)
    {
        return
        [
            new System.Windows.Controls.Primitives.CustomPopupPlacement(
                new Point((targetSize.Width - tipSize.Width) / 2, -tipSize.Height - 25),
                System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
        ];
    }

    private ContextMenu BuildContextMenuForItem(DockAppItem item)
    {
        var menu = new ContextMenu();

        // 1. Header (App Icon + App Name)
        int sizePx = ResolveIconPixelSize();
        var headerItem = new MenuItem
        {
            IsEnabled = false,
            Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 4, 8, 4),
        };
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var headerIcon = new System.Windows.Controls.Image
        {
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            Source = string.IsNullOrWhiteSpace(item.Path)
                ? _icons.GetIcon(string.Empty, sizePx)
                : _icons.GetIcon(item.Path, sizePx),
            Margin = new Thickness(0, 0, 8, 0),
        };
        RenderOptions.SetBitmapScalingMode(headerIcon, BitmapScalingMode.HighQuality);
        var headerText = new TextBlock
        {
            Text = item.Label,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
        };
        headerStack.Children.Add(headerIcon);
        headerStack.Children.Add(headerText);
        headerItem.Header = headerStack;
        menu.Items.Add(headerItem);
        menu.Items.Add(new Separator());

        // 2. Pin / Unpin
        var pinItem = new MenuItem
        {
            Header = item.IsPinned ? "Dock'tan kaldır" : "Dock'a sabitle",
            Icon = CreateMenuIcon(item.IsPinned ? SymbolRegular.PinOff24 : SymbolRegular.Pin24),
        };
        pinItem.Click += (_, _) => TogglePin(item);
        menu.Items.Add(pinItem);

        // 3. Görevi sonlandır (Force Quit)
        var forceQuitItem = new MenuItem
        {
            Header = "Görevi sonlandır",
            Icon = CreateMenuIcon(SymbolRegular.DismissCircle24),
            IsEnabled = item.IsRunning && item.MainWindowHandle != IntPtr.Zero,
        };
        forceQuitItem.Click += (_, _) => ForceQuitApp(item);
        menu.Items.Add(forceQuitItem);

        // 4. Pencereyi kapat (Close Window)
        var closeItem = new MenuItem
        {
            Header = "Pencereyi kapat",
            Icon = CreateMenuIcon(SymbolRegular.Dismiss24),
            IsEnabled = item.IsRunning && item.MainWindowHandle != IntPtr.Zero,
        };
        closeItem.Click += (_, _) => CloseAppWindow(item);
        menu.Items.Add(closeItem);

        // 5. Additional actions if path exists
        if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
        {
            menu.Items.Add(new Separator());

            var runAdminItem = new MenuItem
            {
                Header = "Yönetici olarak çalıştır",
                Icon = CreateMenuIcon(SymbolRegular.Shield24),
            };
            runAdminItem.Click += (_, _) => _launcher.LaunchAsAdmin(item.Path, item.Args);
            menu.Items.Add(runAdminItem);

            var openFolderItem = new MenuItem
            {
                Header = "Dosya konumunu aç",
                Icon = CreateMenuIcon(SymbolRegular.Folder24),
            };
            openFolderItem.Click += (_, _) => OpenFileLocation(item.Path);
            menu.Items.Add(openFolderItem);

            var newInstanceItem = new MenuItem
            {
                Header = "Yeni pencere aç",
                Icon = CreateMenuIcon(SymbolRegular.Add24),
            };
            newInstanceItem.Click += (_, _) => _launcher.Launch(item.Path, item.Args);
            menu.Items.Add(newInstanceItem);
        }

        return menu;
    }

    private static SymbolIcon CreateMenuIcon(SymbolRegular symbol) =>
        new(symbol) { FontSize = 16 };

    private static void ForceQuitApp(DockAppItem item)
    {
        if (item.MainWindowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.GetWindowThreadProcessId(item.MainWindowHandle, out uint pid);
            if (pid > 0)
            {
                using var proc = Process.GetProcessById((int)pid);
                proc.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar force quit failed for {item.Label}: {ex.Message}");
        }
    }

    private static void CloseAppWindow(DockAppItem item)
    {
        if (item.MainWindowHandle != IntPtr.Zero)
        {
            NativeMethods.PostMessage(item.MainWindowHandle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static void OpenFileLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar open file location failed: {ex.Message}");
        }
    }

    private void TogglePin(DockAppItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return;
        }

        string pinnedDir = TaskbarPinnedAppsSource.GetTaskBarPinnedDirectory();

        if (item.IsPinned)
        {
            try
            {
                if (Directory.Exists(pinnedDir))
                {
                    foreach (string lnk in Directory.EnumerateFiles(pinnedDir, "*.lnk"))
                    {
                        if (ShortcutResolver.TryResolve(lnk, out string target, out _, out _)
                            && string.Equals(target, item.Path, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(lnk);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar unpin shortcut delete failed: {ex.Message}");
            }

            _config.PinnedApps.RemoveAll(p => string.Equals(p.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            try
            {
                Directory.CreateDirectory(pinnedDir);
                string safeLabel = SanitizeFileName(item.Label);
                string lnkPath = Path.Combine(pinnedDir, $"{safeLabel}.lnk");
                ShortcutResolver.TryCreateShortcut(lnkPath, item.Path, item.Args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar pin shortcut create failed: {ex.Message}");
            }

            if (!_config.PinnedApps.Any(p => string.Equals(p.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
            {
                _config.PinnedApps.Add(new PinnedApp(item.Path, item.Args, item.Label));
            }
        }

        var configService = (Application.Current as App)?.Config ?? new ConfigService();
        configService.Save(_config);
        // Legacy pin writes above still target config.pinnedApps, not dock.items, so re-reading
        // the store normally changes nothing. Reload still notifies: the composer rebuilds off
        // the event, so this is what would pick up a dock.items write made elsewhere.
        _itemStore?.Reload();
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "AppShortcut";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] cleaned = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        string result = new string(cleaned).Trim();
        return string.IsNullOrWhiteSpace(result) ? "AppShortcut" : result;
    }

    private int ResolveIconPixelSize()
    {
        uint dpi = _source is null ? Monitor.Dpi : Display.GetDpi(_source.Handle);
        // Request the bitmap at the magnification ceiling so a magnified icon stays sharp —
        // upscaling a resting-size bitmap looks blurry (docs/04-gotchas.md:31).
        double maxScale = DockMagnification.Clamp(_config.Dock.Magnification);
        int px = Display.DipToPixels(_iconSizeDip * maxScale, dpi);
        return Math.Clamp(px <= 0 ? 24 : px, 16, 256);
    }

    /// <summary>
    /// Centers the dock on the primary monitor bottom edge in physical pixels. Uses monitor
    /// bounds (not work area) for Y so the strip sits flush when the taskbar is hidden but the
    /// shell work area still reserves that strip. WPF Left/Top/Width are unused (primary-scale bias).
    /// </summary>
    private void ApplyPlacement()
    {
        if (_source is null || _isApplyingPlacement)
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            return;
        }

        _isApplyingPlacement = true;
        try
        {
            int y = ResolvePlacementY(geo);
            SetWindowRect(geo.X, y, geo.WidthPx, geo.HeightPx);
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private int ResolvePlacementY(PlacementGeometry geo)
    {
        if (_autoHide.Enabled && _isAnimatingY && _source is not null
            && NativeMethods.GetWindowRect(_source.Handle, out RECT rect))
        {
            return rect.Top;
        }

        if (_autoHide.Enabled)
        {
            return _autoHide.State is DockAutoHideVisualState.Hidden or DockAutoHideVisualState.Hiding
                ? geo.HiddenY
                : geo.VisibleY;
        }

        return geo.VisibleY;
    }

    private bool TryGetPlacementGeometry(out PlacementGeometry geo)
    {
        geo = default;
        if (_source is null)
        {
            return false;
        }

        IntPtr hwnd = _source.Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        uint dpi = Monitor.Dpi;
        int heightPx = Display.DipToPixels(MaxHeightDip, dpi);
        int bottomGapPx = Display.DipToPixels(BottomGapDip, dpi);
        RECT monitor = Monitor.Bounds;

        if (monitor.Width <= 0 || heightPx <= 0)
        {
            return false;
        }

        int widthPx = ComputeMaxWidthPx(dpi);
        int minWidthPx = Display.DipToPixels(MinWidthDip, dpi);
        widthPx = Math.Clamp(widthPx, minWidthPx, monitor.Width);

        int x = monitor.Left + (monitor.Width - widthPx) / 2;
        int visibleY = monitor.Bottom - heightPx - bottomGapPx;
        int hiddenY = monitor.Bottom;

        _lastVisibleY = visibleY;
        _lastHiddenY = hiddenY;
        _haveSlideRange = true;

        geo = new PlacementGeometry(x, visibleY, hiddenY, widthPx, heightPx);
        return true;
    }

    private EdgeProbeContext BuildEdgeProbe()
    {
        uint dpi = Monitor.Dpi;
        RECT monitor = Monitor.Bounds;

        // Use last SetWindowPos geometry — Measure() on the 50 ms poll stalls the UI
        // thread (laggy hover slide, delayed top-bar menus).
        int widthPx = _lastPlacementWidthPx > 0
            ? _lastPlacementWidthPx
            : Math.Max(Display.DipToPixels(MinWidthDip, dpi), 1);
        int heightPx = _lastPlacementHeightPx > 0
            ? _lastPlacementHeightPx
            : Display.DipToPixels(MaxHeightDip, dpi);
        int x = _lastPlacementWidthPx > 0
            ? _lastPlacementX
            : monitor.Left + (monitor.Width - widthPx) / 2;
        int yVisible = _haveSlideRange
            ? _lastVisibleY
            : monitor.Bottom - heightPx;

        return new EdgeProbeContext(
            monitor.Left,
            monitor.Top,
            monitor.Right,
            monitor.Bottom,
            x,
            yVisible,
            x + widthPx,
            yVisible + heightPx,
            dpi);
    }

    private void OnAutoHideChanged(object? sender, EventArgs e)
    {
        if (_source is null || !_autoHide.Enabled)
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (!TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            return;
        }

        switch (_autoHide.State)
        {
            case DockAutoHideVisualState.Showing:
                DockHitTest.ApplyInteractive(hwnd);
                AnimateYTo(geo.VisibleY);
                break;
            case DockAutoHideVisualState.Hiding:
                DockHitTest.ApplyPassthrough(hwnd);
                AnimateYTo(geo.HiddenY);
                break;
            case DockAutoHideVisualState.Visible:
                CancelYAnimation();
                SetWindowRect(geo.X, geo.VisibleY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyInteractive(hwnd);
                break;
            case DockAutoHideVisualState.Hidden:
                CancelYAnimation();
                SetWindowRect(geo.X, geo.HiddenY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyPassthrough(hwnd);
                break;
        }
    }

    private void AnimateYTo(int targetY)
    {
        if (_source is null || !TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect))
        {
            return;
        }

        int fromY = rect.Top;
        SetWindowRect(geo.X, fromY, geo.WidthPx, geo.HeightPx);

        if (fromY == targetY)
        {
            OnYAnimationCompleted();
            return;
        }

        CancelYAnimation();
        _isAnimatingY = true;
        AnimYOffset = fromY;

        var anim = new DoubleAnimation(fromY, targetY, TimeSpan.FromMilliseconds(AutoHideAnimMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            _isAnimatingY = false;
            SetWindowY(targetY);
            OnYAnimationCompleted();
        };
        BeginAnimation(AnimYOffsetProperty, anim);
    }

    private void CancelYAnimation()
    {
        BeginAnimation(AnimYOffsetProperty, null);
        _isAnimatingY = false;
    }

    private void OnYAnimationCompleted() => _autoHide.NotifyAnimationCompleted();

    private void SetWindowY(int y) =>
        SetWindowRect(_lastPlacementX, y, _lastPlacementWidthPx, _lastPlacementHeightPx);

    private void SetWindowRect(int x, int y, int widthPx, int heightPx)
    {
        if (_source is null)
        {
            return;
        }

        _lastPlacementX = x;
        _lastPlacementWidthPx = widthPx;
        _lastPlacementHeightPx = heightPx;

        IntPtr hwnd = _source.Handle;
        IntPtr insertAfter = _isFullscreenHidden
            ? NativeMethods.HWND_BOTTOM
            : NativeMethods.HWND_TOPMOST;

        NativeMethods.SetWindowPos(
            hwnd,
            insertAfter,
            x,
            y,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE);
    }

    private int ComputeMaxWidthPx(uint dpi)
    {
        // Budget the window for the worst magnified layout. Hover only ever re-runs WPF
        // measure/arrange — no SetWindowPos per frame, which is what used to stutter.
        double panel = _strip.MaxLiveWidth();
        double width = panel + (2d * HorizontalPaddingDip);
        return Display.DipToPixels(width, dpi);
    }

    private double MaxHeightDip =>
        DockStripLayout.MaxExtentHeight(
            _iconSizeDip,
            RunningIndicatorGapDip,
            RunningIndicatorSizeDip,
            VerticalPaddingDip,
            DockMagnification.Clamp(_config.Dock.Magnification));

    /// <summary>Pushes icon metrics and the magnification ceiling into the strip.</summary>
    private void ApplyStripGeometry()
    {
        _strip.IconSize = _iconSizeDip;
        _strip.IconGap = IconGapDip;
        _strip.IndicatorGap = RunningIndicatorGapDip;
        _strip.IndicatorSize = RunningIndicatorSizeDip;
        _strip.MaxScale = DockMagnification.Clamp(_config.Dock.Magnification);
    }

    private void OnWorkAreaChanged(object? sender, EventArgs e)
    {
        if (_autoHide.Enabled
            && _autoHide.State is DockAutoHideVisualState.Showing or DockAutoHideVisualState.Hiding)
        {
            return;
        }

        CancelYAnimation();
        ApplyPlacement();
    }

    private void OnFullscreenChanged(object? sender, EventArgs e)
    {
        // Auto-hide: the fullscreen app must cover the full monitor — hide the dock.
        // Always-visible (auto-hide off): the dock reserves its strip and stays put.
        if (FullscreenState.Instance.IsFullscreen && _autoHide.Enabled)
        {
            _isFullscreenHidden = true;
            _autoHide.NotifyFullscreen(true);
            Visibility = Visibility.Collapsed;
            return;
        }

        _isFullscreenHidden = false;
        Visibility = Visibility.Visible;

        if (_source is null)
        {
            return;
        }

        DockHitTest.ApplyInteractive(_source.Handle);
        _autoHide.NotifyFullscreen(false);

        NativeMethods.SetWindowPos(
            _source.Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        CancelYAnimation();
        ApplyPlacement();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_WINDOWPOSCHANGING:
                ClampDockPlacement(lParam);
                break;

            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CancelYAnimation();
                    ApplyPlacement();
                }));
                break;

            case NativeMethods.WM_THEMECHANGED:
            case NativeMethods.WM_SYSCOLORCHANGE:
            case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                Dispatcher.BeginInvoke(ApplySurfaceFromConfig);
                break;

            case NativeMethods.WM_SETTINGCHANGE:
                if (IsImmersiveColorSet(lParam))
                {
                    Dispatcher.BeginInvoke(ApplySurfaceFromConfig);
                }

                break;
        }

        return IntPtr.Zero;
    }

    private void ClampDockPlacement(IntPtr lParam)
    {
        if (_source is null || lParam == IntPtr.Zero || !_haveSlideRange)
        {
            return;
        }

        WINDOWPOS pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if (!NativeMethods.GetWindowRect(_source.Handle, out RECT current))
        {
            return;
        }

        bool noMove = (pos.flags & NativeMethods.SWP_NOMOVE) != 0;
        int proposedY = noMove ? current.Top : pos.y;
        bool allowSlide = _isAnimatingY
            || _autoHide.State is DockAutoHideVisualState.Showing or DockAutoHideVisualState.Hiding;
        AppBarPlacement.GetDockSlideClampRange(
            _autoHide.Enabled,
            allowSlide,
            parkedHidden: _autoHide.State is DockAutoHideVisualState.Hidden,
            _lastVisibleY,
            _lastHiddenY,
            out int minY,
            out int maxY);

        int clampedY = AppBarPlacement.ClampSlideY(proposedY, minY, maxY);
        if (clampedY == proposedY)
        {
            return;
        }

        pos.x = noMove ? current.Left : pos.x;
        pos.y = clampedY;
        pos.flags &= ~NativeMethods.SWP_NOMOVE;
        Marshal.StructureToPtr(pos, lParam, false);
    }

    private static bool IsImmersiveColorSet(IntPtr lParam)
    {
        return lParam != IntPtr.Zero
            && string.Equals(Marshal.PtrToStringUni(lParam), "ImmersiveColorSet", StringComparison.Ordinal);
    }

    private readonly record struct PlacementGeometry(
        int X,
        int VisibleY,
        int HiddenY,
        int WidthPx,
        int HeightPx);
}
