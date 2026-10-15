using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class IslandColorModeTests
{
    [Theory]
    [InlineData("album", IslandConfig.ColorModeAlbum)]
    [InlineData("ALBUM", IslandConfig.ColorModeAlbum)]
    [InlineData("system", IslandConfig.ColorModeSystem)]
    [InlineData(" System ", IslandConfig.ColorModeSystem)]
    [InlineData("neutral", IslandConfig.ColorModeNeutral)]
    [InlineData("Neutral", IslandConfig.ColorModeNeutral)]
    [InlineData("", IslandConfig.ColorModeAlbum)]
    [InlineData("rainbow", IslandConfig.ColorModeAlbum)]
    [InlineData(null, IslandConfig.ColorModeAlbum)]
    public void Normalize_MapsKnownValuesAndFallsBackToAlbum(string? input, string expected)
        => Assert.Equal(expected, IslandNormalizer.NormalizeColorMode(input));

    [Fact]
    public void NewConfig_DefaultsToAlbum()
        => Assert.Equal(IslandConfig.ColorModeAlbum, new IslandConfig().ColorMode);
}
