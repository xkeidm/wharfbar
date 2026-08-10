using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// Dock settings page: auto-hide, icon size.
/// </summary>
public partial class DockPage : Page
{
    private bool _isLoading = true;

    public DockPage()
    {
        InitializeComponent();
        LoadSettings();

        AutoHideToggle.Checked += OnAutoHideChanged;
        AutoHideToggle.Unchecked += OnAutoHideChanged;

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    private void LoadSettings()
    {
        AutoHideToggle.IsChecked = Cfg.Dock.AutoHide;
        IconSizeSlider.Value = Cfg.Dock.IconSize;
        IconSizeLabel.Text = $"{(int)Cfg.Dock.IconSize} DIP";
    }

    private void OnAutoHideChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Dock.AutoHide = AutoHideToggle.IsChecked == true;
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

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.DockView?.ApplyConfigChanges();
    }
}
