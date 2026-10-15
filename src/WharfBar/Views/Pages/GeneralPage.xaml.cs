using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// General settings page: autostart and display language.
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
        LanguageCombo.SelectedIndex = Cfg.Language switch
        {
            AppConfig.LanguageEn => 1,
            AppConfig.LanguageTr => 2,
            _ => 0,
        };
        LanguageRestartNote.Visibility = Visibility.Collapsed;
        QuitToApplyButton.Visibility = Visibility.Collapsed;
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Autostart = AutostartToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        string language = LanguageCombo.SelectedIndex switch
        {
            1 => AppConfig.LanguageEn,
            2 => AppConfig.LanguageTr,
            _ => AppConfig.LanguageSystem,
        };
        Cfg.Language = LocalizationService.NormalizeLanguage(language);
        Config.Save(Cfg);
        LanguageRestartNote.Visibility = Visibility.Visible;
        QuitToApplyButton.Visibility = Visibility.Visible;
    }

    private void OnQuitToApplyClick(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.ApplyTopBarChanges();
    }
}
