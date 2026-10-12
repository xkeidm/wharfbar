namespace WharfBar.Services;

/// <summary>
/// Names that became connected between two Bluetooth polls.
/// The first poll only seeds; it does not announce devices that were already connected.
/// </summary>
internal static class BluetoothConnect
{
    internal static IReadOnlyList<string> NewlyConnected(
        IReadOnlyList<BluetoothDeviceInfo>? previous,
        IReadOnlyList<BluetoothDeviceInfo> current,
        bool seeded)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!seeded || previous is null)
        {
            return [];
        }

        var was = new HashSet<string>(StringComparer.Ordinal);
        foreach (BluetoothDeviceInfo device in previous)
        {
            if (device.IsConnected && device.Id.Length > 0)
            {
                was.Add(device.Id);
            }
        }

        var names = new List<string>();
        foreach (BluetoothDeviceInfo device in current)
        {
            if (!device.IsConnected || device.Id.Length == 0 || was.Contains(device.Id))
            {
                continue;
            }

            names.Add(string.IsNullOrWhiteSpace(device.Name) ? "Bluetooth" : device.Name.Trim());
        }

        return names;
    }
}
