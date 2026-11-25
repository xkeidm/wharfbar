using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WindowAppearanceTests
{
    [Fact]
    public void TintAlpha_ZeroOpacityStillKeepsTheWindowHitTestable()
    {
        // A layered window with an alpha-0 base is click-through per pixel: the transparent bar and
        // dock receive clicks only on their opaque glyphs. The floor keeps the surface clickable.
        Assert.Equal(WindowAppearance.HitTestFloorAlpha, WindowAppearance.TintAlpha(0));
        Assert.Equal(WindowAppearance.HitTestFloorAlpha, WindowAppearance.TintAlpha(-40));
    }

    [Fact]
    public void TintAlpha_ScalesWithOpacity()
    {
        Assert.Equal((byte)128, WindowAppearance.TintAlpha(50));
        Assert.Equal((byte)255, WindowAppearance.TintAlpha(100));
        Assert.Equal((byte)255, WindowAppearance.TintAlpha(140));
    }
}
