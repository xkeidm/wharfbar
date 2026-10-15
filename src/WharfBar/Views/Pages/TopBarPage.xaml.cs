using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;
using Wpf.Ui.Controls;

namespace WharfBar.Views.Pages;

/// <summary>
/// Top bar settings page: live preview, system-stats toggles, and status item
/// visibility with drag-and-drop ordering.
/// </summary>
public partial class TopBarPage : Page
{
    /// <summary>Tag on the grip, so a press there starts a reorder and a press on the switch does not.</summary>
    private const string DragHandleTag = "status-drag-handle";

    private Point _dragOrigin;
    private string? _draggingId;

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

        bool anyHidden = false;
        foreach (string id in TopBarStatusItemIds.CatalogOrder)
        {
            if (visibleSet.Contains(id))
            {
                continue;
            }

            if (!anyHidden)
            {
                anyHidden = true;
                StatusItemsPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = Strings.TopBarPage_DisabledHint,
                    FontSize = 12,
                    Margin = new Thickness(0, 4, 0, 8),
                    Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                });
            }

            AddCard(id, isChecked: false);
        }

        ShowAllButton.IsEnabled = anyHidden;
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
            // The glyph alone is a poor hit target (only the ink is tested). A transparent
            // border fills the grip column, and the switch — a ToggleButton — never sees
            // this press, so it cannot capture the mouse and cancel the drag.
            var handle = new Border
            {
                Tag = DragHandleTag,
                Background = Brushes.Transparent,
                Cursor = Cursors.SizeAll,
                Padding = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                ToolTip = Strings.DockRow_DragTooltip,
                Child = new SymbolIcon
                {
                    Symbol = SymbolRegular.ReOrderDotsVertical24,
                    FontSize = 16,
                    Foreground = (Brush)FindResource("TextFillColorSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                },
            };
            Grid.SetColumn(handle, 2);
            grid.Children.Add(handle);

            border.PreviewMouseLeftButtonDown += OnCardMouseLeftButtonDown;
            border.PreviewMouseLeftButtonUp += OnCardMouseLeftButtonUp;
            border.PreviewMouseMove += OnCardMouseMove;
        }

        border.Child = grid;
        StatusItemsPanel.Children.Add(border);
    }

    // --- Drag & drop reorder ---

    private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // A press that is not a grip must not keep the previous card armed.
        _draggingId = null;
        _dragOrigin = default;

        if (sender is not Border { Tag: string id } card || !IsDragHandle(e.OriginalSource, card))
        {
            return;
        }

        _dragOrigin = e.GetPosition(null);
        _draggingId = id;
        card.CaptureMouse();
        e.Handled = true;
    }

    private void OnCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingId = null;
        if (sender is UIElement element && element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }
    }

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton is not MouseButtonState.Pressed
            || sender is not Border card
            || _draggingId is null)
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

        string dragged = _draggingId;
        _draggingId = null;
        if (card.IsMouseCaptured)
        {
            card.ReleaseMouseCapture();
        }

        _ = DragDrop.DoDragDrop(card, dragged, DragDropEffects.Move);
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat) && TargetCard(e) is not null
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.StringFormat) is not string draggedId
            || TargetCard(e) is not Border { Tag: string targetId })
        {
            return;
        }

        List<string> items = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems);
        if (!TopBarStatusItemIds.Move(items, draggedId, targetId))
        {
            return;
        }

        PersistAndRebuild(items);
    }

    /// <summary>
    /// The visible card under the pointer, or the closer neighbor when the pointer is in
    /// the gap. Distance is zero while the pointer is inside a card, so a card always
    /// wins over the gap beside it.
    /// </summary>
    private Border? TargetCard(DragEventArgs e)
    {
        var visible = new HashSet<string>(
            TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems),
            StringComparer.Ordinal);
        double y = e.GetPosition(StatusItemsPanel).Y;
        Border? best = null;
        double bestDistance = double.PositiveInfinity;

        foreach (object child in StatusItemsPanel.Children)
        {
            if (child is not Border { Tag: string id } card || !visible.Contains(id))
            {
                continue;
            }

            double distance = VerticalDistance(card, y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = card;
            }
        }

        return best;
    }

    private double VerticalDistance(Border card, double y)
    {
        Point top = card.TranslatePoint(new Point(0, 0), StatusItemsPanel);
        double bottom = top.Y + card.ActualHeight;
        if (y < top.Y)
        {
            return top.Y - y;
        }

        if (y > bottom)
        {
            return y - bottom;
        }

        return 0;
    }

    private static bool IsDragHandle(object? source, DependencyObject card)
    {
        for (DependencyObject? node = source as DependencyObject;
             node is not null && !ReferenceEquals(node, card);
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is FrameworkElement { Tag: DragHandleTag })
            {
                return true;
            }
        }

        return false;
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
                items = TopBarStatusItemIds.InsertInCatalogOrder(items, id);
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

    private void OnShowAllClick(object sender, RoutedEventArgs e)
    {
        List<string> items = TopBarStatusItemIds.ShowAll(Cfg.TopBar.StatusItems);
        if (items.SequenceEqual(TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems)))
        {
            return;
        }

        PersistAndRebuild(items);
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
        TopBarStatusItemIds.TrayMirror => Strings.TopBarItem_TrayMirror,
        TopBarStatusItemIds.TrayOverflow => Strings.TopBarItem_TrayOverflow,
        TopBarStatusItemIds.DateTime => Strings.TopBarItem_DateTime,
        TopBarStatusItemIds.Notifications => Strings.TopBarItem_Notifications,
        TopBarStatusItemIds.ControlCenter => Strings.TopBarItem_ControlCenter,
        TopBarStatusItemIds.Volume => Strings.TopBarItem_Volume,
        TopBarStatusItemIds.Brightness => Strings.TopBarItem_Brightness,
        TopBarStatusItemIds.Media => Strings.TopBarItem_Media,
        TopBarStatusItemIds.Network => Strings.TopBarItem_Network,
        TopBarStatusItemIds.Battery => Strings.TopBarItem_Battery,
        _ => id,
    };
}
