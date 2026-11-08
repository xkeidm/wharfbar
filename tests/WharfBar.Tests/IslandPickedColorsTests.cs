using System.Windows.Media;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The color picker's "recent colors" list is verified through a pure helper: <c>IslandPickedColors.Pushed</c>
/// prepends the new color, does not duplicate the same color, and trims the tail when capacity is exceeded.
/// </summary>
public class IslandPickedColorsTests
{
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    [Fact]
    public void Pushed_EmptyList_PrependsNewColor()
    {
        IReadOnlyList<Color> result = IslandPickedColors.Pushed([], Rgb(10, 20, 30), 8);

        Assert.Single(result);
        Assert.Equal(Rgb(10, 20, 30), result[0]);
    }

    [Fact]
    public void Pushed_ExistingList_FrontAddsAndKeepsOrder()
    {
        var existing = new List<Color> { Rgb(1, 1, 1), Rgb(2, 2, 2) };

        IReadOnlyList<Color> result = IslandPickedColors.Pushed(existing, Rgb(3, 3, 3), 8);

        Assert.Equal(3, result.Count);
        Assert.Equal(Rgb(3, 3, 3), result[0]);
        Assert.Equal(Rgb(1, 1, 1), result[1]);
        Assert.Equal(Rgb(2, 2, 2), result[2]);
    }

    [Fact]
    public void Pushed_DuplicateColor_MovesToFrontWithoutDuplicate()
    {
        var existing = new List<Color> { Rgb(1, 1, 1), Rgb(2, 2, 2) };

        IReadOnlyList<Color> result = IslandPickedColors.Pushed(existing, Rgb(2, 2, 2), 8);

        Assert.Equal(2, result.Count);
        Assert.Equal(Rgb(2, 2, 2), result[0]);
        Assert.Equal(Rgb(1, 1, 1), result[1]);
    }

    [Fact]
    public void Pushed_CapacityReached_DropsOldest()
    {
        var existing = new List<Color> { Rgb(1, 1, 1), Rgb(2, 2, 2) };

        IReadOnlyList<Color> result = IslandPickedColors.Pushed(existing, Rgb(3, 3, 3), 2);

        Assert.Equal(2, result.Count);
        Assert.Equal(Rgb(3, 3, 3), result[0]);
        Assert.Equal(Rgb(1, 1, 1), result[1]);
    }

    [Fact]
    public void Pushed_TransparentSample_IsRejected()
    {
        IReadOnlyList<Color> existing = new List<Color> { Rgb(1, 1, 1) };

        Assert.Equal(existing, IslandPickedColors.Pushed(existing, Colors.Transparent, 8));
    }
}
