using System.Windows;
using WharfBar.Services;
using WharfBar.Views;
using Wpf.Ui.Appearance;

namespace WharfBar;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private DockWindow? _dock;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = SingleInstanceGuard.TryAcquire();

        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Match the system light/dark choice before the first window is realized, so the bar
        // never flashes the wrong theme.
        ApplicationThemeManager.ApplySystemTheme();

        var topBar = new TopBarWindow();
        MainWindow = topBar;
        topBar.Show();

        _dock = new DockWindow();
        _dock.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _dock?.Close();
        _dock = null;

        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }
}
