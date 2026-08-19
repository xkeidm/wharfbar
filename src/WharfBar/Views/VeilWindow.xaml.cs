using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WharfBar.Native;

namespace WharfBar.Views;

/// <summary>
/// Full-screen click-through color overlay (ColorVeil style). Always topmost; never activates;
/// covers the primary monitor only (v1). No Mica — plain color fill, so AllowsTransparency is safe.
/// </summary>
public partial class VeilWindow : Window
{
    private const double FadeMilliseconds = 280d;

    private HwndSource? _source;
    private bool _targetVisible;

    public VeilWindow()
    {
        // Must be set before InitializeComponent — WPF locks AllowsTransparency after creation.
        AllowsTransparency = true;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source is not null)
        {
            WindowOverlayStyles.ExcludeFromTaskSwitcher(_source.Handle);
            _source.AddHook(WndProc);
        }

        EnsureTransparentComposition();
        ClearSystemBackdrop();
        DockHitTest.ApplyPassthrough(_source?.Handle ?? IntPtr.Zero);
        ApplyRect();
    }

    internal void ApplyTint(Color color, int opacity)
    {
        byte alpha = (byte)(Math.Clamp(opacity, 0, 100) * 255 / 100);
        VeilSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        EnsureTransparentComposition();
        ClearSystemBackdrop();
    }

    /// <summary>
    /// Re-asserts the click-through + transparent layered state. Call after startup settles
    /// (theme applies can re-apply a DWM system backdrop to this window) and before showing.
    /// </summary>
    internal void ReassertBackdrop()
    {
        EnsureTransparentComposition();
        ClearSystemBackdrop();
    }

    /// <summary>
    /// Layered windows render the whole surface with <see cref="HwndSource.CompositionTarget"/>
    /// as backing; an opaque fill there turns the veil into a solid screen-covering rectangle.
    /// Mirrors WindowAppearance.SetLayeredWindowBackground.
    /// </summary>
    private void EnsureTransparentComposition()
    {
        if (_source?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }
    }

    /// <summary>
    /// Clears any DWM system backdrop (Mica). A Mica material on a full-screen layered window
    /// renders as an opaque dark surface that hides the desktop and every window behind it —
    /// WPF-UI theme applies can attach it, so re-assert whenever the veil (re)appears.
    /// </summary>
    private void ClearSystemBackdrop()
    {
        if (_source?.Handle is { } hwnd && hwnd != IntPtr.Zero)
        {
            Dwm.SetSystemBackdrop(hwnd, DwmSystemBackdrop.None);
        }
    }

    internal void SetVisible(bool visible, bool animate)
    {
        _targetVisible = visible;

        if (visible)
        {
            if (!IsVisible)
            {
                ClearSystemBackdrop();
                Show();
            }

            if (animate)
            {
                AnimateOpacityTo(1d, completed: null);
            }
            else
            {
                Opacity = 1d;
            }
        }
        else if (animate)
        {
            AnimateOpacityTo(0d, completed: () =>
            {
                if (!_targetVisible)
                {
                    Hide();
                }
            });
        }
        else
        {
            Hide();
        }
    }

    private void AnimateOpacityTo(double to, Action? completed)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(FadeMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        BeginAnimation(OpacityProperty, animation);
    }

    private void ApplyRect()
    {
        if (_source is null)
        {
            return;
        }

        RECT rc = Display.GetPrimaryMonitorBounds(_source.Handle);
        if (rc.Right <= rc.Left || rc.Bottom <= rc.Top)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _source.Handle,
            NativeMethods.HWND_TOPMOST,
            rc.Left,
            rc.Top,
            rc.Right - rc.Left,
            rc.Bottom - rc.Top,
            NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED or NativeMethods.WM_THEMECHANGED)
        {
            ApplyRect();
            ClearSystemBackdrop();
        }

        return IntPtr.Zero;
    }
}
