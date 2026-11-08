using WharfBar.Models;
using WharfBar.Resources;

namespace WharfBar.Services;

/// <summary>
/// How a media session occupies the island. Soft config string
/// (<see cref="Models.IslandConfig.MusicPresence"/>); default is persistent.
/// </summary>
internal enum IslandMusicMode
{
    Persistent,
    Transient,
}

/// <summary>What the compact chip inside the 30 DIP bar is showing.</summary>
internal enum IslandPillFace
{
    Idle,
    NowPlaying,
    Volume,
    Brightness,
    Clip,
    Battery,
    Drop,

    /// <summary>Short notice when a paired Bluetooth device connects. The quick-settings tile stays.</summary>
    Bluetooth,

    /// <summary>Live camera preview inside the pill. Empty when capture is denied.</summary>
    Camera,
}

/// <summary>Glyph beside the chip text. At most two, and never the same kind twice.</summary>
internal enum IslandSatellite
{
    None,
    Clipboard,
    Music,
}

/// <summary>Idle chip text. An empty title means the window draws "Clipboard".</summary>
internal enum IslandIdleLabel
{
    ClipboardName,
    LastClip,
}

/// <summary>Body of the single popup under the chip. Never a second popup.</summary>
internal enum IslandPanelBody
{
    None,
    Music,
    Clipboard,
}

/// <summary>Click on a meter chip opens an existing flyout instead of the island panel.</summary>
internal enum IslandPillCommand
{
    None,
    OpenVolumeFlyout,
    OpenBrightnessFlyout,
}

/// <summary>Media facts the chip and panel need. No WPF types.</summary>
internal readonly record struct IslandMediaCue(
    bool HasSession,
    string? SessionId,
    string? Title,
    string? Artist,
    bool IsPlaying,
    string? PillTitle);

/// <summary>Resolved chip + panel. The window paints this; it does not re-decide the rules.</summary>
internal readonly record struct IslandPresenceView(
    IslandPillFace Face,
    string Title,
    int MeterPercent,
    bool MeterMuted,
    IslandPanelBody Panel,
    bool Pinned,
    bool ShowClipboardBack,
    IslandSatellite FirstSatellite = IslandSatellite.None,
    IslandSatellite SecondSatellite = IslandSatellite.None);

/// <summary>
/// Chip face, HUD preemption, satellite glyphs, and panel lifetime for the center island.
/// Callers pass a millisecond clock (<see cref="Environment.TickCount64"/>).
/// One deadline is exposed so the window can arm a single reset timer.
/// </summary>
internal sealed class IslandPresence
{
    internal const long HudHoldMs = 1600;

    internal const long TransientHoldMs = 2500;

    internal const long HoverGraceMs = 300;

    /// <summary>Pointer must rest on the pill this long before a hover panel opens.</summary>
    internal const long HoverOpenDelayMs = 150;

    /// <summary>Default percent. A sample that crosses from at or above the live threshold to below it raises one battery HUD.</summary>
    internal const int LowBatteryPercent = 20;

    /// <summary>Hard cap on the now-playing title, DIP.</summary>
    internal const double MaxTitleDip = 220;

    /// <summary>Below this, the title is dropped so the chip stays a glyph.</summary>
    internal const double MinTitleDip = 24;

    /// <summary>Gap kept between the centered chip and each side cluster, DIP.</summary>
    internal const double SideClearanceDip = 8;

    private IslandMusicMode _mode = IslandMusicMode.Persistent;
    private bool _modeSet;

    private bool _mediaSeeded;
    private bool _hasSession;
    private string _sessionId = string.Empty;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _pillTitle = string.Empty;
    private bool _isPlaying;

    private bool _volumeSeeded;
    private int _volume;
    private bool _volumeMuted;

    private bool _brightnessSeeded;
    private int _brightness;

    private bool _clipboardSurface = true;
    private IslandIdleLabel _idleLabel = IslandIdleLabel.ClipboardName;
    private string _idlePreview = string.Empty;

    private bool _batterySeeded;
    private bool _batteryCharging;
    private int _batteryPercent;
    private int _lowBatteryPercent = LowBatteryPercent;

    /// <summary>
    /// False after a low-battery HUD until the sample is back at or above the threshold,
    /// or <see cref="SetLowBatteryPercent"/> changes the line.
    /// </summary>
    private bool _lowEdgeArmed = true;

    private long? _hudUntil;
    private IslandPillFace _hudFace;
    private string _hudTitle = string.Empty;
    private bool _cameraOn;
    private string _cameraTitle = Strings.Island_Camera;
    private int _meter;
    private bool _hudMuted;

    private IslandPanelBody _panel;
    private bool _pinned;
    private bool _clipboardFromMusic;
    private bool _hoverArmed;
    private bool _pointerOverPill;
    private bool _pointerOverPanel;
    private long? _hoverOpenAt;
    private long? _hoverCloseAt;
    private long? _transientUntil;

    internal IslandPresence()
    {
        Publish();
    }

    internal IslandPresenceView View { get; private set; }

    internal long? NextDeadlineMs { get; private set; }

    internal void SetMusicMode(IslandMusicMode mode, long nowMs)
    {
        if (_modeSet && _mode == mode)
        {
            return;
        }

        _modeSet = true;
        Expire(nowMs);
        _mode = mode;
        Publish();
    }

    internal void SetClipboardSurface(bool available, long nowMs)
    {
        _clipboardSurface = available;
        Expire(nowMs);
        if (_hoverOpenAt is not null && !CanHoverOpen())
        {
            _hoverOpenAt = null;
        }

        Publish();
    }

    /// <summary>
    /// Live low-battery line. Accepted percents are clamped to 5–50.
    /// A change re-arms the edge so the next downward crossing can fire once;
    /// it does not raise a HUD by itself.
    /// </summary>
    internal void SetLowBatteryPercent(int percent, long nowMs)
    {
        int next = IslandNormalizer.ClampLowBatteryPercent(percent);
        Expire(nowMs);
        if (next == _lowBatteryPercent)
        {
            Publish();
            return;
        }

        _lowBatteryPercent = next;
        _lowEdgeArmed = true;
        Publish();
    }

    internal void SetIdleLabel(IslandIdleLabel label, long nowMs)
    {
        Expire(nowMs);
        _idleLabel = label;
        Publish();
    }

    internal void SetIdlePreview(string? preview, long nowMs)
    {
        Expire(nowMs);
        _idlePreview = string.IsNullOrWhiteSpace(preview) ? string.Empty : preview.Trim();
        Publish();
    }

    internal void ObserveMedia(IslandMediaCue cue, long nowMs)
    {
        Expire(nowMs);

        string session = cue.SessionId ?? string.Empty;
        string title = cue.Title ?? string.Empty;
        string artist = cue.Artist ?? string.Empty;
        string pill = string.IsNullOrWhiteSpace(cue.PillTitle) ? title : cue.PillTitle.Trim();

        bool started = _mediaSeeded && !_hasSession && cue.HasSession;
        bool track = _mediaSeeded && cue.HasSession
            && (session != _sessionId || title != _title || artist != _artist);
        bool playback = _mediaSeeded && cue.HasSession && cue.IsPlaying != _isPlaying;

        _mediaSeeded = true;
        _hasSession = cue.HasSession;
        _sessionId = session;
        _title = title;
        _artist = artist;
        _pillTitle = pill;
        _isPlaying = cue.IsPlaying;

        if (!cue.HasSession)
        {
            if (_panel == IslandPanelBody.Music)
            {
                ClosePanel();
            }
            else if (_clipboardFromMusic)
            {
                _clipboardFromMusic = false;
            }

            Publish();
            return;
        }

        if (_mode == IslandMusicMode.Transient && (started || track || playback))
        {
            OpenTransientMusic(nowMs);
        }

        Publish();
    }

    internal void ObserveVolume(int percent, bool muted, bool available, bool mixerOpen, long nowMs)
    {
        Expire(nowMs);

        if (!available)
        {
            if (_hudUntil is not null && _hudFace == IslandPillFace.Volume)
            {
                ClearHud();
            }

            _volumeSeeded = false;
            Publish();
            return;
        }

        percent = Math.Clamp(percent, 0, 100);
        if (!_volumeSeeded)
        {
            _volumeSeeded = true;
            _volume = percent;
            _volumeMuted = muted;
            Publish();
            return;
        }

        bool changed = percent != _volume || muted != _volumeMuted;
        _volume = percent;
        _volumeMuted = muted;

        if (mixerOpen)
        {
            if (_hudUntil is not null && _hudFace == IslandPillFace.Volume)
            {
                ClearHud();
            }

            Publish();
            return;
        }

        if (changed)
        {
            StartHud(IslandPillFace.Volume, percent, muted, nowMs);
        }

        Publish();
    }

    internal void ObserveBrightness(int percent, bool brightnessUiOpen, long nowMs)
    {
        Expire(nowMs);
        percent = Math.Clamp(percent, 0, 100);

        if (!_brightnessSeeded)
        {
            _brightnessSeeded = true;
            _brightness = percent;
            Publish();
            return;
        }

        bool changed = percent != _brightness;
        _brightness = percent;

        if (brightnessUiOpen)
        {
            if (_hudUntil is not null && _hudFace == IslandPillFace.Brightness)
            {
                ClearHud();
            }

            Publish();
            return;
        }

        if (changed)
        {
            StartHud(IslandPillFace.Brightness, percent, muted: false, nowMs);
        }

        Publish();
    }

    /// <summary>First sample seeds. Later samples HUD on plug-in or on one downward cross of the live threshold.</summary>
    internal void ObserveBattery(bool present, bool charging, int percent, long nowMs)
    {
        Expire(nowMs);

        if (!present)
        {
            if (_hudUntil is not null && _hudFace == IslandPillFace.Battery)
            {
                ClearHud();
            }

            _batterySeeded = false;
            _lowEdgeArmed = true;
            Publish();
            return;
        }

        percent = Math.Clamp(percent, 0, 100);
        if (!_batterySeeded)
        {
            _batterySeeded = true;
            _batteryCharging = charging;
            _batteryPercent = percent;
            Publish();
            return;
        }

        bool pluggedIn = !_batteryCharging && charging;
        bool crossedLow = _lowEdgeArmed
            && _batteryPercent >= _lowBatteryPercent
            && percent < _lowBatteryPercent;
        _batteryCharging = charging;
        _batteryPercent = percent;

        if (percent >= _lowBatteryPercent)
        {
            _lowEdgeArmed = true;
        }

        if (crossedLow)
        {
            _lowEdgeArmed = false;
        }

        if (pluggedIn || crossedLow)
        {
            StartHud(IslandPillFace.Battery, percent, muted: false, nowMs);
        }

        Publish();
    }

    /// <summary>Shows <paramref name="name"/> on the pill until the transient hold ends.</summary>
    internal void NoteBluetooth(string? name, long nowMs)
    {
        Expire(nowMs);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        StartHud(IslandPillFace.Bluetooth, 0, muted: false, nowMs, name.Trim());
        _hudUntil = nowMs + TransientHoldMs;
        Publish();
    }

    /// <summary>Keeps the camera face up while <paramref name="on"/> is true. A HUD still preempts it.</summary>
    internal void SetCamera(bool on, string? title, long nowMs)
    {
        Expire(nowMs);
        _cameraOn = on;
        _cameraTitle = string.IsNullOrWhiteSpace(title) ? Strings.Island_Camera : title.Trim();
        Publish();
    }

    internal void NoteNewClip(string? preview, long nowMs)
    {
        Expire(nowMs);
        if (!_clipboardSurface || string.IsNullOrWhiteSpace(preview))
        {
            Publish();
            return;
        }

        StartHud(IslandPillFace.Clip, 0, muted: false, nowMs, preview.Trim());
        Publish();
    }

    internal void NoteFileDrop(string? displayName, long nowMs)
    {
        Expire(nowMs);
        if (!_clipboardSurface || string.IsNullOrWhiteSpace(displayName))
        {
            Publish();
            return;
        }

        StartHud(IslandPillFace.Drop, 0, muted: false, nowMs, displayName.Trim());
        Publish();
    }

    internal IslandPillCommand Click(long nowMs)
    {
        Expire(nowMs);
        _hoverOpenAt = null;
        IslandPillFace face = CurrentFace();

        if (face == IslandPillFace.Volume)
        {
            ClearHud();
            Publish();
            return IslandPillCommand.OpenVolumeFlyout;
        }

        if (face == IslandPillFace.Brightness)
        {
            ClearHud();
            Publish();
            return IslandPillCommand.OpenBrightnessFlyout;
        }

        if (face is IslandPillFace.Clip or IslandPillFace.Drop)
        {
            if (_clipboardSurface)
            {
                ClearHud();
                OpenClipboard(fromMusic: false);
            }

            Publish();
            return IslandPillCommand.None;
        }

        if (face == IslandPillFace.Battery || face == IslandPillFace.Bluetooth)
        {
            ClearHud();
            Publish();
            return IslandPillCommand.None;
        }

        if (_panel == IslandPanelBody.None)
        {
            if (face == IslandPillFace.NowPlaying)
            {
                OpenPinnedMusic();
            }
            else if (face == IslandPillFace.Idle && _clipboardSurface)
            {
                OpenClipboard(fromMusic: false);
            }

            Publish();
            return IslandPillCommand.None;
        }

        if (!_pinned)
        {
            Pin();
            Publish();
            return IslandPillCommand.None;
        }

        ClosePanel();
        Publish();
        return IslandPillCommand.None;
    }

    internal IslandPillCommand ClickSatellite(IslandSatellite satellite, long nowMs)
    {
        Expire(nowMs);

        if (satellite == IslandSatellite.Music && _hasSession)
        {
            _hoverOpenAt = null;
            ClearHud();
            OpenPinnedMusic();
        }
        else if (satellite == IslandSatellite.Clipboard && _clipboardSurface)
        {
            _hoverOpenAt = null;
            ClearHud();
            OpenClipboard(fromMusic: false);
        }

        Publish();
        return IslandPillCommand.None;
    }

    internal void PointerEnterPill(long nowMs)
    {
        Expire(nowMs);
        _pointerOverPill = true;
        _hoverCloseAt = null;

        if (_panel == IslandPanelBody.None && IsHoverFace(CurrentFace()))
        {
            _hoverOpenAt = nowMs + HoverOpenDelayMs;
        }

        Publish();
    }

    internal void PointerLeavePill(long nowMs)
    {
        _pointerOverPill = false;
        _hoverOpenAt = null;
        Expire(nowMs);
        ArmHoverClose(nowMs);
        Publish();
    }

    internal void PointerEnterPanel(long nowMs)
    {
        Expire(nowMs);
        _pointerOverPanel = true;
        _hoverCloseAt = null;
        Publish();
    }

    internal void PointerLeavePanel(long nowMs)
    {
        Expire(nowMs);
        _pointerOverPanel = false;
        ArmHoverClose(nowMs);
        Publish();
    }

    internal void ShowClipboard(long nowMs)
    {
        Expire(nowMs);
        if (_panel != IslandPanelBody.Music)
        {
            Publish();
            return;
        }

        _panel = IslandPanelBody.Clipboard;
        _clipboardFromMusic = true;
        _pinned = true;
        _hoverArmed = false;
        _hoverCloseAt = null;
        _transientUntil = null;
        Publish();
    }

    internal void BackToMusic(long nowMs)
    {
        Expire(nowMs);
        if (_panel != IslandPanelBody.Clipboard || !_clipboardFromMusic)
        {
            Publish();
            return;
        }

        if (!_hasSession)
        {
            _clipboardFromMusic = false;
            Publish();
            return;
        }

        _panel = IslandPanelBody.Music;
        _clipboardFromMusic = false;
        _pinned = true;
        _hoverArmed = false;
        _hoverCloseAt = null;
        _transientUntil = null;
        Publish();
    }

    internal void Dismiss(long nowMs)
    {
        Expire(nowMs);
        if (_panel != IslandPanelBody.None)
        {
            ClosePanel();
        }

        Publish();
    }

    internal void Tick(long nowMs)
    {
        Expire(nowMs);
        Publish();
    }

    /// <summary>
    /// Max DIP for the now-playing title. The chip is centered on the bar, so the
    /// narrower side (left stats or right strip) limits it. <paramref name="chromeDip"/>
    /// is everything in the chip except the title text (padding, glyph, margins).
    /// </summary>
    internal static double TitleMaxWidth(
        double barDip,
        double leftDip,
        double rightDip,
        double chromeDip,
        double desiredDip)
    {
        if (desiredDip < 0)
        {
            desiredDip = 0;
        }

        if (barDip <= 0)
        {
            return Math.Min(desiredDip, MaxTitleDip);
        }

        double half = barDip / 2d;
        double leftRoom = half - leftDip - SideClearanceDip;
        double rightRoom = half - rightDip - SideClearanceDip;
        double titleRoom = (2d * Math.Min(leftRoom, rightRoom)) - chromeDip;
        if (titleRoom < MinTitleDip)
        {
            return 0;
        }

        return Math.Min(desiredDip, Math.Min(titleRoom, MaxTitleDip));
    }

    private void OpenHoverMusic()
    {
        _panel = IslandPanelBody.Music;
        _pinned = false;
        _clipboardFromMusic = false;
        _hoverArmed = true;
        _transientUntil = null;
        _hoverCloseAt = null;
    }

    private void OpenHoverClipboard()
    {
        _panel = IslandPanelBody.Clipboard;
        _pinned = false;
        _clipboardFromMusic = false;
        _hoverArmed = true;
        _transientUntil = null;
        _hoverCloseAt = null;
    }

    private void OpenPinnedMusic()
    {
        _panel = IslandPanelBody.Music;
        _pinned = true;
        _clipboardFromMusic = false;
        _hoverArmed = false;
        _hoverCloseAt = null;
        _transientUntil = null;
    }

    private void OpenTransientMusic(long nowMs)
    {
        if (_panel == IslandPanelBody.Clipboard)
        {
            return;
        }

        if (_pinned && _panel != IslandPanelBody.None)
        {
            return;
        }

        _panel = IslandPanelBody.Music;
        _pinned = false;
        _clipboardFromMusic = false;
        _hoverArmed = false;
        _hoverCloseAt = null;
        _transientUntil = nowMs + TransientHoldMs;
    }

    private void OpenClipboard(bool fromMusic)
    {
        _panel = IslandPanelBody.Clipboard;
        _clipboardFromMusic = fromMusic;
        _pinned = true;
        _hoverArmed = false;
        _hoverCloseAt = null;
        _transientUntil = null;
    }

    private void Pin()
    {
        _pinned = true;
        _hoverArmed = false;
        _hoverCloseAt = null;
        _transientUntil = null;
    }

    private void ClosePanel()
    {
        _panel = IslandPanelBody.None;
        _pinned = false;
        _clipboardFromMusic = false;
        _hoverArmed = false;
        _hoverCloseAt = null;
        _transientUntil = null;
    }

    private void StartHud(IslandPillFace face, int percent, bool muted, long nowMs, string? title = null)
    {
        _hudFace = face;
        _meter = percent;
        _hudMuted = muted;
        _hudTitle = title ?? string.Empty;
        _hudUntil = nowMs + HudHoldMs;
        _hoverOpenAt = null;
    }

    private void ClearHud()
    {
        _hudUntil = null;
        _hudMuted = false;
    }

    private void ArmHoverClose(long nowMs)
    {
        if (_hoverArmed && !_pinned && _panel != IslandPanelBody.None && !_pointerOverPill && !_pointerOverPanel)
        {
            _hoverCloseAt = nowMs + HoverGraceMs;
            return;
        }

        _hoverCloseAt = null;
    }

    private void Expire(long nowMs)
    {
        bool hudJustExpired = false;
        if (_hudUntil is long hud && nowMs >= hud)
        {
            ClearHud();
            hudJustExpired = true;
        }

        if (_hoverCloseAt is long hover && nowMs >= hover)
        {
            _hoverCloseAt = null;
            if (_hoverArmed && !_pinned && _panel != IslandPanelBody.None && !_pointerOverPill && !_pointerOverPanel)
            {
                ClosePanel();
            }
        }

        if (_transientUntil is long transientAt && nowMs >= transientAt)
        {
            _transientUntil = null;
            if (!_pinned && _panel == IslandPanelBody.Music)
            {
                ClosePanel();
            }
        }

        if (hudJustExpired && CanHoverOpen())
        {
            _hoverOpenAt = nowMs + HoverOpenDelayMs;
            return;
        }

        if (_hoverOpenAt is long openAt && nowMs >= openAt)
        {
            _hoverOpenAt = null;
            if (CanHoverOpen())
            {
                if (CurrentFace() == IslandPillFace.NowPlaying)
                {
                    OpenHoverMusic();
                }
                else
                {
                    OpenHoverClipboard();
                }
            }
        }
    }

    private bool CanHoverOpen()
        => _pointerOverPill && _panel == IslandPanelBody.None && IsHoverFace(CurrentFace());

    private bool IsHoverFace(IslandPillFace face)
    {
        if (face == IslandPillFace.NowPlaying)
        {
            return true;
        }

        return face == IslandPillFace.Idle && _clipboardSurface;
    }

    private IslandPillFace CurrentFace()
    {
        if (_hudUntil is not null)
        {
            return _hudFace;
        }

        if (_cameraOn)
        {
            return IslandPillFace.Camera;
        }

        if (_mode == IslandMusicMode.Persistent && _hasSession)
        {
            return IslandPillFace.NowPlaying;
        }

        return IslandPillFace.Idle;
    }

    private string ResolveTitle(IslandPillFace face)
    {
        if (face == IslandPillFace.NowPlaying)
        {
            return _pillTitle;
        }

        if (face is IslandPillFace.Clip or IslandPillFace.Drop or IslandPillFace.Bluetooth)
        {
            return _hudTitle;
        }

        if (face == IslandPillFace.Camera)
        {
            return _cameraTitle;
        }

        if (face == IslandPillFace.Idle
            && _idleLabel == IslandIdleLabel.LastClip
            && _idlePreview.Length > 0)
        {
            return _idlePreview;
        }

        return string.Empty;
    }

    /// <summary>
    /// Now-playing keeps a clipboard satellite only. A HUD can show persistent music
    /// and, on the primary bar, clipboard beside it. Transient music is not a satellite.
    /// Idle is empty. The two slots are never the same kind.
    /// </summary>
    private void ResolveSatellites(IslandPillFace face, out IslandSatellite first, out IslandSatellite second)
    {
        first = IslandSatellite.None;
        second = IslandSatellite.None;
        bool music = _mode == IslandMusicMode.Persistent && _hasSession;
        bool clipboard = _clipboardSurface;

        if (face == IslandPillFace.NowPlaying)
        {
            if (clipboard)
            {
                first = IslandSatellite.Clipboard;
            }

            return;
        }

        if (face is not (
            IslandPillFace.Volume
            or IslandPillFace.Brightness
            or IslandPillFace.Clip
            or IslandPillFace.Battery
            or IslandPillFace.Drop
            or IslandPillFace.Bluetooth
            or IslandPillFace.Camera))
        {
            return;
        }

        if (music)
        {
            first = IslandSatellite.Music;
        }

        if (clipboard && first != IslandSatellite.Clipboard)
        {
            if (first == IslandSatellite.None)
            {
                first = IslandSatellite.Clipboard;
            }
            else
            {
                second = IslandSatellite.Clipboard;
            }
        }
    }

    private void Publish()
    {
        IslandPillFace face = CurrentFace();
        bool meter = face is IslandPillFace.Volume or IslandPillFace.Brightness or IslandPillFace.Battery;
        ResolveSatellites(face, out IslandSatellite first, out IslandSatellite second);
        View = new IslandPresenceView(
            face,
            ResolveTitle(face),
            meter ? _meter : 0,
            face == IslandPillFace.Volume && _hudMuted,
            _panel,
            _pinned && _panel != IslandPanelBody.None,
            _panel == IslandPanelBody.Clipboard && _clipboardFromMusic && _hasSession,
            first,
            second);
        NextDeadlineMs = ComputeDeadline();
    }

    private long? ComputeDeadline()
    {
        long? next = null;
        Consider(ref next, _hudUntil);
        Consider(ref next, _hoverOpenAt);
        Consider(ref next, _hoverCloseAt);
        Consider(ref next, _transientUntil);
        return next;
    }

    private static void Consider(ref long? next, long? candidate)
    {
        if (candidate is not long value)
        {
            return;
        }

        if (next is null || value < next)
        {
            next = value;
        }
    }
}
