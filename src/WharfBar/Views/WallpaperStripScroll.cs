using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Animates a <see cref="ScrollViewer"/>'s horizontal offset with the gallery easing.
/// ScrollViewer offsets are not animatable directly; the attached property bridges that.
/// The base value always tracks the real offset, because <c>FillBehavior.Stop</c> reverts
/// to the base value when the animation completes — otherwise the strip would snap back.
/// </summary>
internal static class WallpaperStripScroll
{
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "HorizontalOffset",
            typeof(double),
            typeof(WallpaperStripScroll),
            new PropertyMetadata(0d, OnHorizontalOffsetChanged));

    internal static void Page(ScrollViewer? strip, double target)
    {
        if (strip is null || strip.ScrollableWidth <= 0)
        {
            return;
        }

        double clamped = Math.Clamp(target, 0, strip.ScrollableWidth);
        double current = strip.HorizontalOffset;

        // Land the base value on the current offset before clearing a running animation,
        // so removing one (e.g. replaced by a new page) cannot bounce the strip back.
        strip.SetValue(HorizontalOffsetProperty, current);
        strip.BeginAnimation(HorizontalOffsetProperty, null);

        var animation = new DoubleAnimation(current, clamped, WallpaperGalleryMotion.StripScroll)
        {
            EasingFunction = WallpaperGalleryMotion.CreateEase(),
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) =>
        {
            // Drop the animation with the base value already at the final offset so the
            // FillBehavior.Stop revert lands exactly there (no snap-back to zero).
            strip.SetValue(HorizontalOffsetProperty, clamped);
            strip.ScrollToHorizontalOffset(clamped);
            strip.BeginAnimation(HorizontalOffsetProperty, null);
        };
        strip.BeginAnimation(HorizontalOffsetProperty, animation);
    }

    private static void OnHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer strip && e.NewValue is double offset)
        {
            strip.ScrollToHorizontalOffset(offset);
        }
    }
}
