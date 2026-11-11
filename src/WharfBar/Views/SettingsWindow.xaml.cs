using System.Windows;
using System.Windows.Threading;
using WharfBar.Services;
using WharfBar.Views.Pages;
using Wpf.Ui.Controls;

namespace WharfBar.Views;

/// <summary>
/// WharfBar settings window using WPF-UI FluentWindow with NavigationView.
/// Automatically adapts to Windows 10/11 visual styles via Mica/Acrylic backdrops.
/// <see cref="AppThemeWatcher"/> keeps the frame on the configured light/dark preference when the
/// OS theme or colorization changes (WPF-UI's SystemThemeWatcher would force the system theme).
/// Managed as a singleton via <see cref="App.ShowSettings"/>.
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    private const WindowBackdropType Backdrop = WindowBackdropType.Mica;

    public SettingsWindow()
    {
        InitializeComponent();
        // updateAccents: false — accent remapping was painting page body text blue.
        AppThemeWatcher.Watch(this, Backdrop, () => (Application.Current as App)?.ApplyThemeChanges());
        Loaded += OnLoaded;
    }

    /// <summary>Re-asserts the window frame for the current app theme (in-app theme switch).</summary>
    internal void RefreshTheme() => AppThemeWatcher.RefreshWindow(this, Backdrop);

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Defer until NavigationView finishes measuring its content presenter.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => NavView.Navigate(typeof(GeneralPage))));
    }
}
