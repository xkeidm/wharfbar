using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Wpf.Ui.Controls;

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

    private const double RunningDotSizeDip = 4d;
    private const double RunningDotGapDip = 2d;

    /// <summary>
    /// Gap between dock window bottom and the primary monitor's bottom edge (DIP).
    /// Kept at 0 so the strip sits on the screen edge (work-area bottom often leaves a taskbar-sized hole
    /// while Shell_TrayWnd is hidden).
    /// </summary>
    private const double BottomGapDip = 0d;

    private const double HoverScale = 1.12d;
    private const int HoverDurationMs = 140;
    private const int AutoHideAnimMs = 200;
    private const double MinWidthDip = 180d;

    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;
    private const DwmCornerPreference Corners = DwmCornerPreference.Round;

    private readonly AppConfig _config;
    private readonly double _iconSizeDip;
    private readonly double _dockHeightDip;
    private readonly DockAutoHideController _autoHide;
    private HwndSource? _source;
    private WindowAppearance? _appearance;
    private readonly AppIconService _icons = new();
    private readonly ProcessLauncherService _launcher = new();
    private TaskbarPinnedAppsSource? _pinSource;
    private RunningAppsSource? _runningSource;
    private DockAppsComposer? _composer;
    private WindowActivationService? _activation;
    private bool _isFullscreenHidden;
    private bool _isApplyingPlacement;
    private bool _isAnimatingY;
    private int _lastPlacementX;
    private int _lastPlacementWidthPx;
    private int _lastPlacementHeightPx;

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

    internal DockWindow(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        InitializeComponent();

        _config = config;
        _iconSizeDip = Math.Clamp(
            config.Dock.IconSize <= 0 ? DockConfig.DefaultIconSize : config.Dock.IconSize,
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);
        // Icon + running-dot stack + vertical padding ≈ Windows taskbar (~40–48 DIP at default).
        _dockHeightDip = VerticalPaddingDip * 2
            + _iconSizeDip
            + RunningDotGapDip
            + RunningDotSizeDip;
        _autoHide = new DockAutoHideController(config.Dock.AutoHide, Dispatcher);

        // Keep XAML padding in sync with the height budget used by SetWindowPos.
        Surface.Padding = new Thickness(
            HorizontalPaddingDip,
            VerticalPaddingDip,
            HorizontalPaddingDip,
            VerticalPaddingDip);
    }

    /// <summary>
    /// Re-applies config-driven settings (e.g. auto-hide behavior).
    /// </summary>
    internal void ApplyConfigChanges()
    {
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
        }
        else
        {
            CancelYAnimation();
            DockHitTest.ApplyInteractive(hwnd);
            ApplyPlacement();
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

        _appearance = new WindowAppearance(this, hwnd, Backdrop, Corners);
        _appearance.Apply();

        DockHitTest.ApplyInteractive(hwnd);

        FullscreenState.Instance.Changed += OnFullscreenChanged;
        WorkAreaChanged.Raised += OnWorkAreaChanged;

        _pinSource = new TaskbarPinnedAppsSource(Dispatcher);
        _runningSource = new RunningAppsSource(Dispatcher);
        _composer = new DockAppsComposer(_pinSource, _runningSource, _config.PinnedApps);
        _activation = new WindowActivationService();
        _composer.Changed += OnComposerChanged;
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

        if (FullscreenState.Instance.IsFullscreen)
        {
            OnFullscreenChanged(this, EventArgs.Empty);
        }
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

        _pinSource?.Dispose();
        _pinSource = null;
        _runningSource?.Dispose();
        _runningSource = null;
        _activation = null;

        _autoHide.Changed -= OnAutoHideChanged;
        _autoHide.Dispose();

        _source?.RemoveHook(WndProc);
        _source = null;

        base.OnClosed(e);
    }

    private void OnComposerChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RebuildStrip);

    private void RebuildStrip()
    {
        Pins.Items.Clear();
        IReadOnlyList<DockAppItem> items = _composer!.Items;
        bool sawUnpinned = false;

        for (int i = 0; i < items.Count; i++)
        {
            DockAppItem item = items[i];
            if (!item.IsPinned && !sawUnpinned && Pins.Items.Count > 0)
            {
                sawUnpinned = true;
                Pins.Items.Add(CreateSeparator());
            }

            Pins.Items.Add(CreateIconVisual(item, i < items.Count - 1));
        }

        ApplyPlacement();
    }

    private FrameworkElement CreateSeparator()
    {
        return new System.Windows.Shapes.Rectangle
        {
            Width = 1,
            Opacity = 0.3,
            Margin = new Thickness(6, 2, 6, 2),
            Fill = (Brush)FindResource("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    private FrameworkElement CreateIconVisual(DockAppItem item, bool trailingMargin)
    {
        int sizePx = ResolveIconPixelSize();
        var transform = new ScaleTransform(1, 1);
        var image = new System.Windows.Controls.Image
        {
            Width = _iconSizeDip,
            Height = _iconSizeDip,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            Source = string.IsNullOrWhiteSpace(item.Path)
                ? _icons.GetIcon(string.Empty, sizePx)
                : _icons.GetIcon(item.Path, sizePx),
            ToolTip = item.Label,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = transform,
            Cursor = Cursors.Hand,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        image.MouseEnter += (_, _) => AnimateScale(transform, HoverScale);
        image.MouseLeave += (_, _) => AnimateScale(transform, 1.0);
        image.MouseLeftButtonUp += (_, _) => _activation!.LaunchOrActivate(item, _launcher);

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        if (trailingMargin)
        {
            stack.Margin = new Thickness(0, 0, IconGapDip, 0);
        }

        stack.Children.Add(image);

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = RunningDotSizeDip,
            Height = RunningDotSizeDip,
            Margin = new Thickness(0, RunningDotGapDip, 0, 0),
            Fill = (Brush)FindResource("TextFillColorPrimaryBrush"),
            Visibility = item.IsRunning ? Visibility.Visible : Visibility.Hidden,
        };
        stack.Children.Add(dot);
        return stack;
    }

    private int ResolveIconPixelSize()
    {
        uint dpi = _source is null ? 96u : Display.GetDpi(_source.Handle);
        // Request at least the DIP size in px; prefer even sizes for GDI icons.
        int px = Display.DipToPixels(_iconSizeDip, dpi);
        return Math.Clamp(px <= 0 ? 24 : px, 16, 256);
    }

    private static void AnimateScale(ScaleTransform transform, double to)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(HoverDurationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
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

        uint dpi = Display.GetDpi(hwnd);
        int heightPx = Display.DipToPixels(_dockHeightDip, dpi);
        int bottomGapPx = Display.DipToPixels(BottomGapDip, dpi);
        RECT monitor = Display.GetPrimaryMonitorBounds(hwnd);

        if (monitor.Width <= 0 || heightPx <= 0)
        {
            return false;
        }

        int widthPx = MeasureDesiredWidthPx(dpi);
        int minWidthPx = Display.DipToPixels(MinWidthDip, dpi);
        widthPx = Math.Clamp(widthPx, minWidthPx, monitor.Width);

        int x = monitor.Left + (monitor.Width - widthPx) / 2;
        int visibleY = monitor.Bottom - heightPx - bottomGapPx;
        int hiddenY = monitor.Bottom;

        geo = new PlacementGeometry(x, visibleY, hiddenY, widthPx, heightPx);
        return true;
    }

    private EdgeProbeContext BuildEdgeProbe()
    {
        IntPtr hwnd = _source!.Handle;
        uint dpi = Display.GetDpi(hwnd);
        RECT monitor = Display.GetPrimaryMonitorBounds(hwnd);
        int heightPx = Display.DipToPixels(_dockHeightDip, dpi);
        int widthPx = MeasureDesiredWidthPx(dpi);
        int minWidthPx = Display.DipToPixels(MinWidthDip, dpi);
        widthPx = Math.Clamp(widthPx, minWidthPx, monitor.Width);

        int x = monitor.Left + (monitor.Width - widthPx) / 2;
        int yVisible = monitor.Bottom - heightPx;

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

    private int MeasureDesiredWidthPx(uint dpi)
    {
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Display.DipToPixels(Surface.DesiredSize.Width, dpi);
    }

    private void OnWorkAreaChanged(object? sender, EventArgs e)
    {
        CancelYAnimation();
        ApplyPlacement();
    }

    private void OnFullscreenChanged(object? sender, EventArgs e)
    {
        if (FullscreenState.Instance.IsFullscreen)
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
