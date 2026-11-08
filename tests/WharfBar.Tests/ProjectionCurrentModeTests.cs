using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// If QueryDisplayConfig fails, the current projection mode is inferred from the GetSystemMetrics geometry.
/// </summary>
public class ProjectionCurrentModeTests
{
    [Fact]
    public void SingleMonitor_IsInternalOnly()
    {
        Assert.Equal(ProjectionMode.InternalOnly,
            ProjectionService.GuessCurrentMode(1, 0, 0, 1920, 1080, 1920, 1080));
    }

    [Fact]
    public void ZeroMonitors_IsNull()
    {
        Assert.Null(ProjectionService.GuessCurrentMode(0, 0, 0, 0, 0, 1920, 1080));
    }

    [Fact]
    public void VirtualScreenEqualsPrimary_IsClone()
    {
        Assert.Equal(ProjectionMode.Clone,
            ProjectionService.GuessCurrentMode(2, 0, 0, 1920, 1080, 1920, 1080));
    }

    [Fact]
    public void SideBySide_IsExtend()
    {
        Assert.Equal(ProjectionMode.Extend,
            ProjectionService.GuessCurrentMode(2, 0, 0, 3840, 1080, 1920, 1080));
    }

    [Fact]
    public void SecondMonitorOnLeft_IsExtend()
    {
        Assert.Equal(ProjectionMode.Extend,
            ProjectionService.GuessCurrentMode(2, -1920, 0, 3840, 1080, 1920, 1080));
    }

    [Fact]
    public void Stacked_IsExtend()
    {
        Assert.Equal(ProjectionMode.Extend,
            ProjectionService.GuessCurrentMode(2, 0, 0, 1920, 2160, 1920, 1080));
    }
}
