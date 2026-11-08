using System.Diagnostics;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WharfBar.Services;

internal sealed record BluetoothDeviceInfo(string Id, string Name, bool IsConnected, string? Address = null);

/// <summary>
/// Lists paired Bluetooth devices; connected ones are flagged.
/// Pairing.IsPaired can wrongly return false right after the radio turns on / while it is off
/// (the AEP cache is cold), so it is combined with the System.Devices.Aep.IsPaired property.
/// </summary>
internal sealed class BluetoothService : IDisposable
{
    private static readonly TimeSpan PageInterval = TimeSpan.FromSeconds(2);

    /// <summary>Island connect notice. The quick-settings page keeps <see cref="PageInterval"/>.</summary>
    internal static readonly TimeSpan NoticeInterval = TimeSpan.FromSeconds(10);
    private static readonly string[] AdditionalProperties =
    [
        "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.IsPaired",
        "System.Devices.Aep.DeviceAddress",
    ];

    private readonly DispatcherTimer _timer;
    private bool _page;
    private bool _watch;
    private int _refreshGate;
    private bool _disposed;

    internal BluetoothService()
    {
        _timer = new DispatcherTimer { Interval = NoticeInterval };
        _timer.Tick += (_, _) => Refresh();
    }

    internal IReadOnlyList<BluetoothDeviceInfo> Devices { get; private set; } = [];

    internal event EventHandler? Changed;

    internal void StartPolling()
    {
        if (_disposed)
        {
            return;
        }

        _page = true;
        Arm(PageInterval, immediate: true);
    }

    internal void StopPolling()
    {
        _page = false;
        if (_watch && !_disposed)
        {
            Arm(NoticeInterval, immediate: false);
            return;
        }

        _timer.Stop();
    }

    /// <summary>Slow connect watch for the island notice. Does not speed up an open settings page.</summary>
    internal void WatchConnections()
    {
        if (_disposed)
        {
            return;
        }

        _watch = true;
        if (_page)
        {
            return;
        }

        Arm(NoticeInterval, immediate: true);
    }

    internal void StopWatching()
    {
        _watch = false;
        if (_page || _disposed)
        {
            return;
        }

        _timer.Stop();
    }

    private void Arm(TimeSpan interval, bool immediate)
    {
        _timer.Interval = interval;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        if (immediate)
        {
            Refresh();
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

    private async void Refresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshGate, 1) != 0)
        {
            return;
        }

        try
        {
            var found = new List<DeviceInformation>();
            DeviceInformationCollection classic = await DeviceInformation.FindAllAsync(
                BluetoothDevice.GetDeviceSelector(), AdditionalProperties);
            found.AddRange(classic);
            try
            {
                DeviceInformationCollection le = await DeviceInformation.FindAllAsync(
                    BluetoothLEDevice.GetDeviceSelector(), AdditionalProperties);
                found.AddRange(le);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar: Bluetooth LE refresh failed — {ex.Message}");
            }

            if (_disposed)
            {
                return;
            }

            var raw = new List<BluetoothDeviceInfo>();
            foreach (DeviceInformation device in found)
            {
                device.Properties.TryGetValue(ConnectedProperty, out object? aepConnected);
                device.Properties.TryGetValue(PairedProperty, out object? aepPaired);
                bool paired = device.Pairing.IsPaired || aepPaired is true;
                if (!paired)
                {
                    continue;
                }

                // Aep.IsConnected is reliable per container; fall back to FromIdAsync when absent.
                bool connected = aepConnected is bool b
                    ? b
                    : await TryGetConnectedAsync(device.Id);
                raw.Add(new BluetoothDeviceInfo(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? "(Unnamed device)" : device.Name,
                    connected,
                    ReadAddress(device)));
            }

            if (_disposed)
            {
                return;
            }

            List<BluetoothDeviceInfo> result = MergeDevices(raw).ToList();
            if (SameDevices(Devices, result))
            {
                return;
            }

            Devices = result;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Bluetooth refresh failed — {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshGate, 0);
        }
    }

    internal static bool SameDevices(
        IReadOnlyList<BluetoothDeviceInfo> left,
        IReadOnlyList<BluetoothDeviceInfo> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Id, right[i].Id, StringComparison.Ordinal)
                || !string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal)
                || left[i].IsConnected != right[i].IsConnected)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Merges classic and LE records by address, or by id when there is no address. The connected one wins.</summary>
    internal static IReadOnlyList<BluetoothDeviceInfo> MergeDevices(IEnumerable<BluetoothDeviceInfo> devices)
    {
        var byKey = new Dictionary<string, BluetoothDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (BluetoothDeviceInfo device in devices)
        {
            string key = string.IsNullOrWhiteSpace(device.Address)
                ? "id:" + device.Id
                : "addr:" + device.Address;
            if (!byKey.TryGetValue(key, out BluetoothDeviceInfo? existing)
                || (device.IsConnected && !existing.IsConnected))
            {
                byKey[key] = device;
            }
        }

        return byKey.Values.OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
    }

    internal static string? ReadAddress(DeviceInformation device)
    {
        if (!device.Properties.TryGetValue(AddressProperty, out object? value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
            ulong address => address.ToString("X12"),
            long address => unchecked((ulong)address).ToString("X12"),
            _ => null,
        };
    }

    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string PairedProperty = "System.Devices.Aep.IsPaired";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";

    private static async Task<bool> TryGetConnectedAsync(string deviceId)
    {
        try
        {
            BluetoothDevice? device = await BluetoothDevice.FromIdAsync(deviceId);
            if (device is not null)
            {
                return device.ConnectionStatus == BluetoothConnectionStatus.Connected;
            }

            BluetoothLEDevice? le = await BluetoothLEDevice.FromIdAsync(deviceId);
            return le is not null && le.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: BT status failed — {ex.Message}");
            return false;
        }
    }
}
