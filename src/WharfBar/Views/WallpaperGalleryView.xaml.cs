using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class WallpaperGalleryView : UserControl
{
    private const double TileSize = 80;
    private const int DecodeWidth = 160;

    private WallpaperService? _service;

    public WallpaperGalleryView()
    {
        InitializeComponent();
    }

    public event EventHandler? Applied;

    internal void Attach(WallpaperService service)
    {
        _service = service;
        Refresh();
    }

    internal void Refresh()
    {
        ContentHost.Children.Clear();
        if (_service is null)
        {
            return;
        }

        IReadOnlyList<WallpaperItem> items = WallpaperCatalog.List(_service.BundledRoot, _service.UserRoot);
        string? current = _service.TryGetCurrentPath();

        var bundled = items.Where(i => i.Source == WallpaperSource.Bundled).ToList();
        var user = items.Where(i => i.Source == WallpaperSource.User).ToList();

        if (bundled.Count == 0 && user.Count == 0)
        {
            ContentHost.Children.Add(new TextBlock
            {
                Text = "Duvar kağıdı yok. Klasöre görsel ekleyebilirsin.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8),
                Opacity = 0.7,
            });
            return;
        }

        AddSection("WharfBar", bundled, current);
        AddSection("Senin", user, current);
    }

    private void AddSection(string title, List<WallpaperItem> items, string? current)
    {
        if (items.Count == 0)
        {
            return;
        }

        ContentHost.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.7,
            Margin = new Thickness(2, ContentHost.Children.Count == 0 ? 0 : 10, 0, 6),
        });

        var panel = new WrapPanel();
        foreach (WallpaperItem item in items)
        {
            panel.Children.Add(BuildTile(item, current));
        }

        ContentHost.Children.Add(panel);
    }

    private UIElement BuildTile(WallpaperItem item, string? current)
    {
        bool selected = current is not null
            && string.Equals(Path.GetFullPath(item.Path), current, StringComparison.OrdinalIgnoreCase);

        UIElement visual = CreateThumbnail(item.Path);

        var inner = new Border
        {
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = visual,
        };

        var tile = new Border
        {
            Width = TileSize,
            Height = TileSize,
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = item.DisplayName,
            Background = Brushes.Transparent,
            Child = inner,
            Tag = item.Path,
        };

        if (selected)
        {
            tile.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        }
        else
        {
            tile.BorderBrush = Brushes.Transparent;
        }

        tile.MouseLeftButtonUp += Tile_MouseLeftButtonUp;
        return tile;
    }

    private static UIElement CreateThumbnail(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = DecodeWidth;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return new Image { Source = bmp, Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException
                                       or UnauthorizedAccessException or InvalidOperationException
                                       or FileFormatException or ArgumentException)
        {
            return CreatePlaceholder();
        }
    }

    private static UIElement CreatePlaceholder()
    {
        var border = new Border { CornerRadius = new CornerRadius(6) };
        border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        var glyph = new TextBlock
        {
            Text = "\uE91B",
            FontSize = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFontFamily");
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        border.Child = glyph;
        return border;
    }

    private void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_service is null || sender is not FrameworkElement { Tag: string path })
        {
            return;
        }

        e.Handled = true;
        if (_service.TryApply(path))
        {
            Refresh();
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_service is null)
        {
            return;
        }

        try
        {
            _service.EnsureUserRoot();
            Process.Start(new ProcessStartInfo { FileName = _service.UserRoot, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"WallpaperGalleryView open folder failed: {ex.Message}");
        }
    }
}
