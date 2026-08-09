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
    private const double DockHeightDip = 72d;
    private const double HoverScale = 1.2d;
    private const int HoverDurationMs = 180;
    private const double MinWidthDip = 200d;

    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;
    private const DwmCornerPreference Corners = DwmCornerPreference.Round;

    private readonly AppConfig _config;
    private readonly double _iconSizeDip;
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

    internal DockWindow(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        InitializeComponent();

        _config = config;
        _iconSizeDip = Math.Clamp(
            config.Dock.IconSize <= 0 ? DockConfig.DefaultIconSize : config.Dock.IconSize,
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);
        _autoHide = new DockAutoHideController(config.Dock.AutoHide);
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

        // Preference is live from config; proximity hide remains a no-op until a later phase.
        if (_autoHide.Enabled)
        {
            _autoHide.Show();
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
            Margin = new Thickness(8, 4, 8, 4),
            Fill = (Brush)FindResource("TextFillColorPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
    }

    private FrameworkElement CreateIconVisual(DockAppItem item, bool trailingMargin)
    {
        var transform = new ScaleTransform(1, 1);
        var image = new System.Windows.Controls.Image
        {
            Width = _iconSizeDip,
            Height = _iconSizeDip,
            Stretch = Stretch.Uniform,
            Source = string.IsNullOrWhiteSpace(item.Path)
                ? _icons.GetIcon(string.Empty)
                : _icons.GetIcon(item.Path),
            ToolTip = item.Label,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = transform,
            Cursor = Cursors.Hand,
        };

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
            stack.Margin = new Thickness(0, 0, 8, 0);
        }

        stack.Children.Add(image);

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Margin = new Thickness(0, 4, 0, 0),
            Fill = (Brush)FindResource("TextFillColorPrimaryBrush"),
            Visibility = item.IsRunning ? Visibility.Visible : Visibility.Hidden,
        };
        stack.Children.Add(dot);
        return stack;
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
    /// Centers the dock on the work-area bottom in physical pixels. WPF Left/Top/Width are
    /// deliberately unused — they assume the primary monitor's scale factor.
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

        _isApplyingPlacement = true;
        try
        {
            uint dpi = Display.GetDpi(hwnd);
            int heightPx = Display.DipToPixels(DockHeightDip, dpi);
            RECT work = Display.GetPrimaryMonitorWorkArea(hwnd);

            if (work.Width <= 0 || heightPx <= 0)
            {
                return;
            }

            int widthPx = MeasureDesiredWidthPx(dpi);
            int minWidthPx = Display.DipToPixels(MinWidthDip, dpi);
            widthPx = Math.Clamp(widthPx, minWidthPx, work.Width);

            int x = work.Left + (work.Width - widthPx) / 2;
            int y = work.Bottom - heightPx;

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
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private int MeasureDesiredWidthPx(uint dpi)
    {
        Surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Display.DipToPixels(Surface.DesiredSize.Width, dpi);
    }

    private void OnWorkAreaChanged(object? sender, EventArgs e) => ApplyPlacement();

    private void OnFullscreenChanged(object? sender, EventArgs e)
    {
        if (FullscreenState.Instance.IsFullscreen)
        {
            _isFullscreenHidden = true;
            Visibility = Visibility.Collapsed;
            return;
        }

        _isFullscreenHidden = false;
        Visibility = Visibility.Visible;

        if (_source is null)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _source.Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        ApplyPlacement();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(new Action(ApplyPlacement));
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
}
