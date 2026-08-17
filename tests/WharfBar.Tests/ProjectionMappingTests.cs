using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ProjectionMappingTests
{
    [Theory]
    [InlineData(1, (int)ProjectionMode.InternalOnly)]
    [InlineData(2, (int)ProjectionMode.Clone)]
    [InlineData(3, (int)ProjectionMode.Extend)]
    [InlineData(4, (int)ProjectionMode.ExternalOnly)]
    public void FromTopologyId_MapsKnownIds(uint id, int expectedIndex)
    {
        Assert.Equal((ProjectionMode)expectedIndex, ProjectionService.FromTopologyId(id));
    }

    [Fact]
    public void FromTopologyId_UnknownId_IsNull()
    {
        Assert.Null(ProjectionService.FromTopologyId(0));
        Assert.Null(ProjectionService.FromTopologyId(99));
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