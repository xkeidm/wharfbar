using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class IslandConfigTests
{
    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(30, 30)]
    [InlineData(3650, 3650)]
    [InlineData(99999, 3650)]
    public void ClampRetentionDays_StaysInRange(int input, int expected)
    {
        Assert.Equal(expected, IslandNormalizer.ClampRetentionDays(input));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(9, 10)]
    [InlineData(200, 200)]
    [InlineData(5000, 5000)]
    [InlineData(99999, 5000)]
    public void ClampMaxItems_StaysInRange(int input, int expected)
    {
        Assert.Equal(expected, IslandNormalizer.ClampMaxItems(input));
    }

    [Fact]
    public void Defaults_MatchSpec()
    {
        var config = new IslandConfig();
        Assert.True(config.Enabled);
        Assert.Equal(IslandConfig.PersistenceMode.Session, config.Persistence);
        Assert.Equal(IslandConfig.DefaultRetentionDays, config.RetentionDays);
        Assert.Equal(IslandConfig.DefaultMaxItems, config.MaxItems);
    }
}
