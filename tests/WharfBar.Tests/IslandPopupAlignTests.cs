using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandPopupAlignTests
{
    [Fact]
    public void Center_PlacesPanelMiddleOnPill()
    {
        // 108 DIP pill, 420 DIP panel, pill left at 500px, plenty of work area.
        double offset = IslandPopupAlign.HorizontalOffset(108, 420, 500, 96, 0, 1920);

        Assert.Equal(-156, offset, 3);
    }

    [Fact]
    public void Unmeasured_ReturnsZero()
    {
        Assert.Equal(0, IslandPopupAlign.HorizontalOffset(0, 420, 100, 96, 0, 1920));
        Assert.Equal(0, IslandPopupAlign.HorizontalOffset(108, 0, 100, 96, 0, 1920));
        Assert.Equal(0, IslandPopupAlign.HorizontalOffset(108, 420, 100, 0, 0, 1920));
    }

    [Fact]
    public void ClampLeft_KeepsPanelOnWorkArea()
    {
        double offset = IslandPopupAlign.HorizontalOffset(108, 420, 20, 96, 0, 1920);

        Assert.Equal(-20, offset, 3);
    }

    [Fact]
    public void ClampRight_KeepsPanelOnWorkArea()
    {
        double offset = IslandPopupAlign.HorizontalOffset(108, 420, 900, 96, 0, 1000);

        // Right edge lands on the work-area edge: 900 + offset + 420 = 1000.
        Assert.Equal(-320, offset, 3);
    }

    [Fact]
    public void Dpi_ScalesThePixelClamp_NotTheDipCenter()
    {
        // 2x. Unclamped center is still (108-420)/2 DIP. Pixels: left = 1000 + -156*2 = 688,
        // right = 688 + 840 = 1528, inside 0..2000.
        double offset = IslandPopupAlign.HorizontalOffset(108, 420, 1000, 192, 0, 2000);

        Assert.Equal(-156, offset, 3);
    }

    [Fact]
    public void WiderFallback_OpensLeftOfCenter()
    {
        const double pill = 108;
        const double real = 264;
        const double xamlFallback = 420;
        double centered = IslandPopupAlign.HorizontalOffset(pill, real, 800, 96, 0, 4000);
        double shifted = IslandPopupAlign.HorizontalOffset(pill, xamlFallback, 800, 96, 0, 4000);

        Assert.True(shifted < centered);
        Assert.Equal((xamlFallback - real) / 2d, centered - shifted, 3);
    }

    [Fact]
    public void WidthForPlacement_UsesArrangedWidth_NotXamlFallback()
    {
        Assert.Equal(0, IslandPopupAlign.WidthForPlacement(0));
        Assert.Equal(0, IslandPopupAlign.WidthForPlacement(double.NaN));
        Assert.Equal(264, IslandPopupAlign.WidthForPlacement(264));

        double used = IslandPopupAlign.WidthForPlacement(264);
        double offset = IslandPopupAlign.HorizontalOffset(108, used, 800, 96, 0, 4000);
        double fallbackOffset = IslandPopupAlign.HorizontalOffset(108, 420, 800, 96, 0, 4000);
        Assert.NotEqual(fallbackOffset, offset);
    }

    [Fact]
    public void Dpi_ClampUsesPhysicalPixels()
    {
        // 2x, pill flush with the left edge. Unclamped left = 0 + -156*2 = -312px.
        double offset = IslandPopupAlign.HorizontalOffset(108, 420, 0, 192, 0, 4000);

        Assert.Equal(0, offset, 3);
    }
}
