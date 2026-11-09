using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MediaSessionSelectorTests
{
    [Fact]
    public void AddSession_Appends_And_Current_IsFirst()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        Assert.Equal(["A", "B"], sel.Sessions);
        Assert.Equal("A", sel.Current);
    }

    [Fact]
    public void MarkActive_MovesSessionToFront()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.MarkActive("B");
        Assert.Equal(["B", "A"], sel.Sessions);
        Assert.Equal("B", sel.Current);
    }

    [Fact]
    public void MarkActive_UnknownId_IsNoOp()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.MarkActive("X");
        Assert.Equal(["A"], sel.Sessions);
        Assert.Equal("A", sel.Current);
    }

    [Fact]
    public void AddSession_Existing_IsNoOp()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("A");
        Assert.Equal(["A"], sel.Sessions);
    }

    [Fact]
    public void RemoveSession_Current_FallsBackToMostRecentRemaining()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.MarkActive("A"); // A, B
        sel.RemoveSession("A");
        Assert.Equal("B", sel.Current);
        Assert.Equal(["B"], sel.Sessions);
    }

    [Fact]
    public void RemoveSession_All_CurrentIsNull()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.RemoveSession("A");
        Assert.Null(sel.Current);
        Assert.Empty(sel.Sessions);
    }

    [Fact]
    public void MoveCurrent_WrapsAround()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.AddSession("C");
        Assert.Equal("B", sel.MoveCurrent(1));          // [B, A, C]
        Assert.Equal(["B", "A", "C"], sel.Sessions);
        Assert.Equal("C", sel.MoveCurrent(1));          // [C, B, A]
        Assert.Equal(["C", "B", "A"], sel.Sessions);
        Assert.Equal("A", sel.MoveCurrent(1));          // [A, C, B]
        Assert.Equal(["A", "C", "B"], sel.Sessions);
        Assert.Equal("C", sel.MoveCurrent(-1));         // [C, A, B]
        Assert.Equal(["C", "A", "B"], sel.Sessions);
    }

    [Fact]
    public void MoveCurrent_UsesRingOrder_NotMruOrder()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.AddSession("C");
        sel.MarkActive("C");                            // MRU [C, A, B]
        Assert.Equal(["C", "A", "B"], sel.Sessions);
        Assert.Equal("A", sel.MoveCurrent(1));          // halka [A, B, C] → A
        Assert.Equal(["A", "C", "B"], sel.Sessions);
    }

    [Fact]
    public void MoveCurrent_Empty_ReturnsNull()
    {
        var sel = new MediaSessionSelector();
        Assert.Null(sel.MoveCurrent(1));
    }

    [Fact]
    public void SetPlaybackState_TransitionIntoPlaying_PromotesSession()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("Zen");
        sel.SetPlaybackState("Zen", MediaPlaybackState.Paused);
        sel.AddSession("YouTubeMusic");
        sel.SetPlaybackState("YouTubeMusic", MediaPlaybackState.Paused);
        Assert.Equal("Zen", sel.Current);

        sel.SetPlaybackState("YouTubeMusic", MediaPlaybackState.Playing);

        Assert.Equal("YouTubeMusic", sel.Current);
        Assert.Equal(["YouTubeMusic", "Zen"], sel.Sessions);
    }

    [Fact]
    public void SetPlaybackState_PausedSession_DoesNotStealCurrent()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("playing");
        sel.SetPlaybackState("playing", MediaPlaybackState.Playing);
        sel.AddSession("Zen");
        sel.SetPlaybackState("Zen", MediaPlaybackState.Paused);

        Assert.Equal("playing", sel.Current);
        Assert.Equal(["playing", "Zen"], sel.Sessions);
    }

    [Fact]
    public void SetPlaybackState_Unknown_StateChange_DoesNotPromote()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("playing");
        sel.SetPlaybackState("playing", MediaPlaybackState.Playing);
        sel.SetPlaybackState("playing", MediaPlaybackState.Paused);

        Assert.Equal("playing", sel.Current);
        Assert.Equal(MediaPlaybackState.Paused, sel.StateOf("playing"));
    }

    [Fact]
    public void SetPlaybackState_NewSession_IsListedWithoutPromotion()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("current");
        sel.SetPlaybackState("newcomer", MediaPlaybackState.Playing);

        Assert.Equal("current", sel.Current);
        Assert.Equal(["current", "newcomer"], sel.Sessions);
        Assert.Equal(MediaPlaybackState.Playing, sel.StateOf("newcomer"));
    }

    [Fact]
    public void SetPlaybackState_PlayingToPlaying_KeepsSelection()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.SetPlaybackState("A", MediaPlaybackState.Playing);

        // B appears already playing: it is listed but a new session never steals the selection.
        sel.SetPlaybackState("B", MediaPlaybackState.Playing);

        Assert.Equal("A", sel.Current);
        Assert.Equal(["A", "B"], sel.Sessions);

        sel.SetPlaybackState("B", MediaPlaybackState.Playing);
        Assert.Equal("A", sel.Current);
    }

    [Fact]
    public void RemoveSession_Current_PrefersStillPlayingFallback()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("pausedApp");
        sel.SetPlaybackState("pausedApp", MediaPlaybackState.Paused);

        // The playing app appeared while the paused one was selected: listed, but not promoted.
        sel.SetPlaybackState("playingApp", MediaPlaybackState.Playing);
        Assert.Equal("pausedApp", sel.Current);

        sel.RemoveSession("pausedApp");

        Assert.Equal("playingApp", sel.Current);
        Assert.Equal(["playingApp"], sel.Sessions);
    }

    [Fact]
    public void RemoveSession_NonCurrent_KeepsCurrent()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.SetPlaybackState("A", MediaPlaybackState.Playing);

        sel.RemoveSession("B");

        Assert.Equal("A", sel.Current);
        Assert.Equal(["A"], sel.Sessions);
    }

    [Fact]
    public void StateOf_UnknownSession_IsUnknown()
    {
        var sel = new MediaSessionSelector();
        Assert.Equal(MediaPlaybackState.Unknown, sel.StateOf("missing"));
    }

    [Fact]
    public void ShouldPromoteNewlyPlaying_NoCurrent_ReturnsTrue()
    {
        var sel = new MediaSessionSelector();
        Assert.True(sel.ShouldPromoteNewlyPlaying("YouTubeMusic"));
    }

    [Fact]
    public void ShouldPromoteNewlyPlaying_CurrentPaused_PromotesNewPlayingSession()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("Zen");
        sel.SetPlaybackState("Zen", MediaPlaybackState.Paused);
        // The playing session re-appears while already playing: listed but not promoted.
        sel.SetPlaybackState("YouTubeMusic", MediaPlaybackState.Playing);
        Assert.Equal("Zen", sel.Current);

        Assert.True(sel.ShouldPromoteNewlyPlaying("YouTubeMusic"));
        sel.MarkActive("YouTubeMusic");

        Assert.Equal("YouTubeMusic", sel.Current);
    }

    [Fact]
    public void ShouldPromoteNewlyPlaying_CurrentPlaying_DoesNotSteal()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.SetPlaybackState("A", MediaPlaybackState.Playing);
        sel.SetPlaybackState("B", MediaPlaybackState.Playing);
        Assert.Equal("A", sel.Current);

        Assert.False(sel.ShouldPromoteNewlyPlaying("B"));
        Assert.Equal("A", sel.Current);
    }

    [Fact]
    public void ShouldPromoteNewlyPlaying_AlreadyCurrent_ReturnsFalse()
    {
        var sel = new MediaSessionSelector();
        sel.SetPlaybackState("YouTubeMusic", MediaPlaybackState.Playing);

        Assert.False(sel.ShouldPromoteNewlyPlaying("YouTubeMusic"));
        Assert.Equal("YouTubeMusic", sel.Current);
    }

    [Fact]
    public void ReconcileFlow_PausedFallbackThenPlayingSessionAppears_PromotesPlayingSession()
    {
        var sel = new MediaSessionSelector();
        // The paused session is the fallback selection (e.g. it survived a removal).
        sel.SetPlaybackState("Zen", MediaPlaybackState.Paused);
        Assert.Equal("Zen", sel.Current);

        // A player re-appears already playing: seeding must not promote it, the reconcile rule does.
        sel.SetPlaybackState("YouTubeMusic", MediaPlaybackState.Playing);
        Assert.Equal("Zen", sel.Current);

        if (sel.ShouldPromoteNewlyPlaying("YouTubeMusic"))
        {
            sel.MarkActive("YouTubeMusic");
        }

        Assert.Equal("YouTubeMusic", sel.Current);
    }
}
