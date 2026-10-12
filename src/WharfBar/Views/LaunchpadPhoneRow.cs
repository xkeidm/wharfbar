using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Vertical Phone Link panel. <see cref="Beside"/> places it to the right of the launchpad card.
/// </summary>
internal static class LaunchpadPhoneRow
{
    private static readonly Brush TransparentFill = Brushes.Transparent;
    private static readonly ControlTemplate FlatTemplate = BuildFlatTemplate();
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly SolidColorBrush ConnectedBrush = CreateConnectedBrush();

    internal const double PanelWidth = 268;

    /// <summary>Displayed width of the phone bezel. Height follows the frame bitmap.</summary>
    internal const double PhoneMarkWidth = 76;

    internal static UIElement Beside(UIElement card, LaunchpadPhoneSnapshot phone, Action<string> openPath)
    {
        ArgumentNullException.ThrowIfNull(card);
        var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(card);
        UIElement panel = Create(phone, openPath);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
        Grid.SetColumn(panel, 1);
        row.Children.Add(panel);
        return row;
    }

    internal static UIElement Create(LaunchpadPhoneSnapshot phone, Action<string> openPath)
    {
        ArgumentNullException.ThrowIfNull(phone);
        ArgumentNullException.ThrowIfNull(openPath);

        var root = new Border
        {
            Width = PanelWidth,
            MaxHeight = 740,
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 18, 16, 16),
            VerticalAlignment = VerticalAlignment.Stretch,
            SnapsToDevicePixels = true,
        };
        root.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");

        var stack = new StackPanel();
        stack.Children.Add(Header(phone));
        stack.Children.Add(Actions(phone, openPath));
        if (phone.Recent.Count > 0)
        {
            stack.Children.Add(Recent(phone.Recent));
        }

        root.Child = stack;
        return root;
    }

    private static UIElement Header(LaunchpadPhoneSnapshot phone)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(DeviceMark(phone));
        var name = Caption(phone.Name, 16);
        name.FontWeight = FontWeights.SemiBold;
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.Margin = new Thickness(0, 10, 0, 0);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxWidth = PanelWidth - 40;
        stack.Children.Add(name);

        var status = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 4),
        };
        if (phone.Connected)
        {
            var check = Glyph("\uE73E", 12);
            check.Foreground = ConnectedBrush;
            check.Margin = new Thickness(0, 0, 8, 0);
            check.ToolTip = "Bağlı";
            status.Children.Add(check);
        }

        if (phone.BluetoothOn == true)
        {
            var bluetooth = Glyph("\uE702", 12);
            bluetooth.Margin = new Thickness(0, 0, 8, 0);
            bluetooth.ToolTip = "Bluetooth";
            status.Children.Add(bluetooth);
        }

        string? battery = LaunchpadPhoneLink.FormatBattery(phone.BatteryPercent);
        if (battery is not null)
        {
            status.Children.Add(Caption(battery, 13));
        }

        if (status.Children.Count > 0)
        {
            stack.Children.Add(status);
        }

        return stack;
    }

    private static UIElement DeviceMark(LaunchpadPhoneSnapshot phone)
    {
        BitmapSource? screen = Decode(phone.DeviceImageUri);
        BitmapSource? frame = Decode(phone.DeviceFrameUri);
        if (frame is not null && ScreenHole(frame) is Int32Rect hole)
        {
            return StackedPhone(screen, frame, hole);
        }

        if (screen is not null)
        {
            return DrawnPhone(screen);
        }

        if (frame is not null)
        {
            return FittedImage(frame, PhoneMarkWidth);
        }

        return PhoneOutline();
    }

    /// <summary>
    /// Transparent interior of a phone bezel, in bitmap pixels. Null when the bitmap has no hole.
    /// </summary>
    internal static Int32Rect? ScreenHole(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        BitmapSource pixels = source.Format == PixelFormats.Bgra32 || source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int width = pixels.PixelWidth;
        int height = pixels.PixelHeight;
        if (width < 8 || height < 8)
        {
            return null;
        }

        var buffer = new byte[width * height * 4];
        pixels.CopyPixels(buffer, width * 4, 0);
        bool Opaque(int x, int y) => buffer[((y * width) + x) * 4 + 3] > 128;

        int midX = width / 2;
        int midY = height / 2;
        if (Opaque(midX, midY))
        {
            return null;
        }

        if (!TrySpan(width, x => Opaque(x, midY), out int holeLeft, out int holeRight)
            || !TrySpan(height, y => Opaque(midX, y), out int holeTop, out int holeBottom))
        {
            return null;
        }

        int holeWidth = holeRight - holeLeft + 1;
        int holeHeight = holeBottom - holeTop + 1;
        if (holeWidth < 4 || holeHeight < 4 || holeWidth > width - 2 || holeHeight > height - 2)
        {
            return null;
        }

        return new Int32Rect(holeLeft, holeTop, holeWidth, holeHeight);
    }

    private static bool TrySpan(int length, Func<int, bool> opaque, out int holeStart, out int holeEnd)
    {
        holeStart = 0;
        holeEnd = 0;
        int leading = -1;
        for (int index = 0; index < length; index++)
        {
            if (opaque(index))
            {
                leading = index;
                break;
            }
        }

        int trailing = -1;
        for (int index = length - 1; index >= 0; index--)
        {
            if (opaque(index))
            {
                trailing = index;
                break;
            }
        }

        if (leading < 0 || trailing < 0 || trailing <= leading)
        {
            return false;
        }

        holeStart = leading;
        for (int index = leading; index <= trailing; index++)
        {
            if (!opaque(index))
            {
                holeStart = index;
                break;
            }
        }

        holeEnd = trailing;
        for (int index = trailing; index >= leading; index--)
        {
            if (!opaque(index))
            {
                holeEnd = index;
                break;
            }
        }

        return holeEnd > holeStart;
    }

    private static UIElement StackedPhone(BitmapSource? screen, BitmapSource frame, Int32Rect hole)
    {
        double scale = PhoneMarkWidth / frame.PixelWidth;
        double width = PhoneMarkWidth;
        double height = frame.PixelHeight * scale;
        var host = new Grid
        {
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };

        if (screen is not null)
        {
            double holeWidth = hole.Width * scale;
            double holeHeight = hole.Height * scale;
            double shotHeight = screen.PixelHeight * (holeWidth / screen.PixelWidth);
            double radius = Math.Max(4, holeWidth * 0.08);
            var shot = new Image
            {
                Width = holeWidth,
                Height = shotHeight,
                Source = screen,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
            var layer = new Canvas
            {
                Width = width,
                Height = height,
                IsHitTestVisible = false,
                Clip = new RectangleGeometry(
                    new Rect(hole.X * scale, hole.Y * scale, holeWidth, holeHeight),
                    radius,
                    radius),
            };
            Canvas.SetLeft(shot, hole.X * scale);
            Canvas.SetTop(shot, hole.Y * scale);
            layer.Children.Add(shot);
            host.Children.Add(layer);
        }

        host.Children.Add(new Image
        {
            Width = width,
            Height = height,
            Source = frame,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        });
        return host;
    }

    private static UIElement DrawnPhone(BitmapSource screen)
    {
        const double bezel = 8;
        double innerWidth = PhoneMarkWidth - (bezel * 2);
        double shotHeight = screen.PixelHeight * (innerWidth / screen.PixelWidth);
        const double maxInner = 200;
        double viewHeight = Math.Min(shotHeight, maxInner);
        var shot = new Image
        {
            Width = innerWidth,
            Height = shotHeight,
            Source = screen,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        var viewport = new Canvas
        {
            Width = innerWidth,
            Height = viewHeight,
            IsHitTestVisible = false,
            Clip = new RectangleGeometry(new Rect(0, 0, innerWidth, viewHeight), 8, 8),
        };
        viewport.Children.Add(shot);
        var shell = new Border
        {
            Width = PhoneMarkWidth,
            Height = viewHeight + (bezel * 2),
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(bezel),
            Background = Brushes.Black,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = viewport,
            SnapsToDevicePixels = true,
        };
        shell.SetResourceReference(Border.BorderBrushProperty, "TextFillColorSecondaryBrush");
        return shell;
    }

    private static UIElement FittedImage(BitmapSource source, double width)
    {
        double height = source.PixelHeight * (width / source.PixelWidth);
        return new Image
        {
            Width = width,
            Height = height,
            Source = source,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    private static UIElement PhoneOutline()
    {
        var glyph = Glyph("\uE717", 28);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var shell = new Border
        {
            Width = PhoneMarkWidth,
            Height = PhoneMarkWidth * (128d / 72d),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(3),
            Child = glyph,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        shell.SetResourceReference(Border.BorderBrushProperty, "TextFillColorSecondaryBrush");
        return shell;
    }

    private static UIElement Actions(LaunchpadPhoneSnapshot phone, Action<string> openPath)
    {
        var actions = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(Link("\uE8BD", "Mesajlar", LaunchpadPhoneLink.MessagesUri, openPath));
        actions.Children.Add(Link("\uE717", "Aramalar", LaunchpadPhoneLink.CallsUri, openPath));
        actions.Children.Add(Link("\uE91B", "Fotoğraflar", LaunchpadPhoneLink.PhotosUri, openPath));
        if (phone.SendFiles)
        {
            actions.Children.Add(Link("\uE898", "Dosyaları gönder", LaunchpadPhoneLink.FilesUri, openPath));
        }

        return actions;
    }

    private static UIElement Recent(IReadOnlyList<LaunchpadPhoneNotice> notices)
    {
        var block = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var header = Caption("En son", 11);
        header.FontWeight = FontWeights.SemiBold;
        header.Opacity = 0.7;
        header.Margin = new Thickness(0, 0, 0, 2);
        block.Children.Add(header);

        int shown = 0;
        foreach (LaunchpadPhoneNotice notice in notices)
        {
            if (shown == 4)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(notice.AppName) || string.IsNullOrWhiteSpace(notice.Summary))
            {
                continue;
            }

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
            };
            row.Children.Add(AppMark(notice));
            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var app = Caption(notice.AppName.Trim(), 13);
            app.TextTrimming = TextTrimming.CharacterEllipsis;
            app.MaxWidth = PanelWidth - 80;
            var summary = Caption(notice.Summary.Trim(), 12);
            summary.Opacity = 0.65;
            text.Children.Add(app);
            text.Children.Add(summary);
            row.Children.Add(text);
            block.Children.Add(row);
            shown++;
        }

        return block;
    }

    private static UIElement AppMark(LaunchpadPhoneNotice notice)
    {
        ImageSource? source = Decode(notice.IconDataUri);
        if (source is not null)
        {
            return new Image
            {
                Width = 32,
                Height = 32,
                Source = source,
                Stretch = Stretch.UniformToFill,
                IsHitTestVisible = false,
            };
        }

        string letter = notice.AppName.Trim().Length == 0
            ? "?"
            : notice.AppName.Trim()[0].ToString().ToUpperInvariant();
        var disk = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Child = Caption(letter, 13),
        };
        disk.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
        if (disk.Child is FrameworkElement mark)
        {
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
        }

        return disk;
    }

    private static BitmapSource? Decode(string? dataUri)
    {
        if (string.IsNullOrWhiteSpace(dataUri))
        {
            return null;
        }

        int comma = dataUri.IndexOf(',');
        if (comma < 0 || comma > 80)
        {
            return null;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(dataUri[(comma + 1)..]);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static Button Link(string glyph, string label, string uri, Action<string> openPath)
    {
        var icon = Glyph(glyph, 16);
        icon.Width = 20;
        var text = Caption(label, 14);
        text.Margin = new Thickness(10, 0, 0, 0);
        var line = new StackPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
        line.Children.Add(icon);
        line.Children.Add(text);
        var button = new Button
        {
            Content = line,
            Margin = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(8, 8, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = TransparentFill,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = FlatTemplate,
            FocusVisualStyle = null,
        };
        button.Click += (_, _) => openPath(uri);
        return button;
    }

    private static TextBlock Caption(string text, double size)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        return block;
    }

    private static TextBlock Glyph(string code, double size)
    {
        var block = new TextBlock
        {
            Text = code,
            FontFamily = IconFont,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return block;
    }

    private static SolidColorBrush CreateConnectedBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x3D, 0xD6, 0x8C));
        brush.Freeze();
        return brush;
    }

    private static ControlTemplate BuildFlatTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }
}
