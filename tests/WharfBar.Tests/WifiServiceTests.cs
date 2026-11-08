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
        // Empty SSID = hidden network (an AP that turned off SSID broadcast); "(Unknown network)" was misleading.
        Assert.Equal("(Hidden network)", WifiService.SsidToString(Array.Empty<byte>()));
        Assert.Equal("(Hidden network)", WifiService.SsidToString(new byte[32]));
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

    [Fact]
    public void BuildProfileXml_PassphraseIsCleartext()
    {
        string xml = WifiService.BuildProfileXml("Cafe", "s3cret");

        Assert.Contains("<protected>false</protected>", xml, StringComparison.Ordinal);
        Assert.Contains("<keyMaterial>s3cret</keyMaterial>", xml, StringComparison.Ordinal);
        Assert.Contains("<authentication>WPA2PSK</authentication>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<protected>true</protected>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProfileXml_EscapesPassword()
    {
        string xml = WifiService.BuildProfileXml("Cafe", "a<b&c");

        Assert.Contains("<keyMaterial>a&lt;b&amp;c</keyMaterial>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProfileXml_OpenNetworkHasNoKey()
    {
        string xml = WifiService.BuildProfileXml("Open", null);

        Assert.Contains("<authentication>open</authentication>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("keyMaterial", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProfileXml_Wpa3Sae()
    {
        string xml = WifiService.BuildProfileXml("Cafe", "s3cret", WifiService.AuthWpa3Sae);

        Assert.Contains("<authentication>WPA3SAE</authentication>", xml, StringComparison.Ordinal);
        Assert.Contains("<protected>false</protected>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void DedupeNetworks_KeepsConnectedCopyOfSameSsid()
    {
        var weak = new WifiNetworkInfo("Cafe", 10, true, false, false, null);
        var known = new WifiNetworkInfo("Cafe", 80, true, true, false, "Cafe");
        var connected = new WifiNetworkInfo("Cafe", 40, true, true, true, "Cafe");
        var home = new WifiNetworkInfo("Home", 20, false, false, false, null);

        IReadOnlyList<WifiNetworkInfo> deduped = WifiService.DedupeNetworks([weak, known, connected, home]);

        Assert.Equal(2, deduped.Count);
        Assert.Contains(deduped, n => n.Ssid == "Cafe" && n.IsConnected);
        Assert.Contains(deduped, n => n.Ssid == "Home");
    }

    [Theory]
    [InlineData(WifiService.AuthRsna, true)]
    [InlineData(WifiService.AuthWpa3Ent, true)]
    [InlineData(WifiService.AuthRsnaPsk, false)]
    [InlineData(WifiService.AuthWpa3Sae, false)]
    [InlineData(0u, false)]
    public void IsEnterpriseAuth_OnlyEnterpriseAlgorithms(uint auth, bool enterprise)
    {
        Assert.Equal(enterprise, WifiService.IsEnterpriseAuth(auth));
    }

    [Fact]
    public void ConnectBlockReason_HiddenEnterpriseAndUnconnectable()
    {
        Assert.NotNull(WifiService.ConnectBlockReason(new WifiNetworkInfo("(Hidden network)", 10, true, false, false, null)));
        Assert.NotNull(WifiService.ConnectBlockReason(new WifiNetworkInfo("Corp", 10, true, false, false, null, WifiService.AuthRsna)));
        Assert.NotNull(WifiService.ConnectBlockReason(new WifiNetworkInfo("Nope", 10, true, false, false, null, IsConnectable: false)));
        Assert.Null(WifiService.ConnectBlockReason(new WifiNetworkInfo("Cafe", 10, true, false, false, null, WifiService.AuthRsnaPsk)));
    }
}