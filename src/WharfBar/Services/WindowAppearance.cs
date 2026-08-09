using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WharfBar.Native;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WharfBar.Services;

/// <summary>
/// Owns the Fluent appearance of one borderless window: theme dictionaries, system backdrop,
/// DWM chrome, and re-applying all of it when the system theme changes.
///
/// Division of labour — WPF-UI supplies the theme dictionaries and the backdrop call; raw DWM
/// covers the two things it only exposes on its own <c>FluentWindow</c>: corner preference and
/// frame extension. The backdrop type is never driven from both layers at once.
///
/// <see cref="SystemThemeWatcher"/> is deliberately not used: it reacts to theme changes by
/// calling <c>ApplicationThemeManager.ApplySystemTheme</c>, which re-applies the library default
/// (<see cref="WindowBackdropType.Mica"/>) and silently overrides any other requested backdrop.
/// The owning window forwards the theme messages to <see cref="Refresh"/> instead.
/// Theme choice comes from <see cref="AppThemePreference"/> (config <c>theme</c>).
/// </summary>
internal sealed class WindowAppearance
{
    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly WindowBackdropType _backdrop;
    private readonly DwmCornerPreference _corners;

    internal WindowAppearance(
        Window window,
        IntPtr hwnd,
        WindowBackdropType backdrop,
        DwmCornerPreference corners)
    {
        _window = window;
        _hwnd = hwnd;
        _backdrop = backdrop;
        _corners = corners;
    }

    /// <summary>True when a real system backdrop is drawing; false means the opaque fallback.</summary>
    internal bool IsBackdropActive { get; private set; }

    internal bool Apply()
    {
        Dwm.SetCornerPreference(_hwnd, _corners);

        ApplicationTheme theme = AppThemePreference.Resolve();

        // Swaps the theme resource dictionaries. Passing our backdrop explicitly keeps WPF-UI
        // from force-applying its default one to Application.MainWindow.
        ApplicationThemeManager.Apply(theme, _backdrop, updateAccent: false);

        // Plain Window (not FluentWindow) does not always get this from WPF-UI; set it so the
        // system backdrop tints with light/dark correctly.
        Dwm.SetImmersiveDarkMode(_hwnd, theme == ApplicationTheme.Dark);

        IsBackdropActive = TryApplyBackdrop();

        if (!IsBackdropActive)
        {
            ApplyFallbackBackground();
        }

        return IsBackdropActive;
    }

    /// <summary>Call when the system reports a theme or colourization change.</summary>
    internal void Refresh() => Apply();

    private bool TryApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero || !WindowBackdrop.IsSupported(_backdrop) || !Dwm.IsCompositionEnabled())
        {
            return false;
        }

        // A WindowStyle=None window has no non-client glass region, so extend the frame across
        // the client area before asking for a system backdrop.
        Dwm.ExtendFrameIntoClientArea(_hwnd);

        DwmSystemBackdrop expected = ToDwmBackdrop(_backdrop);

        bool applied = WindowBackdrop.ApplyBackdrop(_hwnd, _backdrop)
            || Dwm.SetSystemBackdrop(_hwnd, expected);

        // DWM can return success and still ignore the attribute; read it back rather than
        // trusting the HRESULT, otherwise a silent fallback to flat colour looks like success.
        if (applied && Dwm.TryGetSystemBackdrop(_hwnd, out DwmSystemBackdrop actual) && actual != expected)
        {
            applied = false;
        }

        if (!applied)
        {
            Dwm.SetSystemBackdrop(_hwnd, DwmSystemBackdrop.None);
            return false;
        }

        // Only safe once the material is confirmed behind the window, otherwise this would
        // punch a transparent hole with nothing to fill it.
        WindowBackdrop.RemoveBackground(_window);
        return true;
    }

    private void ApplyFallbackBackground()
    {
        Color color = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Light
            ? Color.FromRgb(0xFA, 0xFA, 0xFA)
            : Color.FromRgb(0x20, 0x20, 0x20);

        _window.SetCurrentValue(Control.BackgroundProperty, new SolidColorBrush(color));
    }

    /// <summary>WPF-UI and DWM number these differently for <c>Auto</c> and <c>None</c>.</summary>
    private static DwmSystemBackdrop ToDwmBackdrop(WindowBackdropType backdrop) => backdrop switch
    {
        WindowBackdropType.Auto => DwmSystemBackdrop.Auto,
        WindowBackdropType.Mica => DwmSystemBackdrop.Mica,
        WindowBackdropType.Acrylic => DwmSystemBackdrop.Acrylic,
        WindowBackdropType.Tabbed => DwmSystemBackdrop.MicaAlt,
        _ => DwmSystemBackdrop.None,
    };
}
