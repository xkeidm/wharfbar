using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

/// <summary>If a display is present in this session, QueryDisplayConfig should return a topology id.</summary>
public class QuickSettingsApiSmokeTests
{
    [Fact]
    public void QueryDisplayConfig_ReadsCurrentTopology()
    {
        Assert.True(DisplayTopology.TryGetCurrentTopology(out uint id));
        Assert.True(id is 1 or 2 or 4 or 8, $"unexpected topology {id}");
    }
}
