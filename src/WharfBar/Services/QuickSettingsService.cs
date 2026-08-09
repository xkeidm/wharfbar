using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

internal enum QuickSettingsPage
{
    Main,
    Mixer,
    Network,
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
/// and Settings URIs / Win+P for the rest (no tray-icon mirroring).
/// </summary>
internal sealed class QuickSettingsService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private bool _wifiOn = true;
    private bool _bluetoothOn;
    private bool _airplaneOn;
    private bool _radiosKnown;

    internal QuickSettingsService()
    {
        Network = ReadNetworkDetails();
        Tiles = BuildTiles();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
        _ = RefreshRadiosAsync();
    }

    internal NetworkDetails Network { get; private set; }

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
            case "project":
                SendInputKeys.TrySendWinP();
                break;
            case "nightlight":
                OpenSettingsUri("ms-settings:nightlight");
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
                IsEnabled: true,
                IsActive: _wifiOn && !_airplaneOn,
                HasChevron: true,
                Tooltip: "Wi-Fi"),
            new QuickTileState(
                "bluetooth",
                "Bluetooth",
                "\uE702",
                IsEnabled: true,
                IsActive: _bluetoothOn && !_airplaneOn,
                HasChevron: true,
                Tooltip: "Bluetooth"),
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
                Tooltip: "Yansıt (Win+P)"),
            new QuickTileState(
                "nightlight",
                "Gece ışığı",
                "\uE708",
                IsEnabled: true,
                IsActive: false,
                HasChevron: false,
                Tooltip: "Gece ışığı ayarları"),
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
            // Airplane approximation: both Wi-Fi and BT off when those radios exist.
            _airplaneOn = _radiosKnown
                ? (!wifi && !bt && (sawWifi || sawBt))
                : (!anyOn && (sawWifi || sawBt));
            _radiosKnown = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Radio refresh failed — {ex.Message}");
        }
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

            _airplaneOn = enable;
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
