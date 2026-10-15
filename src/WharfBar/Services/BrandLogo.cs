using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using WharfBar.Models;
using Wpf.Ui.Appearance;

namespace WharfBar.Services;

/// <summary>
/// The WharfBar "Dusk" mark and the Windows logo as vector geometry on a 128x128 grid
/// (ADR-019). The mark is the small solid pier (no plank gaps, no sea lines) + sun.
/// The top bar draws <see cref="BarGlyph"/> in one theme colour; the dock launch tile is a
/// themed <see cref="DrawingImage"/> from <see cref="BrandPalette"/>. Everything is frozen;
/// tiles are cached by style and palette.
/// </summary>
internal static class BrandLogo
{
    internal const double Grid = 128d;

    private const int MaxCachedTiles = 8;

    private static readonly Color FallbackAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    private static readonly Geometry TileShape = Freeze(new RectangleGeometry(new Rect(0, 0, Grid, Grid), 28, 28));

    private static readonly Geometry Sun = Parse("M42 38A22 22 0 0 1 86 38Z");

    /// <summary>Solid pier trapezoid (no plank cut-outs).</summary>
    private static readonly Geometry Pier = Parse("M55 43H73L108 112H20Z");

    private static readonly Geometry WharfMark = Group(Sun, Pier);

    /// <summary>Top bar Windows logo: heavier panes so it weighs like the mark at ~18 DIP.</summary>
    private static readonly Geometry WindowsBarPanes = Panes(origin: 20, size: 40, gap: 8);

    /// <summary>Windows logo inside the rounded tile, with room for the tile corners.</summary>
    private static readonly Geometry WindowsTilePanes = Panes(origin: 29, size: 32, gap: 6);

    private static readonly Dictionary<TileKey, DrawingImage> Tiles = new();

    /// <summary>One-colour top bar glyph. The caller fills it with the bar foreground brush.</summary>
    internal static Geometry BarGlyph(string? logoStyle) =>
        LogoStyles.IsWindows(logoStyle) ? WindowsBarPanes : WharfMark;

    /// <summary>Themed dock tile for the current accent and app theme.</summary>
    internal static ImageSource DockTile(string? logoStyle, bool useSystemAccent, int sizePx)
    {
        _ = sizePx;
        bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        return DockTile(logoStyle, BrandPalette.For(CurrentAccent(useSystemAccent), dark), sizePx);
    }

    internal static DrawingImage DockTile(string? logoStyle, BrandColors colors, int sizePx)
    {
        _ = sizePx;
        string style = LogoStyles.Normalize(logoStyle);
        var key = new TileKey(style, colors);
        lock (Tiles)
        {
            if (Tiles.TryGetValue(key, out DrawingImage? cached))
            {
                return cached;
            }

            if (Tiles.Count >= MaxCachedTiles)
            {
                Tiles.Clear();
            }

            DrawingImage image = BuildTile(key);
            Tiles[key] = image;
            return image;
        }
    }

    /// <summary>
    /// The accent the tile follows. With the system accent on, it is read fresh from Windows so
    /// a colour change shows on the next theme message; otherwise the app's applied accent.
    /// </summary>
    internal static Color CurrentAccent(bool useSystemAccent)
    {
        if (useSystemAccent)
        {
            try
            {
                return ApplicationAccentColorManager.GetColorizationColor();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar logo accent read failed: {ex.Message}");
            }
        }

        return Application.Current?.TryFindResource("SystemAccentColor") is Color applied
            ? applied
            : FallbackAccent;
    }

    private static DrawingImage BuildTile(TileKey key)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brush(key.Colors.Background), null, TileShape));

        Brush pier = Brush(key.Colors.Pier);
        if (key.Style == LogoStyles.Windows)
        {
            group.Children.Add(new GeometryDrawing(pier, null, WindowsTilePanes));
        }
        else
        {
            group.Children.Add(new GeometryDrawing(Brush(key.Colors.Sun), null, Sun));
            group.Children.Add(new GeometryDrawing(pier, null, Pier));
        }

        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Geometry Panes(double origin, double size, double gap)
    {
        double second = origin + size + gap;
        var group = new GeometryGroup();
        foreach (double y in new[] { origin, second })
        {
            foreach (double x in new[] { origin, second })
            {
                group.Children.Add(new RectangleGeometry(new Rect(x, y, size, size)));
            }
        }

        return Freeze(group);
    }

    private static Geometry Group(params Geometry[] parts)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (Geometry part in parts)
        {
            group.Children.Add(part);
        }

        return Freeze(group);
    }

    private static Geometry Parse(string data) => Freeze(Geometry.Parse(data));

    private static Brush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Geometry Freeze(Geometry geometry)
    {
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        return geometry;
    }

    private readonly record struct TileKey(string Style, BrandColors Colors);
}
