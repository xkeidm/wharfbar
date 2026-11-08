using System.Windows.Threading;

namespace WharfBar.Services;

/// <summary>
/// Owns the service instances shared by every per-monitor top bar window. Services are
/// observable (Changed events); each window subscribes to the ones that drive its own UI.
/// Created once on the UI thread and disposed once on app exit.
/// </summary>
internal sealed class TopBarEngine : IDisposable
{
    internal StatusService Status { get; }
    internal VolumeService Volume { get; }
    internal AudioMixerService Mixer { get; }
    internal NightLightService NightLight { get; }
    internal WifiService Wifi { get; }
    internal BluetoothService Bluetooth { get; }
    internal ProjectionService Projection { get; }
    internal NetworkDetailsService NetworkDetails { get; }
    internal QuickSettingsService QuickSettings { get; }
    internal MonitorBrightnessService MonitorBrightness { get; }
    internal MediaSessionService Media { get; }
    internal TrayMirrorService TrayMirror { get; }
    internal TrayOverflowInvoker TrayOverflow { get; }
    internal NotificationCenterInvoker Notifications { get; }
    internal SystemStatsService SystemStats { get; }

    /// <summary>
    /// Clipboard history + dropped files service. Always constructed; the <c>Enabled</c> setting
    /// is managed inside the service, so turning it on later from settings works too. The property
    /// is kept nullable because it is cleared in <see cref="Dispose"/>.
    /// </summary>
    internal IslandService? Island { get; private set; }

    internal TopBarEngine(ConfigService config, TaskbarVisibilityService taskbar, VeilService veil)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(taskbar);
        ArgumentNullException.ThrowIfNull(veil);

        Dispatcher dispatcher = System.Windows.Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;

        TrayOverflow = new TrayOverflowInvoker(taskbar);
        Notifications = new NotificationCenterInvoker();
        Status = new StatusService();
        Volume = new VolumeService();
        Mixer = new AudioMixerService(Volume);
        NightLight = new NightLightService();
        Wifi = new WifiService();
        Bluetooth = new BluetoothService();
        Projection = new ProjectionService();
        NetworkDetails = new NetworkDetailsService();
        QuickSettings = new QuickSettingsService(NightLight, veil);
        MonitorBrightness = new MonitorBrightnessService();
        Media = new MediaSessionService();
        TrayMirror = new TrayMirrorService(dispatcher, taskbar);
        SystemStats = new SystemStatsService();

        // Island is always constructed: if it were only built here, enabling it later from settings
        // would never create the service at all ("I turned it on and nothing happened"). While disabled
        // the constructor neither installs the clipboard listener nor touches island.json (the
        // !Config.Enabled guard in IslandService.LoadPersisted); on/off is handled by IslandService.ApplyConfig.
        Island = new IslandService(
            dispatcher,
            new IslandStore(config.ConfigDirectory),
            config.Current.Island);
    }

    internal void Initialize()
    {
        _ = Media.InitializeAsync();
        TrayMirror.Reenumerate();
    }

    public void Dispose()
    {
        Island?.Dispose();
        Island = null;
        TrayMirror.Dispose();
        SystemStats.Dispose();
        Media.Dispose();
        MonitorBrightness.Dispose();
        QuickSettings.Dispose();
        Bluetooth.Dispose();
        Wifi.Dispose();
        NetworkDetails.Dispose();
        Mixer.Dispose();
        Volume.Dispose();
        Status.Dispose();
    }
}