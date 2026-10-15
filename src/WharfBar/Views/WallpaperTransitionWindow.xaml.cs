using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Crossfades the previous wallpaper into the next one on the shell WorkerW
/// (behind desktop icons). Attaches as a WorkerW child at 1×1 before growing so the
/// shell never sees a top-level fullscreen HWND (<c>ABN_FULLSCREENAPP</c> would hide the dock).
/// </summary>
public partial class WallpaperTransitionWindow : Window
{
    private HwndSource? _source;
    private Action? _completed;
    private bool _finished;
    private BlurEffect? _blur;

    public WallpaperTransitionWindow()
    {
        AllowsTransparency = true;
        // Stay 1×1 off-screen until parented under WorkerW. A top-level monitor-sized
        // Show() trips ABN_FULLSCREENAPP and collapses the dock for the whole fade.
        Width = 1;
        Height = 1;
        Left = -32000;
        Top = -32000;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source is not null)
        {
            ApplyOverlayStyles(_source.Handle);
            if (_source.CompositionTarget is { } target)
            {
                target.BackgroundColor = Colors.Black;
            }

            Dwm.SetSystemBackdrop(_source.Handle, DwmSystemBackdrop.None);
        }
    }

    private static void ApplyOverlayStyles(IntPtr hwnd)
    {
        WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>Decodes into a frozen, fully loaded bitmap; null on failure.</summary>
    internal static BitmapSource? TryDecode(string imagePath, int monitorPx)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = WallpaperTransitionMotion.DecodePixelWidth(monitorPx);
            bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Crossfades <paramref name="from"/> → <paramref name="to"/> behind desktop icons.
    /// Hosts on WorkerW when available; returns false if the host cannot be attached.
    /// </summary>
    internal bool TryPlay(
        BitmapSource from,
        BitmapSource to,
        bool useBlur,
        Action? completed)
    {
        _completed = completed;
        _finished = false;

        IntPtr worker = DesktopWallpaperHost.TryGetWorkerW();
        if (worker == IntPtr.Zero)
        {
            return false;
        }

        FromLayer.Source = from;
        ToLayer.Source = to;
        FromLayer.Opacity = 1;
        ToLayer.Opacity = 0;
        FromScale.ScaleX = FromScale.ScaleY = 1;
        ToScale.ScaleX = ToScale.ScaleY = WallpaperTransitionMotion.FadeScaleEnd;

        if (useBlur)
        {
            _blur = new BlurEffect
            {
                Radius = 0.5,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Quality,
            };
            FromLayer.Effect = _blur;
        }
        else
        {
            _blur = null;
            FromLayer.Effect = null;
        }

        // HWND first at 1×1 off-screen, parent under WorkerW, then grow. Never paint
        // a top-level fullscreen frame — that hides the dock via ABN_FULLSCREENAPP.
        Show();
        IntPtr hwnd = _source?.Handle ?? new WindowInteropHelper(this).EnsureHandle();
        _source ??= (HwndSource?)PresentationSource.FromVisual(this);
        ApplyOverlayStyles(hwnd);
        if (_source is not null)
        {
            Dwm.SetSystemBackdrop(_source.Handle, DwmSystemBackdrop.None);
        }

        if (!NativeMethods.GetClientRect(worker, out RECT client)
            || client.Right <= client.Left
            || client.Bottom <= client.Top)
        {
            Finish(close: true, invokeCompleted: false);
            return false;
        }

        _ = NativeMethods.SetParent(hwnd, worker);
        _ = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOP,
            0,
            0,
            client.Right - client.Left,
            client.Bottom - client.Top,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        int ms = WallpaperTransitionMotion.DurationMs(useBlur);
        var clock = TimeSpan.FromMilliseconds(ms);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var fromFade = new DoubleAnimation(1d, 0d, clock) { EasingFunction = ease };
        var toFade = new DoubleAnimation(0d, 1d, clock) { EasingFunction = ease };
        fromFade.Completed += (_, _) => Finish(close: true);

        FromLayer.BeginAnimation(OpacityProperty, fromFade);
        ToLayer.BeginAnimation(OpacityProperty, toFade);

        if (useBlur && _blur is not null)
        {
            var blurAnim = new DoubleAnimation(0.5, WallpaperTransitionMotion.BlurRadiusEnd, clock)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            _blur.BeginAnimation(BlurEffect.RadiusProperty, blurAnim);
        }
        else
        {
            double end = WallpaperTransitionMotion.FadeScaleEnd;
            FromScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(1d, end, clock) { EasingFunction = ease });
            FromScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(1d, end, clock) { EasingFunction = ease });
            ToScale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                new DoubleAnimation(end, 1d, clock) { EasingFunction = ease });
            ToScale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                new DoubleAnimation(end, 1d, clock) { EasingFunction = ease });
        }

        return true;
    }

    internal void Cancel() => Finish(close: true, invokeCompleted: false);

    private static int MarshalGetLastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();

    private void Finish(bool close, bool invokeCompleted = true)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        FromLayer.BeginAnimation(OpacityProperty, null);
        ToLayer.BeginAnimation(OpacityProperty, null);
        FromScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        FromScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ToScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ToScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        if (_blur is not null)
        {
            _blur.BeginAnimation(BlurEffect.RadiusProperty, null);
        }

        // Commit the real wallpaper while the "to" frame still covers WorkerW, then tear down.
        Action? cb = _completed;
        _completed = null;
        if (invokeCompleted)
        {
            cb?.Invoke();
        }

        FromLayer.Source = null;
        ToLayer.Source = null;
        FromLayer.Effect = null;
        _blur = null;

        if (close)
        {
            try
            {
                IntPtr hwnd = _source?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                {
                    // Hide + shrink before unparenting so teardown does not flash a
                    // top-level fullscreen HWND (same ABN_FULLSCREENAPP dock collapse).
                    _ = NativeMethods.SetWindowPos(
                        hwnd,
                        IntPtr.Zero,
                        -32000,
                        -32000,
                        1,
                        1,
                        NativeMethods.SWP_HIDEWINDOW
                        | NativeMethods.SWP_NOACTIVATE
                        | NativeMethods.SWP_NOZORDER);
                    _ = NativeMethods.SetParent(hwnd, IntPtr.Zero);
                }

                Close();
            }
            catch (InvalidOperationException)
            {
                // Already closing.
            }
        }
    }
}
