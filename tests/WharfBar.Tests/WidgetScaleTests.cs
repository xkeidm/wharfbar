using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WidgetScaleTests
{
    [Fact]
    public void RoundTrip_At96Dpi_KeepsThePixel()
    {
        Assert.Equal(120, WidgetScale.ToPhysical(WidgetScale.ToDip(120, 96), 96));
    }

    [Fact]
    public void ToDip_ZeroDpi_Matches96Dpi()
    {
        Assert.Equal(WidgetScale.ToDip(96, 96), WidgetScale.ToDip(96, 0));
    }

    [Fact]
    public void ToDip_At144Dpi_ShrinksPhysicalPixels()
    {
        Assert.Equal(80, WidgetScale.ToDip(120, 144), 3);
    }

    [Fact]
    public void WorkToDip_ConvertsTheWholeRect()
    {
        var physical = new RECT { Left = -1920, Top = 0, Right = 0, Bottom = 1080 };

        DipRect dip = WidgetScale.WorkToDip(physical, 96);

        Assert.Equal(new DipRect(-1920, 0, 1920, 1080), dip);
    }
}
