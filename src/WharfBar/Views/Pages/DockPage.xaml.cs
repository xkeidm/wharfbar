using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// Dock settings page: content list, auto-hide, icon size, background, position,
/// running-apps visibility.
/// </summary>
public partial class DockPage : Page
{
    private const double MinimumDrag = 4d;

    private bool _isLoading = true;
    private Point _dragOrigin;
    private string? _draggingId;

    public DockPage()
    {
        InitializeComponent();
        LoadSettings();

        AutoHideToggle.Checked += OnAutoHideChanged;
        AutoHideToggle.Unchecked += OnAutoHideChanged;
        ShowRunningToggle.Checked += OnShowRunningChanged;
        ShowRunningToggle.Unchecked += OnShowRunningChanged;
        BackgroundCombo.SelectionChanged += OnBackgroundModeChanged;
        PositionCombo.SelectionChanged += OnPositionChanged;
        AddItemButton.Click += OnAddItemClick;

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    /// <summary>
    /// The app-wide store — the same instance every dock window renders, so an add/remove/
    /// reorder here lands on the docks without a restart.
    /// </summary>
    private DockItemStore Store => WharfApp.DockItems!;

    private void LoadSettings()
    {
        AutoHideToggle.IsChecked = Cfg.Dock.AutoHide;
        IconSizeSlider.Value = Cfg.Dock.IconSize;
        IconSizeLabel.Text = $"{(int)Cfg.Dock.IconSize} DIP";
        ShowRunningToggle.IsChecked = Cfg.Dock.ShowRunningApps;
        string mode = TopBarBackgroundModes.NormalizeMode(Cfg.Dock.BackgroundMode);
        BackgroundCombo.SelectedIndex = mode switch
        {
            TopBarBackgroundModes.Transparent => 1,
            TopBarBackgroundModes.Tint => 2,
            _ => 0,
        };
        OpacitySlider.Value = TopBarBackgroundModes.ClampOpacity(Cfg.Dock.TintOpacity);
        OpacityLabel.Text = $"{TopBarBackgroundModes.ClampOpacity(Cfg.Dock.TintOpacity)}%";
        UpdateOpacityRow(mode);
        PositionCombo.SelectedIndex = DockPositions.Normalize(Cfg.Dock.Position) switch
        {
            DockPositions.BottomLeft => 1,
            DockPositions.BottomRight => 2,
            _ => 0,
        };

        RebuildItems();
    }

    private void RebuildItems()
    {
        ItemsPanel.Items.Clear();
        foreach (PinnedApp item in Store.Items)
        {
            var card = new DockSettingsRow(item);
            ItemsPanel.Items.Add(card);
            HookRow(card);
        }

        EmptyHint.Visibility = Store.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Bind order/label path in one block: a row missing one handler is silently dead
    // (see TopBarWindow.xaml:156-157).
    private void HookRow(DockSettingsRow row)
    {
        row.RemoveClicked += (_, _) =>
        {
            Store.Remove(row.Id);
            RebuildItems();
        };
        row.PreviewMouseLeftButtonDown += OnRowMouseLeftButtonDown;
        row.PreviewMouseMove += OnRowMouseMove;
        row.DragOver += OnRowDragOver;
        row.Drop += OnRowDrop;
    }

    private void OnAddItemClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Dock'a ekle",
            Filter = "Uygulamalar (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (string path in dialog.FileNames)
        {
            // Resolve first: an unsupported extension yields an empty path, which TryAdd refuses.
            (string resolvedPath, string args) = DockDropFormats.Resolve(path);
            Store.TryAdd(resolvedPath, args, string.Empty);
        }

        RebuildItems();
    }

    // --- Drag & drop reorder ---

    private void OnRowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Clear first: a press that cannot become a drag must not leave the previous row's
        // bookkeeping behind for the next PreviewMouseMove to pick up.
        _dragOrigin = default;
        _draggingId = null;

        if (sender is not DockSettingsRow row || IsInsideRemoveGlyph(row, e.OriginalSource))
        {
            return;
        }

        _dragOrigin = e.GetPosition(null);
        _draggingId = row.Id;
    }

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton is not MouseButtonState.Pressed
            || sender is not DockSettingsRow row
            || _draggingId is null)
        {
            return;
        }

        Point position = e.GetPosition(null);
        double dx = position.X - _dragOrigin.X;
        double dy = position.Y - _dragOrigin.Y;
        if (Math.Abs(dx) < MinimumDrag && Math.Abs(dy) < MinimumDrag)
        {
            return;
        }

        string dragged = _draggingId;
        _draggingId = null;
        _ = DragDrop.DoDragDrop(row, dragged, DragDropEffects.Move);
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        // A drop on the dragged row itself is a no-op, never a reorder: MoveCore would take the
        // row out and put it back one slot along.
        if (e.Data.GetData(DataFormats.StringFormat) is not string dragged
            || sender is not DockSettingsRow target
            || string.Equals(dragged, target.Id, StringComparison.Ordinal))
        {
            return;
        }

        int from = IndexOf(dragged);
        int to = IndexOf(target.Id);
        if (from < 0 || to < 0)
        {
            return;
        }

        // MoveCore's toIndex is an index in the list *after* the row is removed, while `to` is the
        // target's index before it. Handing over the pre-removal index lands the row adjacent to
        // the target — after it when dragged from above, before it when dragged from below —
        // which is exactly what the strip's own reorder produces.
        Store.Move(dragged, to);
        RebuildItems();
    }

    private int IndexOf(string id)
    {
        for (int i = 0; i < Store.Items.Count; i++)
        {
            if (string.Equals(DockItemStore.IdFor(Store.Items[i]), id, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsInsideRemoveGlyph(DockSettingsRow row, object? original)
    {
        for (DependencyObject? node = original as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, row.RemoveGlyph))
            {
                return true;
            }
        }

        return false;
    }

    // --- Settings handlers ---

    private void OnAutoHideChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Dock.AutoHide = AutoHideToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnShowRunningChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Dock.ShowRunningApps = ShowRunningToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Dock.Position = PositionCombo.SelectedIndex switch
        {
            1 => DockPositions.BottomLeft,
            2 => DockPositions.BottomRight,
            _ => DockPositions.BottomCenter,
        };
        SaveAndApply();
    }

    private void OnIconSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        IconSizeLabel.Text = $"{v} DIP";

        if (_isLoading) return;

        Cfg.Dock.IconSize = Math.Clamp(v, DockConfig.MinIconSize, DockConfig.MaxIconSize);
        SaveAndApply();
    }

    private void OnBackgroundModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        string newMode = BackgroundCombo.SelectedIndex switch
        {
            1 => TopBarBackgroundModes.Transparent,
            2 => TopBarBackgroundModes.Tint,
            _ => TopBarBackgroundModes.Mica,
        };

        string oldMode = TopBarBackgroundModes.NormalizeMode(Cfg.Dock.BackgroundMode);
        bool oldNeedsTx = TopBarBackgroundModes.NeedsTransparency(oldMode);
        bool newNeedsTx = TopBarBackgroundModes.NeedsTransparency(newMode);

        Cfg.Dock.BackgroundMode = newMode;
        Config.Save(Cfg);
        UpdateOpacityRow(newMode);

        if (oldNeedsTx != newNeedsTx)
        {
            WharfApp.RecreateDock();
        }
        else
        {
            WharfApp.ApplyDockChanges();
        }
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        OpacityLabel.Text = $"{v}%";

        if (_isLoading) return;

        Cfg.Dock.TintOpacity = TopBarBackgroundModes.ClampOpacity(v);
        SaveAndApply();
    }

    private void UpdateOpacityRow(string mode)
    {
        OpacityRow.Visibility = mode == TopBarBackgroundModes.Tint
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.ApplyDockChanges();
    }
}
