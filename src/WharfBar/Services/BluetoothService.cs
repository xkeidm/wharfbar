using System.Diagnostics;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WharfBar.Services;

internal sealed record BluetoothDeviceInfo(string Id, string Name, bool IsConnected);

/// <summary>Eşleşmiş Bluetooth cihazlarını listeler; bağlı olanlar işaretlenir.</summary>
internal sealed class BluetoothService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _timer;
    private bool _disposed;

    internal BluetoothService()
    {
        _timer = new DispatcherTimer { Interval = PollInterval };
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

    private async void Refresh()
    {
        try
        {
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector());
            var result = new List<BluetoothDeviceInfo>();
            foreach (DeviceInformation device in devices)
            {
                if (!device.Pairing.IsPaired)
                {
                    continue;
                }

                bool connected = await TryGetConnectedAsync(device.Id);
                result.Add(new BluetoothDeviceInfo(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? "(Adsız cihaz)" : device.Name,
                    connected));
            }

            Devices = result;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Bluetooth refresh failed — {ex.Message}");
        }
    }

    private static async Task<bool> TryGetConnectedAsync(string deviceId)
    {
        try
        {
            BluetoothDevice? device = await BluetoothDevice.FromIdAsync(deviceId);
            return device is not null && device.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: BT status failed — {ex.Message}");
            return false;
        }
    }
}
