using System.Windows;
using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>
/// Flyout surface: a semi-transparent version of the theme background color (like the dock's Acrylic surface).
/// The content behind shows through faintly; it is regenerated on every open, so theme changes are followed.
/// </summary>
internal static class FlyoutSurface
{
    /// <summary>Surface opacity (~94%) — the remaining ~6% lets the content behind show through faintly.</summary>
    private const byte Alpha = 0xF0;

    internal static SolidColorBrush Create()
    {
        Color color = (Application.Current?.TryFindResource("ApplicationBackgroundBrush") as SolidColorBrush)?.Color
            ?? Color.FromRgb(0x20, 0x20, 0x20);
        color.A = Alpha;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
