using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class IslandNormalizerLastHubWallpaperTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(" C:\\x\\a.jpg ", "C:\\x\\a.jpg")]
    public void NormalizeLastHubWallpaper(string? input, string expected)
        => Assert.Equal(expected, IslandNormalizer.NormalizeLastHubWallpaper(input));
}
