using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class SystemMonitorTests
{
    [Fact]
    public void IsGpu3DEngineInstance_Accepts3DSuffix()
    {
        Assert.True(SystemMonitorService.IsGpu3DEngineInstance(
            "pid_1234_luid_0x00000000_0x0000D3EA_phys_0_eng_0_engtype_3D"));
    }

    [Fact]
    public void IsGpu3DEngineInstance_RejectsCopyEngine()
    {
        Assert.False(SystemMonitorService.IsGpu3DEngineInstance(
            "pid_1234_luid_0x00000000_0x0000D3EA_phys_0_eng_1_engtype_Copy"));
    }
}
