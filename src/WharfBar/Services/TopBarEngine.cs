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
    /// Clipboard geçmişi + bırakılan dosyalar servisi. Her zaman kurulur; <c>Enabled</c> ayarı
    /// servis içinde yönetilir, böylece ayardan sonradan açmak da çalışır. Nitelik null olabilir
    /// tutuldu çünkü <see cref="Dispose"/> sırasında temizlenir.
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

        // Island her zaman kurulur: yalnizca burada kurulsaydi, kullanici ayarlardan sonradan
        // actiginda servis hic dogmazdi ("acitim ama hicbir sey olmadi"). Kapaliyken kurucu ne
        // pano dinleyicisi kurar ne de island.json'a dokunur (IslandService.LoadPersisted'in
        // !Config.Enabled guard'i); acma/kapama IslandService.ApplyConfig icindir.
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