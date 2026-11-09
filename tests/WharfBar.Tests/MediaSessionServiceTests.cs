using WharfBar.Services;
using Windows.Media.Control;
using Xunit;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using PlaybackState = WharfBar.Services.MediaPlaybackState;

namespace WharfBar.Tests;

/// <summary>
/// Pure playback-status → selection-state mapping. The selection policy only promotes a session on a
/// transition into Playing, so this mapping decides the panel behavior: in particular a track skip
/// reports "Changing", which must keep counting as playing (otherwise every skip would look like a fresh
/// play transition and could steal the selection).
/// </summary>
public class MediaSessionServiceTests
{
    [Fact]
    public void ToPlaybackState_PlayingAndChanging_ArePlaying()
    {
        Assert.Equal(PlaybackState.Playing, MediaSessionService.ToPlaybackState(PlaybackStatus.Playing));
        Assert.Equal(PlaybackState.Playing, MediaSessionService.ToPlaybackState(PlaybackStatus.Changing));
    }

    [Fact]
    public void ToPlaybackState_PausedOpenedStopped_ArePaused()
    {
        Assert.Equal(PlaybackState.Paused, MediaSessionService.ToPlaybackState(PlaybackStatus.Paused));
        Assert.Equal(PlaybackState.Paused, MediaSessionService.ToPlaybackState(PlaybackStatus.Opened));
        Assert.Equal(PlaybackState.Paused, MediaSessionService.ToPlaybackState(PlaybackStatus.Stopped));
    }

    [Fact]
    public void ToPlaybackState_Closed_IsUnknown()
    {
        Assert.Equal(PlaybackState.Unknown, MediaSessionService.ToPlaybackState(PlaybackStatus.Closed));
    }
}
