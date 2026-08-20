using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MonitorIdentityMatcherTests
{
    private static MonitorInfo Monitor(string deviceId, string deviceName, bool primary) =>
        new(IntPtr.Zero, deviceId, deviceName, "Test Display", primary, default, default, 96);

    private static MonitorConfig Entry(string deviceId, string deviceName, bool topBar, bool dock) =>
        new(deviceId, deviceName, "Test Display", topBar, dock);

    [Fact]
    public void Resolve_MatchingByDeviceId_ReturnsConfiguredEntry()
    {
        var live = Monitor("PCI\\VEN_1", "\\\\.\\DISPLAY2", primary: false);
        var configured = new[] { Entry("PCI\\VEN_1", "\\\\.\\DISPLAY2", topBar: true, dock: false) };

        MonitorConfig result = MonitorIdentityMatcher.Resolve(configured, live);

        Assert.True(result.TopBar);
        Assert.False(result.Dock);
    }

    [Fact]
    public void Resolve_MatchingByDeviceNameWhenNoDeviceId_ReturnsConfiguredEntry()
    {
        var live = Monitor(string.Empty, "\\\\.\\DISPLAY3", primary: false);
        var configured = new[] { Entry("", "\\\\.\\DISPLAY3", topBar: false, dock: true) };

        MonitorConfig result = MonitorIdentityMatcher.Resolve(configured, live);

        Assert.False(result.TopBar);
        Assert.True(result.Dock);
    }

    [Fact]
    public void Resolve_NoMatch_PrimaryDefaultsBothOn()
    {
        var live = Monitor("PCI\\VEN_NEW", "\\\\.\\DISPLAY1", primary: true);

        MonitorConfig result = MonitorIdentityMatcher.Resolve([], live);

        Assert.True(result.TopBar);
        Assert.True(result.Dock);
        Assert.Equal("PCI\\VEN_NEW", result.DeviceId);
    }

    [Fact]
    public void Resolve_NoMatch_SecondaryDefaultsBothOff()
    {
        var live = Monitor("PCI\\VEN_NEW", "\\\\.\\DISPLAY2", primary: false);

        MonitorConfig result = MonitorIdentityMatcher.Resolve([], live);

        Assert.False(result.TopBar);
        Assert.False(result.Dock);
    }

    [Fact]
    public void Resolve_PrefersDeviceIdOverDeviceName()
    {
        // DeviceId match wins even though the device name matches a different entry.
        var live = Monitor("PCI\\VEN_A", "\\\\.\\DISPLAY9", primary: false);
        var configured = new[]
        {
            Entry("PCI\\VEN_A", "\\\\.\\DISPLAY1", topBar: true, dock: false),
            Entry("PCI\\VEN_B", "\\\\.\\DISPLAY9", topBar: false, dock: true),
        };

        MonitorConfig result = MonitorIdentityMatcher.Resolve(configured, live);

        Assert.True(result.TopBar);
        Assert.False(result.Dock);
    }

    [Fact]
    public void MissingMonitors_ReturnsLiveMonitorsWithNoConfiguredEntry()
    {
        var m1 = Monitor("PCI\\VEN_A", "\\\\.\\DISPLAY1", primary: true);
        var m2 = Monitor("PCI\\VEN_B", "\\\\.\\DISPLAY2", primary: false);
        var configured = new[] { Entry("PCI\\VEN_A", "\\\\.\\DISPLAY1", topBar: true, dock: true) };

        IReadOnlyList<MonitorInfo> missing = MonitorIdentityMatcher.MissingMonitors(configured, new[] { m1, m2 });

        Assert.Single(missing);
        Assert.Equal("PCI\\VEN_B", missing[0].DeviceId);
    }
}