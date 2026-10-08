using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockPlateMetricsTests
{
    [Fact]
    public void ButtonSize_AtDefaultIcon_IsForty()
    {
        Assert.Equal(40d, DockPlateMetrics.ButtonSize(24d));
    }

    [Fact]
    public void PlateHeight_WrapsTheButtonWithEightDipPadding()
    {
        Assert.Equal(56d, DockPlateMetrics.PlateHeight(24d));
    }

    [Fact]
    public void StripWidth_UsesFourDipGapsBetweenButtons()
    {
        // 3 * 40 + 2 * 4
        Assert.Equal(128d, DockPlateMetrics.StripWidth(3, 24d));
    }

    [Fact]
    public void PlateWidth_AddsTenDipSidePadding()
    {
        Assert.Equal(148d, DockPlateMetrics.PlateWidth(3, 24d));
    }

    [Fact]
    public void EmptyStrip_HasNoButtonWidth()
    {
        Assert.Equal(0d, DockPlateMetrics.StripWidth(0, 24d));
        Assert.Equal(20d, DockPlateMetrics.PlateWidth(0, 24d));
    }
}
