using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;

namespace WharfBar.Views;

/// <summary>
/// Widget store and the built-in plugin list. Plugins are toggles in this
/// process; nothing loads an outside assembly.
/// </summary>
internal sealed class GalleryWindow : Window
{
    private GalleryWindow(AppConfig config, bool widgets)
    {
        config.Island.OpenWidgets ??= [];
        Title = widgets ? "Widget mağazası" : "Eklenti galerisi";
        Width = 420;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        var root = new StackPanel { Margin = new Thickness(16) };
        Content = root;
        if (widgets)
        {
            root.Children.Add(Row(config, "Saat", "clock", "Masaüstünde saat. Adada durmaz."));
            root.Children.Add(Row(config, "Takvim", "calendar", "Bu ayın ızgarası. Adada durmaz."));
        }
        else
        {
            root.Children.Add(Toggle(config, "Görselleştirici", "Müzik çalarken hapta çubuklar.", config.Island.Visualizer, value => config.Island.Visualizer = value));
            root.Children.Add(Toggle(config, "Bluetooth bildirimi", "Bağlanan cihazın adı hapta kısa süre durur.", config.Island.BluetoothNotice, value => config.Island.BluetoothNotice = value));
            root.Children.Add(Toggle(config, "Kamera aynası", "Hapta canlı önizleme. İzin yoksa görüntü yok.", config.Island.CameraMirror, value =>
            {
                config.Island.CameraMirror = value;
                CameraMirrorHost.Apply(value);
            }));
        }
    }

    internal static void Show(AppConfig config, bool widgets)
    {
        var window = new GalleryWindow(config, widgets);
        window.Show();
    }

    private UIElement Row(AppConfig config, string title, string id, string detail)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, FontSize = 14 });
        text.Children.Add(new TextBlock { Text = detail, Opacity = 0.7 });
        var button = new Button { Content = config.Island.OpenWidgets.Contains(id) ? "Kaldır" : "Ekle", MinWidth = 72 };
        button.Click += (_, _) =>
        {
            if (config.Island.OpenWidgets.Contains(id))
            {
                config.Island.OpenWidgets.Remove(id);
                button.Content = "Ekle";
            }
            else
            {
                config.Island.OpenWidgets.Add(id);
                button.Content = "Kaldır";
            }

            Save(config);
            WidgetHost.Apply(config);
        };
        Grid.SetColumn(button, 1);
        grid.Children.Add(text);
        grid.Children.Add(button);
        return grid;
    }

    private UIElement Toggle(AppConfig config, string title, string detail, bool on, Action<bool> set)
    {
        var box = new CheckBox { Content = title, IsChecked = on, FontSize = 14, Margin = new Thickness(0, 10, 0, 0) };
        var caption = new TextBlock { Text = detail, Opacity = 0.7, Margin = new Thickness(20, 2, 0, 0) };
        box.Checked += (_, _) =>
        {
            set(true);
            Save(config);
        };
        box.Unchecked += (_, _) =>
        {
            set(false);
            Save(config);
        };
        var stack = new StackPanel();
        stack.Children.Add(box);
        stack.Children.Add(caption);
        return stack;
    }

    private static void Save(AppConfig config) => ((App)Application.Current).Config?.Save(config);
}
