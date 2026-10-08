using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandPresenceTests
{
    private static IslandMediaCue Song(string title, bool playing, string session = "app")
        => new(true, session, title, "Artist", playing, title);

    [Fact]
    public void IdleClick_OpensClipboard_WithoutBack_SecondClickCloses()
    {
        var presence = new IslandPresence();

        Assert.Equal(IslandPillCommand.None, presence.Click(0));
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);
        Assert.False(presence.View.ShowClipboardBack);

        presence.Click(10);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Idle_Hover_OpensClipboard_LeaveGraceCloses()
    {
        var presence = new IslandPresence();

        presence.PointerEnterPill(100);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.False(presence.View.Pinned);
        Assert.False(presence.View.ShowClipboardBack);

        presence.PointerLeavePill(200);
        presence.Tick(200 + IslandPresence.HoverGraceMs - 1);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);

        presence.Tick(200 + IslandPresence.HoverGraceMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Idle_Hover_EnterPanel_CancelsGrace()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(0);
        presence.PointerLeavePill(100);
        presence.PointerEnterPanel(200);

        presence.Tick(100 + IslandPresence.HoverGraceMs + 50);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.False(presence.View.Pinned);
    }

    [Fact]
    public void Idle_Click_PinsHover_AndSecondClickCloses()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(0);
        presence.PointerLeavePill(100);

        presence.Click(200);
        Assert.True(presence.View.Pinned);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        presence.Tick(100 + IslandPresence.HoverGraceMs + 50);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);

        presence.Click(900);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Hud_Hover_DoesNotOpenPanel()
    {
        var presence = new IslandPresence();
        presence.ObserveVolume(10, muted: false, available: true, mixerOpen: false, nowMs: 0);
        presence.ObserveVolume(40, muted: false, available: true, mixerOpen: false, nowMs: 100);

        presence.PointerEnterPill(150);

        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Persistent_Session_ShowsTitle_AndDoesNotOpen()
    {
        var presence = new IslandPresence();
        presence.SetMusicMode(IslandMusicMode.Persistent, 0);

        presence.ObserveMedia(Song("Dawn", playing: true), 0);

        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal("Dawn", presence.View.Title);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Persistent_Hover_OpensMusic_LeaveGraceCloses()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);

        presence.PointerEnterPill(100);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.False(presence.View.Pinned);

        presence.PointerLeavePill(200);
        presence.Tick(200 + IslandPresence.HoverGraceMs - 1);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);

        presence.Tick(200 + IslandPresence.HoverGraceMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Persistent_Hover_EnterPanel_CancelsGrace()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.PointerEnterPill(0);
        presence.PointerLeavePill(100);
        presence.PointerEnterPanel(200);

        presence.Tick(100 + IslandPresence.HoverGraceMs + 50);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.False(presence.View.Pinned);
    }

    [Fact]
    public void Persistent_Click_PinsHover_AndSecondClickCloses()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.PointerEnterPill(0);
        presence.PointerLeavePill(100);

        presence.Click(200);
        Assert.True(presence.View.Pinned);
        presence.Tick(100 + IslandPresence.HoverGraceMs + 50);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);

        presence.Click(900);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Persistent_ClickWhileClosed_OpensPinnedMusic()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);

        presence.Click(50);

        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.True(presence.View.Pinned);
        Assert.Null(presence.NextDeadlineMs);
    }

    [Fact]
    public void Transient_Seed_StaysIdle_PlayStartOpensThenCloses()
    {
        var presence = new IslandPresence();
        presence.SetMusicMode(IslandMusicMode.Transient, 0);
        presence.ObserveMedia(Song("Dawn", playing: false), 0);

        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        presence.ObserveMedia(Song("Dawn", playing: true), 100);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.False(presence.View.Pinned);
        Assert.Equal(100 + IslandPresence.TransientHoldMs, presence.NextDeadlineMs);

        presence.Tick(100 + IslandPresence.TransientHoldMs - 1);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        presence.Tick(100 + IslandPresence.TransientHoldMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Transient_ClickDuringWindow_Pins()
    {
        var presence = new IslandPresence();
        presence.SetMusicMode(IslandMusicMode.Transient, 0);
        presence.ObserveMedia(Song("Dawn", playing: false), 0);
        presence.ObserveMedia(Song("Dawn", playing: true), 100);

        presence.Click(200);
        presence.Tick(100 + IslandPresence.TransientHoldMs + 10);

        Assert.True(presence.View.Pinned);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
    }

    [Fact]
    public void Transient_TrackChange_Rearms_IdenticalCueDoesNot()
    {
        var presence = new IslandPresence();
        presence.SetMusicMode(IslandMusicMode.Transient, 0);
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.ObserveMedia(Song("Dawn", playing: false), 100);
        long armed = presence.NextDeadlineMs!.Value;

        presence.ObserveMedia(Song("Dawn", playing: false), 200);
        Assert.Equal(armed, presence.NextDeadlineMs);

        presence.ObserveMedia(Song("Dusk", playing: false), 500);
        Assert.Equal(500 + IslandPresence.TransientHoldMs, presence.NextDeadlineMs);
    }

    [Fact]
    public void VolumeHud_PreemptsTitle_ThenRestores()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.ObserveVolume(10, muted: false, available: true, mixerOpen: false, nowMs: 0);

        presence.ObserveVolume(40, muted: false, available: true, mixerOpen: false, nowMs: 1000);

        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.Equal(40, presence.View.MeterPercent);
        Assert.False(presence.View.MeterMuted);

        presence.Tick(1000 + IslandPresence.HudHoldMs - 1);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        presence.Tick(1000 + IslandPresence.HudHoldMs);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal("Dawn", presence.View.Title);
    }

    [Fact]
    public void Volume_WhileMixerOpen_DoesNotHud_AndClearsMeter()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.ObserveVolume(10, false, true, false, 0);
        presence.ObserveVolume(40, false, true, false, 1000);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);

        presence.ObserveVolume(55, false, true, mixerOpen: true, nowMs: 1200);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
    }

    [Fact]
    public void Brightness_ReplacesVolumeHud_ThenRestoresIdle()
    {
        var presence = new IslandPresence();
        presence.SetMusicMode(IslandMusicMode.Transient, 0);
        presence.ObserveBrightness(20, brightnessUiOpen: false, nowMs: 0);
        presence.ObserveVolume(10, false, true, false, 0);
        presence.ObserveVolume(30, false, true, false, 1000);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);

        presence.ObserveBrightness(80, brightnessUiOpen: false, nowMs: 1400);
        Assert.Equal(IslandPillFace.Brightness, presence.View.Face);
        Assert.Equal(80, presence.View.MeterPercent);

        presence.Tick(1400 + IslandPresence.HudHoldMs);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
    }

    [Fact]
    public void Brightness_WhileUiOpen_DoesNotHud()
    {
        var presence = new IslandPresence();
        presence.ObserveBrightness(20, false, 0);
        presence.ObserveBrightness(70, brightnessUiOpen: true, nowMs: 500);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
    }

    [Fact]
    public void MuteChange_ShowsVolumeHud()
    {
        var presence = new IslandPresence();
        presence.ObserveVolume(40, muted: false, available: true, mixerOpen: false, nowMs: 0);
        presence.ObserveVolume(40, muted: true, available: true, mixerOpen: false, nowMs: 50);

        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.True(presence.View.MeterMuted);
        Assert.Equal(40, presence.View.MeterPercent);
    }

    [Fact]
    public void LaterVolumeChange_ResetsHudHold()
    {
        var presence = new IslandPresence();
        presence.ObserveVolume(10, false, true, false, 0);
        presence.ObserveVolume(20, false, true, false, 1000);
        presence.ObserveVolume(30, false, true, false, 1500);

        Assert.Equal(1500 + IslandPresence.HudHoldMs, presence.NextDeadlineMs);
        presence.Tick(1000 + IslandPresence.HudHoldMs);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
    }

    [Fact]
    public void SessionEnd_ClosesMusic_AndIdlesPill()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.Click(10);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);

        presence.ObserveMedia(new IslandMediaCue(false, "", "", "", false, ""), 20);

        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void MusicClipboardSwap_ShowsBack_AndReturns()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.Click(10);
        presence.ShowClipboard(20);

        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.ShowClipboardBack);
        Assert.True(presence.View.Pinned);

        presence.BackToMusic(30);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.False(presence.View.ShowClipboardBack);
        Assert.True(presence.View.Pinned);
    }

    [Fact]
    public void Dismiss_ClosesPinnedPanel_AndKeepsHud()
    {
        var presence = new IslandPresence();
        presence.Click(0);
        presence.ObserveVolume(10, false, true, false, 0);
        presence.ObserveVolume(25, false, true, false, 100);

        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);

        presence.Dismiss(200);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
    }

    [Fact]
    public void HudClick_RequestsExistingFlyout_AndClearsMeter()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.ObserveVolume(10, false, true, false, 0);
        presence.ObserveVolume(33, false, true, false, 100);

        IslandPillCommand command = presence.Click(150);

        Assert.Equal(IslandPillCommand.OpenVolumeFlyout, command);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        presence.ObserveBrightness(10, false, 0);
        presence.ObserveBrightness(60, false, 400);
        Assert.Equal(IslandPillCommand.OpenBrightnessFlyout, presence.Click(450));
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
    }

    [Fact]
    public void TitleMaxWidth_UsesNarrowerSide_AndCaps()
    {
        double wide = IslandPresence.TitleMaxWidth(1920, 300, 400, chromeDip: 96, desiredDip: 400);
        Assert.Equal(IslandPresence.MaxTitleDip, wide);

        double blocked = IslandPresence.TitleMaxWidth(1024, 280, 500, chromeDip: 96, desiredDip: 180);
        Assert.Equal(0, blocked);

        double unmeasured = IslandPresence.TitleMaxWidth(0, 0, 0, chromeDip: 96, desiredDip: 400);
        Assert.Equal(IslandPresence.MaxTitleDip, unmeasured);
    }

    [Fact]
    public void Transient_DoesNotStealOpenClipboard()
    {
        var presence = new IslandPresence();
        presence.SetMusicMode(IslandMusicMode.Transient, 0);
        presence.Click(0);
        presence.ObserveMedia(Song("Dawn", playing: false), 10);
        presence.ObserveMedia(Song("Dawn", playing: true), 20);

        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.False(presence.View.ShowClipboardBack);
    }
}
