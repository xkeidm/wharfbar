using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Services;
using Wpf.Ui.Controls;

namespace WharfBar.Views.Pages;

/// <summary>
/// Top bar settings page: live preview, system-stats toggles, and status item
/// visibility with drag-and-drop ordering.
/// </summary>
public partial class TopBarPage : Page
{
    private Point _dragOrigin;

    public TopBarPage()
    {
        InitializeComponent();
        SystemStatsToggle.IsChecked = Cfg.TopBar.ShowSystemStats;
        GpuPerAdapterToggle.IsChecked = Cfg.TopBar.ShowGpuPerAdapter;
        UpdateGpuToggleState();
        RebuildPreview();
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
        foreach (string id in visible)
        {
            AddCard(id, isChecked: true);
        }

        // Unchecked items
        foreach (string id in TopBarStatusItemIds.DefaultOrder)
        {
            if (!visibleSet.Contains(id))
            {
                AddCard(id, isChecked: false);
            }
        }
    }

    private void AddCard(string id, bool isChecked)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = (Brush)FindResource("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 8),
            Tag = id,
            AllowDrop = isChecked,
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

        var toggle = new ToggleSwitch
        {
            IsChecked = isChecked,
            Content = StatusItemName(id),
            Tag = id,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
        };
        toggle.Checked += OnStatusToggled;
        toggle.Unchecked += OnStatusToggled;
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);

        if (isChecked)
        {
            var handle = new SymbolIcon
            {
                Symbol = SymbolRegular.ReOrderDotsVertical24,
                FontSize = 16,
                Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Sürükleyerek sırayı değiştirin",
            };
            Grid.SetColumn(handle, 2);
            grid.Children.Add(handle);

            border.PreviewMouseLeftButtonDown += OnCardMouseLeftButtonDown;
            border.PreviewMouseMove += OnCardMouseMove;
            border.DragOver += OnCardDragOver;
            border.Drop += OnCardDrop;
        }

        border.Child = grid;
        StatusItemsPanel.Children.Add(border);
    }

    // --- Drag & drop reorder ---

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border)
        {
            _dragOrigin = e.GetPosition(null);
        }
    }

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Border { Tag: string id } || e.LeftButton is not MouseButtonState.Pressed)
        {
            return;
        }

        Point position = e.GetPosition(null);
        double dx = position.X - _dragOrigin.X;
        double dy = position.Y - _dragOrigin.Y;
        if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _ = DragDrop.DoDragDrop((Border)sender, id, DragDropEffects.Move);
    }

    private void OnCardDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCardDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.StringFormat) is not string draggedId
            || sender is not Border { Tag: string targetId }
            || string.Equals(draggedId, targetId, StringComparison.Ordinal))
        {
            return;
        }

        List<string> items = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems);
        int targetIndex = items.FindIndex(x => string.Equals(x, targetId, StringComparison.Ordinal));
        if (targetIndex < 0 || !items.Remove(draggedId))
        {
            return;
        }

        items.Insert(items.IndexOf(targetId), draggedId);
        PersistAndRebuild(items);
    }

    // --- Status item toggles ---

    private void OnStatusToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: string id, IsChecked: bool isChecked })
        {
            return;
        }

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

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        PersistAndRebuild(TopBarStatusItemIds.DefaultOrder.ToList());
    }

    private void PersistAndRebuild(List<string> items)
    {
        Cfg.TopBar.StatusItems = TopBarStatusItemIds.Normalize(items);
        Config.Save(Cfg);
        WharfApp.ApplyTopBarChanges();
        RebuildList();
        RebuildPreview();
    }

    // --- System stats toggles ---

    private void OnSystemStatsToggled(object sender, RoutedEventArgs e)
    {
        if (SystemStatsToggle.IsChecked is not bool isChecked)
        {
            return;
        }

        Cfg.TopBar.ShowSystemStats = isChecked;
        Config.Save(Cfg);
        UpdateGpuToggleState();
        WharfApp.ApplyTopBarChanges();
        RebuildPreview();
    }

    private void OnGpuPerAdapterToggled(object sender, RoutedEventArgs e)
    {
        if (GpuPerAdapterToggle.IsChecked is not bool isChecked)
        {
            return;
        }

        Cfg.TopBar.ShowGpuPerAdapter = isChecked;
        Config.Save(Cfg);
        WharfApp.ApplyTopBarChanges();
        RebuildPreview();
    }

    private void UpdateGpuToggleState()
    {
        GpuPerAdapterToggle.IsEnabled = SystemStatsToggle.IsChecked == true;
    }

    // --- Live preview ---

    private void RebuildPreview()
    {
        bool showStats = SystemStatsToggle.IsChecked == true;
        SystemStatsPreviewText.Text = SystemStatsService.FormatText(
            cpuPercent: 12.4,
            gpuPercents: [4.2, 23.5],
            ramPercent: 46.5,
            showGpuPerAdapter: GpuPerAdapterToggle.IsChecked == true);
        SystemStatsPreviewText.Visibility = showStats ? Visibility.Visible : Visibility.Collapsed;

        PreviewPanel.Children.Clear();
        foreach (string id in TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems))
        {
            PreviewPanel.Children.Add(CreatePreviewChip(id));
        }
    }

    private static FrameworkElement CreatePreviewChip(string id)
    {
        var chip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };

        var icon = new SymbolIcon
        {
            Symbol = StatusItemSymbol(id),
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
        };
        chip.Children.Add(icon);

        if (StatusItemPreviewText(id) is string sample)
        {
            chip.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = sample,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            });
        }

        return chip;
    }

    private static string? StatusItemPreviewText(string id) => id switch
    {
        TopBarStatusItemIds.DateTime => "14:32",
        TopBarStatusItemIds.Battery => "%87",
        _ => null,
    };

    private static SymbolRegular StatusItemSymbol(string id) => id switch
    {
        TopBarStatusItemIds.TrayMirror => SymbolRegular.Apps24,
        TopBarStatusItemIds.TrayOverflow => SymbolRegular.ChevronUp24,
        TopBarStatusItemIds.DateTime => SymbolRegular.Clock24,
        TopBarStatusItemIds.Notifications => SymbolRegular.Alert24,
        TopBarStatusItemIds.ControlCenter => SymbolRegular.Settings24,
        TopBarStatusItemIds.Volume => SymbolRegular.Speaker224,
        TopBarStatusItemIds.Brightness => SymbolRegular.BrightnessHigh24,
        TopBarStatusItemIds.Media => SymbolRegular.Play24,
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
        TopBarStatusItemIds.Media => "Medya",
        TopBarStatusItemIds.Network => "Ağ",
        TopBarStatusItemIds.Battery => "Pil",
        _ => id,
    };
}
