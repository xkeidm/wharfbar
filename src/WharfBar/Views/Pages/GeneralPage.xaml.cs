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
        SaveAndApply();
    }

    private void UpdateSubToggleVisibility()
    {
        bool show = SystemStatsToggle.IsChecked == true;
        CpuCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        GpuCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RamCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.TopBar?.ApplyConfigChanges();
    }
}
