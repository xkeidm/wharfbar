using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MonitorConfigNormalizerTests
{
    [Fact]
    public void Normalize_Null_ReturnsEmpty()
    {
        Assert.Empty(MonitorConfigNormalizer.Normalize(null));
    }

    [Fact]
    public void Normalize_DropsNullAndBlankDeviceIdEntries()
    {
        var list = new List<MonitorConfig>
        {
            new() { DeviceId = "PCI\\VEN_A" },
            new() { DeviceId = "" },
            new() { DeviceId = "   " },
            null!,
        };

        IReadOnlyList<MonitorConfig> result = MonitorConfigNormalizer.Normalize(list);

        Assert.Single(result);
        Assert.Equal("PCI\\VEN_A", result[0].DeviceId);
    }

    [Fact]
    public void Normalize_DedupesByDeviceId_KeepFirst()
    {
        var list = new List<MonitorConfig>
        {
            new() { DeviceId = "PCI\\VEN_A", TopBar = true, Dock = false },
            new() { DeviceId = "PCI\\VEN_A", TopBar = false, Dock = true },
        };

        IReadOnlyList<MonitorConfig> result = MonitorConfigNormalizer.Normalize(list);

        Assert.Single(result);
        Assert.True(result[0].TopBar);
        Assert.False(result[0].Dock);
    }

    [Fact]
    public void Normalize_TrimsDeviceIdAndName()
    {
        var list = new List<MonitorConfig>
        {
            new() { DeviceId = " PCI\\VEN_A ", Name = " DELL U2720Q " },
        };

        IReadOnlyList<MonitorConfig> result = MonitorConfigNormalizer.Normalize(list);

        Assert.Equal("PCI\\VEN_A", result[0].DeviceId);
        Assert.Equal("DELL U2720Q", result[0].Name);
    }
}
