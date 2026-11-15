using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// One captured pre-switch frame used as the top layer of the light/dark crossfade. It covers the
/// source window's content rectangle (physical pixels), shows the old palette, and fades out over
/// <see cref="ThemeTransitionPolicy.Crossfade"/> so the palette swap underneath reads as a
/// crossfade instead of a hard flip.
///
/// The window is invisible to input and activation (<c>WS_EX_NOACTIVATE</c>,
/// <c>WS_EX_TRANSPARENT</c>, <c>WS_EX_TOOLWINDOW</c>) and is excluded from Alt+Tab, so a 180 ms
/// overlay can never steal a click or focus from the surface it covers.
/// </summary>
internal sealed class ThemeCrossfadeOverlayWindow : Window
{
    private readonly Image _frame;
    private readonly Rect _physicalRect;
    private readonly double _dpiScale;
    private DispatcherTimer? _watchdog;
    private bool _finished;

    /// <param name="frame">Old-palette snapshot of the source window's content.</param>
    /// <param name="physicalRect">
    /// On-screen rectangle of that content in physical pixels (the overlay is positioned with
    /// <c>SetWindowPos</c>, so it lands exactly where it was captured regardless of DPI).
    /// </param>
    /// <param name="dpiScale">Effective scale of the source monitor, for the initial DIP size.</param>
    internal ThemeCrossfadeOverlayWindow(ImageSource frame, Rect physicalRect, double dpiScale)
    {
        _physicalRect = physicalRect;
        _dpiScale = dpiScale > 0 ? dpiScale : 1;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;

        // DIP size mirrors the captured rect so WPF's own sizing agrees with SetWindowPos.
        Width = Math.Max(1, physicalRect.Width / _dpiScale);
        Height = Math.Max(1, physicalRect.Height / _dpiScale);
        Left = 0;
        Top = 0;

        _frame = new Image
        {
            Source = frame,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true,
        };
        Content = _frame;
        SourceInitialized += (_, _) => ApplyOverlayWindow();
    }

    private void ApplyOverlayWindow()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);
        WindowOverlayStyles.MakeClickThroughNoActivate(hwnd);

        _ = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            (int)Math.Round(_physicalRect.X),
            (int)Math.Round(_physicalRect.Y),
            Math.Max(1, (int)Math.Round(_physicalRect.Width)),
            Math.Max(1, (int)Math.Round(_physicalRect.Height)),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Fades the captured frame out and closes the overlay. <paramref name="finished"/> runs exactly
    /// once, so the caller can drop its reference without worrying about the animation clock.
    /// </summary>
    internal void FadeOut(Action finished)
    {
        var fade = new DoubleAnimation(1, 0, ThemeTransitionPolicy.Crossfade)
        {
            EasingFunction = ThemeTransitionPolicy.CreateEase(),
            FillBehavior = FillBehavior.HoldEnd,
        };
        fade.Completed += (_, _) => Finish(finished);
        _frame.BeginAnimation(OpacityProperty, fade);

        // Safety net for a dispatcher that stops pumping animations (shutdown, remote session):
        // the overlay must never outlive the fade by more than a few hundred ms.
        _watchdog = new DispatcherTimer(
            TimeSpan.FromMilliseconds(ThemeTransitionPolicy.CrossfadeMs + 400),
            DispatcherPriority.Background,
            (_, _) => Finish(finished),
            Dispatcher);
    }

    /// <summary>
    /// Drops the overlay immediately (a newer theme switch superseded this frame, or the transition
    /// failed) without running the fade completion callback.
    /// </summary>
    internal void Cancel()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _watchdog?.Stop();
        _watchdog = null;

        try
        {
            _frame.BeginAnimation(OpacityProperty, null);
            _frame.Source = null;
            Close();
        }
        catch (InvalidOperationException)
        {
            // Already closing/closed (app shutdown).
        }
    }

    private void Finish(Action finished)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _watchdog?.Stop();
        _watchdog = null;

        try
        {
            _frame.Source = null;
            _frame.BeginAnimation(OpacityProperty, null);
            Close();
        }
        catch (InvalidOperationException)
        {
            // Already closing/closed (app shutdown): nothing left to release.
        }

        finished();
    }
}
