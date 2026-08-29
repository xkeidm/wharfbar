using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WifiServiceTests
{
    [Theory]
    [InlineData(100, 4)]
    [InlineData(75, 4)]
    [InlineData(74, 3)]
    [InlineData(50, 3)]
    [InlineData(49, 2)]
    [InlineData(25, 2)]
    [InlineData(24, 1)]
    [InlineData(0, 1)]
    public void SignalBars_MapsQualityToBars(int signal, int expected)
    {
        Assert.Equal(expected, WifiService.SignalBars(signal));
    }

    [Fact]
    public void SsidToString_TrimsTrailingNulls()
    {
        var ssid = new byte[32];
        Array.Copy("MyNet"u8.ToArray(), ssid, 5);
        Assert.Equal("MyNet", WifiService.SsidToString(ssid));
    }

    [Fact]
    public void SsidToString_EmptyOrAllNulls_ReturnsHiddenNetwork()
    {
        // Boş SSID = gizli ağ (SSID yayınını kapatmış AP); "(Bilinmeyen ağ)" yanıltıcıydı.
        Assert.Equal("(Gizli ağ)", WifiService.SsidToString(Array.Empty<byte>()));
        Assert.Equal("(Gizli ağ)", WifiService.SsidToString(new byte[32]));
    }

    [Fact]
    public void SsidToString_Latin1Fallback()
    {
        Assert.Equal("Aé", WifiService.SsidToString(new byte[] { 0x41, 0xE9 }));
    }

    [Fact]
    public void SsidToString_Utf8Kept()
    {
        Assert.Equal("Merhaba", WifiService.SsidToString(new byte[] { 0x4D, 0x65, 0x72, 0x68, 0x61, 0x62, 0x61 }));
    }

    [Fact]
    public void SortNetworks_ConnectedFirst_ThenBySignal()
    {
        var a = new WifiNetworkInfo("weak", 10, false, false, false, null);
        var b = new WifiNetworkInfo("strong", 90, false, false, false, null);
        var c = new WifiNetworkInfo("connected", 20, false, false, true, "connected");

        var sorted = WifiService.SortNetworks([a, b, c]);

        Assert.Equal(["connected", "strong", "weak"], sorted.Select(n => n.Ssid));
    }
}