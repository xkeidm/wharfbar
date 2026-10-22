using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class WallpaperGalleryView : UserControl
{
    private const double PreviewDisplayWidthDip = 388;
    private static readonly Color BadgeTextColor = Color.FromRgb(0x12, 0x12, 0x1A);

    private WallpaperService? _service;
    private bool _hasPreviewSource;

    /// <summary>Strip tiles keyed by full path, so the accent ring can move without a rebuild.</summary>
    private readonly Dictionary<string, (Border Tile, Border Badge)> _tilesByPath =
        new(StringComparer.OrdinalIgnoreCase);

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
        _tilesByPath.Clear();
        PreviewTop.BeginAnimation(Image.OpacityProperty, null);
        PreviewTop.Source = null;
        PreviewTop.Visibility = Visibility.Collapsed;
        PreviewTop.Opacity = 0;
        PreviewBase.Source = null;
        PreviewBase.Visibility = Visibility.Collapsed;
        PreviewBadge.Visibility = Visibility.Collapsed;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        _hasPreviewSource = false;

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

        ShowPreview(current, animate: false);
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
            Margin = new Thickness(2, ContentHost.Children.Count == 0 ? 2 : 6, 0, 4),
        });

        var stripViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            PanningMode = PanningMode.HorizontalOnly,
        };
        var stripPanel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (WallpaperItem item in items)
        {
            stripPanel.Children.Add(BuildTile(item, current));
        }

        stripViewer.Content = stripPanel;
        stripViewer.PreviewMouseWheel += (_, e) => OnStripWheel(stripViewer, e);

        var left = new Button
        {
            Style = (Style)Resources["StripArrow"],
            Content = ArrowGlyph("\uE76B"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        left.Click += (_, _) => WallpaperStripScroll.Page(
            stripViewer, stripViewer.HorizontalOffset - WallpaperGalleryMotion.ScrollStepDip);
        var right = new Button
        {
            Style = (Style)Resources["StripArrow"],
            Content = ArrowGlyph("\uE76C"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        right.Click += (_, _) => WallpaperStripScroll.Page(
            stripViewer, stripViewer.HorizontalOffset + WallpaperGalleryMotion.ScrollStepDip);

        stripViewer.ScrollChanged += (_, _) => UpdateArrows(stripViewer, left, right);
        stripViewer.MouseEnter += (_, _) => UpdateArrows(stripViewer, left, right);

        var strip = new Grid();
        strip.Children.Add(stripViewer);
        strip.Children.Add(left);
        strip.Children.Add(right);

        ContentHost.Children.Add(strip);
    }

    private static TextBlock ArrowGlyph(string glyph)
    {
        var text = new TextBlock();
        text.SetResourceReference(TextBlock.FontFamilyProperty, "AppIconFontFamily");
        text.FontSize = 12;
        // Scrim çip üstünde her iki temada da okunabilir beyaz glif.
        text.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
        text.Text = glyph;
        return text;
    }

    private void UpdateArrows(ScrollViewer stripViewer, Button left, Button right)
    {
        if (stripViewer.ScrollableWidth <= 0)
        {
            ShowArrow(left, wanted: false);
            ShowArrow(right, wanted: false);
            return;
        }

        ShowArrow(left, wanted: stripViewer.HorizontalOffset > 0.5);
        ShowArrow(right, wanted: stripViewer.HorizontalOffset < stripViewer.ScrollableWidth - 0.5);
    }

    /// <summary>
    /// Şerit oklarının hedef durumuna geçişi: hedef değişmedikçe animasyonu yeniden başlatmaz.
    /// Görünmezken opacity 0 + hit-test kapalı (tile'lara tıklamayı engellemesin); Collapsed
    /// yerine fade kullanılır — ScrollChanged her karede geldiğinde state makinesi bozulmaz.
    /// </summary>
    private static void ShowArrow(Button arrow, bool wanted)
    {
        if (arrow.Tag is bool current && current == wanted)
        {
            return;
        }

        arrow.Tag = wanted;
        arrow.IsHitTestVisible = wanted;
        arrow.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(
                wanted ? 1d : 0d,
                TimeSpan.FromMilliseconds(WallpaperGalleryMotion.ArrowFadeMs))
            {
                EasingFunction = WallpaperGalleryMotion.CreateEase(),
            });
    }

    private void OnStripWheel(ScrollViewer stripViewer, MouseWheelEventArgs e)
    {
        // Şerit o yöne kayamıyorsa olayı düşür → dıştaki dikey listeyi tekerlek beslemeye devam etsin.
        (bool handles, double target) = WallpaperGalleryMotion.WheelTarget(
            stripViewer.ScrollableWidth, stripViewer.HorizontalOffset, e.Delta);
        if (!handles)
        {
            return;
        }

        e.Handled = true;
        WallpaperStripScroll.Page(stripViewer, target);
    }

    private UIElement BuildTile(WallpaperItem item, string? current)
    {
        string full = Path.GetFullPath(item.Path);
        bool selected = current is not null
            && string.Equals(full, current, StringComparison.OrdinalIgnoreCase);

        var tile = new Border
        {
            Width = WallpaperGalleryMotion.ThumbWidthDip,
            Height = WallpaperGalleryMotion.ThumbHeightDip,
            Margin = new Thickness(0, 0, WallpaperGalleryMotion.ThumbGapDip, 0),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            ToolTip = item.DisplayName,
            Background = Brushes.Transparent,
            Tag = full,
            Style = (Style)Resources["GalleryThumb"],
        };

        var imageHolder = new Border
        {
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = CreateThumbVisual(full, selected),
        };

        var badge = new Border
        {
            Width = 15,
            Height = 15,
            CornerRadius = new CornerRadius(7.5),
            Visibility = selected ? Visibility.Visible : Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 3, 0),
            IsHitTestVisible = false,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        badge.Child = new TextBlock
        {
            Text = "\u2713",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(BadgeTextColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var content = new Grid();
        content.Children.Add(imageHolder);
        content.Children.Add(badge);
        tile.Child = content;

        if (selected)
        {
            tile.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        }
        else
        {
            tile.BorderBrush = Brushes.Transparent;
        }

        _tilesByPath[full] = (tile, badge);
        tile.MouseLeftButtonUp += Tile_MouseLeftButtonUp;
        return tile;
    }

    private UIElement CreateThumbVisual(string path, bool selected)
    {
        BitmapImage? bmp = TryDecode(path, WallpaperGalleryMotion.DecodeWidth(WallpaperGalleryMotion.ThumbWidthDip));
        if (bmp is null)
        {
            return CreatePlaceholder();
        }

        var image = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
        if (selected)
        {
            return image;
        }

        // Şerit ilk boyanırken seçili kart hariç hafifçe belirir.
        // Taban değeri 0'da kalır; animasyon varsayılan HoldEnd ile son değeri tutar.
        // FillBehavior.Stop kullanırsak WPF opaklığı taban değerine (0) geri çevirir
        // ve kartlar animasyon biter bitmez kaybolur — Bug A düzeltmesi.
        image.Opacity = 0;
        image.Loaded += (_, _) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, WallpaperGalleryMotion.PreviewCrossfade)
            {
                EasingFunction = WallpaperGalleryMotion.CreateEase(),
            };
            image.BeginAnimation(Image.OpacityProperty, fadeIn);
        };
        return image;
    }

    private static UIElement CreatePlaceholder()
    {
        var border = new Border();
        border.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        var glyph = new TextBlock
        {
            Text = "\uE91B",
            FontSize = 18,
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
        if (_service is null || sender is not Border { Tag: string path })
        {
            return;
        }

        e.Handled = true;
        if (!_service.TryApply(path))
        {
            return;
        }

        UpdateSelection(path);
        ShowPreview(path, animate: true);
        Applied?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelection(string appliedPath)
    {
        foreach (var (path, entry) in _tilesByPath)
        {
            bool selected = string.Equals(path, appliedPath, StringComparison.OrdinalIgnoreCase);
            if (selected)
            {
                entry.Tile.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
                entry.Badge.Visibility = Visibility.Visible;
            }
            else
            {
                entry.Tile.BorderBrush = Brushes.Transparent;
                entry.Badge.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ShowPreview(string? path, bool animate)
    {
        if (path is null)
        {
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewBadge.Visibility = Visibility.Collapsed;
            return;
        }

        BitmapImage? bmp = TryDecode(path, WallpaperGalleryMotion.DecodeWidth(PreviewDisplayWidthDip));
        if (bmp is null)
        {
            return;
        }

        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PreviewBadge.Visibility = Visibility.Visible;

        if (!animate || !_hasPreviewSource)
        {
            PreviewBase.Source = bmp;
            PreviewBase.Visibility = Visibility.Visible;
            PreviewTop.Source = null;
            PreviewTop.Visibility = Visibility.Collapsed;
            PreviewTop.Opacity = 0;
            _hasPreviewSource = true;
            return;
        }

        // Hafif crossfade: alt katmanda önceki görsel kalır; üst katmandaki yeni opaklık 0 → 1.
        if (PreviewTop.Source is not null)
        {
            PreviewBase.Source = PreviewTop.Source;
        }

        PreviewTop.Source = bmp;
        PreviewTop.Visibility = Visibility.Visible;
        PreviewTop.Opacity = 0;
        var fade = new DoubleAnimation(1, WallpaperGalleryMotion.PreviewCrossfade)
        {
            EasingFunction = WallpaperGalleryMotion.CreateEase(),
            FillBehavior = FillBehavior.Stop,
        };
        fade.Completed += (_, _) =>
        {
            PreviewBase.Source = PreviewTop.Source;
            PreviewTop.Source = null;
            PreviewTop.Visibility = Visibility.Collapsed;
        };
        PreviewTop.BeginAnimation(Image.OpacityProperty, fade);
    }

    private static BitmapImage? TryDecode(string path, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException
                                       or UnauthorizedAccessException or InvalidOperationException
                                       or FileFormatException or ArgumentException)
        {
            return null;
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
