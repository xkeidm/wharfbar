using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using Microsoft.Win32;
using WharfBar.Native;

namespace WharfBar.Services;

internal enum QuickSettingsPage
{
    Main,
    Mixer,
    Network,
    Wifi,
    Bluetooth,
    Project,
}

internal sealed record QuickTileState(
    string Id,
    string Label,
    string Glyph,
    bool IsEnabled,
    bool IsActive,
    bool HasChevron,
    string Tooltip);

internal sealed record NetworkDetails(
    bool IsOnline,
    NetworkKind Kind,
    string Title,
    string Subtitle,
    string AdapterName,
    string? Ipv4);

/// <summary>
/// Quick Settings actions: network details, documented WinRT radio toggles where available,
/// Settings URIs for the rest; Project opens the native Yansıt panel (no tray-icon mirroring).
/// </summary>
internal sealed class QuickSettingsService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private bool _wifiOn = true;
    private bool _bluetoothOn;
    private bool _airplaneOn;
    private bool _radiosKnown;
    private int _radioRefreshBusy;
    private readonly NightLightService _nightLight;

    /// <summary>
    /// Session truth for airplane mode after <em>we</em> toggled it. The OS flag
    /// (<c>SystemRadioState</c>) does not follow radio-API toggles, so the tile would
    /// otherwise snap back to the stale OS value. <c>null</c> = follow the OS flag.
    /// </summary>
    private bool? _airplaneSessionOverride;

    internal QuickSettingsService(NightLightService nightLight)
    {
        _nightLight = nightLight;
        Network = ReadNetworkDetails();
        Tiles = BuildTiles();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => OnPollTick();
        _ = RefreshRadiosAsync();
    }

    internal NetworkDetails Network { get; private set; }

    internal bool IsWifiRadioOn => _wifiOn;

    internal bool IsBluetoothRadioOn => _bluetoothOn;

    internal IReadOnlyList<QuickTileState> Tiles { get; private set; }

    internal event EventHandler? Changed;

    internal void StartPolling()
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        _timer.Start();
        _ = RefreshRadiosAsync();
    }

    internal void StopPolling() => _timer.Stop();

    private void OnPollTick()
    {
        Refresh();
        // Keep tiles live against external changes (Windows quick settings, radio switch).
        _ = RefreshRadiosAsync();
    }

    internal async Task ToggleTileAsync(string tileId)
    {
        switch (tileId)
        {
            case "wifi":
                await SetRadioKindAsync(Windows.Devices.Radios.RadioKind.WiFi, !_wifiOn).ConfigureAwait(true);
                break;
            case "bluetooth":
                await SetRadioKindAsync(Windows.Devices.Radios.RadioKind.Bluetooth, !_bluetoothOn)
                    .ConfigureAwait(true);
                break;
            case "airplane":
                await SetAirplaneAsync(!_airplaneOn).ConfigureAwait(true);
                break;
            case "nightlight":
                _nightLight.SetEnabled(!_nightLight.IsEnabled);
                break;
            case "batterysaver":
                OpenSettingsUri("ms-settings:batterysaver");
                break;
        }

        await RefreshRadiosAsync().ConfigureAwait(true);
        Refresh();
    }

    internal void OpenNetworkSettings()
    {
        string uri = Network.Kind switch
        {
            NetworkKind.WiFi => "ms-settings:network-wifi",
            NetworkKind.Ethernet => "ms-settings:network-ethernet",
            _ => "ms-settings:network",
        };
        OpenSettingsUri(uri);
    }

    internal void OpenSoundSettings() => OpenSettingsUri("ms-settings:sound");

    internal void OpenSettingsHome() => OpenSettingsUri("ms-settings:");

    internal static void OpenSettingsUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Open settings failed — {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    private void Refresh()
    {
        _nightLight.PollRegistry();
        NetworkDetails nextNet = ReadNetworkDetails();
        IReadOnlyList<QuickTileState> nextTiles = BuildTiles();
        Network = nextNet;
        Tiles = nextTiles;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<QuickTileState> BuildTiles()
    {
        return
        [
            new QuickTileState(
                "wifi",
                "Wi-Fi",
                "\uE701",
                IsEnabled: !_airplaneOn,
                IsActive: _wifiOn && !_airplaneOn,
                HasChevron: true,
                Tooltip: _airplaneOn ? "Uçak modunu kapatın" : "Wi-Fi"),
            new QuickTileState(
                "bluetooth",
                "Bluetooth",
                "\uE702",
                IsEnabled: !_airplaneOn,
                IsActive: _bluetoothOn && !_airplaneOn,
                HasChevron: true,
                Tooltip: _airplaneOn ? "Uçak modunu kapatın" : "Bluetooth"),
            new QuickTileState(
                "airplane",
                "Uçak modu",
                "\uE709",
                IsEnabled: true,
                IsActive: _airplaneOn,
                HasChevron: false,
                Tooltip: "Uçak modu"),
            new QuickTileState(
                "project",
                "Yansıt",
                "\uE7F4",
                IsEnabled: true,
                IsActive: false,
                HasChevron: true,
                Tooltip: "Yansıt"),
            new QuickTileState(
                "nightlight",
                "Gece ışığı",
                "\uE708",
                IsEnabled: true,
                IsActive: _nightLight.IsEnabled,
                HasChevron: false,
                Tooltip: "Gece ışığı"),
            new QuickTileState(
                "batterysaver",
                "Enerji tasarrufu",
                "\uEBC0",
                IsEnabled: true,
                IsActive: false,
                HasChevron: false,
                Tooltip: "Enerji tasarrufu ayarları"),
        ];
    }

    private async Task RefreshRadiosAsync()
    {
        if (Interlocked.Exchange(ref _radioRefreshBusy, 1) != 0)
        {
            return;
        }

        try
        {
            Windows.Devices.Radios.RadioAccessStatus access =
                await Windows.Devices.Radios.Radio.RequestAccessAsync();
            if (access != Windows.Devices.Radios.RadioAccessStatus.Allowed)
            {
                return;
            }

            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            bool wifi = false;
            bool bt = false;
            bool anyOn = false;
            bool sawWifi = false;
            bool sawBt = false;

            foreach (Windows.Devices.Radios.Radio radio in radios)
            {
                bool on = radio.State == Windows.Devices.Radios.RadioState.On;
                anyOn |= on;
                if (radio.Kind == Windows.Devices.Radios.RadioKind.WiFi)
                {
                    sawWifi = true;
                    wifi |= on;
                }
                else if (radio.Kind == Windows.Devices.Radios.RadioKind.Bluetooth)
                {
                    sawBt = true;
                    bt |= on;
                }
            }

            _wifiOn = !sawWifi || wifi;
            _bluetoothOn = sawBt && bt;

            // Airplane mode is an OS flag, not "all radios off": turning Wi-Fi off must not
            // light the airplane tile. Prefer the flag; fall back to the aggregate
            // approximation only when the flag is not readable (no radio manager).
            if (_airplaneSessionOverride is bool session)
            {
                _airplaneOn = session;
            }
            else if (TryReadSystemRadioState(out bool systemAirplane))
            {
                _airplaneOn = systemAirplane;
            }
            else
            {
                _airplaneOn = _radiosKnown
                    ? (!wifi && !bt && (sawWifi || sawBt))
                    : (!anyOn && (sawWifi || sawBt));
            }

            _radiosKnown = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Radio refresh failed — {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _radioRefreshBusy, 0);
        }
    }

    /// <summary>
    /// Real airplane-mode flag as maintained by the Windows radio manager
    /// (<c>radiosvc</c>). Only changes when airplane mode itself is toggled —
    /// individual radio toggles leave it untouched.
    /// </summary>
    private static bool TryReadSystemRadioState(out bool airplaneOn)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState");
            if (key?.GetValue(null) is int value)
            {
                airplaneOn = value != 0;
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: SystemRadioState read failed — {ex.Message}");
        }

        airplaneOn = false;
        return false;
    }

    private async Task SetRadioKindAsync(Windows.Devices.Radios.RadioKind kind, bool turnOn)
    {
        try
        {
            Windows.Devices.Radios.RadioAccessStatus access =
                await Windows.Devices.Radios.Radio.RequestAccessAsync();
            if (access != Windows.Devices.Radios.RadioAccessStatus.Allowed)
            {
                OpenSettingsUri(kind == Windows.Devices.Radios.RadioKind.WiFi
                    ? "ms-settings:network-wifi"
                    : "ms-settings:bluetooth");
                return;
            }

            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            foreach (Windows.Devices.Radios.Radio radio in radios)
            {
                if (radio.Kind != kind)
                {
                    continue;
                }

                if (turnOn)
                {
                    await radio.SetStateAsync(Windows.Devices.Radios.RadioState.On);
                }
                else
                {
                    await radio.SetStateAsync(Windows.Devices.Radios.RadioState.Off);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Radio toggle failed — {ex.Message}");
            OpenSettingsUri("ms-settings:network");
        }
    }

    private async Task SetAirplaneAsync(bool enable)
    {
        try
        {
            Windows.Devices.Radios.RadioAccessStatus access =
                await Windows.Devices.Radios.Radio.RequestAccessAsync();
            if (access != Windows.Devices.Radios.RadioAccessStatus.Allowed)
            {
                OpenSettingsUri("ms-settings:network-airplanemode");
                return;
            }

            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            foreach (Windows.Devices.Radios.Radio radio in radios)
            {
                if (radio.Kind is Windows.Devices.Radios.RadioKind.WiFi
                    or Windows.Devices.Radios.RadioKind.Bluetooth
                    or Windows.Devices.Radios.RadioKind.MobileBroadband)
                {
                    await radio.SetStateAsync(
                        enable
                            ? Windows.Devices.Radios.RadioState.Off
                            : Windows.Devices.Radios.RadioState.On);
                }
            }

            // The OS flag does not follow radio-API toggles (and writing it needs admin) —
            // keep the tile honest with a session override.
            _airplaneOn = enable;
            _airplaneSessionOverride = enable;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Airplane toggle failed — {ex.Message}");
            OpenSettingsUri("ms-settings:network-airplanemode");
        }
    }

    private static NetworkDetails ReadNetworkDetails()
    {
        bool available = NetworkInterface.GetIsNetworkAvailable();
        if (!available)
        {
            return new NetworkDetails(
                false,
                NetworkKind.Offline,
                "Bağlantı yok",
                "Ağ kullanılamıyor",
                string.Empty,
                null);
        }

        NetworkKind kind = NetworkKind.Other;
        string adapter = string.Empty;
        string? ipv4 = null;

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                        or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                NetworkKind candidate = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => NetworkKind.WiFi,
                    NetworkInterfaceType.Ethernet
                        or NetworkInterfaceType.Ethernet3Megabit
                        or NetworkInterfaceType.FastEthernetT
                        or NetworkInterfaceType.FastEthernetFx
                        or NetworkInterfaceType.GigabitEthernet => NetworkKind.Ethernet,
                    _ => NetworkKind.Other,
                };

                bool better = kind == NetworkKind.Other && candidate != NetworkKind.Other
                    || candidate == NetworkKind.WiFi && kind != NetworkKind.WiFi
                    || candidate == NetworkKind.Ethernet && kind == NetworkKind.Other;

                if (!better && adapter.Length > 0)
                {
                    continue;
                }

                kind = candidate;
                adapter = nic.Name;
                ipv4 = null;
                foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        ipv4 = addr.Address.ToString();
                        break;
                    }
                }

                if (candidate == NetworkKind.WiFi)
                {
                    break;
                }
            }
        }
        catch (NetworkInformationException)
        {
            return new NetworkDetails(true, NetworkKind.Other, "Bağlı", "Ağ bağlı", string.Empty, null);
        }

        string title = kind switch
        {
            NetworkKind.WiFi => "Wi-Fi",
            NetworkKind.Ethernet => "Ethernet",
            _ => "Ağ",
        };

        string subtitle = string.IsNullOrEmpty(adapter)
            ? "Bağlı"
            : string.IsNullOrEmpty(ipv4)
                ? adapter
                : $"{adapter} · {ipv4}";

        return new NetworkDetails(true, kind, title, subtitle, adapter, ipv4);
    }
}
