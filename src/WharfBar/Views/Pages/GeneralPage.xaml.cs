using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// General settings page: autostart, system stats visibility.
/// </summary>
public partial class GeneralPage : Page
{
    private bool _isLoading = true;

    public GeneralPage()
    {
        InitializeComponent();
        LoadSettings();

        AutostartToggle.Checked += OnAutostartChanged;
        AutostartToggle.Unchecked += OnAutostartChanged;
        SystemStatsToggle.Checked += OnSystemStatsChanged;
        SystemStatsToggle.Unchecked += OnSystemStatsChanged;
        CpuToggle.Checked += OnStatsDetailChanged;
        CpuToggle.Unchecked += OnStatsDetailChanged;
        GpuToggle.Checked += OnStatsDetailChanged;
        GpuToggle.Unchecked += OnStatsDetailChanged;
        RamToggle.Checked += OnStatsDetailChanged;
        RamToggle.Unchecked += OnStatsDetailChanged;
        CpuPercentCombo.SelectionChanged += OnPlacementChanged;
        CpuGhzCombo.SelectionChanged += OnPlacementChanged;
        GpuPercentCombo.SelectionChanged += OnPlacementChanged;
        GpuVramCombo.SelectionChanged += OnPlacementChanged;
        RamPercentCombo.SelectionChanged += OnPlacementChanged;
        RamUsedTotalCombo.SelectionChanged += OnPlacementChanged;

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    private void LoadSettings()
    {
        AutostartToggle.IsChecked = Cfg.Autostart;
        SystemStatsToggle.IsChecked = Cfg.TopBar.ShowSystemStats;
        CpuToggle.IsChecked = Cfg.TopBar.ShowCpuStats;
        GpuToggle.IsChecked = Cfg.TopBar.ShowGpuStats;
        RamToggle.IsChecked = Cfg.TopBar.ShowRamStats;
        CpuPercentCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.CpuPercentPlacement, StatPlacement.Bar);
        CpuGhzCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.CpuGhzPlacement, StatPlacement.Tooltip);
        GpuPercentCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.GpuPercentPlacement, StatPlacement.Bar);
        GpuVramCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.GpuVramPlacement, StatPlacement.Tooltip);
        RamPercentCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.RamPercentPlacement, StatPlacement.Bar);
        RamUsedTotalCombo.SelectedIndex = StatPlacement.ToComboIndex(
            Cfg.TopBar.RamUsedTotalPlacement, StatPlacement.Tooltip);
        UpdateSubToggleVisibility();
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Autostart = AutostartToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnSystemStatsChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.TopBar.ShowSystemStats = SystemStatsToggle.IsChecked == true;
        UpdateSubToggleVisibility();
        SaveAndApply();
    }

    private void OnStatsDetailChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.TopBar.ShowCpuStats = CpuToggle.IsChecked == true;
        Cfg.TopBar.ShowGpuStats = GpuToggle.IsChecked == true;
        Cfg.TopBar.ShowRamStats = RamToggle.IsChecked == true;
        UpdateSubToggleVisibility();
        SaveAndApply();
    }

    private void OnPlacementChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.TopBar.CpuPercentPlacement = StatPlacement.FromComboIndex(CpuPercentCombo.SelectedIndex);
        Cfg.TopBar.CpuGhzPlacement = StatPlacement.FromComboIndex(CpuGhzCombo.SelectedIndex);
        Cfg.TopBar.GpuPercentPlacement = StatPlacement.FromComboIndex(GpuPercentCombo.SelectedIndex);
        Cfg.TopBar.GpuVramPlacement = StatPlacement.FromComboIndex(GpuVramCombo.SelectedIndex);
        Cfg.TopBar.RamPercentPlacement = StatPlacement.FromComboIndex(RamPercentCombo.SelectedIndex);
        Cfg.TopBar.RamUsedTotalPlacement = StatPlacement.FromComboIndex(RamUsedTotalCombo.SelectedIndex);
        SaveAndApply();
    }

    private void UpdateSubToggleVisibility()
    {
        bool show = SystemStatsToggle.IsChecked == true;
        CpuCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        GpuCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RamCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CpuPlacementPanel.Visibility = CpuToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GpuPlacementPanel.Visibility = GpuToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RamPlacementPanel.Visibility = RamToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.TopBar?.ApplyConfigChanges();
    }
}
