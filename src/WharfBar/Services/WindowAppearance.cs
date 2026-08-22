using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WharfBar.Models;
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

    /// <summary>
    /// Raised when <see cref="ApplicationThemeManager.Apply"/> has rewritten
    /// <see cref="Application.MainWindow"/> chrome (WPF-UI
    /// <c>RestoreContentBackground</c> → opaque <see cref="SystemColors.WindowColor"/>).
    /// Top bar re-applies translucent/mica after Dock (or any secondary window) theme Apply.
    /// </summary>
    internal static event Action? MainWindowChromeClobbered;

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
        EnsureApplicationTheme(theme, _backdrop);
        RaiseMainWindowChromeClobberedIfNeeded();

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

    /// <summary>
    /// No system backdrop. Window must already have AllowsTransparency=true.
    /// <paramref name="tintOpacity"/> null = fully clear; else 0–100 theme tint.
    /// </summary>
    internal void ApplyTranslucent(int? tintOpacity)
    {
        Dwm.SetCornerPreference(_hwnd, _corners);

        ApplicationTheme theme = AppThemePreference.Resolve();
        EnsureApplicationTheme(theme, WindowBackdropType.None);
        RaiseMainWindowChromeClobberedIfNeeded();
        Dwm.SetImmersiveDarkMode(_hwnd, theme == ApplicationTheme.Dark);

        ClearSystemBackdrop();
        IsBackdropActive = false;

        // ApplicationThemeManager.Apply → RemoveBackdrop → RestoreContentBackground sets
        // CompositionTarget.BackgroundColor = SystemColors.WindowColor (opaque white/gray)
        // and an opaque Window.Background. Local brush alone is not enough for layered
        // windows — composition target must be Transparent too.
        if (tintOpacity is null)
        {
            SetLayeredWindowBackground(Brushes.Transparent);
            return;
        }

        int opacity = TopBarBackgroundModes.ClampOpacity(tintOpacity.Value);
        byte a = (byte)(int)Math.Round(opacity / 100.0 * 255.0);
        Color baseColor = theme == ApplicationTheme.Dark
            ? Color.FromRgb(0x20, 0x20, 0x20)
            : Color.FromRgb(0xFA, 0xFA, 0xFA);
        var brush = new SolidColorBrush(Color.FromArgb(a, baseColor.R, baseColor.G, baseColor.B));
        brush.Freeze();
        SetLayeredWindowBackground(brush);
    }

    /// <summary>
    /// Runs <see cref="ApplicationThemeManager.Apply"/> only when the cached app theme
    /// actually differs. WPF-UI replaces its theme dictionary on EVERY call and then
    /// repaints <c>Application.MainWindow</c> content opaque (RestoreContentBackground);
    /// with backdrop None that opaque paint is never cleared again. Skipping redundant
    /// global applies keeps per-window mode switches from clobbering the main window.
    /// </summary>
    private static void EnsureApplicationTheme(ApplicationTheme theme, WindowBackdropType backdrop)
    {
        if (ApplicationThemeManager.GetAppTheme() == theme)
        {
            return;
        }

        ApplicationThemeManager.Apply(theme, backdrop, updateAccent: false);
    }

    /// <summary>Call when the system reports a theme or colourization change.</summary>
    internal void Refresh() => Apply();

    private void RaiseMainWindowChromeClobberedIfNeeded()
    {
        Window? main = Application.Current?.MainWindow;
        if (main is null || ReferenceEquals(main, _window))
        {
            return;
        }

        // Raised for every non-main window Apply, regardless of AllowsTransparency:
        // RestoreContentBackground paints an opaque SystemColors.WindowColor over the main
        // window in translucent modes AND on top of a live Mica backdrop alike — both need
        // the owner to re-assert its configured background.
        MainWindowChromeClobbered?.Invoke();
    }

    /// <summary>
    /// Layered (<c>AllowsTransparency</c>) windows need both WPF Background and
    /// <see cref="HwndSource.CompositionTarget"/> clear of opaque fill.
    /// </summary>
    private void SetLayeredWindowBackground(Brush brush)
    {
        _window.Background = brush;

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        HwndSource? source = HwndSource.FromHwnd(_hwnd);
        if (source?.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
    }
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

    private void ClearSystemBackdrop()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        Dwm.SetSystemBackdrop(_hwnd, DwmSystemBackdrop.None);
        // Do not call WindowBackdrop.RemoveBackground here for translucent — we own the brush.
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
