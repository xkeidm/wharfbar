using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WidgetSnapTests
{
    private static readonly DipSize Card = new(40, 40);
    private static readonly DipRect Work = new(0, 0, 400, 400);

    [Fact]
    public void Snap_FreePoint_StaysPut()
    {
        DipPoint result = WidgetSnap.Snap(new DipPoint(100, 80), Card, Work, []);

        Assert.Equal(new DipPoint(100, 80), result);
    }

    [Fact]
    public void Snap_HittingAnObstacle_MovesToNearestFreeCell()
    {
        var blocked = new DipRect(0, 0, 80, 80);

        DipPoint result = WidgetSnap.Snap(new DipPoint(16, 16), Card, Work, [blocked]);

        Assert.Equal(new DipPoint(80, 16), result);
    }

    [Fact]
    public void Snap_NoFreeCell_PicksSmallestOverlap()
    {
        var work = new DipRect(0, 0, 80, 40);
        var card = new DipSize(40, 20);
        var blocked = new DipRect(0, 0, 50, 40);

        DipPoint result = WidgetSnap.Snap(new DipPoint(0, 0), card, work, [blocked]);

        Assert.Equal(new DipPoint(40, 0), result);
    }

    [Fact]
    public void Snap_CardStaysInsideTheWorkArea()
    {
        DipPoint result = WidgetSnap.Snap(new DipPoint(390, 390), Card, Work, []);

        Assert.Equal(new DipPoint(360, 360), result);
    }

    [Fact]
    public void Snap_NegativeFreePoint_IsPreserved()
    {
        var work = new DipRect(-1920, 0, 1920, 1080);

        DipPoint result = WidgetSnap.Snap(new DipPoint(-100, 40), new DipSize(20, 20), work, []);

        Assert.Equal(new DipPoint(-100, 40), result);
    }

    [Fact]
    public void Obstacles_InflatesIconsAndTheOtherCard()
    {
        IReadOnlyList<DipRect> obstacles = WidgetSnap.Obstacles(
            [new DipRect(0, 0, 10, 10)],
            new DipRect(100, 100, 20, 20));

        Assert.Equal(
            [new DipRect(-8, -8, 26, 26), new DipRect(92, 92, 36, 36)],
            obstacles);
    }
}
