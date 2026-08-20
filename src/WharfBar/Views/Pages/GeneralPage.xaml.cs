using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// General settings page: autostart.
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

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    private void LoadSettings()
    {
        AutostartToggle.IsChecked = Cfg.Autostart;
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Autostart = AutostartToggle.IsChecked == true;
        SaveAndApply();
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.ApplyTopBarChanges();
    }
}
