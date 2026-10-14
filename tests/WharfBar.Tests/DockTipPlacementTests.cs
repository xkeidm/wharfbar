using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockTipPlacementTests
{
    [Fact]
    public void CenterAbove_PutsTheBubbleOnTheTargetCenter()
    {
        (double x, double y) = DockTipPlacement.CenterAbove(180, 28, 40, dpiScale: 1);

        Assert.Equal(-70, x, 3);
        Assert.Equal(-53, y, 3);
    }

    [Fact]
    public void CenterAbove_ScalesTheGapWithDpi()
    {
        (double _, double y) = DockTipPlacement.CenterAbove(180, 28, 40, dpiScale: 2);

        Assert.Equal(-78, y, 3);
    }

    [Fact]
    public void CenterAbove_NonPositiveDpi_UsesAScaleOfOne()
    {
        (double _, double y) = DockTipPlacement.CenterAbove(10, 10, 40, dpiScale: 0);

        Assert.Equal(-35, y, 3);
    }
}
