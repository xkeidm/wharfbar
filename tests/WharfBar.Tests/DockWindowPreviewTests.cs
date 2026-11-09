using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class DockWindowPreviewTests
{
    [Fact]
    public void FitScale_ShrinksToFitTheWidthWhenWidthIsTheLimit()
    {
        Assert.Equal(0.125d, DockWindowPreview.FitScale(1600, 800, 200, 200), 6);
    }

    [Fact]
    public void FitScale_ShrinksToFitTheHeightWhenHeightIsTheLimit()
    {
        Assert.Equal(0.1d, DockWindowPreview.FitScale(800, 1000, 400, 100), 6);
    }

    [Fact]
    public void FitScale_DoesNotUpscaleASmallFrame()
    {
        Assert.Equal(1d, DockWindowPreview.FitScale(160, 90, 400, 400));
    }

    [Theory]
    [InlineData(0, 100, 100, 100)]
    [InlineData(100, 0, 100, 100)]
    [InlineData(100, 100, 0, 100)]
    [InlineData(100, 100, 100, 0)]
    [InlineData(-5, 100, 100, 100)]
    public void FitScale_InvalidMeasurementsScaleToNothing(int width, int height, int maxWidth, int maxHeight)
    {
        Assert.Equal(0d, DockWindowPreview.FitScale(width, height, maxWidth, maxHeight));
    }

    [Fact]
    public void FitScale_FitsExactlyAtTheBoxEdge()
    {
        Assert.Equal(1d, DockWindowPreview.FitScale(176, 99, 176, 99));
    }
}
