using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class NetworkDetailsServiceTests
{
    // --- Live traffic: byte delta → Mbps ---

    [Fact]
    public void ComputeThroughputMbps_ConvertsByteDelta()
    {
        // 2_500_000 byte / 2 sn = 1_250_000 B/s * 8 = 10_000_000 bit/s = 10 Mbps
        double? result = NetworkDetailsService.ComputeThroughputMbps(0, 2_500_000, 2.0);
        Assert.NotNull(result);
        Assert.Equal(10.0, result.Value, precision: 3);
    }

    [Fact]
    public void ComputeThroughputMbps_ZeroElapsed_ReturnsNull()
    {
        Assert.Null(NetworkDetailsService.ComputeThroughputMbps(0, 2_500_000, 0.0));
        Assert.Null(NetworkDetailsService.ComputeThroughputMbps(0, 2_500_000, -1.0));
    }

    [Fact]
    public void ComputeThroughputMbps_CounterReset_ReturnsNull()
    {
        // Adapter change / counter reset: negative delta → no reliable measurement
        Assert.Null(NetworkDetailsService.ComputeThroughputMbps(5_000, 4_000, 2.0));
    }

    // --- Link speed ---

    [Theory]
    [InlineData(1_000_000_000, "1 Gbps")]
    [InlineData(2_400_000_000, "2.4 Gbps")]
    [InlineData(100_000_000, "100 Mbps")]
    [InlineData(10_000_000, "10 Mbps")]
    public void FormatLinkSpeed_FormatsBitsPerSecond(long bitsPerSecond, string expected)
    {
        Assert.Equal(expected, NetworkDetailsService.FormatLinkSpeed(bitsPerSecond, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatLinkSpeed_ZeroOrNegative_ReturnsNull()
    {
        Assert.Null(NetworkDetailsService.FormatLinkSpeed(0, CultureInfo.InvariantCulture));
        Assert.Null(NetworkDetailsService.FormatLinkSpeed(-1, CultureInfo.InvariantCulture));
    }

    // --- Text formatting ---

    [Fact]
    public void FormatDns_JoinsAddresses()
    {
        string? dns = NetworkDetailsService.FormatDns(
            [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("8.8.8.8")]);
        Assert.Equal("1.1.1.1, 8.8.8.8", dns);
    }

    [Fact]
    public void FormatDns_Empty_ReturnsNull()
    {
        Assert.Null(NetworkDetailsService.FormatDns([]));
    }

    [Fact]
    public void FormatMac_DashSeparatedHex()
    {
        var mac = PhysicalAddress.Parse("AABBCCDDEEFF");
        Assert.Equal("AA-BB-CC-DD-EE-FF", NetworkDetailsService.FormatMac(mac));
    }

    [Fact]
    public void FormatMac_Empty_ReturnsNull()
    {
        Assert.Null(NetworkDetailsService.FormatMac(PhysicalAddress.None));
    }

    // --- Address selection ---

    [Fact]
    public void PickAddress_Ipv4SkipsLoopback()
    {
        var addresses = new List<IPAddress>
        {
            IPAddress.Loopback,
            IPAddress.Parse("192.168.1.5"),
            IPAddress.Parse("fe80::1"),
        };

        IPAddress? ipv4 = NetworkDetailsService.PickAddress(addresses, System.Net.Sockets.AddressFamily.InterNetwork);
        IPAddress? ipv6 = NetworkDetailsService.PickAddress(addresses, System.Net.Sockets.AddressFamily.InterNetworkV6);

        Assert.Equal(IPAddress.Parse("192.168.1.5"), ipv4);
        Assert.Equal(IPAddress.Parse("fe80::1"), ipv6);
    }

    [Fact]
    public void PickAddress_NoMatch_ReturnsNull()
    {
        var addresses = new List<IPAddress> { IPAddress.Parse("fe80::1") };
        Assert.Null(NetworkDetailsService.PickAddress(
            addresses, System.Net.Sockets.AddressFamily.InterNetwork));
    }

    // --- Live speed text ---

    [Fact]
    public void FormatMbps_OneDecimalSpaceUnit()
    {
        Assert.Equal("10 Mbps", NetworkDetailsService.FormatMbps(10.0, CultureInfo.InvariantCulture));
        Assert.Equal("2.5 Mbps", NetworkDetailsService.FormatMbps(2.5, CultureInfo.InvariantCulture));
        Assert.Equal("0 Mbps", NetworkDetailsService.FormatMbps(0.0, CultureInfo.InvariantCulture));
    }
}
