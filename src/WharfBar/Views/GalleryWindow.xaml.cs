using System.IO;
using System.Windows;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;
using Wpf.Ui.Controls;

namespace WharfBar.Views;

/// <summary>
/// Widget and plugin gallery opened from the launchpad.
/// Plugins are toggles in this process; nothing loads an outside assembly.
/// </summary>
internal partial class GalleryWindow : FluentWindow
{
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    private static GalleryWindow? _plugins;
    private static GalleryWindow? _widgets;

    public GalleryWindow()
    {
        InitializeComponent();
        // SystemThemeWatcher would apply the SYSTEM theme on open and override a pinned
        // light/dark preference; AppThemeWatcher re-resolves the configured theme instead.
        AppThemeWatcher.Watch(this, Backdrop, () => (Application.Current as App)?.ApplyThemeChanges());
    }

    /// <summary>Re-asserts this window's frame for the current app theme.</summary>
    internal void RefreshTheme() => AppThemeWatcher.RefreshWindow(this, Backdrop);

    /// <summary>Re-asserts every open gallery window (the instances are cached per scope).</summary>
    internal static void RefreshThemeForAll()
    {
        _widgets?.RefreshTheme();
        _plugins?.RefreshTheme();
    }

    internal static void Show(AppConfig config, bool widgets)
    {
        GalleryWindow? window = widgets ? _widgets : _plugins;
        if (window is null)
        {
            window = new GalleryWindow();
            window.Closed += (_, _) =>
            {
                if (widgets)
                {
                    _widgets = null;
                }
                else
                {
                    _plugins = null;
                }
            };

            if (widgets)
            {
                _widgets = window;
            }
            else
            {
                _plugins = window;
            }
        }

        window.Open(config, widgets);
        window.Show();
        window.Activate();
    }

    private void Open(AppConfig config, bool widgets)
    {
        string title = widgets ? Strings.Gallery_Widgets : Strings.Gallery_Plugins;
        Title = title;
        Bar.Title = title;
        Gallery.Bind(config, ResolvePluginsDirectory(), widgets ? PluginGalleryScope.Widgets : PluginGalleryScope.Plugins);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Gallery.Refresh();
    }

    private static string ResolvePluginsDirectory()
    {
        if (Application.Current is App app && app.Config is not null)
        {
            return PluginCatalog.PluginsDirectory(app.Config.ConfigDirectory);
        }

        return PluginCatalog.PluginsDirectory(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WharfBar"));
    }
}
