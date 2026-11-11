using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Dock glyph: themed brand tile for Launchpad, themed Task View glyph, package logo for an
/// AppUserModelId, shell icon for a file or folder, stock recycle icon for Trash, letter glyph
/// when the asset is missing.
/// </summary>
internal static class DockIconFactory
{
    /// <summary>Segoe MDL2 Assets / Segoe Fluent Icons "TaskView" glyph.</summary>
    private const string TaskViewGlyph = "\uE7C4";

    /// <summary>Rendered at this em size and scaled to the target box, so the outline stays sharp.</summary>
    private const double GlyphEmSize = 100d;

    /// <summary>Ink-to-box ratio: a shell icon does not touch the edges of its 24 DIP box either.</summary>
    private const double GlyphInkScale = 0.88d;

    private const int MaxCachedGlyphs = 8;

    private static readonly Color FallbackGlyphColor = Color.FromRgb(0x88, 0x88, 0x88);

    private static readonly Dictionary<(Color Color, int Size), ImageSource> Glyphs = new();

    internal static ImageSource Load(
        AppIconService icons,
        DockAppItem item,
        int sizePx,
        string? logoStyle = LogoStyles.Default,
        bool useSystemAccent = true)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(item);

        if (item.Kind == DockTileKind.Launchpad)
        {
            return BrandLogo.DockTile(logoStyle, useSystemAccent, sizePx);
        }

        if (item.Kind == DockTileKind.TaskView)
        {
            return TaskView(sizePx);
        }

        if (item.Kind == DockTileKind.Trash)
        {
            return StockOrLetter(item.Label);
        }

        string? aumid = PackageLogoSelection.TrySplitAumid(item.Id, out _, out _)
            ? item.Id
            : PackageLogoSelection.TrySplitAumid(item.Path, out _, out _)
                ? item.Path
                : null;
        if (aumid is not null)
        {
            return PackageIconResolver.TryLoad(aumid, item.Label, sizePx)
                ?? TrayLetterGlyph.Build(item.Label);
        }

        string? directory = item.Kind switch
        {
            DockTileKind.Downloads => string.IsNullOrWhiteSpace(item.Path) ? null : item.Path,
            DockTileKind.Folder => DockBuiltInTiles.ResolveDirectory(item.Path),
            _ => Directory.Exists(item.Path) ? item.Path : null,
        };

        if (directory is not null && TryShell(directory, sizePx, out ImageSource? folderIcon) && folderIcon is not null)
        {
            return folderIcon;
        }

        return icons.GetIcon(item.Path, sizePx);
    }

    /// <summary>
    /// Themed Task View glyph. The dock cell hosts an <c>Image</c>, not a <c>TextBlock</c>, so
    /// this is a frozen bitmap like <see cref="BrandLogo.DockTile"/>: the palette is read from
    /// the applied theme dictionary and the image is rebuilt through the dock's theme-change
    /// path (<c>RefreshBrandTile</c>), which is what keeps a light/dark switch from leaving the
    /// old colour behind.
    /// </summary>
    private static ImageSource TaskView(int sizePx)
    {
        int size = Math.Clamp(sizePx, 16, 256);
        Color color = GlyphColor();
        var key = (color, size);
        lock (Glyphs)
        {
            if (Glyphs.TryGetValue(key, out ImageSource? cached))
            {
                return cached;
            }

            if (Glyphs.Count >= MaxCachedGlyphs)
            {
                Glyphs.Clear();
            }

            ImageSource image;
            try
            {
                image = BuildGlyph(TaskViewGlyph, size, color);
            }
            catch (Exception ex)
            {
                // A dock rebuild must never die on one glyph: fall back like the shell paths do.
                Debug.WriteLine($"WharfBar task view glyph failed: {ex.Message}");
                image = TrayLetterGlyph.Build("T");
            }

            Glyphs[key] = image;
            return image;
        }
    }

    /// <summary>The app theme's primary text colour; the shell taskbar button is themed the same way.</summary>
    private static Color GlyphColor()
    {
        if (Application.Current?.TryFindResource("TextFillColorPrimaryBrush") is SolidColorBrush brush)
        {
            return brush.Color;
        }

        // Unit tests and very early startup have no theme dictionary yet.
        return FallbackGlyphColor;
    }

    private static ImageSource BuildGlyph(string glyph, int sizePx, Color color)
    {
        var typeface = new Typeface(
            SystemVersion.IconFontFamily,
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        var text = new FormattedText(
            glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            GlyphEmSize,
            new SolidColorBrush(color),
            pixelsPerDip: 1d);
        Geometry ink = text.BuildGeometry(new Point(0, 0));
        Rect bounds = ink.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            // The icon font is missing: keep a readable tile instead of an empty slot.
            return TrayLetterGlyph.Build("T");
        }

        double span = sizePx * GlyphInkScale;
        double scale = Math.Min(span / bounds.Width, span / bounds.Height);
        var place = new TransformGroup();
        place.Children.Add(new ScaleTransform(scale, scale));
        place.Children.Add(new TranslateTransform(
            ((sizePx - (bounds.Width * scale)) / 2d) - (bounds.X * scale),
            ((sizePx - (bounds.Height * scale)) / 2d) - (bounds.Y * scale)));

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.PushTransform(place);
            dc.DrawGeometry(new SolidColorBrush(color), null, ink);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(sizePx, sizePx, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource StockOrLetter(string label)
    {
        if (!RecycleBinNative.TryGetStockIcon(out IntPtr hIcon))
        {
            return TrayLetterGlyph.Build(label);
        }

        try
        {
            return FromIcon(hIcon);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar recycle icon failed: {ex.Message}");
            return TrayLetterGlyph.Build(label);
        }
        finally
        {
            ShellIcon.Destroy(hIcon);
        }
    }

    private static bool TryShell(string path, int sizePx, out ImageSource? source)
    {
        source = null;
        if (!ShellIcon.TryExtract(path, sizePx, out IntPtr hBitmap, out IntPtr hIcon))
        {
            return false;
        }

        try
        {
            source = hBitmap != IntPtr.Zero
                ? Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions())
                : FromIcon(hIcon);
            source.Freeze();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar dock shell icon failed: {ex.Message}");
            source = null;
            return false;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
            }

            ShellIcon.Destroy(hIcon);
        }
    }

    private static ImageSource FromIcon(IntPtr hIcon)
    {
        ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
            hIcon,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
