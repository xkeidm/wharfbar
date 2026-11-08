using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ProjectionMappingTests
{
    [Theory]
    // Actual DISPLAYCONFIG_TOPOLOGY_ID values (wingdi.h): INTERNAL=1, CLONE=2, EXTEND=4, EXTERNAL=8
    [InlineData(1, (int)ProjectionMode.InternalOnly)]
    [InlineData(2, (int)ProjectionMode.Clone)]
    [InlineData(4, (int)ProjectionMode.Extend)]
    [InlineData(8, (int)ProjectionMode.ExternalOnly)]
    public void FromTopologyId_MapsKnownIds(uint id, int expectedIndex)
    {
        Assert.Equal((ProjectionMode)expectedIndex, ProjectionService.FromTopologyId(id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(99)]
    public void FromTopologyId_UnknownId_IsNull(uint id)
    {
        Assert.Null(ProjectionService.FromTopologyId(id));
    }

    [Fact]
    public void SdcFlagFor_MapsModes()
    {
        Assert.Equal(DisplayTopology.SDC_TOPOLOGY_INTERNAL, ProjectionService.SdcFlagFor(ProjectionMode.InternalOnly));
        Assert.Equal(DisplayTopology.SDC_TOPOLOGY_CLONE, ProjectionService.SdcFlagFor(ProjectionMode.Clone));
        Assert.Equal(DisplayTopology.SDC_TOPOLOGY_EXTEND, ProjectionService.SdcFlagFor(ProjectionMode.Extend));
        Assert.Equal(DisplayTopology.SDC_TOPOLOGY_EXTERNAL, ProjectionService.SdcFlagFor(ProjectionMode.ExternalOnly));
    }
}