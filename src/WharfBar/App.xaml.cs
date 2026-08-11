using System.Windows;
using System.Windows.Interop;
using WharfBar.Models;
using WharfBar.Native;
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
    private SettingsWindow? _settings;

    /// <summary>Exposes the configuration service for settings pages.</summary>
    internal ConfigService? Config => _config;

    /// <summary>Exposes the dock window for settings-driven updates.</summary>
    internal DockWindow? DockView => _dock;

    /// <summary>The currently active <see cref="TopBarWindow"/> (same as <see cref="MainWindow"/>).</summary>
    internal TopBarWindow? TopBar => MainWindow as TopBarWindow;

    /// <summary>
    /// Opens the settings window or activates it if already open.
    /// Surfaces reliably despite Topmost bar/dock and tray-menu foreground lock.
    /// </summary>
    internal void ShowSettings()
    {
        if (_settings is null)
        {
            _settings = new SettingsWindow();
            _settings.Closed += (_, _) => _settings = null;
            _settings.Show();
        }
        else
        {
            if (_settings.WindowState == WindowState.Minimized)
            {
                _settings.WindowState = WindowState.Normal;
            }

            _settings.Show();
        }

        BringSettingsToFront(_settings);
    }

    private static void BringSettingsToFront(SettingsWindow settings)
    {
        // TopBar + Dock are Topmost; a brief Topmost pulse lifts settings above them
        // without permanently stealing z-order from the shell overlays.
        settings.Topmost = true;
        _ = settings.Activate();
        _ = settings.Focus();
        settings.Topmost = false;

        IntPtr hwnd = new WindowInteropHelper(settings).EnsureHandle();
        if (hwnd != IntPtr.Zero)
        {
            _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            _ = NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = SingleInstanceGuard.TryAcquire();

        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        Resources["AppTextFontFamily"] = SystemVersion.TextFontFamily;
        Resources["AppIconFontFamily"] = SystemVersion.IconFontFamily;

        _config = new ConfigService();
        AppConfig config = _config.LoadOrCreate();

        // LoadOrCreate already Save()'s on first run / reset (which syncs Startup).
        // Explicit Apply covers the common case where config exists and Save is skipped.
        new AutostartService().Apply(config.Autostart);

        // Match the configured theme before the first window is realized, so the bar
        // never flashes the wrong palette.
        AppThemePreference.Mode = config.Theme;
        ApplicationThemeManager.Apply(AppThemePreference.Resolve());
        if (config.UseSystemAccentColor)
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }

        _taskbar = new TaskbarVisibilityService();
        _taskbar.CaptureAndHide();

        var topBar = new TopBarWindow(_taskbar, _config);
        MainWindow = topBar;
        topBar.Show();

        _dock = new DockWindow(config);
        _dock.Show();

        // Dock's WindowAppearance.Apply calls ApplicationThemeManager.Apply, which
        // RestoreContentBackground on MainWindow — re-assert top bar translucent/mica.
        topBar.ApplyConfigChanges();

        // Own tray icon only (quit menu). Not reading system status tray icons.
        _tray = new TrayIconService();
    }

    /// <summary>
    /// Rebuilds the top bar when <c>AllowsTransparency</c> must flip (Mica ↔ translucent).
    /// Removes the old AppBar only — does <strong>not</strong> restore the Windows taskbar.
    /// Constructs the next window first (no AppBar until Show), then ABM_REMOVE on old;
    /// on failure, re-registers the old AppBar and keeps it as MainWindow.
    /// </summary>
    internal void RecreateTopBar()
    {
        if (_config is null || _taskbar is null)
        {
            return;
        }

        if (MainWindow is not TopBarWindow old)
        {
            return;
        }

        TopBarWindow? next = null;
        var prepared = false;
        try
        {
            // Ctor only — AppBar registers in OnSourceInitialized during Show.
            next = new TopBarWindow(_taskbar, _config);
            old.PrepareForRecreate(); // ABM_REMOVE; no taskbar restore
            prepared = true;
            MainWindow = next;
            next.Show();
        }
        catch
        {
            if (next is not null)
            {
                try
                {
                    // Drop failed next without restoring the taskbar (Quit path only Restores).
                    next.PrepareForRecreate();
                    next.Close();
                }
                catch
                {
                    // Best-effort discard.
                }
            }

            MainWindow = old;
            if (prepared)
            {
                old.AbortRecreate();
            }

            return;
        }

        old.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbar?.Restore();
        _taskbar = null;

        _tray?.Dispose();
        _tray = null;

        _settings?.Close();
        _settings = null;

        _dock?.Close();
        _dock = null;

        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }
}
