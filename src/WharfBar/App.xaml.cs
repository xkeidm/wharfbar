using System.Linq;
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
    private DockItemStore? _dockItems;
    private MonitorManager? _monitors;
    private TrayIconService? _tray;
    private TaskbarVisibilityService? _taskbar;
    private SettingsWindow? _settings;
    private VeilService? _veil;

    /// <summary>Exposes the configuration service for settings pages.</summary>
    internal ConfigService? Config => _config;

    /// <summary>
    /// The single dock item store. Shared by every per-monitor dock window and the settings
    /// page so a list edit made in settings reaches the docks live instead of only on the
    /// next launch — one store per window would leave N independent lists.
    /// </summary>
    internal DockItemStore? DockItems => _dockItems;

    /// <summary>Exposes the monitor manager for the settings pages.</summary>
    internal MonitorManager? Monitors => _monitors;

    /// <summary>Exposes the primary dock window for settings-driven updates.</summary>
    internal DockWindow? DockView => _monitors?.Docks.FirstOrDefault(d => d.Monitor.IsPrimary)
        ?? _monitors?.Docks.FirstOrDefault();

    /// <summary>The primary top bar (MainWindow when a bar exists).</summary>
    internal TopBarWindow? TopBar => _monitors?.PrimaryTopBar;

    /// <summary>Explorer restart drops Shell_NotifyIcon registrations. Put ours back.</summary>
    internal void ReaddTrayIcon() => _tray?.Readd();

    /// <summary>Applies top-bar settings to every per-monitor bar.</summary>
    internal void ApplyTopBarChanges()
    {
        _monitors?.ApplyTopBarChanges();
    }

    /// <summary>
    /// Logo style, theme or accent changed in settings: re-assert every bar (its glyph follows
    /// <c>logoStyle</c>) and re-tint every dock launch tile.
    /// </summary>
    internal void ApplyLogoChanges()
    {
        _monitors?.ApplyTopBarChanges();
        _monitors?.RefreshDockBrandTiles();
    }

    /// <summary>Reconciles and applies dock settings to every per-monitor dock.</summary>
    internal void ApplyDockChanges()
    {
        _monitors?.Reconcile();
        _monitors?.ApplyDockConfigChanges();
    }

    /// <summary>Exposes the veil service for settings pages and shutdown.</summary>
    internal VeilService? Veil => _veil;

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
        LocalizationService.Apply(config.Language);

        // Before _monitors.Initialize() creates the dock windows: they read this same store.
        _dockItems = new DockItemStore(_config);

        // LoadOrCreate already Save()'s on first run / reset (which syncs Startup).
        // Explicit Apply covers the common case where config exists and Save is skipped.
        new AutostartService().Apply(config.Autostart);
        WidgetHost.Apply(config);
        CameraMirrorHost.Apply(config.Island.CameraMirror);

        // Match the configured theme before the first window is realized, so the bar
        // never flashes the wrong palette. VeilService.ApplyConfig stays after this:
        // creating the full-screen layered veil window before a theme apply lets WPF-UI
        // attach a DWM Mica backdrop to it — an opaque full-screen cover for the whole
        // startup. Disabled veils must not realize the window at all (VeilService).
        AppThemePreference.Mode = config.Theme;
        ApplicationThemeManager.Apply(AppThemePreference.Resolve());
        if (config.UseSystemAccentColor)
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }

        _veil = new VeilService(_config);
        _veil.ApplyConfig(config.Veil);

        _taskbar = new TaskbarVisibilityService();
        _taskbar.CaptureAndHide();

        _monitors = new MonitorManager(_config, _taskbar, _veil);
        _monitors.Initialize(); // creates engine + top bars + docks

        MainWindow = _monitors.PrimaryTopBar ?? CreateHiddenUtilityWindow();

        // Dock's WindowAppearance.Apply calls ApplicationThemeManager.Apply, which
        // RestoreContentBackground on MainWindow — re-assert top bar translucent/mica.
        _monitors.ApplyTopBarChanges();

        // Own tray icon only (quit menu). Not reading system status tray icons.
        _tray = new TrayIconService();

        // Shell_NotifyIcon / AppBar SETPOS can re-show Shell_TrayWnd after the initial hide.
        _taskbar.ReapplyHide();
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () => _taskbar.ReapplyHide());

        // Startup theme applies can attach a DWM Mica backdrop to the veil window, which would
        // render the full-screen overlay as an opaque rectangle; re-assert its transparent state
        // once everything has settled.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            () => _veil?.ReassertBackdrop());
    }

    private static Window CreateHiddenUtilityWindow()
    {
        var window = new Window
        {
            Width = 0,
            Height = 0,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Visibility = Visibility.Hidden,
        };
        return window;
    }

    /// <summary>
    /// Rebuilds every top bar when <c>AllowsTransparency</c> must flip (Mica ↔ translucent).
    /// Removes old AppBars only — does <strong>not</strong> restore the Windows taskbar.
    /// </summary>
    internal void RecreateTopBar()
    {
        _monitors?.RecreateTopBars();
    }

    /// <summary>
    /// Rebuilds every dock when <c>AllowsTransparency</c> must flip (Mica ↔ tint/transparent).
    /// </summary>
    internal void RecreateDock()
    {
        _monitors?.RecreateDocks();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TaskbarVisibilityService? taskbar = _taskbar;
        taskbar?.Restore();
        _taskbar = null;

        _monitors?.Dispose();

        _tray?.Dispose();
        _tray = null;

        _veil?.Dispose();
        _veil = null;

        _settings?.Close();
        _settings = null;

        _singleInstance?.Dispose();
        _singleInstance = null;

        // Restore was kicked to a background thread in OnClosing (shell calls can block
        // for many seconds on a slow WM_SETTINGCHANGE broadcast). The bar/tray visuals
        // are gone by now, so waiting here only delays the invisible process exit — but
        // never exit before the taskbar/work-area restore is guaranteed complete.
        taskbar?.WaitForRestore(TaskbarVisibilityService.RestoreWaitTimeout);
        base.OnExit(e);
    }
}
