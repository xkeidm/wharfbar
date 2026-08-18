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
        _source?.AddHook(WndProc);
        DockHitTest.ApplyPassthrough(_source?.Handle ?? IntPtr.Zero);
        ApplyRect();
    }

    internal void ApplyTint(Color color, int opacity)
    {
        byte alpha = (byte)(Math.Clamp(opacity, 0, 100) * 255 / 100);
        VeilSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    internal void SetVisible(bool visible, bool animate)
    {
        _targetVisible = visible;

        if (visible)
        {
            if (!IsVisible)
            {
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
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED)
        {
            ApplyRect();
        }

        return IntPtr.Zero;
    }
}
