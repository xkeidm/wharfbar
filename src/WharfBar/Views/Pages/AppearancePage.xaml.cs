using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Wpf.Ui.Appearance;

namespace WharfBar.Views.Pages;

/// <summary>
/// Appearance settings: theme mode, accent style (system accent vs theme default), top bar background mode, tint opacity, logo style.
/// </summary>
public partial class AppearancePage : Page
{
    private bool _isLoading = true;

    public AppearancePage()
    {
        InitializeComponent();
        LoadSettings();
        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    private void LoadSettings()
    {
        // Accent style combo: 0=System Accent, 1=Fixed Theme Mode
        AccentStyleCombo.SelectedIndex = Cfg.UseSystemAccentColor ? 0 : 1;

        // Theme combo: 0=System, 1=Light, 2=Dark
        ThemeCombo.SelectedIndex = Cfg.Theme switch
        {
            AppConfig.ThemeLight => 1,
            AppConfig.ThemeDark => 2,
            _ => 0,
        };

        // Background mode combo: 0=Mica, 1=Transparent, 2=Tint
        string mode = TopBarBackgroundModes.NormalizeMode(Cfg.TopBar.BackgroundMode);
        BackgroundCombo.SelectedIndex = mode switch
        {
            TopBarBackgroundModes.Transparent => 1,
            TopBarBackgroundModes.Tint => 2,
            _ => 0,
        };

        int opacity = TopBarBackgroundModes.ClampOpacity(Cfg.TopBar.TintOpacity);
        OpacitySlider.Value = opacity;
        OpacityLabel.Text = $"{opacity}%";
        UpdateOpacityCardVisibility(mode);

        LogoSectionHeader.Text = LocalizationService.Get("Appearance_Logo_Section");
        LogoTitleText.Text = LocalizationService.Get("Appearance_Logo_Title");
        LogoDescriptionText.Text = LocalizationService.Get("Appearance_Logo_Description");
        ((ComboBoxItem)LogoStyleCombo.Items[0]).Content = LocalizationService.Get("Appearance_Logo_WharfBar");
        ((ComboBoxItem)LogoStyleCombo.Items[1]).Content = LocalizationService.Get("Appearance_Logo_Windows");

        // Combo: 0=WharfBar, 1=Windows
        LogoStyleCombo.SelectedIndex = LogoStyles.IsWindows(Cfg.LogoStyle) ? 1 : 0;
        LogoPreviewGlyph.Data = BrandLogo.BarGlyph(Cfg.LogoStyle);
    }

    private void OnLogoStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        Cfg.LogoStyle = LogoStyleCombo.SelectedIndex == 1 ? LogoStyles.Windows : LogoStyles.WharfBar;
        Config.Save(Cfg);
        LogoPreviewGlyph.Data = BrandLogo.BarGlyph(Cfg.LogoStyle);
        WharfApp.ApplyLogoChanges();
    }

    private void OnAccentStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        bool useSystemAccent = AccentStyleCombo.SelectedIndex == 0;
        Cfg.UseSystemAccentColor = useSystemAccent;
        Config.Save(Cfg);

        if (useSystemAccent)
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }
        else
        {
            ApplicationThemeManager.Apply(AppThemePreference.Resolve());
        }

        // Direct library applies retarget Application.MainWindow's backdrop/chrome
        // regardless of the configured top-bar background mode — re-assert every bar.
        // The dock launch tile is tinted from the accent and theme, so it follows too.
        WharfApp.ApplyLogoChanges();
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        string theme = ThemeCombo.SelectedIndex switch
        {
            1 => AppConfig.ThemeLight,
            2 => AppConfig.ThemeDark,
            _ => AppConfig.ThemeSystem,
        };

        Cfg.Theme = theme;
        AppThemePreference.Mode = theme;
        ApplicationThemeManager.Apply(AppThemePreference.Resolve());

        // Light/dark also write Windows personalization; system leaves Windows alone.
        if (theme == AppConfig.ThemeLight)
        {
            _ = SystemThemeWriter.TryApply(light: true);
        }
        else if (theme == AppConfig.ThemeDark)
        {
            _ = SystemThemeWriter.TryApply(light: false);
        }

        if (Cfg.UseSystemAccentColor)
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }

        Config.Save(Cfg);

        // See OnAccentStyleChanged: library applies rewrite MainWindow chrome; re-assert bars.
        // A theme switch also has to re-establish the dock DWM dark mode and the settings/gallery
        // frames — switching to "system" emits no system broadcast of its own.
        WharfApp.ApplyThemeChanges();

        WharfApp.FollowWallpaperTheme(fromExternalWindows: false);
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

        string oldMode = TopBarBackgroundModes.NormalizeMode(Cfg.TopBar.BackgroundMode);
        bool oldNeedsTx = TopBarBackgroundModes.NeedsTransparency(oldMode);
        bool newNeedsTx = TopBarBackgroundModes.NeedsTransparency(newMode);

        Cfg.TopBar.BackgroundMode = newMode;
        Config.Save(Cfg);

        UpdateOpacityCardVisibility(newMode);

        if (oldNeedsTx != newNeedsTx)
        {
            // AllowsTransparency must flip — rebuild the TopBar window.
            WharfApp.RecreateTopBar();
        }
        else
        {
            WharfApp.ApplyTopBarChanges();
        }
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        OpacityLabel.Text = $"{v}%";

        if (_isLoading) return;

        Cfg.TopBar.TintOpacity = TopBarBackgroundModes.ClampOpacity(v);
        Config.Save(Cfg);
        WharfApp.ApplyTopBarChanges();
    }

    private void UpdateOpacityCardVisibility(string mode)
    {
        OpacityCard.Visibility = mode == TopBarBackgroundModes.Tint
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
