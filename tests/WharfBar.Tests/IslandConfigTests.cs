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
    [InlineData(-5, 10)]
    [InlineData(0, 10)]
    [InlineData(9, 10)]
    [InlineData(200, 200)]
    [InlineData(5000, 5000)]
    [InlineData(99999, 5000)]
    public void ClampMaxItems_StaysInRange(int input, int expected)
    {
        Assert.Equal(expected, IslandNormalizer.ClampMaxItems(input));
    }

    [Theory]
    [InlineData("session", "session")]
    [InlineData("Session", "session")]
    [InlineData("SESSION", "session")]
    [InlineData("persistent", "persistent")]
    [InlineData("Persistent", "persistent")]
    [InlineData("PERSISTENT", "persistent")]
    public void NormalizePersistence_AcceptsKnownValuesInAnyCasing(string input, string expected)
    {
        Assert.Equal(expected, IslandNormalizer.NormalizePersistence(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("7")]
    public void NormalizePersistence_Unresolvable_FallsBackToDefault(string? input)
    {
        Assert.Equal("session", IslandNormalizer.NormalizePersistence(input));
    }

    [Fact]
    public void Defaults_MatchSpec()
    {
        Assert.True(IslandConfig.DefaultEnabled);
        Assert.Equal("session", IslandConfig.DefaultPersistence);
        Assert.Equal(30, IslandConfig.DefaultRetentionDays);
        Assert.Equal(200, IslandConfig.DefaultMaxItems);

        var config = new IslandConfig();
        Assert.True(config.Enabled);
        Assert.Equal("session", config.Persistence);
        Assert.Equal(30, config.RetentionDays);
        Assert.Equal(200, config.MaxItems);
        Assert.Equal("persistent", config.MusicPresence);
    }

    [Theory]
    [InlineData("persistent", "persistent")]
    [InlineData("Persistent", "persistent")]
    [InlineData("transient", "transient")]
    [InlineData("Transient", "transient")]
    [InlineData(null, "persistent")]
    [InlineData("", "persistent")]
    [InlineData("session", "persistent")]
    [InlineData("nope", "persistent")]
    public void NormalizeMusicPresence_FallsBackToPersistent(string? input, string expected)
    {
        Assert.Equal(expected, IslandNormalizer.NormalizeMusicPresence(input));
    }
}
