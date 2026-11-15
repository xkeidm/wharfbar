using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;
using WharfBar.Views.Pages;
using Wpf.Ui.Controls;

// Wpf.Ui.Controls also defines a TextBlock; the rows are plain WPF text.
using TextBlock = System.Windows.Controls.TextBlock;

namespace WharfBar.Views.Onboarding;

/// <summary>
/// Setup step 3: status items, system stats and the bar background. Every control edits the live
/// config, saves it and calls the matching <see cref="App"/> apply method, so the real top bar
/// behind the wizard reflects the choice immediately.
/// </summary>
public partial class TopBarStep : UserControl
{
    /// <summary>Guards the XAML-declared handlers while the ctor pushes the saved config into them.</summary>
    private bool _isLoading = true;

    public TopBarStep()
    {
        InitializeComponent();
        LoadState();
        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    // --- Load ---

    private void LoadState()
    {
        BuildStatusItems();

        StatsToggle.IsChecked = Cfg.TopBar.ShowSystemStats;
        GpuPerAdapterToggle.IsChecked = Cfg.TopBar.ShowGpuPerAdapter;
        UpdateGpuToggleState();

        string mode = TopBarBackgroundModes.NormalizeMode(Cfg.TopBar.BackgroundMode);
        BackgroundCombo.SelectedIndex = mode switch
        {
            TopBarBackgroundModes.Transparent => 1,
            TopBarBackgroundModes.Tint => 2,
            _ => 0,
        };
        OpacitySlider.Value = TopBarBackgroundModes.ClampOpacity(Cfg.TopBar.TintOpacity);
        OpacityLabel.Text = $"{TopBarBackgroundModes.ClampOpacity(Cfg.TopBar.TintOpacity)}%";
        UpdateOpacityRow(mode);
    }

    // --- Status items ---

    /// <summary>
    /// One row per catalog item: visible ones in the saved order first, then the disabled ones
    /// under the "Hidden" sub-header (catalog order, matching the Top Bar settings page).
    /// </summary>
    private void BuildStatusItems()
    {
        VisibleItemsPanel.Children.Clear();
        HiddenItemsPanel.Children.Clear();

        List<string> visible = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems);
        var visibleSet = new HashSet<string>(visible, StringComparer.Ordinal);

        foreach (string id in visible)
        {
            VisibleItemsPanel.Children.Add(CreateItemRow(id, isChecked: true));
        }

        var hidden = new List<string>();
        foreach (string id in TopBarStatusItemIds.CatalogOrder)
        {
            if (!visibleSet.Contains(id))
            {
                hidden.Add(id);
            }
        }

        foreach (string id in hidden)
        {
            HiddenItemsPanel.Children.Add(CreateItemRow(id, isChecked: false));
        }

        HiddenHeader.Visibility = hidden.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement CreateItemRow(string id, bool isChecked)
    {
        var border = new Border
        {
            Style = (Style)FindResource("OnboardingItemRow"),
            Tag = id,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new SymbolIcon
        {
            Symbol = TopBarPage.StatusItemSymbol(id),
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        // Live reference: the wizard can be reopened after a theme swap.
        icon.SetResourceReference(
            SymbolIcon.ForegroundProperty,
            isChecked ? "AccentFillColorDefaultBrush" : "TextFillColorSecondaryBrush");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var label = new TextBlock
        {
            Text = TopBarPage.StatusItemName(id),
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var toggle = new ToggleSwitch
        {
            IsChecked = isChecked,
            Tag = id,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Hooked after IsChecked: a value set before hooking cannot fire a spurious save.
        toggle.Checked += OnStatusItemToggled;
        toggle.Unchecked += OnStatusItemToggled;
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);

        border.Child = grid;
        return border;
    }

    private void OnStatusItemToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not ToggleSwitch { Tag: string id, IsChecked: bool isChecked })
        {
            return;
        }

        List<string> items = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems);

        if (isChecked)
        {
            if (!items.Contains(id, StringComparer.Ordinal))
            {
                items = TopBarStatusItemIds.InsertInCatalogOrder(items, id);
            }
        }
        else
        {
            items.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
            if (items.Count == 0)
            {
                // The strip must never be empty.
                items.Add(TopBarStatusItemIds.DateTime);
            }
        }

        Cfg.TopBar.StatusItems = TopBarStatusItemIds.Normalize(items);
        Config.Save(Cfg);
        WharfApp.ApplyTopBarChanges();

        // The toggled row has to move between the visible and hidden groups. Rebuilding it from
        // inside its own Checked/Unchecked event is unsafe, so wait for the event to finish.
        Dispatcher.BeginInvoke(new Action(BuildStatusItems), DispatcherPriority.Background);
    }

    // --- System stats ---

    private void OnStatsToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.TopBar.ShowSystemStats = StatsToggle.IsChecked == true;
        SaveAndApply();
        UpdateGpuToggleState();
    }

    private void OnGpuPerAdapterToggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.TopBar.ShowGpuPerAdapter = GpuPerAdapterToggle.IsChecked == true;
        SaveAndApply();
    }

    private void UpdateGpuToggleState()
    {
        GpuPerAdapterToggle.IsEnabled = StatsToggle.IsChecked == true;
    }

    // --- Background ---

    private void OnBackgroundModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        string newMode = BackgroundCombo.SelectedIndex switch
        {
            1 => TopBarBackgroundModes.Transparent,
            2 => TopBarBackgroundModes.Tint,
            _ => TopBarBackgroundModes.Mica,
        };

        string oldMode = TopBarBackgroundModes.NormalizeMode(Cfg.TopBar.BackgroundMode);
        bool oldNeedsTx = TopBarBackgroundModes.NeedsTransparency(oldMode);
        bool newNeedsTx = TopBarBackgroundModes.NeedsTransparency(newMode);

        Cfg.TopBar.BackgroundMode = newMode;
        Config.Save(Cfg);
        UpdateOpacityRow(newMode);

        // A mode that adds/removes WS_EX_LAYERED cannot be reached by a repaint: rebuild the bar.
        if (oldNeedsTx != newNeedsTx)
        {
            WharfApp.RecreateTopBar();
        }
        else
        {
            WharfApp.ApplyTopBarChanges();
        }
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int value = (int)Math.Round(e.NewValue);
        if (OpacityLabel is not null)
        {
            // Fires once during XAML parse, before the label field exists.
            OpacityLabel.Text = $"{value}%";
        }

        if (_isLoading)
        {
            return;
        }

        Cfg.TopBar.TintOpacity = TopBarBackgroundModes.ClampOpacity(value);
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
        WharfApp.ApplyTopBarChanges();
    }
}
