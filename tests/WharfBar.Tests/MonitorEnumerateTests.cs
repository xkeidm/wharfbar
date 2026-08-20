using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class MonitorEnumerateTests
{
    [Fact]
    public void Enumerate_ReturnsAtLeastOneMonitor()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorNative.Enumerate();
        Assert.NotEmpty(monitors);
    }

    [Fact]
    public void Enumerate_ExactlyOneMonitorIsPrimary()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorNative.Enumerate();
        Assert.Equal(1, monitors.Count(m => m.IsPrimary));
    }

    [Fact]
    public void Enumerate_PrimaryComesFirst()
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorNative.Enumerate();
        Assert.True(monitors[0].IsPrimary);
    }

    [Fact]
    public void Enumerate_AllMonitorsHaveNonEmptyDeviceIdAndPositiveDpi()
    {
        foreach (MonitorInfo monitor in MonitorNative.Enumerate())
        {
            Assert.False(string.IsNullOrWhiteSpace(monitor.DeviceId));
            Assert.True(monitor.Dpi > 0);
            Assert.True(monitor.Bounds.Width > 0 && monitor.Bounds.Height > 0);
        }
    }
}
