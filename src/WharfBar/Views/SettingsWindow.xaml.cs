using System.Windows;
using WharfBar.Views.Pages;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WharfBar.Views;

/// <summary>
/// WharfBar settings window using WPF-UI FluentWindow with NavigationView.
/// Automatically adapts to Windows 10/11 visual styles via Mica/Acrylic backdrops.
/// SystemThemeWatcher ensures backdrop and accent color stay persistent across control interactions.
/// Managed as a singleton via <see cref="App.ShowSettings"/>.
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    public SettingsWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: true);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Navigate to the first page automatically.
        NavView.Navigate(typeof(GeneralPage));
    }
}
