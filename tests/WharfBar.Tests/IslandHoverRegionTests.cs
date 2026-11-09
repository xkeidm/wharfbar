using System.Windows;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandHoverRegionTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(50, 12, true)]
    [InlineData(100, 24, true)]
    [InlineData(-0.5, 12, false)]
    [InlineData(100.5, 12, false)]
    [InlineData(50, -0.5, false)]
    [InlineData(50, 24.5, false)]
    public void Contains_InteriorAndEdgesHit_OutsideMisses(double x, double y, bool expected)
        => Assert.Equal(expected, IslandHoverRegion.Contains(new Point(x, y), 100, 24));

    [Fact]
    public void Contains_UnarrangedRect_NeverHits()
    {
        Assert.False(IslandHoverRegion.Contains(new Point(0, 0), 0, 24));
        Assert.False(IslandHoverRegion.Contains(new Point(0, 0), 100, 0));
        Assert.False(IslandHoverRegion.Contains(new Point(0, 0), double.NaN, 24));
        Assert.False(IslandHoverRegion.Contains(new Point(0, 0), 100, -1));
    }

    [Fact]
    public void IsOverIsland_PillAlone()
    {
        Assert.True(IslandHoverRegion.IsOverIsland(
            new Point(54, 12), new Size(108, 24), panelPoint: null, panelSize: default));
        Assert.False(IslandHoverRegion.IsOverIsland(
            new Point(200, 12), new Size(108, 24), panelPoint: null, panelSize: default));
    }

    [Fact]
    public void IsOverIsland_TransparentPanelPadCounts()
    {
        // The popup child rect includes the panel's transparent shadow pad (24 DIP on the
        // sides, 10 DIP above the panel top), so a pointer resting on the pad is still over
        // the island and must not close a hover panel.
        Assert.True(IslandHoverRegion.IsOverIsland(
            new Point(300, 200), new Size(108, 24), new Point(10, 2), new Size(528, 500)));
    }

    [Fact]
    public void IsOverIsland_OutsideBothRects()
    {
        Assert.False(IslandHoverRegion.IsOverIsland(
            new Point(300, 200), new Size(108, 24), new Point(600, 200), new Size(528, 500)));
    }

    [Fact]
    public void IsOverIsland_UnarrangedPanelDoesNotSwallowThePointer()
    {
        Assert.False(IslandHoverRegion.IsOverIsland(
            new Point(300, 200), new Size(108, 24), new Point(0, 0), new Size(0, 0)));
    }
}
