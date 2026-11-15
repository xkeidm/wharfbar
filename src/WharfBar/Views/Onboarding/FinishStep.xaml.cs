using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views.Onboarding;

/// <summary>
/// Last setup step: a read-back summary of what the wizard changed plus the two choices that do
/// not fit an earlier step (start with Windows, UI language). Everything is already saved by the
/// earlier steps; this page only edits config and lets <see cref="ConfigService.Save"/> sync the
/// Startup shortcut.
/// </summary>
public partial class FinishStep : UserControl
{
    private bool _isLoading = true;

    public FinishStep()
    {
        InitializeComponent();
        LoadSummary();

        AutostartToggle.IsChecked = Cfg.Autostart;
        LanguageCombo.SelectedIndex = Cfg.Language switch
        {
            AppConfig.LanguageEn => 1,
            AppConfig.LanguageTr => 2,
            _ => 0,
        };

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    private void LoadSummary()
    {
        ThemeValueText.Text = Cfg.Theme switch
        {
            AppConfig.ThemeLight => Strings.Appearance_ThemeLight,
            AppConfig.ThemeDark => Strings.Appearance_ThemeDark,
            _ => Strings.Appearance_ThemeSystem,
        };

        int itemCount = TopBarStatusItemIds.Normalize(Cfg.TopBar.StatusItems).Count;
        BarItemsValueText.Text = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Onboarding_Finish_ItemsFormat,
            itemCount);

        DockValueText.Text = DockPositions.Normalize(Cfg.Dock.Position) switch
        {
            DockPositions.BottomLeft => Strings.DockPage_PositionLeft,
            DockPositions.BottomRight => Strings.DockPage_PositionRight,
            DockPositions.Left => Strings.DockPage_PositionLeftEdge,
            _ => Strings.DockPage_PositionCenter,
        };
    }

    private void OnAutostartChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Autostart = AutostartToggle.IsChecked == true;

        // ConfigService.Save re-applies the Startup-folder shortcut, so no extra apply call.
        Config.Save(Cfg);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Language = LocalizationService.NormalizeLanguage(LanguageCombo.SelectedIndex switch
        {
            1 => AppConfig.LanguageEn,
            2 => AppConfig.LanguageTr,
            _ => AppConfig.LanguageSystem,
        });

        // LocalizationService.Apply runs before the first window; a live switch would need a
        // restart, same rule as Settings → General.
        Config.Save(Cfg);
        LanguageRestartNote.Visibility = Visibility.Visible;
    }
}
