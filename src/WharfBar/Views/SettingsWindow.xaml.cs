using System.Windows;
using System.Windows.Threading;
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
        // updateAccents: false — accent remapping was painting page body text blue.
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: false);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Defer until NavigationView finishes measuring its content presenter.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => NavView.Navigate(typeof(GeneralPage))));
    }
}
