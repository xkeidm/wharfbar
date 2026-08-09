using System.Windows;
using WharfBar.Models;
using WharfBar.Services;
using WharfBar.Views;
using Wpf.Ui.Appearance;

namespace WharfBar;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private ConfigService? _config;
    private DockWindow? _dock;
    private TrayIconService? _tray;
    private TaskbarVisibilityService? _taskbar;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = SingleInstanceGuard.TryAcquire();

        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _config = new ConfigService();
        AppConfig config = _config.LoadOrCreate();

        // LoadOrCreate already Save()'s on first run / reset (which syncs Startup).
        // Explicit Apply covers the common case where config exists and Save is skipped.
        new AutostartService().Apply(config.Autostart);

        // Match the configured theme before the first window is realized, so the bar
        // never flashes the wrong palette.
        AppThemePreference.Mode = config.Theme;
        ApplicationThemeManager.Apply(AppThemePreference.Resolve());

        _taskbar = new TaskbarVisibilityService();
        _taskbar.CaptureAndHide();

        var topBar = new TopBarWindow(_taskbar, _config);
        MainWindow = topBar;
        topBar.Show();

        _dock = new DockWindow(config);
        _dock.Show();

        // Own tray icon only (quit menu). Not reading system status tray icons.
        _tray = new TrayIconService();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbar?.Restore();
        _taskbar = null;

        _tray?.Dispose();
        _tray = null;

        _dock?.Close();
        _dock = null;

        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }
}
