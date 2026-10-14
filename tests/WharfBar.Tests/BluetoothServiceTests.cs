using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class BluetoothServiceTests
{
    [Fact]
    public void MergeDevices_DedupesByAddressAndPrefersConnected()
    {
        var classic = new BluetoothDeviceInfo("classic-id", "Buds", false, "AA11");
        var le = new BluetoothDeviceInfo("le-id", "Buds", true, "aa11");
        var other = new BluetoothDeviceInfo("watch", "Watch", false);

        IReadOnlyList<BluetoothDeviceInfo> merged = BluetoothService.MergeDevices([classic, le, other]);

        Assert.Equal(2, merged.Count);
        BluetoothDeviceInfo buds = Assert.Single(merged, d => d.Name == "Buds");
        Assert.True(buds.IsConnected);
        Assert.Contains(merged, d => d.Id == "watch");
    }

    [Fact]
    public void MergeDevices_DifferentIdsStaySeparateWithoutAddress()
    {
        var left = new BluetoothDeviceInfo("a", "Speaker", false);
        var right = new BluetoothDeviceInfo("b", "Speaker", true);

        IReadOnlyList<BluetoothDeviceInfo> merged = BluetoothService.MergeDevices([left, right]);

        Assert.Equal(2, merged.Count);
    }
}
