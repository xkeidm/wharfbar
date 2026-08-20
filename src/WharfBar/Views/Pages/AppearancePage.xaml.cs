using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;
using Wpf.Ui.Appearance;

namespace WharfBar.Views.Pages;

/// <summary>
/// Appearance settings: theme mode, accent style (system accent vs theme default), top bar background mode, tint opacity.
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

        if (Cfg.UseSystemAccentColor)
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }

        Config.Save(Cfg);
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
