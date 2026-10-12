using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class EnergySaverTileTests
{
    [Fact]
    public void ApiOn_LightsTheTile_AndTheNextClickAsksOff()
    {
        Assert.True(EnergySaverTile.IsActive(apiOn: true, systemFlagOn: false, alwaysOnSwitch: false));
        Assert.False(EnergySaverTile.RequestedOn(apiOn: true, systemFlagOn: false, alwaysOnSwitch: false));
    }

    [Fact]
    public void SystemFlag_LightsTheTile_WithoutTheSettingsSwitch()
    {
        Assert.True(EnergySaverTile.IsActive(apiOn: false, systemFlagOn: true, alwaysOnSwitch: null));
    }

    [Fact]
    public void AlwaysOnSwitch_LightsTheTile_WhenTheApiStaysOff()
    {
        Assert.True(EnergySaverTile.IsActive(apiOn: false, systemFlagOn: false, alwaysOnSwitch: true));
        Assert.False(EnergySaverTile.RequestedOn(apiOn: false, systemFlagOn: false, alwaysOnSwitch: true));
    }

    [Fact]
    public void MissingSwitch_StaysOff_AndTheNextClickAsksOn()
    {
        Assert.False(EnergySaverTile.IsActive(apiOn: false, systemFlagOn: false, alwaysOnSwitch: null));
        Assert.False(EnergySaverTile.IsActive(apiOn: false, systemFlagOn: false, alwaysOnSwitch: false));
        Assert.True(EnergySaverTile.RequestedOn(apiOn: false, systemFlagOn: false, alwaysOnSwitch: null));
    }

    [Fact]
    public void ApiOn_WinsOverASwitchThatReadsOff()
    {
        Assert.True(EnergySaverTile.IsActive(apiOn: true, systemFlagOn: false, alwaysOnSwitch: false));
    }
}
