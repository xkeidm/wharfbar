using WharfBar.Models;
using WharfBar.Services;
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
        Assert.Equal("clipboard", config.IdleLabel);
        Assert.Equal("clipboard", IslandConfig.DefaultHubTab);
        Assert.Equal("clipboard", new IslandConfig().HubTab);
        Assert.True(new IslandConfig().ShelfTab);
        Assert.True(new IslandConfig().ColorPickerTab);
        Assert.Equal("blurFade", IslandConfig.DefaultWallpaperTransition);
        Assert.True(IslandConfig.DefaultWallpaperRespectSystemAnimation);
        Assert.Equal("blurFade", new IslandConfig().WallpaperTransition);
        Assert.True(new IslandConfig().WallpaperRespectSystemAnimation);
        Assert.True(IslandConfig.DefaultWallpaperAccent);
        Assert.True(new IslandConfig().WallpaperAccent);
        Assert.Equal(20, config.LowBatteryPercent);
        Assert.Equal(20, IslandConfig.DefaultLowBatteryPercent);
    }

    [Fact]
    public void Defaults_MatchSpec_WidgetTabs()
    {
        Assert.True(IslandConfig.DefaultShelfTab);
        Assert.True(IslandConfig.DefaultColorPickerTab);
    }

    [Fact]
    public void Defaults_MatchSpec_PrompterTab()
    {
        Assert.True(IslandConfig.DefaultPrompterTab);
        Assert.True(new IslandConfig().PrompterTab);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(20, 20)]
    [InlineData(50, 50)]
    [InlineData(4, 20)]
    [InlineData(51, 20)]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(100, 20)]
    public void NormalizeLowBatteryPercent_OutOfRangeBecomesDefault(int input, int expected)
    {
        Assert.Equal(expected, IslandNormalizer.NormalizeLowBatteryPercent(input));
    }

    [Theory]
    [InlineData(3, 5)]
    [InlineData(5, 5)]
    [InlineData(20, 20)]
    [InlineData(50, 50)]
    [InlineData(80, 50)]
    [InlineData(0, 5)]
    public void ClampLowBatteryPercent_StaysInAcceptedRange(int input, int expected)
    {
        Assert.Equal(expected, IslandNormalizer.ClampLowBatteryPercent(input));
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

    [Theory]
    [InlineData("clipboard", "clipboard")]
    [InlineData("Clipboard", "clipboard")]
    [InlineData("lastClip", "lastClip")]
    [InlineData("LastClip", "lastClip")]
    [InlineData("LASTCLIP", "lastClip")]
    [InlineData(null, "clipboard")]
    [InlineData("", "clipboard")]
    [InlineData("pano", "clipboard")]
    [InlineData("persistent", "clipboard")]
    public void NormalizeIdleLabel_FallsBackToClipboard(string? input, string expected)
    {
        Assert.Equal(expected, IslandNormalizer.NormalizeIdleLabel(input));
    }

    [Theory]
    [InlineData("off", "off")]
    [InlineData("Off", "off")]
    [InlineData("fade", "fade")]
    [InlineData("Fade", "fade")]
    [InlineData("blurFade", "blurFade")]
    [InlineData("BlurFade", "blurFade")]
    [InlineData(null, "blurFade")]
    [InlineData("", "blurFade")]
    [InlineData("garbage", "blurFade")]
    public void NormalizeWallpaperTransition_FallsBackToBlurFade(string? input, string expected)
    {
        Assert.Equal(expected, IslandNormalizer.NormalizeWallpaperTransition(input));
    }

    [Theory]
    [InlineData("clipboard", "clipboard")]
    [InlineData("music", "music")]
    [InlineData("shelf", "shelf")]
    [InlineData("SheLF", "shelf")]
    [InlineData("colorpicker", "colorpicker")]
    [InlineData("ColorPicker", "colorpicker")]
    [InlineData(null, "clipboard")]
    [InlineData("garbage", "clipboard")]
    public void NormalizeHubTab_AcceptsKnownIdsInAnyCasing(string? input, string expected)
    {
        Assert.Equal(expected, IslandNormalizer.NormalizeHubTab(input));
    }
}
