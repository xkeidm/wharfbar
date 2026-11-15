using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;

namespace WharfBar.Views.Onboarding;

/// <summary>
/// First-run Appearance step: theme mode, accent source and logo style. Changes are persisted and
/// applied through <see cref="AppearanceApplier"/> immediately, so the real top bar and dock behind
/// the wizard update while the user is still on this step.
/// </summary>
public partial class AppearanceStep : UserControl
{
    private bool _isLoading = true;
    private string _theme = AppConfig.ThemeSystem;
    private bool _useSystemAccent = true;
    private string _logoStyle = LogoStyles.Default;

    public AppearanceStep()
    {
        InitializeComponent();
        LoadState();
        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    /// <summary>Reads the current config once; the window creates a fresh step on every visit.</summary>
    private void LoadState()
    {
        // Appearance_Logo_* exists in the resx but not in the generated Designer (the Appearance
        // settings page reads the same keys through LocalizationService).
        LogoSectionHeader.Text = LocalizationService.Get("Appearance_Logo_Section");
        LogoWharfBarLabel.Text = LocalizationService.Get("Appearance_Logo_WharfBar");
        LogoWindowsLabel.Text = LocalizationService.Get("Appearance_Logo_Windows");

        _theme = Cfg.Theme switch
        {
            AppConfig.ThemeLight => AppConfig.ThemeLight,
            AppConfig.ThemeDark => AppConfig.ThemeDark,
            _ => AppConfig.ThemeSystem,
        };
        UpdateThemeSelection();

        _useSystemAccent = Cfg.UseSystemAccentColor;
        UpdateAccentSelection();

        _logoStyle = LogoStyles.Normalize(Cfg.LogoStyle);
        WharfLogoPath.Data = BrandLogo.BarGlyph(LogoStyles.WharfBar);
        WindowsLogoPath.Data = BrandLogo.BarGlyph(LogoStyles.Windows);
        UpdateLogoSelection();
    }

    private void OnThemeSystemClick(object sender, RoutedEventArgs e) => SelectTheme(AppConfig.ThemeSystem);

    private void OnThemeLightClick(object sender, RoutedEventArgs e) => SelectTheme(AppConfig.ThemeLight);

    private void OnThemeDarkClick(object sender, RoutedEventArgs e) => SelectTheme(AppConfig.ThemeDark);

    private void SelectTheme(string theme)
    {
        if (_isLoading || theme == _theme) return;

        _theme = theme;
        AppearanceApplier.ApplyTheme(theme);

        // The library's theme apply swaps resource dictionaries in place; the elements survive,
        // but re-assert the selection chrome so every brush reference is re-evaluated.
        UpdateThemeSelection();
    }

    private void OnAccentSystemClick(object sender, RoutedEventArgs e) => SelectAccent(useSystemAccent: true);

    private void OnAccentFixedClick(object sender, RoutedEventArgs e) => SelectAccent(useSystemAccent: false);

    private void SelectAccent(bool useSystemAccent)
    {
        if (_isLoading || useSystemAccent == _useSystemAccent) return;

        _useSystemAccent = useSystemAccent;
        AppearanceApplier.ApplyAccentStyle(useSystemAccent);
        UpdateAccentSelection();
    }

    private void OnLogoWharfBarClick(object sender, RoutedEventArgs e) => SelectLogo(LogoStyles.WharfBar);

    private void OnLogoWindowsClick(object sender, RoutedEventArgs e) => SelectLogo(LogoStyles.Windows);

    private void SelectLogo(string style)
    {
        if (_isLoading || style == _logoStyle) return;

        _logoStyle = style;
        AppearanceApplier.ApplyLogoStyle(style);
        UpdateLogoSelection();
    }

    private void UpdateThemeSelection()
    {
        SetCardSelection(ThemeSystemCard, ThemeSystemCheck, _theme == AppConfig.ThemeSystem);
        SetCardSelection(ThemeLightCard, ThemeLightCheck, _theme == AppConfig.ThemeLight);
        SetCardSelection(ThemeDarkCard, ThemeDarkCheck, _theme == AppConfig.ThemeDark);
    }

    private void UpdateAccentSelection()
    {
        SetCardSelection(AccentSystemCard, AccentSystemCheck, _useSystemAccent);
        SetCardSelection(AccentFixedCard, AccentFixedCheck, !_useSystemAccent);
    }

    private void UpdateLogoSelection()
    {
        SetCardSelection(LogoWharfBarCard, LogoWharfBarCheck, !LogoStyles.IsWindows(_logoStyle));
        SetCardSelection(LogoWindowsCard, LogoWindowsCheck, LogoStyles.IsWindows(_logoStyle));
    }

    /// <summary>
    /// Selected card = 2 px accent border + check glyph. The padding shrinks by one pixel on the
    /// selected card so the outer size never jumps when the border thickens.
    /// </summary>
    private static void SetCardSelection(Border card, SymbolIcon check, bool selected)
    {
        card.BorderThickness = new Thickness(selected ? 2 : 1);
        card.Padding = selected ? new Thickness(15, 13, 15, 13) : new Thickness(16, 14, 16, 14);
        card.SetResourceReference(
            Border.BorderBrushProperty,
            selected ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush");
        check.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    }
}
