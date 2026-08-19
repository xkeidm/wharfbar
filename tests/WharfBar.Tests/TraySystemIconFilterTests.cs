using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TraySystemIconFilterTests
{
    [Theory]
    [InlineData("Ses")]
    [InlineData("Ağ durumu")]
    [InlineData("Volume")]
    [InlineData("Battery 75%")]
    [InlineData("Görev çubuğu")]
    [InlineData("Show hidden icons")]
    public void IsSystemIcon_True_ForKnownSystemNames(string tooltip) =>
        Assert.True(TraySystemIconFilter.IsSystemIcon(tooltip));

    [Theory]
    [InlineData("Spotify")]
    [InlineData("Update available")]
    [InlineData("Discord")]
    [InlineData("")]
    public void IsSystemIcon_False_ForOthers(string tooltip) =>
        Assert.False(TraySystemIconFilter.IsSystemIcon(tooltip));

    [Fact]
    public void IsSystemIcon_Null_IsFalse() =>
        Assert.False(TraySystemIconFilter.IsSystemIcon(null));

    [Fact]
    public void IsWharfBarIcon_MatchesOurTray()
    {
        Assert.True(TraySystemIconFilter.IsWharfBarIcon("WharfBar"));
        Assert.True(TraySystemIconFilter.IsWharfBarIcon("wharfbar menüsü"));
        Assert.False(TraySystemIconFilter.IsWharfBarIcon("Spotify"));
    }

    [Fact]
    public void Normalize_CollapsesWhitespaceAndCase() =>
        Assert.Equal("ağ durumu", TraySystemIconFilter.Normalize("  Ağ   DURUMU "));
}