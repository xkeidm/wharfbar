using System.Windows;
using System.Windows.Controls;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>Settings page for built-in plugins/widgets and local manifests.</summary>
public partial class PluginsPage : Page
{
    public PluginsPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Bind();
            }
        };
        Bind();
    }

    private void Bind()
    {
        if (Application.Current is not App app || app.Config is null)
        {
            return;
        }

        Gallery.Bind(
            app.Config.Current,
            PluginCatalog.PluginsDirectory(app.Config.ConfigDirectory),
            PluginGalleryScope.All);
    }
}
