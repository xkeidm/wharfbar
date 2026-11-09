using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Pure tests for the async metadata reload staleness guard: a completion may publish only while it
/// belongs to the newest reload generation and its session is still selected.
/// </summary>
public class MediaReloadGuardTests
{
    [Fact]
    public void IsCurrent_NewestGenerationForSelectedSession_ReturnsTrue()
    {
        Assert.True(MediaReloadGuard.IsCurrent(
            issuedGeneration: 7,
            latestGeneration: 7,
            issuedSessionId: "Spotify.exe",
            currentSessionId: "Spotify.exe"));
    }

    [Fact]
    public void IsCurrent_OlderGeneration_ReturnsFalse()
    {
        Assert.False(MediaReloadGuard.IsCurrent(
            issuedGeneration: 6,
            latestGeneration: 7,
            issuedSessionId: "Spotify.exe",
            currentSessionId: "Spotify.exe"));
    }

    [Fact]
    public void IsCurrent_NewerThanLatest_ReturnsFalse()
    {
        Assert.False(MediaReloadGuard.IsCurrent(
            issuedGeneration: 8,
            latestGeneration: 7,
            issuedSessionId: "Spotify.exe",
            currentSessionId: "Spotify.exe"));
    }

    [Fact]
    public void IsCurrent_SessionChanged_ReturnsFalse()
    {
        Assert.False(MediaReloadGuard.IsCurrent(
            issuedGeneration: 7,
            latestGeneration: 7,
            issuedSessionId: "Spotify.exe",
            currentSessionId: "msedge.exe"));
    }

    [Fact]
    public void IsCurrent_NoSelectedSession_ReturnsFalse()
    {
        Assert.False(MediaReloadGuard.IsCurrent(
            issuedGeneration: 7,
            latestGeneration: 7,
            issuedSessionId: "Spotify.exe",
            currentSessionId: null));
    }

    [Fact]
    public void IsCurrent_NoIssuedSession_ReturnsFalse()
    {
        Assert.False(MediaReloadGuard.IsCurrent(
            issuedGeneration: 7,
            latestGeneration: 7,
            issuedSessionId: null,
            currentSessionId: "Spotify.exe"));
    }

    [Fact]
    public void IsCurrent_SessionIdComparisonIsOrdinal()
    {
        Assert.False(MediaReloadGuard.IsCurrent(
            issuedGeneration: 7,
            latestGeneration: 7,
            issuedSessionId: "Spotify.exe",
            currentSessionId: "spotify.exe"));
    }
}
