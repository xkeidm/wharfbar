using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;

namespace WharfBar.Services;

/// <summary>Ağ paneli ("Özellikler" bölümü) için zengin bağlantı durumu; hızlar Mbps.</summary>
internal sealed record NetworkDetailsSnapshot(
    bool IsOnline,
    NetworkKind Kind,
    string? AdapterName,
    string? Ipv4,
    string? Ipv6,
    string? Gateway,
    string? Dns,
    string? Mac,
    string? LinkSpeed,
    double? DownloadMbps,
    double? UploadMbps);

/// <summary>
/// Aktif adaptörden (Wi-Fi > Ethernet önceliği) IP/gateway/DNS/MAC/link hızı okur ve
/// IPv4 byte sayaçlarının delta'sından canlı ↓/↑ hızını hesaplar. Sadece Ağ sayfası
/// açıkken poll edilir (flyout pattern'i: WifiService/BluetoothService ile aynı).
/// </summary>
internal sealed class NetworkDetailsService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private string? _prevAdapter;
    private long _prevBytesReceived;
    private long _prevBytesSent;
    private DateTime _prevSampleAt;
    private bool _havePrevSample;

    internal NetworkDetailsService()
    {
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Refresh();
    }

    internal NetworkDetailsSnapshot Snapshot { get; private set; } =
        new(false, NetworkKind.Offline, null, null, null, null, null, null, null, null, null);

    internal event EventHandler? Changed;

    internal void StartPolling()
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        _timer.Start();
    }

    internal void StopPolling() => _timer.Stop();

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
        Snapshot = ReadSnapshot(out NetworkDetailsSnapshot next);
        Snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal NetworkDetailsSnapshot ReadSnapshot(out NetworkDetailsSnapshot snapshot)
    {
        snapshot = BuildSnapshot();
        return snapshot;
    }

    private NetworkDetailsSnapshot BuildSnapshot()
    {
        NetworkInterface? adapter = PickAdapter();
        if (adapter is null)
        {
            _havePrevSample = false;
            return new NetworkDetailsSnapshot(
                NetworkInterface.GetIsNetworkAvailable(),
                NetworkKind.Offline,
                null, null, null, null, null, null, null, null, null);
        }

        IPInterfaceProperties ipProps = adapter.GetIPProperties();
        IPv4InterfaceStatistics stats = adapter.GetIPv4Statistics();

        (double? down, double? up) = ComputeThroughput(adapter.Name, stats.BytesReceived, stats.BytesSent);

        return new NetworkDetailsSnapshot(
            IsOnline: true,
            Kind: adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                ? NetworkKind.WiFi
                : adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT
                    or NetworkInterfaceType.FastEthernetFx
                    or NetworkInterfaceType.Ethernet3Megabit
                    ? NetworkKind.Ethernet
                    : NetworkKind.Other,
            AdapterName: adapter.Name,
            Ipv4: FormatOrNull(PickAddress(
                ipProps.UnicastAddresses.Select(a => a.Address).ToList(),
                AddressFamily.InterNetwork)),
            Ipv6: FormatOrNull(PickAddress(
                ipProps.UnicastAddresses.Select(a => a.Address).ToList(),
                AddressFamily.InterNetworkV6)),
            Gateway: FormatOrNull(ipProps.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork)?.Address),
            Dns: FormatDns([.. ipProps.DnsAddresses]),
            Mac: FormatMac(adapter.GetPhysicalAddress()),
            LinkSpeed: FormatLinkSpeed(adapter.Speed),
            DownloadMbps: down,
            UploadMbps: up);
    }

    /// <summary>Aktif adaptörü seçer: Wi-Fi öncelikli, sonra Ethernet, sonra ilk "Up" arayüz.</summary>
    private static NetworkInterface? PickAdapter()
    {
        try
        {
            NetworkInterface? best = null;
            int bestRank = int.MaxValue;
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                int rank = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => 0,
                    NetworkInterfaceType.Ethernet
                        or NetworkInterfaceType.GigabitEthernet
                        or NetworkInterfaceType.FastEthernetT
                        or NetworkInterfaceType.FastEthernetFx
                        or NetworkInterfaceType.Ethernet3Megabit => 1,
                    _ => 2,
                };
                if (rank < bestRank)
                {
                    best = nic;
                    bestRank = rank;
                }
            }

            return best;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private (double? Down, double? Up) ComputeThroughput(string adapterName, long bytesReceived, long bytesSent)
    {
        DateTime now = DateTime.UtcNow;
        (double? Down, double? Up) result = (null, null);
        if (_havePrevSample
            && string.Equals(_prevAdapter, adapterName, StringComparison.Ordinal)
            && _prevSampleAt < now)
        {
            double elapsed = (now - _prevSampleAt).TotalSeconds;
            result = (
                ComputeThroughputMbps(_prevBytesReceived, bytesReceived, elapsed),
                ComputeThroughputMbps(_prevBytesSent, bytesSent, elapsed));
        }

        _prevAdapter = adapterName;
        _prevBytesReceived = bytesReceived;
        _prevBytesSent = bytesSent;
        _prevSampleAt = now;
        _havePrevSample = true;
        return result;
    }

    /// <summary>Byte delta'sını Mbps'ye çevirir; geçersiz ölçüm (sayaç sıfırlaması, 0 süre) → null.</summary>
    internal static double? ComputeThroughputMbps(long prevBytes, long nowBytes, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0 || nowBytes < prevBytes)
        {
            return null;
        }

        return (nowBytes - prevBytes) * 8d / elapsedSeconds / 1_000_000d;
    }

    /// <summary>Link hızını okunur metne çevirir; bilinmiyorsa null.</summary>
    internal static string? FormatLinkSpeed(long bitsPerSecond, CultureInfo? culture = null) =>
        bitsPerSecond <= 0
            ? null
            : bitsPerSecond >= 1_000_000_000
                ? $"{(bitsPerSecond / 1_000_000_000d).ToString("0.#", culture ?? CultureInfo.CurrentCulture)} Gbps"
                : $"{(bitsPerSecond / 1_000_000d).ToString("0.#", culture ?? CultureInfo.CurrentCulture)} Mbps";

    internal static string? FormatDns(IReadOnlyList<IPAddress> addresses) =>
        addresses.Count == 0
            ? null
            : string.Join(", ", addresses.Select(a => a.ToString()));

    internal static string? FormatMac(PhysicalAddress address) =>
        address is null || address.GetAddressBytes() is { Length: 0 }
            ? null
            : string.Join('-', address.GetAddressBytes().Select(b => b.ToString("X2")));

    /// <summary>Verilen aileden ilk loopback olmayan adresi seçer.</summary>
    internal static IPAddress? PickAddress(IEnumerable<IPAddress> addresses, AddressFamily family) =>
        addresses.FirstOrDefault(a => a.AddressFamily == family && !IPAddress.IsLoopback(a));

    internal static string FormatMbps(double mbps, CultureInfo? culture = null) =>
        $"{mbps.ToString("0.#", culture ?? CultureInfo.CurrentCulture)} Mbps";

    private static string? FormatOrNull(IPAddress? address) => address?.ToString();
}
