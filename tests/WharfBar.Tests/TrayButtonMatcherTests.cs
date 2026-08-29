using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayButtonMatcherTests
{
    [Theory]
    [InlineData(" monitorian ", new[] { "Monitorian", "Other" }, 0)]
    [InlineData("OTHER", new[] { "a", "other" }, 1)]
    public void ResolveIndex_ExactMatchWinsCaseInsensitive(string key, string[] liveNames, int expected) =>
        Assert.Equal(expected, TrayButtonMatcher.ResolveIndex(liveNames, key, fallbackIndex: 99));

    [Theory]
    [InlineData("EarTrumpet", new[] { "EarTrumpet: 42% - Hoparlör" })]
    [InlineData("güvenliği", new[] { "Windows Güvenliği - Eylem gerekmiyor." })]
    public void ResolveIndex_ContainmentBothWaysWhenKeyNonEmpty(string key, string[] liveNames) =>
        Assert.Equal(0, TrayButtonMatcher.ResolveIndex(liveNames, key, fallbackIndex: 99));

    [Fact]
    public void ResolveIndex_EmptyKeyNeverContainmentMatches() =>
        Assert.Equal(-1, TrayButtonMatcher.ResolveIndex(new[] { "A", "B" }, key: string.Empty, fallbackIndex: -1));

    [Fact]
    public void ResolveIndex_EmptyKeyFallsBackToStoredIndex() =>
        Assert.Equal(1, TrayButtonMatcher.ResolveIndex(new[] { "A", "B" }, key: string.Empty, fallbackIndex: 1));

    [Fact]
    public void ResolveIndex_EmptyKeyOutOfRangeIndexYieldsMinusOne() =>
        Assert.Equal(-1, TrayButtonMatcher.ResolveIndex(new[] { "A", "B" }, key: string.Empty, fallbackIndex: 7));

    [Fact]
    public void ResolveIndex_EmptyKeyMatchesEmptyNamedButtonBeforeIndexFallback()
    {
        string[] liveNames = [string.Empty, "B"];
        Assert.Equal(0, TrayButtonMatcher.ResolveIndex(liveNames, key: string.Empty, fallbackIndex: 5));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public void ResolveIndex_WhitespaceOrNullKeyTreatedAsEmpty(string? key) =>
        Assert.Equal(-1, TrayButtonMatcher.ResolveIndex(new[] { "A", "B" }, key, fallbackIndex: -1));

    [Fact]
    public void ResolveIndex_NullLiveNamesCountAsEmptyButtons()
    {
        string?[] liveNames = [null, "X"];
        Assert.Equal(0, TrayButtonMatcher.ResolveIndex(liveNames, key: string.Empty, fallbackIndex: 9));
    }

    [Fact]
    public void ResolveIndex_NoMatchReturnsMinusOneEvenWithValidFallback() =>
        Assert.Equal(-1, TrayButtonMatcher.ResolveIndex(new[] { "A" }, "Z", fallbackIndex: 0));

    [Fact]
    public void ResolveIndex_MultipleContainmentCandidatesPickFirst()
    {
        string[] liveNames = ["Steam - Dükkan", "Steam - Eşitleme"];
        Assert.Equal(0, TrayButtonMatcher.ResolveIndex(liveNames, "Steam", fallbackIndex: 99));
    }
}
