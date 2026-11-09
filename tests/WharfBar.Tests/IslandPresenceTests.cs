using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandPresenceTests
{
    private static IslandMediaCue Song(string title, bool playing, string session = "app")
        => new(true, session, title, "Artist", playing, title);

    private static void AssertHoverStaysClosed(IslandPresence presence, long enterAt, IslandPillFace face)
    {
        presence.PointerEnterPill(enterAt);
        presence.Tick(enterAt + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(face, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

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
        presence.Tick(100 + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.False(presence.View.Pinned);
        Assert.False(presence.View.ShowClipboardBack);

        long leftAt = 100 + IslandPresence.HoverOpenDelayMs;
        presence.PointerLeavePill(leftAt);
        presence.Tick(leftAt + IslandPresence.HoverGraceMs - 1);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);

        presence.Tick(leftAt + IslandPresence.HoverGraceMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Idle_Hover_EnterPanel_CancelsGrace()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(0);
        presence.Tick(IslandPresence.HoverOpenDelayMs);
        presence.PointerLeavePill(IslandPresence.HoverOpenDelayMs);
        presence.PointerEnterPanel(IslandPresence.HoverOpenDelayMs + 100);

        presence.Tick(IslandPresence.HoverOpenDelayMs + IslandPresence.HoverGraceMs + 50);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.False(presence.View.Pinned);
    }

    [Fact]
    public void Idle_Click_PinsHover_AndSecondClickCloses()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(0);
        presence.Tick(IslandPresence.HoverOpenDelayMs);
        presence.PointerLeavePill(IslandPresence.HoverOpenDelayMs);

        presence.Click(IslandPresence.HoverOpenDelayMs + 100);
        Assert.True(presence.View.Pinned);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        presence.Tick(IslandPresence.HoverOpenDelayMs + IslandPresence.HoverGraceMs + 50);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);

        presence.Click(900);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Hud_Hover_DoesNotOpenPanel()
    {
        var volume = new IslandPresence();
        volume.ObserveVolume(10, muted: false, available: true, mixerOpen: false, nowMs: 0);
        volume.ObserveVolume(40, muted: false, available: true, mixerOpen: false, nowMs: 100);
        AssertHoverStaysClosed(volume, 150, IslandPillFace.Volume);

        var brightness = new IslandPresence();
        brightness.ObserveBrightness(20, brightnessUiOpen: false, nowMs: 0);
        brightness.ObserveBrightness(70, brightnessUiOpen: false, nowMs: 100);
        AssertHoverStaysClosed(brightness, 150, IslandPillFace.Brightness);

        var clip = new IslandPresence();
        clip.NoteNewClip("Copied", 100);
        AssertHoverStaysClosed(clip, 150, IslandPillFace.Clip);

        var battery = new IslandPresence();
        battery.ObserveBattery(present: true, charging: false, percent: 80, nowMs: 0);
        battery.ObserveBattery(present: true, charging: true, percent: 80, nowMs: 100);
        AssertHoverStaysClosed(battery, 150, IslandPillFace.Battery);

        var drop = new IslandPresence();
        drop.NoteFileDrop("notes.txt", 100);
        AssertHoverStaysClosed(drop, 150, IslandPillFace.Drop);
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
        presence.Tick(100 + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.False(presence.View.Pinned);

        long leftAt = 100 + IslandPresence.HoverOpenDelayMs;
        presence.PointerLeavePill(leftAt);
        presence.Tick(leftAt + IslandPresence.HoverGraceMs - 1);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);

        presence.Tick(leftAt + IslandPresence.HoverGraceMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void Persistent_Hover_EnterPanel_CancelsGrace()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.PointerEnterPill(0);
        presence.Tick(IslandPresence.HoverOpenDelayMs);
        presence.PointerLeavePill(IslandPresence.HoverOpenDelayMs);
        presence.PointerEnterPanel(IslandPresence.HoverOpenDelayMs + 100);

        presence.Tick(IslandPresence.HoverOpenDelayMs + IslandPresence.HoverGraceMs + 50);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.False(presence.View.Pinned);
    }

    [Fact]
    public void Persistent_Click_PinsHover_AndSecondClickCloses()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.PointerEnterPill(0);
        presence.Tick(IslandPresence.HoverOpenDelayMs);
        presence.PointerLeavePill(IslandPresence.HoverOpenDelayMs);

        presence.Click(IslandPresence.HoverOpenDelayMs + 100);
        Assert.True(presence.View.Pinned);
        presence.Tick(IslandPresence.HoverOpenDelayMs + IslandPresence.HoverGraceMs + 50);
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
        Assert.Equal(IslandSatellite.Music, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.Clipboard, presence.View.SecondSatellite);

        presence.Tick(1000 + IslandPresence.HudHoldMs - 1);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        presence.Tick(1000 + IslandPresence.HudHoldMs);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal("Dawn", presence.View.Title);
        Assert.Equal(IslandSatellite.None, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.None, presence.View.SecondSatellite);
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

    [Fact]
    public void PersistentMusic_HasNoSatellites()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);

        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal("Dawn", presence.View.Title);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
        Assert.Equal(IslandSatellite.None, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.None, presence.View.SecondSatellite);
    }

    [Fact]
    public void VolumeHud_KeepsMusicSatellite_AndHoverDoesNotOpen()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.ObserveVolume(10, muted: false, available: true, mixerOpen: false, nowMs: 0);
        presence.ObserveVolume(44, muted: true, available: true, mixerOpen: false, nowMs: 100);

        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.Equal(44, presence.View.MeterPercent);
        Assert.True(presence.View.MeterMuted);
        Assert.Equal(IslandSatellite.Music, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.Clipboard, presence.View.SecondSatellite);

        presence.PointerEnterPill(200);
        presence.Tick(200 + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
        Assert.Equal(IslandSatellite.Music, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.Clipboard, presence.View.SecondSatellite);
    }

    [Fact]
    public void Transient_VolumeHud_UsesClipboardSatellite()
    {
        var presence = new IslandPresence();
        presence.SetMusicMode(IslandMusicMode.Transient, 0);
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.ObserveVolume(10, muted: false, available: true, mixerOpen: false, nowMs: 0);
        presence.ObserveVolume(30, muted: false, available: true, mixerOpen: false, nowMs: 50);

        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.Equal(IslandSatellite.Clipboard, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.None, presence.View.SecondSatellite);
    }

    [Fact]
    public void ClickSatellite_Music_ClearsHud_AndOpensPinnedMusic()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.ObserveVolume(10, muted: false, available: true, mixerOpen: false, nowMs: 0);
        presence.ObserveVolume(22, muted: false, available: true, mixerOpen: false, nowMs: 100);
        Assert.Equal(IslandSatellite.Music, presence.View.FirstSatellite);

        Assert.Equal(IslandPillCommand.None, presence.ClickSatellite(IslandSatellite.Music, 200));
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.True(presence.View.Pinned);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal("Dawn", presence.View.Title);

        presence.ClickSatellite(IslandSatellite.None, 300);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);

        presence.ClickSatellite(IslandSatellite.Clipboard, 400);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);
        Assert.False(presence.View.ShowClipboardBack);

        var idle = new IslandPresence();
        idle.ClickSatellite(IslandSatellite.Music, 0);
        Assert.Equal(IslandPanelBody.None, idle.View.Panel);
    }

    [Fact]
    public void NewClipHud_RestoresPersistentMusic_WithClipboardSatellite()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.NoteNewClip("   ", 10);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal("Dawn", presence.View.Title);

        presence.NoteNewClip("  hello world  ", 100);
        Assert.Equal(IslandPillFace.Clip, presence.View.Face);
        Assert.Equal("hello world", presence.View.Title);
        Assert.Equal(IslandSatellite.Music, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.Clipboard, presence.View.SecondSatellite);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        presence.Tick(100 + IslandPresence.HudHoldMs - 1);
        Assert.Equal(IslandPillFace.Clip, presence.View.Face);
        presence.Tick(100 + IslandPresence.HudHoldMs);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal("Dawn", presence.View.Title);
        Assert.Equal(IslandSatellite.None, presence.View.FirstSatellite);

        presence.NoteNewClip("second", 3000);
        Assert.Equal(IslandPillCommand.None, presence.Click(3100));
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.False(presence.View.ShowClipboardBack);
    }

    [Fact]
    public void Battery_SeedsWithoutHud_PlugAndThresholdFireOnce()
    {
        var presence = new IslandPresence();
        int low = IslandPresence.LowBatteryPercent - 1;

        presence.ObserveBattery(present: false, charging: false, percent: 10, nowMs: 0);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: false, percent: low, nowMs: 10);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(0, presence.View.MeterPercent);

        presence.ObserveBattery(present: true, charging: false, percent: low - 1, nowMs: 20);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: false, percent: 40, nowMs: 30);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: false, percent: low, nowMs: 40);
        Assert.Equal(IslandPillFace.Battery, presence.View.Face);
        Assert.Equal(string.Empty, presence.View.Title);
        Assert.Equal(low, presence.View.MeterPercent);
        Assert.False(presence.View.MeterMuted);
        Assert.Equal(IslandSatellite.Clipboard, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.None, presence.View.SecondSatellite);
        long lowDeadline = 40 + IslandPresence.HudHoldMs;
        Assert.Equal(lowDeadline, presence.NextDeadlineMs);

        presence.ObserveBattery(present: true, charging: false, percent: 12, nowMs: 50);
        Assert.Equal(IslandPillFace.Battery, presence.View.Face);
        Assert.Equal(lowDeadline, presence.NextDeadlineMs);
        Assert.Equal(low, presence.View.MeterPercent);

        presence.Tick(lowDeadline);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: false, percent: 11, nowMs: lowDeadline + 10);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: true, percent: 12, nowMs: 2000);
        Assert.Equal(IslandPillFace.Battery, presence.View.Face);
        Assert.Equal(string.Empty, presence.View.Title);
        Assert.Equal(12, presence.View.MeterPercent);
        Assert.False(presence.View.MeterMuted);

        presence.ObserveBattery(present: true, charging: true, percent: 18, nowMs: 2100);
        Assert.Equal(2000 + IslandPresence.HudHoldMs, presence.NextDeadlineMs);

        presence.Tick(2000 + IslandPresence.HudHoldMs);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: false, percent: 18, nowMs: 4000);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: false, percent: IslandPresence.LowBatteryPercent, nowMs: 4100);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: false, percent: low, nowMs: 4200);
        Assert.Equal(IslandPillFace.Battery, presence.View.Face);

        presence.ObserveBattery(present: false, charging: false, percent: low, nowMs: 4300);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(0, presence.View.MeterPercent);

        presence.ObserveBattery(present: true, charging: true, percent: 5, nowMs: 4400);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: false, percent: 50, nowMs: 4500);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.ObserveBattery(present: true, charging: true, percent: 40, nowMs: 4600);
        Assert.Equal(IslandPillFace.Battery, presence.View.Face);
        Assert.Equal(IslandPillCommand.None, presence.Click(4700));
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
        Assert.Equal(0, presence.View.MeterPercent);
    }

    [Fact]
    public void Idle_HasNoSatellites()
    {
        var presence = new IslandPresence();
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandSatellite.None, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.None, presence.View.SecondSatellite);
    }

    [Fact]
    public void SetLowBatteryPercent_ClampsAndRearmsTheNextCrossing()
    {
        var high = new IslandPresence();
        high.SetLowBatteryPercent(99, 0);
        high.ObserveBattery(present: true, charging: false, percent: 80, nowMs: 0);
        high.ObserveBattery(present: true, charging: false, percent: 60, nowMs: 10);
        Assert.Equal(IslandPillFace.Idle, high.View.Face);
        high.ObserveBattery(present: true, charging: false, percent: 49, nowMs: 20);
        Assert.Equal(IslandPillFace.Battery, high.View.Face);
        Assert.Equal(49, high.View.MeterPercent);

        var low = new IslandPresence();
        low.SetLowBatteryPercent(-4, 0);
        low.ObserveBattery(present: true, charging: false, percent: 10, nowMs: 0);
        low.ObserveBattery(present: true, charging: false, percent: 6, nowMs: 10);
        Assert.Equal(IslandPillFace.Idle, low.View.Face);
        low.ObserveBattery(present: true, charging: false, percent: 4, nowMs: 20);
        Assert.Equal(IslandPillFace.Battery, low.View.Face);
        long end = 20 + IslandPresence.HudHoldMs;
        low.Tick(end);
        Assert.Equal(IslandPillFace.Idle, low.View.Face);

        low.ObserveBattery(present: true, charging: false, percent: 3, nowMs: end + 1);
        Assert.Equal(IslandPillFace.Idle, low.View.Face);

        low.SetLowBatteryPercent(8, end + 2);
        Assert.Equal(IslandPillFace.Idle, low.View.Face);
        Assert.Null(low.NextDeadlineMs);

        low.ObserveBattery(present: true, charging: false, percent: 2, nowMs: end + 3);
        Assert.Equal(IslandPillFace.Idle, low.View.Face);

        low.ObserveBattery(present: true, charging: false, percent: 9, nowMs: end + 4);
        Assert.Equal(IslandPillFace.Idle, low.View.Face);

        low.ObserveBattery(present: true, charging: false, percent: 7, nowMs: end + 5);
        Assert.Equal(IslandPillFace.Battery, low.View.Face);
        Assert.Equal(7, low.View.MeterPercent);

        low.ObserveBattery(present: true, charging: false, percent: 6, nowMs: end + 6);
        Assert.Equal(end + 5 + IslandPresence.HudHoldMs, low.NextDeadlineMs);

        low.SetLowBatteryPercent(8, end + 7);
        Assert.Equal(IslandPillFace.Battery, low.View.Face);
    }

    [Fact]
    public void FileDrop_HudShowsTrimmedName_ClickOpensClipboard()
    {
        var presence = new IslandPresence();
        presence.NoteFileDrop("   ", 0);
        presence.NoteFileDrop(null, 10);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Null(presence.NextDeadlineMs);

        presence.NoteFileDrop("  report.pdf  ", 100);
        Assert.Equal(IslandPillFace.Drop, presence.View.Face);
        Assert.Equal("report.pdf", presence.View.Title);
        Assert.Equal(0, presence.View.MeterPercent);
        Assert.Equal(IslandSatellite.Clipboard, presence.View.FirstSatellite);
        Assert.Equal(IslandSatellite.None, presence.View.SecondSatellite);
        Assert.Equal(100 + IslandPresence.HudHoldMs, presence.NextDeadlineMs);

        Assert.Equal(IslandPillCommand.None, presence.Click(150));
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.False(presence.View.ShowClipboardBack);
    }

    [Fact]
    public void ClipboardSurfaceOff_SuppressesClipDropSatellite_AndIdleClick()
    {
        var presence = new IslandPresence();
        presence.SetClipboardSurface(available: false, nowMs: 0);

        Assert.Equal(IslandPillCommand.None, presence.Click(0));
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        presence.NoteNewClip("secret", 10);
        presence.NoteFileDrop("a.txt", 20);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Null(presence.NextDeadlineMs);

        presence.PointerEnterPill(30);
        presence.Tick(30 + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        presence.ObserveVolume(3, muted: false, available: true, mixerOpen: false, nowMs: 400);
        presence.ObserveVolume(9, muted: false, available: true, mixerOpen: false, nowMs: 500);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.Equal(IslandSatellite.None, presence.View.FirstSatellite);
        presence.ClickSatellite(IslandSatellite.Clipboard, 600);
        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        long hudEnd = 500 + IslandPresence.HudHoldMs;
        presence.Tick(hudEnd);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
        Assert.Null(presence.NextDeadlineMs);
        presence.Tick(hudEnd + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        var playing = new IslandPresence();
        playing.SetClipboardSurface(available: false, nowMs: 0);
        playing.ObserveMedia(Song("Dawn", playing: true), 10);
        Assert.Equal(IslandPillFace.NowPlaying, playing.View.Face);
        Assert.Equal(IslandSatellite.None, playing.View.FirstSatellite);
        Assert.Equal(IslandSatellite.None, playing.View.SecondSatellite);

        playing.ObserveVolume(5, muted: false, available: true, mixerOpen: false, nowMs: 20);
        playing.ObserveVolume(15, muted: false, available: true, mixerOpen: false, nowMs: 30);
        Assert.Equal(IslandPillFace.Volume, playing.View.Face);
        Assert.Equal(IslandSatellite.Music, playing.View.FirstSatellite);
        Assert.Equal(IslandSatellite.None, playing.View.SecondSatellite);
        playing.ClickSatellite(IslandSatellite.Clipboard, 40);
        Assert.Equal(IslandPanelBody.None, playing.View.Panel);
        playing.ClickSatellite(IslandSatellite.Music, 50);
        Assert.Equal(IslandPanelBody.Music, playing.View.Panel);
        Assert.True(playing.View.Pinned);
        Assert.Equal(IslandPillFace.NowPlaying, playing.View.Face);

        var transient = new IslandPresence();
        transient.SetClipboardSurface(available: false, nowMs: 0);
        transient.SetMusicMode(IslandMusicMode.Transient, 0);
        transient.ObserveMedia(Song("Dawn", playing: false), 0);
        transient.ObserveMedia(Song("Dawn", playing: true), 100);
        Assert.Equal(IslandPanelBody.Music, transient.View.Panel);
        transient.Click(150);
        Assert.True(transient.View.Pinned);
        Assert.Equal(IslandPanelBody.Music, transient.View.Panel);
    }

    [Fact]
    public void Hover_OpensAfterDelay_AndLeaveBeforeDelayDoesNot()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(100);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
        Assert.Equal(100 + IslandPresence.HoverOpenDelayMs, presence.NextDeadlineMs);

        presence.Tick(100 + IslandPresence.HoverOpenDelayMs - 1);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        presence.Tick(100 + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.False(presence.View.Pinned);

        var left = new IslandPresence();
        left.PointerEnterPill(0);
        left.PointerLeavePill(50);
        left.Tick(IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.None, left.View.Panel);
    }

    [Fact]
    public void SamplePointer_OverPillTwice_DoesNotPushTheHoverOpenDeadline()
    {
        var presence = new IslandPresence();
        presence.SamplePointer(overPill: true, overPanel: false, nowMs: 100);
        Assert.Equal(100 + IslandPresence.HoverOpenDelayMs, presence.NextDeadlineMs);

        // The hover watch samples repeatedly while the pointer rests: only the entry may arm.
        presence.SamplePointer(overPill: true, overPanel: false, nowMs: 160);
        presence.SamplePointer(overPill: true, overPanel: false, nowMs: 220);
        Assert.Equal(100 + IslandPresence.HoverOpenDelayMs, presence.NextDeadlineMs);

        presence.Tick(100 + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
    }

    [Fact]
    public void SamplePointer_FakePillLeaveCorrectedByPanelPad_KeepsHoverPanelOpen()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(100);
        long openAt = 100 + IslandPresence.HoverOpenDelayMs;
        presence.Tick(openAt);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);

        // The popup window takes the pointer: a pill leave arrives while the cursor sits on
        // the panel's transparent pad. The geometry sample then proves it is still on the pad.
        presence.SamplePointer(overPill: false, overPanel: false, nowMs: openAt + 10);
        Assert.NotNull(presence.NextDeadlineMs);
        presence.SamplePointer(overPill: false, overPanel: true, nowMs: openAt + 20);
        Assert.Null(presence.NextDeadlineMs);

        presence.Tick(openAt + IslandPresence.HoverGraceMs + 500);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.False(presence.View.Pinned);
    }

    [Fact]
    public void SamplePointer_GenuineLeave_ClosesAfterTheNormalGrace()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(0);
        long openAt = IslandPresence.HoverOpenDelayMs;
        presence.Tick(openAt);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);

        presence.SamplePointer(overPill: false, overPanel: false, nowMs: openAt);
        Assert.Equal(openAt + IslandPresence.HoverGraceMs, presence.NextDeadlineMs);
        presence.Tick(openAt + IslandPresence.HoverGraceMs - 1);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);

        presence.Tick(openAt + IslandPresence.HoverGraceMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
    }

    [Fact]
    public void SamplePointer_ReenterBeforeTheHoverCloseDeadline_CancelsIt()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(0);
        long openAt = IslandPresence.HoverOpenDelayMs;
        presence.Tick(openAt);
        presence.SamplePointer(overPill: false, overPanel: false, nowMs: openAt);
        Assert.NotNull(presence.NextDeadlineMs);

        presence.SamplePointer(overPill: true, overPanel: false, nowMs: openAt + 100);
        Assert.Null(presence.NextDeadlineMs);
        presence.Tick(openAt + IslandPresence.HoverGraceMs + 1000);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.False(presence.View.Pinned);
    }

    [Fact]
    public void SamplePointer_PinnedPanelIsNotClosedByAGenuineLeave()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(0);
        long openAt = IslandPresence.HoverOpenDelayMs;
        presence.Tick(openAt);
        presence.Click(openAt + 10);
        Assert.True(presence.View.Pinned);

        presence.SamplePointer(overPill: false, overPanel: false, nowMs: openAt + 20);
        Assert.Null(presence.NextDeadlineMs);
        presence.Tick(openAt + IslandPresence.HoverGraceMs + 1000);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);
    }

    [Fact]
    public void Click_DuringHoverDelay_ActsImmediately()
    {
        var presence = new IslandPresence();
        presence.PointerEnterPill(0);
        presence.Click(40);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);

        presence.Tick(IslandPresence.HoverOpenDelayMs);
        Assert.True(presence.View.Pinned);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);

        var playing = new IslandPresence();
        playing.ObserveMedia(Song("Dawn", playing: true), 0);
        playing.PointerEnterPill(10);
        playing.Click(20);
        Assert.Equal(IslandPanelBody.Music, playing.View.Panel);
        Assert.True(playing.View.Pinned);
        playing.Tick(10 + IslandPresence.HoverOpenDelayMs);
        Assert.True(playing.View.Pinned);
        Assert.Equal(IslandPanelBody.Music, playing.View.Panel);
    }

    [Fact]
    public void HudExpiry_WhilePointerOver_ArmsHoverDelay()
    {
        var presence = new IslandPresence();
        presence.ObserveMedia(Song("Dawn", playing: true), 0);
        presence.ObserveVolume(8, muted: false, available: true, mixerOpen: false, nowMs: 0);
        presence.ObserveVolume(18, muted: false, available: true, mixerOpen: false, nowMs: 100);

        presence.PointerEnterPill(200);
        presence.Tick(200 + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);

        long hudEnd = 100 + IslandPresence.HudHoldMs;
        presence.Tick(hudEnd);
        Assert.Equal(IslandPillFace.NowPlaying, presence.View.Face);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
        Assert.Equal(hudEnd + IslandPresence.HoverOpenDelayMs, presence.NextDeadlineMs);

        presence.Tick(hudEnd + IslandPresence.HoverOpenDelayMs - 1);
        Assert.Equal(IslandPanelBody.None, presence.View.Panel);
        presence.Tick(hudEnd + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.Music, presence.View.Panel);
        Assert.False(presence.View.Pinned);

        var idle = new IslandPresence();
        idle.ObserveVolume(1, muted: false, available: true, mixerOpen: false, nowMs: 0);
        idle.ObserveVolume(2, muted: false, available: true, mixerOpen: false, nowMs: 10);
        idle.PointerEnterPill(20);
        long end = 10 + IslandPresence.HudHoldMs;
        idle.Tick(end);
        Assert.Equal(IslandPillFace.Idle, idle.View.Face);
        Assert.Equal(IslandPanelBody.None, idle.View.Panel);
        Assert.Equal(end + IslandPresence.HoverOpenDelayMs, idle.NextDeadlineMs);
        idle.Tick(end + IslandPresence.HoverOpenDelayMs);
        Assert.Equal(IslandPanelBody.Clipboard, idle.View.Panel);
        Assert.False(idle.View.Pinned);
    }

    [Fact]
    public void Idle_LastClip_UsesPreviewTitle()
    {
        var presence = new IslandPresence();
        presence.SetIdlePreview("  pasted line  ", 0);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(string.Empty, presence.View.Title);
        Assert.Equal(IslandSatellite.None, presence.View.FirstSatellite);
        Assert.Null(presence.NextDeadlineMs);

        presence.SetIdleLabel(IslandIdleLabel.LastClip, 10);
        Assert.Equal("pasted line", presence.View.Title);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);

        presence.SetIdlePreview("   ", 20);
        Assert.Equal(string.Empty, presence.View.Title);

        presence.SetIdlePreview("again", 30);
        Assert.Equal("again", presence.View.Title);

        presence.SetIdleLabel(IslandIdleLabel.ClipboardName, 40);
        Assert.Equal(string.Empty, presence.View.Title);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
    }

    [Fact]
    public void PinnedPanel_SurvivesVolumeHud()
    {
        var presence = new IslandPresence();
        presence.Click(0);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);

        presence.ObserveVolume(10, muted: false, available: true, mixerOpen: false, nowMs: 0);
        presence.ObserveVolume(25, muted: false, available: true, mixerOpen: false, nowMs: 100);

        Assert.Equal(IslandPillFace.Volume, presence.View.Face);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);

        presence.Tick(100 + IslandPresence.HudHoldMs);
        Assert.Equal(IslandPillFace.Idle, presence.View.Face);
        Assert.Equal(IslandPanelBody.Clipboard, presence.View.Panel);
        Assert.True(presence.View.Pinned);
    }
}
