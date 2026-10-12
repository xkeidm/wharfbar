using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace WharfBar.Views;

/// <summary>
/// Scales a minimize snapshot toward a dock icon. The foreign window is not moved.
/// </summary>
internal sealed class DockMinimizeCue : IDisposable
{
    private readonly Popup _popup;
    private readonly Image _image;
    private readonly ScaleTransform _scale;
    private bool _disposed;

    internal DockMinimizeCue(Popup popup, Image image, ScaleTransform scale)
    {
        _popup = popup;
        _image = image;
        _scale = scale;
        _popup.Placement = PlacementMode.Absolute;
        _popup.AllowsTransparency = true;
        _image.IsHitTestVisible = false;
    }

    internal void Play(BitmapSource snapshot, Rect windowDip, Point iconDip)
    {
        if (_disposed || snapshot is null)
        {
            return;
        }

        _image.BeginAnimation(UIElement.OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _popup.BeginAnimation(Popup.HorizontalOffsetProperty, null);
        _popup.BeginAnimation(Popup.VerticalOffsetProperty, null);

        _image.Source = snapshot;
        _image.Width = Math.Max(1, windowDip.Width);
        _image.Height = Math.Max(1, windowDip.Height);
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
        _image.Opacity = 1;
        _popup.HorizontalOffset = windowDip.Left;
        _popup.VerticalOffset = windowDip.Top;
        _popup.IsOpen = true;

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(Services.DockMinimizeMotion.DurationMs);
        var scaleAnim = new DoubleAnimation(1, 0.12, duration) { EasingFunction = ease };
        var fade = new DoubleAnimation(1, 0.2, duration) { EasingFunction = ease };
        double endX = iconDip.X - (windowDip.Width * 0.06);
        double endY = iconDip.Y - (windowDip.Height * 0.06);
        var moveX = new DoubleAnimation(windowDip.Left, endX, duration) { EasingFunction = ease };
        var moveY = new DoubleAnimation(windowDip.Top, endY, duration) { EasingFunction = ease };
        moveY.Completed += (_, _) =>
        {
            if (!_disposed)
            {
                _popup.IsOpen = false;
                _image.Source = null;
            }
        };

        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
        _image.BeginAnimation(UIElement.OpacityProperty, fade);
        _popup.BeginAnimation(Popup.HorizontalOffsetProperty, moveX);
        _popup.BeginAnimation(Popup.VerticalOffsetProperty, moveY);
    }

    public void Dispose()
    {
        _disposed = true;
        _popup.IsOpen = false;
        _image.Source = null;
    }
}
