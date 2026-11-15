using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;
using WharfBar.Views;
using Wpf.Ui.Appearance;

namespace WharfBar;

public partial class App : Application
{
    /// <summary>
    /// Tooling hook: when set before the dispatcher runs the application's startup callback,
    /// <see cref="OnStartup"/> returns without hiding the taskbar, reserving the AppBar or creating
    /// the shell. The offline UI render harness (<c>.superpowers/scratch</c>) uses it to screenshot
    /// windows without starting the shell; the product never sets it.
    /// </summary>
    internal static bool SuppressShellStartup { get; set; }

    private SingleInstanceGuard? _singleInstance;
    private IDisposable? _quitChannel;
    private ConfigService? _config;
    private DockItemStore? _dockItems;
    private MonitorManager? _monitors;
    private TrayIconService? _tray;
    private TaskbarVisibilityService? _taskbar;
    private DesktopIconLayoutGuard? _iconLayout;
    private SettingsWindow? _settings;
    private VeilService? _veil;
    private WindowsKeyLauncher? _winKey;
    private TopBarStripGuard? _stripGuard;
    private OnboardingWindow? _onboarding;

    /// <summary>Exposes the configuration service for settings pages.</summary>
    internal ConfigService? Config => _config;

    /// <summary>Wallpaper theme sync owned by the primary island; null when detached.</summary>
    internal WallpaperThemeSync? WallpaperSync { get; private set; }

    internal void SetWallpaperSync(WallpaperThemeSync? sync) => WallpaperSync = sync;

    internal void FollowWallpaperTheme(bool fromExternalWindows)
        => WallpaperSync?.FollowResolvedTheme(fromExternalWindows);

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

        // A bar that just appeared owns a strip that may already contain a window.
        _stripGuard?.QueueSweep();
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

    /// <summary>
    /// An in-app light/dark switch happened. Re-asserts every shell surface: the library's theme
    /// apply repaints the main window opaque and leaves the docks on their previous DWM dark-mode
    /// state; "system" produces no system broadcast, so nothing else would refresh them.
    /// </summary>
    internal void ApplyThemeChanges()
    {
        _monitors?.ApplyTopBarChanges();
        _monitors?.ApplyDockTheme();
        _settings?.RefreshTheme();
        _onboarding?.RefreshTheme();
        GalleryWindow.RefreshThemeForAll();
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

    /// <summary>
    /// Opens the first-run setup wizard, or brings the open one forward. It runs modally while the
    /// shell is already up, so every choice previews live on the real top bar, dock and desktop.
    /// Closing or skipping marks onboarding complete; quitting the app mid-setup leaves the marker
    /// alone, so the next launch shows it again.
    /// </summary>
    internal void ShowOnboarding(bool replay = false)
    {
        if (_onboarding is not null)
        {
            _ = _onboarding.Activate();
            return;
        }

        var wizard = new OnboardingWindow(replay);
        _onboarding = wizard;
        wizard.Closed += (_, _) =>
        {
            _onboarding = null;

            // A quit request (tray menu, installer `--quit`, Windows shutdown) closes the modal
            // window too; that is not a finished setup.
            if (!Dispatcher.HasShutdownStarted)
            {
                CompleteOnboarding();
            }
        };

        wizard.ShowDialog();
    }

    private void CompleteOnboarding()
    {
        if (_config is null)
        {
            return;
        }

        AppConfig config = _config.Current;
        if (config.OnboardingCompleted)
        {
            return;
        }

        config.OnboardingCompleted = true;
        _config.Save(config);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (SuppressShellStartup)
        {
            return;
        }

        // First, so any exception from the startup path (config, theme, shell) leaves a managed
        // stack in a crash log instead of only an APPCRASH event. Best-effort by design.
        CrashLog.Install();

        _singleInstance = SingleInstanceGuard.TryAcquire();

        if (_singleInstance is null)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Installer/updater quit channel (`WharfBar.exe --quit`, QuitChannel): available before
        // the first window is realized, so a setup run can never have to force-kill a
        // half-started instance and leave the AppBar strip reserved.
        _quitChannel = QuitChannel.Listen(() =>
        {
            if (Dispatcher.HasShutdownStarted)
            {
                return;
            }

            _ = Dispatcher.BeginInvoke(new Action(Shutdown));
        });

        Resources["AppTextFontFamily"] = SystemVersion.TextFontFamily;
        Resources["AppIconFontFamily"] = SystemVersion.IconFontFamily;

        _config = new ConfigService();
        AppConfig config = _config.LoadOrCreate();
        LocalizationService.Apply(config.Language);

        // A token below medium integrity means a restricted launcher (sandboxed terminal or
        // low-integrity parent). UIPI then drops every shell call this process makes — the
        // taskbar SW_HIDE, ABM_NEW/ABM_SETPOS, SetWindowLong — so WharfBar would run with the
        // taskbar still visible and no top-edge reservation, silently. Warn instead, and let
        // the user decide; quitting here leaves no taskbar/AppBar state behind.
        if (ProcessIntegrity.TryGetCurrentRid(out int integrityRid)
            && ProcessIntegrity.IsBelowMedium(integrityRid)
            && !ConfirmRestrictedLaunch())
        {
            Shutdown();
            return;
        }

        // Before _monitors.Initialize() creates the dock windows: they read this same store.
        _dockItems = new DockItemStore(_config);

        // LoadOrCreate already Save()'s on first run / reset (which syncs Startup).
        // Explicit Apply covers the common case where config exists and Save is skipped.
        new AutostartService().Apply(config.Autostart);
        CameraMirrorHost.Apply(config.Island.CameraMirror);

        // Match the configured theme before the first window is realized, so the bar
        // never flashes the wrong palette. VeilService.ApplyConfig stays after this:
        // creating the full-screen layered veil window before a theme apply lets WPF-UI
        // attach a DWM Mica backdrop to it — an opaque full-screen cover for the whole
        // startup. Disabled veils must not realize the window at all (VeilService).
        AppThemePreference.Mode = config.Theme;
        ThemeApplyGuard.Apply(AppThemePreference.Resolve(), force: true);
        if (config.UseSystemAccentColor)
        {
            ApplicationAccentColorManager.ApplySystemAccent();
        }

        _veil = new VeilService(_config);
        _veil.ApplyConfig(config.Veil);

        // The desktop icon arrangement belongs to the user. Capture it before the first work-area
        // change (taskbar hide, AppBar strips), and let the guard keep it stable afterwards.
        _iconLayout = new DesktopIconLayoutGuard();
        _iconLayout.Start();

        _taskbar = new TaskbarVisibilityService();
        _taskbar.CaptureAndHide();

        _monitors = new MonitorManager(_config, _taskbar, _veil);
        _monitors.Initialize(); // creates engine + top bars + docks

        MainWindow = _monitors.PrimaryTopBar ?? CreateHiddenUtilityWindow();

        // A dock's WindowAppearance apply can still swap the theme dictionaries (the guard keeps the
        // library off MainWindow, so this no longer repaints the bar). Re-assert every bar anyway so
        // the startup backdrop/tint of each surface is its configured one.
        _monitors.ApplyTopBarChanges();

        // Windows cannot forbid a window from being dragged under an AppBar (the AppBar only
        // shapes the work area), so the reserved strip is enforced actively instead of painted
        // over: after every interactive move/resize, a window whose top landed under a bar is
        // pushed back below it (TopBarStripGuard). Nothing is painted, so the live wallpaper —
        // slideshows and animated walls included — is always exact.
        _stripGuard = TopBarStripGuard.Install(Dispatcher);
        _stripGuard.QueueSweep();

        // Bare Windows key -> Launchpad. The shell owns the Win-key hotkey, so a low-level hook
        // swallows the physical key and synthesizes it only for Win+<key> chords (docs/04-gotchas.md).
        _winKey = new WindowsKeyLauncher(_config, Dispatcher);
        if (!_winKey.Start())
        {
            Debug.WriteLine("WharfBar: Windows-key Launchpad shortcut unavailable");
        }

        // Own tray icon only (quit menu). Not reading system status tray icons.
        _tray = new TrayIconService();

        // The launchpad opens with the full list and icons already decoded: scan the
        // Start Menu in the background now, then fill the icon pool in idle slices.
        LaunchpadWarmup.Begin(Dispatcher);

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

        // First-run setup. The shell is already created, so the wizard edits the live config and
        // the real bar/dock/desktop show every change behind the window. Deferred to idle so the
        // AppBar placement and dock lay-out are settled before the window measures the work area.
        // `--setup` forces it, which is also how manual QA replays the flow.
        bool wantsSetup = e.Args.Any(
            static arg => string.Equals(arg, "--setup", StringComparison.OrdinalIgnoreCase));
        if (!config.OnboardingCompleted || wantsSetup)
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() =>
                {
                    if (!Dispatcher.HasShutdownStarted)
                    {
                        ShowOnboarding();
                    }
                }));
        }
    }

    /// <summary>
    /// The process token is below medium integrity, so Windows will not let it manage the
    /// shell (UIPI). Returns true when the user chooses to continue anyway.
    /// </summary>
    private static bool ConfirmRestrictedLaunch()
    {
        MessageBoxResult choice = MessageBox.Show(
            Strings.Startup_RestrictedLaunch_Message,
            Strings.Startup_RestrictedLaunch_Title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        return choice == MessageBoxResult.OK;
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
        _stripGuard?.QueueSweep();
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
        // Release the Windows key first: once the bar is gone the shell must behave as usual.
        _winKey?.Dispose();
        _winKey = null;

        // Stop the strip guard before the bars go away: no window move may be posted from a
        // half-torn-down shell.
        _stripGuard?.Dispose();
        _stripGuard = null;

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

        _quitChannel?.Dispose();
        _quitChannel = null;

        // Restore was kicked to a background thread in OnClosing (shell calls can block
        // for many seconds on a slow WM_SETTINGCHANGE broadcast). The bar/tray visuals
        // are gone by now, so waiting here only delays the invisible process exit — but
        // never exit before the taskbar/work-area restore is guaranteed complete.
        taskbar?.WaitForRestore(TaskbarVisibilityService.RestoreWaitTimeout);

        // Bars and taskbar are gone: if the shell did not put the launch icon layout back, repair
        // it before the process (and the guard thread) goes away.
        _iconLayout?.RestoreOnExit();
        _iconLayout?.Dispose();
        _iconLayout = null;

        // Released last: `WharfBar.exe --quit` waits on this mutex before returning, so an
        // installer only replaces the binary once the taskbar and every AppBar are restored.
        _singleInstance?.Dispose();
        _singleInstance = null;

        base.OnExit(e);
    }
}
