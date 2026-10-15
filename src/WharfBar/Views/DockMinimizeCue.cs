using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WharfBar.Services;

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
        var duration = TimeSpan.FromMilliseconds(DockMinimizeMotion.DurationMs);
        double scaleMin = DockMinimizeMotion.CueScaleMin;
        var scaleAnim = new DoubleAnimation(1, scaleMin, duration) { EasingFunction = ease };
        var fade = new DoubleAnimation(1, 0.2, duration) { EasingFunction = ease };
        Point end = DockMinimizeMotion.MinimizeEndOffset(iconDip, windowDip);
        var moveX = new DoubleAnimation(windowDip.Left, end.X, duration) { EasingFunction = ease };
        var moveY = new DoubleAnimation(windowDip.Top, end.Y, duration) { EasingFunction = ease };
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

    internal void Cancel()
    {
        if (_disposed)
        {
            return;
        }

        _image.BeginAnimation(UIElement.OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _popup.BeginAnimation(Popup.HorizontalOffsetProperty, null);
        _popup.BeginAnimation(Popup.VerticalOffsetProperty, null);
        _popup.IsOpen = false;
        _image.Source = null;
    }

    internal void PlayRestore(BitmapSource snapshot, Rect windowDip, Point iconDip, Action? completed = null)
    {
        if (_disposed || snapshot is null)
        {
            completed?.Invoke();
            return;
        }

        Cancel();

        Point start = DockMinimizeMotion.RestoreStartOffset(iconDip, windowDip);
        _image.Source = snapshot;
        _image.Width = Math.Max(1, windowDip.Width);
        _image.Height = Math.Max(1, windowDip.Height);
        double scaleMin = DockMinimizeMotion.CueScaleMin;
        _scale.ScaleX = scaleMin;
        _scale.ScaleY = scaleMin;
        _image.Opacity = 0.2;
        _popup.HorizontalOffset = start.X;
        _popup.VerticalOffset = start.Y;
        _popup.IsOpen = true;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(DockMinimizeMotion.DurationMs);
        var scaleAnim = new DoubleAnimation(scaleMin, 1, duration) { EasingFunction = ease };
        var fade = new DoubleAnimation(0.2, 1, duration) { EasingFunction = ease };
        var moveX = new DoubleAnimation(start.X, windowDip.Left, duration) { EasingFunction = ease };
        var moveY = new DoubleAnimation(start.Y, windowDip.Top, duration) { EasingFunction = ease };
        moveY.Completed += (_, _) =>
        {
            if (!_disposed)
            {
                _popup.IsOpen = false;
                _image.Source = null;
            }

            completed?.Invoke();
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
