using System.Windows;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The screen color picker's affordance placement: the loupe stays centred on the cursor, the
/// HEX/hint card sits below it, and the card flips above the loupe (y-axis flip) when the bottom of
/// the monitor leaves no room — instead of dragging the loupe away from the cursor. Everything must
/// stay inside the viewport; this is pure math, so no WPF window is involved.
/// </summary>
public class ColorPickerLayoutTests
{
    private const double Loupe = 84;
    private const double InfoWidth = 150;
    private const double InfoHeight = 56;
    private const double Gap = 12;
    private const double Margin = 8;

    private static ColorPickerLayout.Placement Place(
        double cursorX, double cursorY, double width = 1920, double height = 1080, double infoHeight = InfoHeight)
        => ColorPickerLayout.Place(
            cursorX, cursorY, width, height, Loupe, InfoWidth, infoHeight, Gap, Margin);

    [Fact]
    public void Place_MidScreen_KeepsLoupeCentredAndCardBelow()
    {
        ColorPickerLayout.Placement placement = Place(960, 540);

        Assert.False(placement.InfoAbove);
        Assert.Equal(918, placement.Loupe.Left, 3);
        Assert.Equal(498, placement.Loupe.Top, 3);
        Assert.Equal(960, placement.Loupe.Left + (Loupe / 2), 3);
        Assert.Equal(540, placement.Loupe.Top + (Loupe / 2), 3);
        // Card: centred on the cursor, one gap below the loupe.
        Assert.Equal(885, placement.Info.Left, 3);
        Assert.Equal(placement.Loupe.Bottom + Gap, placement.Info.Top, 3);
    }

    [Fact]
    public void Place_CursorAtTheBottom_FlipsTheCardAboveTheLoupe()
    {
        ColorPickerLayout.Placement placement = Place(960, 1079);

        Assert.True(placement.InfoAbove);
        // The loupe is only pushed up enough to stay visible; the card flips above it, one gap clear.
        Assert.Equal(1080 - Margin - Loupe, placement.Loupe.Top, 3);
        Assert.Equal(placement.Loupe.Top - Gap - InfoHeight, placement.Info.Top, 3);
        Assert.True(placement.Info.Bottom <= placement.Loupe.Top - Gap);
        Assert.True(placement.Info.Top >= Margin);
        Assert.True(placement.Info.Bottom <= 1080 - Margin);
    }

    [Fact]
    public void Place_JustBeforeTheBottom_KeepsTheCardBelow()
    {
        // 954 + 56 = 1010 <= 1072: the card still fits below.
        ColorPickerLayout.Placement placement = Place(960, 900);

        Assert.False(placement.InfoAbove);
        Assert.Equal(placement.Loupe.Bottom + Gap, placement.Info.Top, 3);
    }

    [Fact]
    public void Place_TallerCard_FlipsEarlierThanTheShorterOne()
    {
        // Same cursor: the short card fits below (1014 + 56 = 1070), the tall one does not (1094) and flips.
        ColorPickerLayout.Placement shortCard = Place(960, 960, infoHeight: InfoHeight);
        ColorPickerLayout.Placement tallCard = Place(960, 960, infoHeight: 80);

        Assert.False(shortCard.InfoAbove);
        Assert.True(tallCard.InfoAbove);
        Assert.Equal(tallCard.Loupe.Top - Gap - 80, tallCard.Info.Top, 3);
    }

    [Fact]
    public void Place_CursorAtTheTopEdge_KeepsTheLoupeAndCardInside()
    {
        ColorPickerLayout.Placement placement = Place(5, 5);

        Assert.Equal(Margin, placement.Loupe.Left, 3);
        Assert.Equal(Margin, placement.Loupe.Top, 3);
        Assert.Equal(Margin, placement.Info.Left, 3);
        Assert.False(placement.InfoAbove);
        Assert.Equal(placement.Loupe.Bottom + Gap, placement.Info.Top, 3);
    }

    [Fact]
    public void Place_CursorAtTheBottomRightCorner_ClampsBothInsideTheViewport()
    {
        ColorPickerLayout.Placement placement = Place(1919, 1079);

        Assert.Equal(1920 - Margin, placement.Loupe.Right, 3);
        Assert.Equal(1080 - Margin, placement.Loupe.Bottom, 3);
        Assert.Equal(1920 - Margin, placement.Info.Right, 3);
        Assert.True(placement.InfoAbove);
        Assert.True(placement.Info.Top >= Margin);
    }

    [Fact]
    public void Place_ShortViewport_KeepsTheCardInsideInsteadOfLosingIt()
    {
        ColorPickerLayout.Placement placement = Place(150, 100, width: 300, height: 200);

        Assert.False(placement.InfoAbove);
        Assert.True(placement.Info.Top >= Margin);
        Assert.True(placement.Info.Bottom <= 200 - Margin);
    }

    [Fact]
    public void Place_InfoWiderThanViewport_ClampsToTheLeftMargin()
    {
        ColorPickerLayout.Placement placement = Place(50, 100, width: 100, height: 400);

        Assert.Equal(Margin, placement.Info.Left, 3);
    }

    [Fact]
    public void Place_DegenerateViewport_ReturnsFiniteRectangles()
    {
        ColorPickerLayout.Placement placement = Place(0, 0, width: 0, height: 0);

        Assert.False(double.IsNaN(placement.Loupe.Left));
        Assert.False(double.IsNaN(placement.Loupe.Top));
        Assert.False(double.IsNaN(placement.Info.Left));
        Assert.False(double.IsNaN(placement.Info.Top));
        Assert.True(placement.Info.Top >= 0);
    }

    [Fact]
    public void Place_CardNeverCoversTheLoupe()
    {
        // Sweep the whole monitor: whichever side the card lands on, it stays clear of the loupe.
        for (int y = 0; y <= 1080; y += 37)
        {
            ColorPickerLayout.Placement placement = Place(960, y);
            bool above = placement.InfoAbove;
            if (above)
            {
                Assert.True(placement.Info.Bottom <= placement.Loupe.Top);
            }
            else
            {
                Assert.True(placement.Info.Top >= placement.Loupe.Bottom);
            }
        }
    }
}
