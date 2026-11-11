using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WharfBar.Native;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WharfBar.Services;

/// <summary>
/// Watches one window for Windows theme / colorization broadcasts and re-applies the theme the
/// user configured (<see cref="AppThemePreference"/>), never the raw system theme.
///
/// WPF-UI's <see cref="SystemThemeWatcher"/> cannot be used for the settings/gallery windows: its
/// first <c>Watch</c> call applies the SYSTEM theme to the whole application, and every later
/// broadcast does the same. A pinned light (or dark) preference is therefore silently overridden
/// the first time one of those windows opens — the app runs in the OS palette while the Appearance
/// page still shows the pinned choice. This watcher keeps the preference authoritative and only
/// uses the system notification as a trigger to re-resolve it.
/// </summary>
internal static class AppThemeWatcher
{
    private static readonly List<WatchedWindow> Watched = [];

    /// <summary>
    /// Starts watching <paramref name="window"/>. <paramref name="onThemeApplied"/> runs after a
    /// global theme dictionary swap, because that swap repaints the shell's main window opaque and
    /// the owning app has to re-assert every top bar / dock background.
    /// </summary>
    internal static void Watch(Window window, WindowBackdropType backdrop, Action onThemeApplied)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(onThemeApplied);

        var watched = new WatchedWindow(window, backdrop, onThemeApplied);
        Watched.Add(watched);

        if (window.IsLoaded)
        {
            watched.Attach();
        }
        else
        {
            window.SourceInitialized += (_, _) => watched.Attach();
        }

        window.Closed += (_, _) =>
        {
            watched.Detach();
            _ = Watched.Remove(watched);
        };
    }

    /// <summary>Re-asserts one window's frame for the current app theme (no system query).</summary>
    internal static void RefreshWindow(Window window, WindowBackdropType backdrop)
        => WindowBackgroundManager.UpdateBackground(window, ApplicationThemeManager.GetAppTheme(), backdrop);

    private static bool IsImmersiveColorSet(IntPtr lParam)
        => lParam != IntPtr.Zero
            && string.Equals(Marshal.PtrToStringUni(lParam), "ImmersiveColorSet", StringComparison.Ordinal);

    private sealed class WatchedWindow
    {
        private readonly Window _window;
        private readonly WindowBackdropType _backdrop;
        private readonly Action _onThemeApplied;
        private HwndSource? _source;

        internal WatchedWindow(Window window, WindowBackdropType backdrop, Action onThemeApplied)
        {
            _window = window;
            _backdrop = backdrop;
            _onThemeApplied = onThemeApplied;
        }

        internal void Attach()
        {
            if (_source is not null)
            {
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WndProc);

            // The window was realized under whatever palette was active at that moment; pin its
            // frame to the configured theme instead of trusting the creation-time default.
            AppThemeWatcher.RefreshWindow(_window, _backdrop);
        }

        internal void Detach()
        {
            _source?.RemoveHook(WndProc);
            _source = null;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg is NativeMethods.WM_THEMECHANGED
                or NativeMethods.WM_SYSCOLORCHANGE
                or NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED
                || (msg == NativeMethods.WM_SETTINGCHANGE && IsImmersiveColorSet(lParam)))
            {
                Refresh();
            }

            return IntPtr.Zero;
        }

        private void Refresh()
        {
            ApplicationTheme desired = AppThemePreference.Resolve();
            if (ApplicationThemeManager.GetAppTheme() != desired)
            {
                // Backdrop None: the shell windows re-assert their own backdrop right after, and a
                // Mica attach to the layered (transparent/tint) top bar is forbidden. The library
                // repaints Application.MainWindow opaque on every apply, hence the callback.
                ApplicationThemeManager.Apply(desired, WindowBackdropType.None, updateAccent: false);
                _onThemeApplied();
            }

            AppThemeWatcher.RefreshWindow(_window, _backdrop);
        }
    }
}
