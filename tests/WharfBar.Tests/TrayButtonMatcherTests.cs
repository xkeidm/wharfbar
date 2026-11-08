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

    [Fact]
    public void ResolveIndex_UnnamedSuffixedKeyUsesTheStoredIndex()
    {
        // TrayMirrorOrder.UniqueKeys turns unnamed buttons into "\0#index"; there is no name to
        // match, so the poll-time index is the only identity.
        string?[] liveNames = ["Discord", "", "Steam"];
        Assert.Equal(1, TrayButtonMatcher.ResolveIndex(liveNames, "\0#1", fallbackIndex: 1));
        Assert.Equal(1, TrayButtonMatcher.ResolveIndex(liveNames, "\0#1", fallbackIndex: 1));
    }

    [Fact]
    public void ResolveIndex_UnnamedSuffixedKeyOutOfRangeYieldsMinusOne() =>
        Assert.Equal(-1, TrayButtonMatcher.ResolveIndex(new[] { "A", "B" }, "\0#7", fallbackIndex: 7));

    [Fact]
    public void ResolveIndex_DuplicateNameSuffixedKeyPrefersItsOwnIndex()
    {
        // Two apps with the same tooltip: the suffix must keep the second mirrored icon pointing
        // at the second live button instead of the first exact-name match.
        string[] liveNames = ["Discord", "Steam"];
        Assert.Equal(0, TrayButtonMatcher.ResolveIndex(liveNames, "Discord\0#0", fallbackIndex: 0));
        liveNames = ["Steam", "Discord"];
        Assert.Equal(1, TrayButtonMatcher.ResolveIndex(liveNames, "Discord\0#1", fallbackIndex: 1));
    }

    [Fact]
    public void ResolveIndex_DuplicateSuffixFallsBackToNameWhenTheIndexMoved()
    {
        // The live order changed since the poll: fall back to the name instead of a stale index.
        string[] liveNames = ["Steam", "Discord"];
        Assert.Equal(1, TrayButtonMatcher.ResolveIndex(liveNames, "Discord\0#0", fallbackIndex: 0));
    }
}
