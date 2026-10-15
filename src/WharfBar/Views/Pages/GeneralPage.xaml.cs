using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// General settings page: autostart, language.
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
        ApplyLocalizedLabels();

        AutostartToggle.IsChecked = Cfg.Autostart;

        LanguageCombo.SelectedIndex = Cfg.Language switch
        {
            AppConfig.LanguageEn => 1,
            AppConfig.LanguageTr => 2,
            _ => 0,
        };
    }

    private void ApplyLocalizedLabels()
    {
        string title = LocalizationService.Get("General_PageTitle");
        PageTitleText.Text = title;
        Title = title;

        LanguageSectionHeader.Text = LocalizationService.Get("General_Language_Title");
        LanguageTitleText.Text = LocalizationService.Get("General_Language_Title");
        LanguageDescriptionText.Text = LocalizationService.Get("General_Language_Description");
        LanguageRestartNote.Text = LocalizationService.Get("General_Language_RestartNote");

        SetComboContent(0, LocalizationService.Get("General_Language_System"));
        SetComboContent(1, LocalizationService.Get("General_Language_English"));
        SetComboContent(2, LocalizationService.Get("General_Language_Turkish"));
    }

    private void SetComboContent(int index, string content)
    {
        if (LanguageCombo.Items[index] is ComboBoxItem item)
        {
            item.Content = content;
        }
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

        Cfg.Language = language;
        Config.Save(Cfg);
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
