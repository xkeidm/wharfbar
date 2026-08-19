using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Services;
using Wpf.Ui.Controls;

namespace WharfBar.Views.Pages;

/// <summary>
/// Top bar settings page: status item visibility and ordering (PowerToys style).
/// </summary>
public partial class TopBarPage : Page
{
    public TopBarPage()
    {
        InitializeComponent();
        RebuildList();
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    private void RebuildList()
    {
        StatusItemsPanel.Children.Clear();

        List<string> visible = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems);
        var visibleSet = new HashSet<string>(visible, StringComparer.Ordinal);

        // Checked items first, in current order
        for (int i = 0; i < visible.Count; i++)
        {
            AddCard(visible[i], isChecked: true, index: i, total: visible.Count);
        }

        // Unchecked items
        foreach (string id in TopBarStatusItemIds.DefaultOrder)
        {
            if (!visibleSet.Contains(id))
            {
                AddCard(id, isChecked: false, index: -1, total: visible.Count);
            }
        }
    }

    private void AddCard(string id, bool isChecked, int index, int total)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new SymbolIcon
        {
            Symbol = StatusItemSymbol(id),
            FontSize = 18,
            Foreground = (Brush)FindResource(isChecked ? "AccentFillColorDefaultBrush" : "TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0),
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var checkBox = new CheckBox
        {
            IsChecked = isChecked,
            Content = StatusItemName(id),
            Tag = id,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
        };
        checkBox.Checked += OnStatusToggled;
        checkBox.Unchecked += OnStatusToggled;
        Grid.SetColumn(checkBox, 1);
        grid.Children.Add(checkBox);

        if (isChecked)
        {
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var upBtn = new Wpf.Ui.Controls.Button
            {
                Content = "▲",
                Tag = id,
                IsEnabled = index > 0,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 11,
            };
            upBtn.Click += OnMoveUp;
            buttonPanel.Children.Add(upBtn);

            var downBtn = new Wpf.Ui.Controls.Button
            {
                Content = "▼",
                Tag = id,
                IsEnabled = index < total - 1,
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(6, 0, 0, 0),
                FontSize = 11,
            };
            downBtn.Click += OnMoveDown;
            buttonPanel.Children.Add(downBtn);

            Grid.SetColumn(buttonPanel, 2);
            grid.Children.Add(buttonPanel);
        }

        border.Child = grid;
        StatusItemsPanel.Children.Add(border);
    }

    private void OnStatusToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string id, IsChecked: bool isChecked })
            return;

        List<string> items = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems);

        if (isChecked)
        {
            if (!items.Contains(id, StringComparer.Ordinal))
            {
                // Insert respecting default order.
                var set = new HashSet<string>(items, StringComparer.Ordinal) { id };
                items = TopBarStatusItemIds.DefaultOrder.Where(set.Contains).ToList();
            }
        }
        else
        {
            items.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
            if (items.Count == 0)
            {
                items.Add(TopBarStatusItemIds.DateTime);
            }
        }

        PersistAndRebuild(items);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
            return;

        List<string> items = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems);
        int i = items.IndexOf(id);
        if (i <= 0) return;

        (items[i - 1], items[i]) = (items[i], items[i - 1]);
        PersistAndRebuild(items);
    }

    private void OnMoveDown(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id })
            return;

        List<string> items = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems);
        int i = items.IndexOf(id);
        if (i < 0 || i >= items.Count - 1) return;

        (items[i + 1], items[i]) = (items[i], items[i + 1]);
        PersistAndRebuild(items);
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        PersistAndRebuild(TopBarStatusItemIds.DefaultOrder.ToList());
    }

    private void PersistAndRebuild(List<string> items)
    {
        Cfg.TopBar.StatusItems = TopBarStatusItemIds.Normalize(items);
        Config.Save(Cfg);
        WharfApp.TopBar?.ApplyConfigChanges();
        RebuildList();
    }

    private static SymbolRegular StatusItemSymbol(string id) => id switch
    {
        TopBarStatusItemIds.TrayMirror => SymbolRegular.Apps24,
        TopBarStatusItemIds.TrayOverflow => SymbolRegular.ChevronUp24,
        TopBarStatusItemIds.DateTime => SymbolRegular.Clock24,
        TopBarStatusItemIds.Notifications => SymbolRegular.Alert24,
        TopBarStatusItemIds.ControlCenter => SymbolRegular.Settings24,
        TopBarStatusItemIds.Volume => SymbolRegular.Speaker224,
        TopBarStatusItemIds.Brightness => SymbolRegular.BrightnessHigh24,
        TopBarStatusItemIds.Network => SymbolRegular.Wifi124,
        TopBarStatusItemIds.Battery => SymbolRegular.Battery524,
        _ => SymbolRegular.Apps24,
    };

    private static string StatusItemName(string id) => id switch
    {
        TopBarStatusItemIds.TrayMirror => "Tepsi simgeleri",
        TopBarStatusItemIds.TrayOverflow => "Gizli simgeler",
        TopBarStatusItemIds.DateTime => "Tarih ve saat",
        TopBarStatusItemIds.Notifications => "Bildirimler",
        TopBarStatusItemIds.ControlCenter => "Hızlı ayarlar",
        TopBarStatusItemIds.Volume => "Ses",
        TopBarStatusItemIds.Brightness => "Parlaklık (Monitörler)",
        TopBarStatusItemIds.Network => "Ağ",
        TopBarStatusItemIds.Battery => "Pil",
        _ => id,
    };
}
