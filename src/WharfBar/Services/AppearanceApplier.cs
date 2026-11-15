using System.Windows;
using WharfBar.Models;
using WharfBar.Native;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WharfBar.Services;

/// <summary>
/// Single home for the appearance apply pipeline shared by the Appearance settings page and the
/// first-run Appearance step: theme mode, accent style and brand logo. Every call persists the
/// config immediately and re-asserts the live shell surfaces through <see cref="App"/>.
/// </summary>
internal static class AppearanceApplier
{
    private static App WharfApp => (App)Application.Current;
    private static ConfigService Config => WharfApp.Config!;
    private static AppConfig Cfg => Config.Current;

    /// <summary>Applies <c>system</c>, <c>light</c> or <c>dark</c>, including Windows personalization.</summary>
    internal static void ApplyTheme(string theme)
    {
        Cfg.Theme = theme;
        AppThemePreference.Mode = theme;
        ApplicationTheme desired = AppThemePreference.Resolve();

        // One coordinated switch: capture the current shell, swap the dictionaries (main window
        // detached, so the bar's configured background can never be repainted by the library),
        // re-assert every surface, then crossfade the captured frame out. Everything that is
        // visible happens in this single dispatcher turn, so no half-themed frame can render.
        ThemeTransition.Run(() =>
        {
            // Backdrop None: the shell manages every backdrop itself; the library must not touch a
            // window (the guard already detaches the main window, this is the second lock).
            ThemeApplyGuard.Apply(desired, WindowBackdropType.None);

            // Light/dark also write Windows personalization; system leaves Windows alone. The
            // broadcast is slow (synchronous HWND_BROADCAST), so it must never run on the UI thread.
            if (theme == AppConfig.ThemeLight)
            {
                _ = SystemThemeWriter.TryApplyAsync(light: true);
            }
            else if (theme == AppConfig.ThemeDark)
            {
                _ = SystemThemeWriter.TryApplyAsync(light: false);
            }

            if (Cfg.UseSystemAccentColor)
            {
                ApplicationAccentColorManager.ApplySystemAccent();
            }

            Config.Save(Cfg);

            // See ApplyAccentStyle: library applies rewrite MainWindow chrome; re-assert bars.
            // A theme switch also has to re-establish the dock DWM dark mode and the settings/gallery
            // frames — switching to "system" emits no system broadcast of its own.
            WharfApp.ApplyThemeChanges();
        },
        // The wallpaper theme-partner switch runs its own full-screen fade; only start it once the
        // shell crossfade has finished, otherwise two independent animations overlap and the switch
        // reads as a stutter again.
        () => WharfApp.FollowWallpaperTheme(fromExternalWindows: false));
    }

    /// <summary>System accent colour (<c>true</c>) vs the fixed theme-mode accent.</summary>
    internal static void ApplyAccentStyle(bool useSystemAccent)
    {
        Cfg.UseSystemAccentColor = useSystemAccent;
        Config.Save(Cfg);

        if (useSystemAccent)
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }
        else
        {
            // Force: the cached theme already matches, but the dictionaries still have to be
            // swapped back so the accent brushes return to the theme defaults.
            ThemeApplyGuard.Apply(AppThemePreference.Resolve(), WindowBackdropType.Mica, force: true);
        }

        // Direct library applies retarget Application.MainWindow's backdrop/chrome regardless of
        // the configured top-bar background mode — re-assert every bar and re-tint dock tiles.
        WharfApp.ApplyLogoChanges();
    }

    /// <summary><c>wharfbar</c> or <c>windows</c> brand glyph on the bar and the dock launch tile.</summary>
    internal static void ApplyLogoStyle(string style)
    {
        Cfg.LogoStyle = LogoStyles.Normalize(style);
        Config.Save(Cfg);
        WharfApp.ApplyLogoChanges();
    }
}
