using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DesktopIconRectTests
{
    [Fact]
    public void KeepUsable_DropsEmptyAndNonFiniteRects()
    {
        IReadOnlyList<DipRect> kept = DesktopIconRects.KeepUsable(
        [
            new DipRect(10, 20, 32, 32),
            new DipRect(0, 0, 0, 10),
            new DipRect(1, 1, 8, 0),
            new DipRect(double.NaN, 4, 8, 8),
        ]);

        Assert.Equal([new DipRect(10, 20, 32, 32)], kept);
    }
}
