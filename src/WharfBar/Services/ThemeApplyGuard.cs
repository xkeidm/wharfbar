using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WharfBar.Services;

/// <summary>
/// The only place that calls <see cref="ApplicationThemeManager.Apply"/>.
///
/// WPF-UI 4.3.0 repaints <c>Application.MainWindow</c> on every theme apply
/// (<see cref="ApplicationThemeManager.Apply"/> → <c>WindowBackgroundManager.UpdateBackground</c>):
/// <c>RestoreContentBackground</c> writes an opaque window background and an opaque composition
/// target, and the matching <c>RemoveBackground</c> only runs when the requested backdrop is not
/// <c>None</c>. WharfBar's main window is the primary top bar, whose background is owned by
/// <c>TopBarWindow.ApplyBackgroundAppearance</c> — a library repaint therefore raced with the
/// configured <c>topBar.backgroundMode</c> and could leave a transparent bar opaque or carrying a
/// Mica backdrop until the next re-assert. Detaching the main window for the duration of the
/// library call means the bar's background is only ever written by the bar itself; every other
/// window applies its own backdrop through <see cref="WindowAppearance"/>, exactly as before.
/// </summary>
internal static class ThemeApplyGuard
{
    private static bool _applying;

    /// <summary>True while the library's global theme apply is running (re-entrancy guard).</summary>
    internal static bool IsApplying => _applying;

    /// <summary>True when <paramref name="theme"/> is the already-applied application theme.</summary>
    internal static bool IsCurrent(ApplicationTheme theme) =>
        ApplicationThemeManager.GetAppTheme() == theme;

    /// <summary>
    /// Applies the application theme dictionaries without touching the main window's background.
    /// </summary>
    /// <param name="theme">Theme to apply.</param>
    /// <param name="backdrop">
    /// Backdrop handed to WPF-UI for windows it manages itself. The main window never receives it.
    /// </param>
    /// <param name="updateAccent">Whether the system accent brushes are refreshed as part of the apply.</param>
    /// <param name="force">
    /// Re-apply even when the cached theme already matches. The accent-style path needs this to reset
    /// the theme's default accent brushes after a system accent was applied.
    /// </param>
    /// <returns>True when the library apply ran.</returns>
    internal static bool Apply(
        ApplicationTheme theme,
        WindowBackdropType backdrop = WindowBackdropType.Mica,
        bool updateAccent = true,
        bool force = false)
    {
        if (!force && IsCurrent(theme))
        {
            return false;
        }

        if (_applying)
        {
            // A nested apply (e.g. an accent apply from inside a theme apply) still needs the
            // dictionary swap, but the main window is already detached by the outer call.
            ApplicationThemeManager.Apply(theme, backdrop, updateAccent);
            return true;
        }

        Application? app = Application.Current;
        Window? mainWindow = app?.MainWindow;

        _applying = true;
        try
        {
            if (app is not null && mainWindow is not null)
            {
                // WPF-UI skips its main-window background write when MainWindow is null.
                app.MainWindow = null;
            }

            ApplicationThemeManager.Apply(theme, backdrop, updateAccent);
        }
        finally
        {
            _applying = false;

            // Never leave MainWindow wrong: WindowAppearance and the top bar's chrome re-assert both
            // key off "application main window = primary bar". Restore unconditionally — WPF can
            // auto-assign MainWindow to a window created while the bar was detached, and leaving that
            // assignment in place would silently stop the bar from ever compensating again.
            if (app is not null && mainWindow is not null)
            {
                app.MainWindow = mainWindow;
            }
        }

        return true;
    }
}
