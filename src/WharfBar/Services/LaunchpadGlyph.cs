using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WharfBar.Services;

/// <summary>
/// Dock glyph for the launchpad tile: four rounded squares. The recycle-bin stock icon
/// stays on the trash tile.
/// </summary>
internal static class LaunchpadGlyph
{
    private static ImageSource? _cached;
    private static Color _cachedColor;

    internal static ImageSource Create()
    {
        Color color = Foreground();
        if (_cached is not null && _cachedColor == color)
        {
            return _cached;
        }

        const int size = 32;
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            const double cell = 12;
            const double gap = 4;
            const double origin = 2;
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    dc.DrawRoundedRectangle(
                        brush,
                        null,
                        new Rect(origin + (col * (cell + gap)), origin + (row * (cell + gap)), cell, cell),
                        3,
                        3);
                }
            }
        }

        bitmap.Render(visual);
        bitmap.Freeze();
        _cached = bitmap;
        _cachedColor = color;
        return bitmap;
    }

    private static Color Foreground()
    {
        if (Application.Current?.TryFindResource("TextFillColorPrimaryBrush") is SolidColorBrush brush)
        {
            return brush.Color;
        }

        return Color.FromRgb(0xF3, 0xF3, 0xF3);
    }
}
