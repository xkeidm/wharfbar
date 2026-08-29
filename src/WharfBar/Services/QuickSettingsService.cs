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
/// Radyo nesneleri bir kez keşfedilip cache'lenir ve StateChanged event'leriyle canlı tutulur —
/// her toggle'da RequestAccessAsync + GetRadiosAsync turu atmak toggle'ı saniyelerce geciktiriyordu.
/// </summary>
internal sealed class QuickSettingsService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;
    private bool _wifiOn = true;
    private bool _bluetoothOn;
    private bool _airplaneOn;
    private bool _radiosKnown;
    private int _radioInitBusy;
    private readonly NightLightService _nightLight;
    private readonly VeilService _veil;
    private readonly EventHandler _onVeilChanged;

    private readonly Dictionary<Windows.Devices.Radios.RadioKind, List<Windows.Devices.Radios.Radio>> _radios = [];

    /// <summary>
    /// Session truth for airplane mode after <em>we</em> toggled it. The OS flag
    /// (<c>SystemRadioState</c>) does not follow radio-API toggles, so the tile would
    /// otherwise snap back to the stale OS value. <c>null</c> = follow the OS flag.
    /// </summary>
    private bool? _airplaneSessionOverride;

    internal QuickSettingsService(NightLightService nightLight, VeilService veil)
    {
        _nightLight = nightLight;
        _veil = veil;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _onVeilChanged = (_, _) =>
        {
            if (_disposed)
            {
                return;
            }

            Tiles = BuildTiles();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        veil.Changed += _onVeilChanged;
        Network = ReadNetworkDetails();
        Tiles = BuildTiles();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Refresh();
        _ = InitializeRadiosAsync();
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
        _ = InitializeRadiosAsync();
    }

    internal void StopPolling() => _timer.Stop();

    internal async Task ToggleTileAsync(string tileId)
    {
        switch (tileId)
        {
            case "wifi":
                await ToggleRadioKindAsync(Windows.Devices.Radios.RadioKind.WiFi).ConfigureAwait(true);
                break;
            case "bluetooth":
                await ToggleRadioKindAsync(Windows.Devices.Radios.RadioKind.Bluetooth).ConfigureAwait(true);
                break;
            case "airplane":
                await SetAirplaneAsync(!_airplaneOn).ConfigureAwait(true);
                break;
            case "nightlight":
                _nightLight.SetEnabled(!_nightLight.IsEnabled);
                break;
            case "veil":
                _veil.SetEnabled(!_veil.IsEnabled);
                break;
            case "batterysaver":
                OpenSettingsUri("ms-settings:batterysaver");
                break;
        }

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
        _veil.Changed -= _onVeilChanged;
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
                "veil",
                "Kaplama",
                "\uE790",
                IsEnabled: true,
                IsActive: _veil.IsEnabled,
                HasChevron: false,
                Tooltip: "Renk kaplaması"),
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

    /// <summary>Radyoları bir kez keşfeder, cache'ler ve StateChanged'e abone olur.</summary>
    private async Task InitializeRadiosAsync()
    {
        if (Interlocked.Exchange(ref _radioInitBusy, 1) != 0)
        {
            return;
        }

        try
        {
            if (_radios.Count > 0 || !await EnsureRadioAccessAsync())
            {
                return;
            }

            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            foreach (Windows.Devices.Radios.Radio radio in radios)
            {
                radio.StateChanged += OnRadioStateChanged;
                if (radio.Kind is Windows.Devices.Radios.RadioKind.WiFi or Windows.Devices.Radios.RadioKind.Bluetooth)
                {
                    if (!_radios.TryGetValue(radio.Kind, out var list))
                    {
                        list = [];
                        _radios[radio.Kind] = list;
                    }

                    list.Add(radio);
                }
            }

            UpdateRadioStates();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Radio init failed — {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _radioInitBusy, 0);
        }
    }

    private void OnRadioStateChanged(Windows.Devices.Radios.Radio sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        // WinRT thread — UI state'ine dokunmadan dispatcher'a aktar.
        _dispatcher.BeginInvoke(UpdateRadioStates);
    }

    private void UpdateRadioStates()
    {
        bool wifi = false;
        bool bt = false;
        foreach (var (kind, list) in _radios)
        {
            bool anyOn = list.Any(r => r.State == Windows.Devices.Radios.RadioState.On);
            if (kind == Windows.Devices.Radios.RadioKind.WiFi)
            {
                wifi = anyOn;
            }
            else if (kind == Windows.Devices.Radios.RadioKind.Bluetooth)
            {
                bt = anyOn;
            }
        }

        if (_radios.Count > 0)
        {
            _wifiOn = wifi;
            _bluetoothOn = bt;
            _radiosKnown = true;
        }

        UpdateAirplaneFlag();
        Tiles = BuildTiles();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAirplaneFlag()
    {
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
            bool sawWifi = _radios.ContainsKey(Windows.Devices.Radios.RadioKind.WiFi);
            bool sawBt = _radios.ContainsKey(Windows.Devices.Radios.RadioKind.Bluetooth);
            _airplaneOn = _radiosKnown
                ? !_wifiOn && !_bluetoothOn && (sawWifi || sawBt)
                : false;
        }
    }

    /// <summary>
    /// Optimistik toggle: karoyu hemen çevir (Changed fırlat), sonra radyoyu arkada çevir.
    /// Radyo değişimi ~100 ms sürüyor; eski akış tüm WinRT turunu bekleyip ondan sonra
    /// UI'ı güncellediği için toggle saniyeler alıyormuş gibi hissettiriyordu.
    /// </summary>
    private async Task ToggleRadioKindAsync(Windows.Devices.Radios.RadioKind kind)
    {
        bool turnOn = kind == Windows.Devices.Radios.RadioKind.WiFi ? !_wifiOn : !_bluetoothOn;
        if (kind == Windows.Devices.Radios.RadioKind.WiFi)
        {
            _wifiOn = turnOn;
        }
        else
        {
            _bluetoothOn = turnOn;
        }

        Tiles = BuildTiles();
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            if (!await EnsureRadioAccessAsync())
            {
                OpenSettingsUri(kind == Windows.Devices.Radios.RadioKind.WiFi
                    ? "ms-settings:network-wifi"
                    : "ms-settings:bluetooth");
                UpdateRadioStates();
                return;
            }

            List<Windows.Devices.Radios.Radio>? targets = _radios.GetValueOrDefault(kind);
            if (targets is null or { Count: 0 })
            {
                // Cache henüz hazır değil — tek seferlik keşif.
                var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
                targets = radios.Where(r => r.Kind == kind).ToList();
            }

            var state = turnOn
                ? Windows.Devices.Radios.RadioState.On
                : Windows.Devices.Radios.RadioState.Off;
            foreach (Windows.Devices.Radios.Radio radio in targets)
            {
                Windows.Devices.Radios.RadioAccessStatus result = await radio.SetStateAsync(state);
                if (result != Windows.Devices.Radios.RadioAccessStatus.Allowed)
                {
                    Debug.WriteLine($"WharfBar: {kind} SetStateAsync -> {result}");
                }
            }

            UpdateRadioStates();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Radio toggle failed — {ex.Message}");
            OpenSettingsUri("ms-settings:network");
            UpdateRadioStates();
        }
    }

    private async Task<bool> EnsureRadioAccessAsync()
    {
        try
        {
            Windows.Devices.Radios.RadioAccessStatus access =
                await Windows.Devices.Radios.Radio.RequestAccessAsync();
            return access == Windows.Devices.Radios.RadioAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Radio access failed — {ex.Message}");
            return false;
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

    private async Task SetAirplaneAsync(bool enable)
    {
        try
        {
            if (!await EnsureRadioAccessAsync())
            {
                OpenSettingsUri("ms-settings:network-airplanemode");
                return;
            }

            List<Windows.Devices.Radios.Radio>? targets =
            [
                .. _radios.GetValueOrDefault(Windows.Devices.Radios.RadioKind.WiFi) ?? [],
                .. _radios.GetValueOrDefault(Windows.Devices.Radios.RadioKind.Bluetooth) ?? [],
                .. _radios.GetValueOrDefault(Windows.Devices.Radios.RadioKind.MobileBroadband) ?? [],
            ];
            if (targets.Count == 0)
            {
                var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
                targets = radios.Where(r => r.Kind
                    is Windows.Devices.Radios.RadioKind.WiFi
                        or Windows.Devices.Radios.RadioKind.Bluetooth
                        or Windows.Devices.Radios.RadioKind.MobileBroadband).ToList();
            }

            var state = enable
                ? Windows.Devices.Radios.RadioState.Off
                : Windows.Devices.Radios.RadioState.On;
            foreach (Windows.Devices.Radios.Radio radio in targets)
            {
                await radio.SetStateAsync(state);
            }

            // The OS flag does not follow radio-API toggles (and writing it needs admin) —
            // keep the tile honest with a session override.
            _airplaneOn = enable;
            _airplaneSessionOverride = enable;
            Tiles = BuildTiles();
            Changed?.Invoke(this, EventArgs.Empty);
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
