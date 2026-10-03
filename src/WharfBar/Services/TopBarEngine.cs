using System.IO;
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

    /// <summary>Clipboard geçmişi + bırakılan dosyalar servisi (null = ada kapalı).</summary>
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

        if (config.Current.Island.Enabled)
        {
            Island = new IslandService(
                dispatcher,
                new IslandStore(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WharfBar")),
                config.Current.Island);
        }
    }

    internal void Initialize()
    {
        _ = Media.InitializeAsync();
        TrayMirror.Reenumerate();
        SystemStats.Start();
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