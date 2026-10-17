using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Resources;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class TopBarWindow
{
    private const double IslandIdleDip = 108;
    private const double IslandHoverDip = 4;
    private const double IslandPadDip = 32;
    private const double IslandGlyphDip = 16;
    private const double IslandMeterDip = 72;
    // Centered slot outside the title: pill margin 24+24, padding 16+16, glyph, title gap.
    private const double IslandTitleChromeDip = 104;
    /// <summary>Margin 8 + five 3 DIP bars with 2 DIP gaps.</summary>
    private const double IslandBarsDip = 28;

    /// <summary>Grid margin on each side. Counted so the title ellipsizes before the chip meets a cluster.</summary>
    private const double IslandContentInsetDip = 10;
    private const double IslandSatelliteDip = 24;
    private const string GlyphClipboard = "\uE8C8";
    private const string GlyphMusic = "\uE90C";
    private const string GlyphBrightness = "\uE706";
    private const string GlyphDrop = "\uE8A5";
    private const string GlyphBluetooth = "\uE702";
    private const string GlyphCamera = "\uE722";

    private bool _islandClosePending;
    private bool _islandAwaitingReveal;
    private bool _islandMeasuring;
    private bool _islandRevealPosted;
    private IslandPresence? _presence;
    private WallpaperService? _wallpaper;
    private IReadOnlyList<BluetoothDeviceInfo>? _btPrevious;
    private bool _btSeeded;
    private DispatcherTimer? _vizTimer;
    private DispatcherTimer? _islandClock;
    private bool _islandFocusMusic;
    private IslandPanelBody _shownPanel;
    private bool _shownBack;
    private bool _islandPulseRunning;
    private bool _islandPressed;
    private IslandPillFace? _shownFace;
    private readonly double[] _barScales = new double[VisualizerBars.Count];
    private string? _islandTip;
    private string _faceTitle = string.Empty;
    private double _faceBar = double.NaN;
    private double _faceLeft = double.NaN;
    private double _faceRight = double.NaN;
    private double _titleWidth;
    private double _meterFill;
    private double _pillWidth = double.NaN;
    private double _faceSatellites;
    private double _faceBars;
    private bool _captureExcluded;
    private IslandShotWindow? _shot;
    private Panel? _pillHome;
    private int _pillHomeIndex;
    private FrameworkElement? _pillSlot;
    private bool _pillParked;
    private Thickness _pillMargin;
    private HorizontalAlignment _pillAlignH;
    private VerticalAlignment _pillAlignV;
    private bool _idleLabelSet;
    private int _appliedLowBattery = int.MinValue;
    private IslandSatellite _selectedSatellite = IslandSatellite.None;
    private IslandIdleLabel _appliedIdleLabel;
    private bool _idlePreviewSet;
    private string? _appliedIdlePreview;

    private static long IslandNow() => Environment.TickCount64;

    private void OnIslandPillEnter(object sender, MouseEventArgs e)
    {
        if (_presence is null)
        {
            return;
        }

        _presence.PointerEnterPill(IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void OnIslandPillLeave(object sender, MouseEventArgs e)
    {
        if (_presence is null)
        {
            return;
        }

        _presence.PointerLeavePill(IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void OnIslandPanelEnter(object sender, MouseEventArgs e)
    {
        if (_presence is null)
        {
            return;
        }

        _presence.PointerEnterPanel(IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void OnIslandPanelLeave(object sender, MouseEventArgs e)
    {
        if (_presence is null)
        {
            return;
        }

        _presence.PointerLeavePanel(IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void OnIslandPillClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ActivateIslandPill();
    }

    private void OnIslandPillPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
        {
            if (MoveSatelliteSelection(e.Key))
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Enter
            && _selectedSatellite != IslandSatellite.None
            && _presence is not null
            && Island is { Config.Enabled: true })
        {
            RunIslandCommand(_presence.ClickSatellite(_selectedSatellite, IslandNow()));
            return;
        }

        ActivateIslandPill();
    }

    /// <summary>
    /// Moves the pill selection across visible satellites. None is skipped.
    /// Returns false when the chip has no satellite, so the key is left alone.
    /// </summary>
    private bool MoveSatelliteSelection(Key key)
    {
        if (_presence is null || Island is not { Config.Enabled: true })
        {
            return false;
        }

        IslandSatellite[] visible = VisibleSatellites(_presence.View);
        if (visible.Length == 0)
        {
            return false;
        }

        int index = Array.IndexOf(visible, _selectedSatellite);
        if (key == Key.Right)
        {
            index = index < 0 ? 0 : Math.Min(visible.Length - 1, index + 1);
        }
        else
        {
            index = index < 0 ? visible.Length - 1 : Math.Max(0, index - 1);
        }

        _selectedSatellite = visible[index];
        ApplySatellites(_presence.View);
        return true;
    }

    private static IslandSatellite[] VisibleSatellites(IslandPresenceView snap)
    {
        if (snap.FirstSatellite == IslandSatellite.None)
        {
            return [];
        }

        if (snap.SecondSatellite == IslandSatellite.None
            || snap.SecondSatellite == snap.FirstSatellite)
        {
            return [snap.FirstSatellite];
        }

        return [snap.FirstSatellite, snap.SecondSatellite];
    }

    private void OnIslandFirstSatelliteClick(object sender, MouseButtonEventArgs e)
        => ActivateSatellite(first: true, e);

    private void OnIslandSecondSatelliteClick(object sender, MouseButtonEventArgs e)
        => ActivateSatellite(first: false, e);

    private void ActivateIslandPill()
    {
        if (_presence is null || Island is not { Config.Enabled: true })
        {
            return;
        }

        RunIslandCommand(_presence.Click(IslandNow()));
    }

    /// <summary>
    /// Uydu tıklaması hapın kendisine de düşmesin diye <see cref="MouseButtonEventArgs.Handled"/> işaretlenir.
    /// </summary>
    private void ActivateSatellite(bool first, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_presence is null || Island is not { Config.Enabled: true })
        {
            return;
        }

        IslandSatellite satellite = first ? _presence.View.FirstSatellite : _presence.View.SecondSatellite;
        if (satellite == IslandSatellite.None)
        {
            return;
        }

        RunIslandCommand(_presence.ClickSatellite(satellite, IslandNow()));
    }

    private void RunIslandCommand(IslandPillCommand command)
    {
        if (_presence is null)
        {
            return;
        }

        switch (command)
        {
            case IslandPillCommand.OpenVolumeFlyout:
                CloseIsland(animate: false);
                ToggleQuickSettings(QuickSettingsPage.Mixer, IslandPill);
                ApplyIslandChrome(focusMusic: false);
                return;
            case IslandPillCommand.OpenBrightnessFlyout:
                CloseIsland(animate: false);
                OpenBrightnessFromPill();
                ApplyIslandChrome(focusMusic: false);
                return;
        }

        bool focus = _presence.View.Pinned && _presence.View.Panel != IslandPanelBody.None;
        ApplyIslandChrome(focus);
    }

    /// <summary>Soft-saves the chosen hub tab (no schema bump). Primary monitor only.</summary>
    private void OnIslandHubTabChanged(object? sender, IslandHubTab tab)
    {
        if (!Monitor.IsPrimary)
        {
            return;
        }

        IslandConfig cfg = _config.Current.Island;
        string value = IslandHub.ToConfig(tab);
        if (string.Equals(cfg.HubTab, value, StringComparison.Ordinal))
        {
            return;
        }

        cfg.HubTab = value;
        _config.Save(_config.Current);
    }

    private IslandHubTab ResolveHubTab(bool musicAvailable)
        => Monitor.IsPrimary
            ? IslandHub.Resolve(_config.Current.Island.HubTab, musicAvailable)
            : IslandHubTab.Music;

    /// <summary>
    /// Session ended while the Music tab is showing: fall back to the saved tab (Clipboard).
    /// </summary>
    private void FallBackFromMusicTab()
    {
        if (!IslandPopup.IsOpen
            || IslandHost.CurrentHubTab != IslandHubTab.Music
            || _engine.Media.Snapshot.HasSession
            || !Monitor.IsPrimary)
        {
            return;
        }

        IslandHost.PresentHub(ResolveHubTab(musicAvailable: false), focus: false, crossfade: true);
    }

    private void OnIslandBarSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_presence is not null)
        {
            ApplyIslandFace(_presence.View);
        }
    }

    private void OnIslandClockTick(object? sender, EventArgs e)
    {
        _islandClock?.Stop();
        if (_presence is null)
        {
            return;
        }

        // Popup açılışı bazen hapta sahte MouseLeave üretir. İmleç hâlâ
        // hapta ya da paneldeyse kapanış süresini Tick'ten önce geri yükle.
        // Panel kapalıyken PointerEnter çağrılmaz: hover açılışı 150 ms'lik
        // süreye bağlı ve her tikte yeniden girmek o süreyi sıfırlardı.
        long now = IslandNow();
        bool panelOpen = _presence.View.Panel != IslandPanelBody.None;
        if (panelOpen)
        {
            if (IslandPill.IsMouseOver)
            {
                _presence.PointerEnterPill(now);
            }
            else if (IslandPopup.IsOpen && IslandHost.IsMouseOver)
            {
                _presence.PointerEnterPanel(now);
            }
        }

        _presence.Tick(now);
        if (panelOpen && IslandPill.IsMouseOver)
        {
            _presence.PointerEnterPill(IslandNow());
        }

        ApplyIslandChrome(focusMusic: false);
    }

    private void OnIslandBrightnessChanged(object? sender, EventArgs e) => NoteIslandBrightness(apply: true);

    private void NoteIslandVolume()
    {
        if (_presence is null)
        {
            return;
        }

        VolumeSnapshot snapshot = _engine.Volume.Snapshot;
        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.ObserveVolume(
            snapshot.Percent,
            snapshot.IsMuted,
            snapshot.IsAvailable,
            IslandMixerOwnsVolume(),
            IslandNow());
        if (before != _presence.View || deadline != _presence.NextDeadlineMs)
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    private void NoteIslandBrightness(bool apply)
    {
        if (_presence is null)
        {
            return;
        }

        if (_engine.MonitorBrightness.AnnouncedBrightness is not int percent)
        {
            return;
        }

        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.ObserveBrightness(percent, BrightnessPopup.IsOpen, IslandNow());
        if (apply && (before != _presence.View || deadline != _presence.NextDeadlineMs))
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    private void NoteIslandMedia(bool apply)
    {
        if (_presence is null)
        {
            return;
        }

        MediaSnapshot snapshot = _engine.Media.Snapshot;
        string pill = string.IsNullOrEmpty(snapshot.Title) ? snapshot.AppName : snapshot.Title;
        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.ObserveMedia(
            new IslandMediaCue(
                snapshot.HasSession,
                snapshot.SessionId,
                snapshot.Title,
                snapshot.Artist,
                snapshot.IsPlaying,
                pill),
            IslandNow());
        if (apply && (before != _presence.View || deadline != _presence.NextDeadlineMs))
        {
            ApplyIslandChrome(focusMusic: false);
        }

        FallBackFromMusicTab();
        ApplyIslandTint(_presence.View.Face);
        SyncVisualizerTimer();
    }

    private void NoteIslandBattery(bool apply)
    {
        if (_presence is null)
        {
            return;
        }

        StatusSnapshot snapshot = _engine.Status.Snapshot;
        IslandPresenceView before = _presence.View;
        long? deadline = _presence.NextDeadlineMs;
        _presence.ObserveBattery(
            snapshot.IsBatteryPresent,
            snapshot.IsBatteryCharging,
            snapshot.BatteryPercent,
            IslandNow());
        if (apply && (before != _presence.View || deadline != _presence.NextDeadlineMs))
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    private bool IslandMixerOwnsVolume()
        => QuickSettingsPopup.IsOpen && QuickSettingsHost.IsMixerPage;

    private void OpenBrightnessFromPill()
    {
        if (BrightnessPopup.IsOpen)
        {
            CloseBrightness();
            return;
        }

        CancelPendingClose(BrightnessHost, ref _brightnessClosePending);
        CloseQuickSettings();
        CloseMedia();
        BrightnessPopup.PlacementTarget = IslandPill;
        BrightnessHost.Refresh();
        BrightnessPopup.IsOpen = true;
    }

    private void ApplyIslandChrome(bool focusMusic)
    {
        if (_presence is null)
        {
            return;
        }

        IslandPresenceView snap = _presence.View;
        if (!SyncIslandVisibility(snap))
        {
            ApplyCaptureAffinity(snap, pillShown: false);
            SyncVisualizerTimer();
            return;
        }

        ApplyIslandFace(snap);
        SyncIslandPopup(snap, focusMusic);
        ApplyCaptureAffinity(snap, pillShown: true);
        ArmIslandClock();
        SyncVisualizerTimer();
    }

    private bool SyncIslandVisibility(IslandPresenceView snap)
    {
        if (!IslandShouldShow(snap))
        {
            if (IslandPill.Visibility != Visibility.Collapsed)
            {
                IslandPill.Visibility = Visibility.Collapsed;
            }

            _shownFace = null;

            if (IslandPopup.IsOpen)
            {
                CloseIslandPopupOnly(animate: false);
            }

            _islandClock?.Stop();
            return false;
        }

        if (IslandPill.Visibility != Visibility.Visible)
        {
            IslandPill.Visibility = Visibility.Visible;
        }

        return true;
    }

    /// <summary>
    /// Kapalı ada her barda çöker. Birincil, açıkken boşta da durur.
    /// İkincil yalnız müzik, sayaç veya açık panel varken görünür.
    /// </summary>
    private bool IslandShouldShow(IslandPresenceView snap)
    {
        if (Island is not { Config.Enabled: true })
        {
            return false;
        }

        if (Monitor.IsPrimary)
        {
            return true;
        }

        return snap.Face != IslandPillFace.Idle || snap.Panel != IslandPanelBody.None;
    }

    private string _islandColorMode = IslandConfig.DefaultColorMode;
    private Color? _lastPillTint; // null == theme accent resource state (initial XAML)

    /// <summary>Bars follow the tint; no tint restores the theme accent.</summary>
    private void ApplyIslandTint(IslandPillFace face)
    {
        MediaSnapshot snapshot = _engine.Media.Snapshot;
        bool showCover = face == IslandPillFace.NowPlaying && snapshot.Thumbnail is not null;
        IslandCoverImage.Source = showCover ? snapshot.Thumbnail : null;
        IslandCoverThumb.Visibility = showCover ? Visibility.Visible : Visibility.Collapsed;
        IslandGlyph.Visibility = showCover ? Visibility.Collapsed : Visibility.Visible;
        bool music = face == IslandPillFace.NowPlaying && snapshot.IsPlaying;
        Color? tint = IslandTintSource.Resolve(_islandColorMode, snapshot.Thumbnail, music);
        if (tint == _lastPillTint)
        {
            return;
        }

        _lastPillTint = tint;
        Border[] bars = [IslandBar0, IslandBar1, IslandBar2, IslandBar3, IslandBar4];
        if (tint is Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            foreach (Border bar in bars)
            {
                bar.Background = brush;
            }

            return;
        }

        foreach (Border bar in bars)
        {
            bar.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        }
    }

    private void PushColorMode(IslandConfig config)
    {
        _islandColorMode = IslandNormalizer.NormalizeColorMode(config.ColorMode);
        IslandHost.ColorMode = _islandColorMode;
        ApplyIslandTint(_presence?.View.Face ?? IslandPillFace.Idle);
    }

    private void ApplyIslandFace(IslandPresenceView snap)
    {
        ApplyIslandTint(snap.Face);
        IslandGlyph.Text = snap.Face switch
        {
            IslandPillFace.NowPlaying => GlyphMusic,
            IslandPillFace.Volume => VolumeHudGlyph(snap.MeterPercent, snap.MeterMuted),
            IslandPillFace.Brightness => GlyphBrightness,
            IslandPillFace.Battery => ResolveBatteryGlyph(_engine.Status.Snapshot),
            IslandPillFace.Drop => GlyphDrop,
            IslandPillFace.Bluetooth => GlyphBluetooth,
            IslandPillFace.Camera => GlyphCamera,
            _ => GlyphClipboard,
        };

        if (_shownFace != snap.Face)
        {
            bool first = _shownFace is null;
            _shownFace = snap.Face;
            if (!first)
            {
                // Face swap: content dips and settles, the glyph pops in slightly. Both finish on
                // their own (FillBehavior.Stop) and never run while the pill sits idle.
                if (IslandGlyph.RenderTransform is not ScaleTransform glyphScale)
                {
                    glyphScale = new ScaleTransform(1, 1);
                    IslandGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
                    IslandGlyph.RenderTransform = glyphScale;
                }

                FadeFrom(
                    IslandContent,
                    IslandMotion.ContentFromOpacity,
                    IslandMotion.Gate(IslandMotion.ContentFadeMs, SystemParameters.ClientAreaAnimation),
                    glyphScale,
                    0.8);
            }
        }

        string tip = snap.Face switch
        {
            IslandPillFace.NowPlaying => BuildMediaTooltip(_engine.Media.Snapshot),
            IslandPillFace.Volume => snap.MeterMuted ? Strings.Island_VolumeMuted : string.Format(CultureInfo.CurrentCulture, Strings.Island_VolumeFormat, snap.MeterPercent),
            IslandPillFace.Brightness => string.Format(CultureInfo.CurrentCulture, Strings.Island_BrightnessFormat, snap.MeterPercent),
            IslandPillFace.Battery => BatteryTip(snap.MeterPercent),
            IslandPillFace.Clip or IslandPillFace.Drop => string.IsNullOrWhiteSpace(snap.Title)
                ? Strings.Island_ClipboardAndDrops
                : snap.Title,
            IslandPillFace.Bluetooth => string.IsNullOrWhiteSpace(snap.Title) ? "Bluetooth" : snap.Title,
            IslandPillFace.Camera => snap.Title,
            _ => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_ClipboardAndDrops : snap.Title,
        };
        if (!string.Equals(tip, _islandTip, StringComparison.Ordinal))
        {
            _islandTip = tip;
            SetPlainToolTip(IslandPill, tip);
        }

        AutomationProperties.SetName(IslandPill, IslandAccessName(snap));

        double satelliteDip = ApplySatellites(snap);
        bool meter = snap.Face is IslandPillFace.Volume or IslandPillFace.Brightness or IslandPillFace.Battery;
        bool showTitle = false;
        double titleDip = 0;
        // Bars sit after the title; reserve them in the title budget before measuring.
        double barsDip = VisualizerBarsWanted(snap) ? IslandBarsDip : 0;

        if (meter)
        {
            ShowMeter();
            double fill = IslandMeterDip * Math.Clamp(snap.MeterPercent, 0, 100) / 100d;
            if (snap.Face == IslandPillFace.Volume && snap.MeterMuted)
            {
                fill = 0;
            }

            AnimateWidth(IslandMeterFill, FrameworkElement.WidthProperty, fill, ref _meterFill);
        }
        else
        {
            HideMeter();
            showTitle = TryShowTitle(snap, satelliteDip, barsDip, out titleDip);
            if (!showTitle)
            {
                ShowIdleChrome(snap.Face == IslandPillFace.Idle);
            }
        }

        double extras = ApplyIslandExtras(snap);
        double content = meter
            ? IslandPadDip + IslandGlyphDip + 8 + IslandMeterDip + satelliteDip
            : showTitle
                ? IslandPadDip + IslandGlyphDip + 8 + titleDip + extras + satelliteDip
                : IslandIdleDip + extras + satelliteDip;
        double hover = IslandPill.IsMouseOver ? IslandHoverDip : 0;
        AnimatePillWidth(Math.Max(IslandIdleDip, content) + hover);
    }

    private bool TryShowTitle(
        IslandPresenceView snap,
        double satelliteDip,
        double barsDip,
        out double titleDip)
    {
        titleDip = 0;
        if (!FaceHasTitle(snap.Face) || string.IsNullOrWhiteSpace(snap.Title))
        {
            return false;
        }

        bool sameLayout = snap.Title == _faceTitle
            && Math.Abs(_faceBar - ActualWidth) < 1
            && Math.Abs(_faceLeft - LeftCluster.ActualWidth) < 1
            && Math.Abs(_faceRight - StatusStrip.ActualWidth) < 1
            && Math.Abs(_faceSatellites - satelliteDip) < 1
            && Math.Abs(_faceBars - barsDip) < 1
            && IslandTitle.Visibility == Visibility.Visible;
        if (!sameLayout)
        {
            _faceTitle = snap.Title;
            _faceBar = ActualWidth;
            _faceLeft = LeftCluster.ActualWidth;
            _faceRight = StatusStrip.ActualWidth;
            _faceSatellites = satelliteDip;
            _faceBars = barsDip;

            IslandTitle.Text = snap.Title;
            IslandTitle.BeginAnimation(FrameworkElement.MaxWidthProperty, null);
            IslandTitle.MaxWidth = 1000;
            _titleWidth = IslandPresence.TitleMaxWidth(
                ActualWidth,
                LeftCluster.ActualWidth + IslandContentInsetDip,
                StatusStrip.ActualWidth + IslandContentInsetDip,
                IslandTitleChromeDip + satelliteDip + barsDip + IslandHoverDip,
                IslandTitleMeasure.Dip(IslandTitle));
        }

        if (_titleWidth <= 0)
        {
            return false;
        }

        titleDip = _titleWidth;
        IslandIdleCaption.Visibility = Visibility.Collapsed;
        IslandTitle.Visibility = Visibility.Visible;
        IslandTitle.Margin = new Thickness(8, 0, 0, 0);
        IslandTitle.BeginAnimation(FrameworkElement.MaxWidthProperty, null);
        IslandTitle.MaxWidth = titleDip;
        return true;
    }

    private void ShowIdleChrome(bool showLabel)
    {
        _faceTitle = string.Empty;
        _titleWidth = 0;
        _faceBars = 0;
        IslandTitle.BeginAnimation(FrameworkElement.MaxWidthProperty, null);
        IslandTitle.MaxWidth = 0;
        IslandTitle.Margin = new Thickness(0);
        IslandTitle.Visibility = Visibility.Collapsed;
        IslandIdleCaption.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMeter()
    {
        ShowIdleChrome(showLabel: false);
        IslandMeter.BeginAnimation(FrameworkElement.WidthProperty, null);
        IslandMeter.Width = IslandMeterDip;
        IslandMeter.Margin = new Thickness(8, 0, 0, 0);
        IslandMeter.Visibility = Visibility.Visible;
    }

    private void HideMeter()
    {
        IslandMeter.BeginAnimation(FrameworkElement.WidthProperty, null);
        IslandMeter.Width = 0;
        IslandMeter.Margin = new Thickness(0);
        IslandMeter.Visibility = Visibility.Collapsed;
        IslandMeterFill.BeginAnimation(FrameworkElement.WidthProperty, null);
        IslandMeterFill.Width = 0;
        _meterFill = 0;
    }

    private void AnimatePillWidth(double to)
    {
        if (double.IsNaN(_pillWidth))
        {
            _pillWidth = to;
            IslandPill.BeginAnimation(FrameworkElement.WidthProperty, null);
            IslandPill.Width = to;
            return;
        }

        AnimateWidth(IslandPill, FrameworkElement.WidthProperty, to, ref _pillWidth);
    }

    private static void AnimateWidth(FrameworkElement element, DependencyProperty property, double to, ref double cached)
    {
        if (Math.Abs(cached - to) < 0.5)
        {
            return;
        }

        // Retarget from the value the user currently sees (an animation may be mid-flight),
        // so a quick second change glides on instead of snapping back to the old target.
        double from = element.GetValue(property) is double live && !double.IsNaN(live) ? live : cached;
        cached = to;
        element.BeginAnimation(property, null);
        element.SetValue(property, to);
        double ms = IslandMotion.WidthMs(from, to, SystemParameters.ClientAreaAnimation);
        if (ms <= 0)
        {
            return;
        }

        // FillBehavior.Stop: the local value is already `to`, so nothing is held after the
        // clock ends and no animation clock lingers on an idle pill.
        element.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = to > from
                ? new QuinticEase { EasingMode = EasingMode.EaseOut }
                : new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        });
    }

    /// <summary>One-shot opacity (and optional scale) animation that leaves nothing running.</summary>
    private static void FadeFrom(UIElement element, double fromOpacity, double ms, ScaleTransform? scale = null, double fromScale = 1)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (ms <= 0)
        {
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(fromOpacity, 1, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        });
        if (scale is null)
        {
            return;
        }

        var grow = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.Stop,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    private ScaleTransform EnsurePillScale()
    {
        if (IslandPill.RenderTransform is ScaleTransform existing)
        {
            return existing;
        }

        var transform = new ScaleTransform(1, 1);
        IslandPill.RenderTransformOrigin = new Point(0.5, 0.5);
        IslandPill.RenderTransform = transform;
        return transform;
    }

    private void OnIslandPillPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_islandPressed || IsInsideSatellite(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _islandPressed = true;
        PressPill(true);
        // Release can land outside the pill; MouseUp on the pill, leave, or capture loss undoes it.
        IslandPill.MouseLeftButtonUp += ReleasePillPress;
        IslandPill.MouseLeave += ReleasePillPress;
    }

    private void ReleasePillPress(object sender, MouseEventArgs e)
    {
        IslandPill.MouseLeftButtonUp -= ReleasePillPress;
        IslandPill.MouseLeave -= ReleasePillPress;
        if (!_islandPressed)
        {
            return;
        }

        _islandPressed = false;
        PressPill(false);
    }

    private bool IsInsideSatellite(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, IslandPill))
        {
            if (ReferenceEquals(source, IslandSatelliteFirst) || ReferenceEquals(source, IslandSatelliteSecond))
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return false;
    }

    private void PressPill(bool down)
    {
        ScaleTransform scale = EnsurePillScale();
        double to = down ? IslandMotion.PressScale : 1;
        double from = scale.ScaleX;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = to;
        scale.ScaleY = to;
        double ms = IslandMotion.Gate(down ? IslandMotion.PressInMs : IslandMotion.PressOutMs, SystemParameters.ClientAreaAnimation);
        if (ms <= 0 || Math.Abs(from - to) < 0.001)
        {
            return;
        }

        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = down
                ? new CubicEase { EasingMode = EasingMode.EaseOut }
                : new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 },
            FillBehavior = FillBehavior.Stop,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private static string VolumeHudGlyph(int percent, bool muted)
    {
        if (muted || percent <= 0)
        {
            return "\uE74F";
        }

        if (percent < 33)
        {
            return "\uE992";
        }

        return percent < 66 ? "\uE993" : "\uE767";
    }

    private void SyncIslandPopup(IslandPresenceView snap, bool focusMusic)
    {
        if (!Monitor.IsPrimary && snap.Panel == IslandPanelBody.Clipboard)
        {
            _presence?.Dismiss(IslandNow());
            CloseIslandPopupOnly(animate: false);
            return;
        }

        if (snap.Panel == IslandPanelBody.None)
        {
            _shownPanel = IslandPanelBody.None;
            _shownBack = false;
            if (IslandPopup.IsOpen)
            {
                CloseIslandPopupOnly(animate: true);
            }

            return;
        }

        if (!IslandPopup.IsOpen)
        {
            _islandFocusMusic = focusMusic;
            CloseQuickSettings();
            CloseBrightness();
            CloseMedia();
            CancelIslandMotion();
            SetIslandJoined(true);
            // Hidden until the arranged width is known. A pre-measure offset (the XAML
            // width) shifts the panel left of the pill; the first painted frame must not.
            IslandHost.Opacity = 0;
            _islandAwaitingReveal = true;
            _islandRevealPosted = false;
            IslandPopup.IsOpen = true;
            return;
        }

        if (_shownPanel == snap.Panel && _shownBack == snap.ShowClipboardBack)
        {
            if (focusMusic)
            {
                if (IslandHost.CurrentHubTab == IslandHubTab.Clipboard)
                {
                    IslandHost.FocusClipboardSearch();
                }
                else
                {
                    IslandHost.PresentHub(IslandHost.CurrentHubTab, focus: true);
                }
            }

            return;
        }

        PresentIslandBody(snap, focusMusic, crossfade: true);
        IslandHost.UpdateLayout();
        AlignIslandPopup(force: false);
    }

    private void PresentIslandBody(IslandPresenceView snap, bool focusMusic, bool crossfade = false)
    {
        _shownPanel = snap.Panel;
        _shownBack = snap.ShowClipboardBack;
        bool music = _engine.Media.Snapshot.HasSession;
        IslandHost.PresentHub(ResolveHubTab(music), focusMusic, crossfade);
    }

    private void CloseIsland(bool animate = true)
    {
        _presence?.Dismiss(IslandNow());

        CloseIslandPopupOnly(animate);
        ArmIslandClock();
    }

    private void CloseIslandPopupOnly(bool animate)
    {
        _islandAwaitingReveal = false;
        _islandRevealPosted = false;
        if (!IslandPopup.IsOpen)
        {
            _islandClosePending = false;
            return;
        }

        if (animate && !_islandClosePending)
        {
            _islandClosePending = true;
            AnimateIsland(opening: false, () =>
            {
                if (!_islandClosePending)
                {
                    return;
                }

                _islandClosePending = false;
                IslandPopup.IsOpen = false;
            });
            return;
        }

        CancelIslandMotion();
        IslandPopup.IsOpen = false;
    }

    private void OnIslandPopupOpened(object sender, EventArgs e)
    {
        CancelIslandMotion();
        if (Monitor.IsPrimary && Island is { } island)
        {
            IslandHost.Attach(island);
        }

        if (_presence is not null)
        {
            PresentIslandBody(_presence.View, _islandFocusMusic);
        }

        TryRevealIsland();
    }

    private void OnIslandPopupClosed(object sender, EventArgs e)
    {
        _shownPanel = IslandPanelBody.None;
        _shownBack = false;
        IslandHost.OnPanelClosed();
        SetIslandJoined(false);
    }

    /// <summary>
    /// Measure, then center, then fade in. Stays at opacity 0 while the arranged width is 0
    /// so the XAML width cannot paint a left-shifted first frame.
    /// </summary>
    private void TryRevealIsland()
    {
        if (!_islandAwaitingReveal || !IslandPopup.IsOpen || _islandMeasuring)
        {
            return;
        }

        _islandMeasuring = true;
        try
        {
            IslandHost.UpdateLayout();
        }
        finally
        {
            _islandMeasuring = false;
        }

        if (!AlignIslandPopup(force: true))
        {
            if (_islandRevealPosted)
            {
                return;
            }

            _islandRevealPosted = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_islandAwaitingReveal && IslandPopup.IsOpen)
                {
                    TryRevealIsland();
                }
            }));
            return;
        }

        _islandAwaitingReveal = false;
        _islandRevealPosted = false;
        AnimateIsland(opening: true, onDone: null);
    }

    /// <summary>
    /// Placement=Bottom left-aligns the popup. Uses only the arranged panel width.
    /// Returns false when that width is not known yet.
    /// </summary>
    private bool AlignIslandPopup(bool force)
    {
        if (!IslandPopup.IsOpen && IslandPill.ActualWidth <= 0)
        {
            return false;
        }

        double panelDip = IslandHost.PanelWidthDip;
        double pillDip = IslandPill.ActualWidth;
        if (panelDip <= 1 || double.IsNaN(pillDip) || pillDip <= 1
            || _source is not { Handle: var hwnd } || hwnd == IntPtr.Zero)
        {
            return false;
        }

        Point origin;
        try
        {
            origin = IslandPill.PointToScreen(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        double dpi = Display.GetDpi(hwnd);
        RECT work = Display.GetMonitorWorkArea(hwnd);
        double offset = IslandPopupAlign.HorizontalOffset(
            pillDip,
            panelDip,
            origin.X,
            dpi,
            work.Left,
            work.Right);
        ApplyIslandOffset(offset - IslandFlyout.ShadowPadDip, force);
        IslandHost.SetJoin(pillDip, -offset);
        return true;
    }

    /// <summary>
    /// Writes <c>HorizontalOffset</c>. On the first reveal, nudge then set so a value
    /// stored before the popup existed is actually applied. Later updates skip a no-op write.
    /// </summary>
    private void ApplyIslandOffset(double offset, bool force)
    {
        if (force)
        {
            IslandPopup.HorizontalOffset = offset + 1;
        }
        else if (Math.Abs(IslandPopup.HorizontalOffset - offset) <= 0.5)
        {
            return;
        }

        IslandPopup.HorizontalOffset = offset;
    }

    private void OnIslandPopupSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IslandPopup.IsOpen || _islandMeasuring)
        {
            return;
        }

        if (_islandAwaitingReveal)
        {
            TryRevealIsland();
            return;
        }

        AlignIslandPopup(force: false);
    }

    private void OnIslandPillSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IslandPopup.IsOpen || !e.WidthChanged || _islandMeasuring)
        {
            return;
        }

        if (_islandAwaitingReveal)
        {
            TryRevealIsland();
            return;
        }

        AlignIslandPopup(force: false);
    }

    /// <summary>
    /// Open: the pill's bottom corners square off into the panel and both share the flyout fill.
    /// Closed: the capsule style returns.
    /// </summary>
    private void SetIslandJoined(bool open)
    {
        if (open)
        {
            IslandHost.ApplySurface();
            IslandPill.Background = FlyoutSurface.Create();
            IslandPill.CornerRadius = new CornerRadius(12, 12, 0, 0);
            IslandPill.BorderThickness = new Thickness(1, 1, 1, 0);
            IslandHost.SetJoin(IslandPill.ActualWidth, IslandHost.PanelWidthDip > 0 ? (IslandHost.PanelWidthDip - IslandPill.ActualWidth) / 2d : 0);
            return;
        }

        IslandPill.ClearValue(Border.BackgroundProperty);
        IslandPill.ClearValue(Border.CornerRadiusProperty);
        IslandPill.ClearValue(Border.BorderThicknessProperty);
    }

    /// <summary>
    /// Drops a scale or fade on both the popup child and the inner surface.
    /// The child must stay at identity or the next open inherits a shifted placement.
    /// </summary>
    private void CancelIslandMotion()
    {
        CancelPendingClose(IslandHost, ref _islandClosePending);
        CancelPendingClose(IslandHost.PanelSurface, ref _islandClosePending);
        IslandHost.ClearValue(UIElement.RenderTransformProperty);
    }

    private void AnimateIsland(bool opening, Action? onDone)
    {
        double panel = Math.Max(1, IslandHost.PanelWidthDip);
        double pill = Math.Max(1, IslandPill.ActualWidth);
        double fromX = Math.Clamp(pill / panel, 0.18, 1);
        // Scale the inner surface only. Popup placement reads the child's transformed
        // top-left; scaling IslandHost and then centering on the full width shifts the
        // panel left of the pill, and that position sticks after the scale returns to 1.
        IslandHost.ClearValue(UIElement.RenderTransformProperty);
        TimeSpan motion = SystemParameters.ClientAreaAnimation
            ? TimeSpan.FromMilliseconds(opening ? ChipFlyoutOpenMs : ChipFlyoutCloseMs)
            : TimeSpan.Zero;
        AnimateChipFlyout(IslandHost.PanelSurface, opening, onDone, fromX, 0.05, IslandHost, motion);
    }

    private void ArmIslandClock()
    {
        if (_islandClock is null)
        {
            return;
        }

        _islandClock.Stop();
        if (_presence?.NextDeadlineMs is not long deadline)
        {
            return;
        }

        long delay = Math.Max(1, deadline - IslandNow());
        _islandClock.Interval = TimeSpan.FromMilliseconds(delay);
        _islandClock.Start();
    }

    private WallpaperTransitionRequest? BuildWallpaperTransitionRequest()
    {
        IslandConfig? cfg = Island?.Config;
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (cfg is null || hwnd == IntPtr.Zero)
        {
            return null;
        }

        RECT rc = Display.GetPrimaryMonitorBounds(hwnd);
        int widthPx = rc.Right - rc.Left;
        if (widthPx <= 0)
        {
            return null;
        }

        return new WallpaperTransitionRequest(
            cfg.WallpaperTransition,
            cfg.WallpaperRespectSystemAnimation,
            SystemParameters.ClientAreaAnimation,
            widthPx);
    }

    private void AttachIsland()
    {
        IslandService? island = Island;
        _presence = new IslandPresence();
        _islandClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
        _islandClock.Tick += OnIslandClockTick;
        SizeChanged += OnIslandBarSizeChanged;
        IslandPill.SizeChanged += OnIslandPillSizeChanged;
        IslandHost.SizeChanged += OnIslandPopupSizeChanged;
        IslandHost.HubTabChanged += OnIslandHubTabChanged;
        IslandHost.AttachMedia(_engine.Media);
        if (Monitor.IsPrimary)
        {
            _wallpaper ??= new WallpaperService(WallpaperPaths.BundledRoot, WallpaperPaths.UserRoot);
            _wallpaper.TransitionRequest = BuildWallpaperTransitionRequest;
            _wallpaper.AccentEnabled = () =>
            {
                try
                {
                    IslandConfig? cfg = Island?.Config;
                    return cfg?.WallpaperAccent ?? IslandConfig.DefaultWallpaperAccent;
                }
                catch (Exception)
                {
                    return IslandConfig.DefaultWallpaperAccent;
                }
            };
            if (Application.Current is App app && app.Config is { } appConfig)
            {
                var sync = new WallpaperThemeSync(appConfig, _wallpaper);
                _wallpaper.HubApplied = sync.Remember;
                app.SetWallpaperSync(sync);
            }

            IslandHost.AttachWallpaper(_wallpaper);
        }

        IslandHost.SetClipboardChrome(Monitor.IsPrimary);
        IslandPill.AllowDrop = Monitor.IsPrimary;
        _engine.MonitorBrightness.BrightnessChanged += OnIslandBrightnessChanged;
        _presence.SetClipboardSurface(Monitor.IsPrimary, IslandNow());
        PushLowBattery(island?.Config);

        if (island is not null)
        {
            island.Changed += OnIslandChanged;
            if (Monitor.IsPrimary)
            {
                island.EntryAdded += OnIslandEntryAdded;
                _engine.Bluetooth.Changed += OnBluetoothNotice;
                SyncBluetoothWatch();
                CameraMirrorHost.Updated += OnCameraMirrorUpdated;
            }
        }

        NoteIslandVolume();
        NoteIslandBrightness(apply: false);
        NoteIslandMedia(apply: false);
        NoteIslandBattery(apply: false);
        UpdateIslandPill();
    }

    private void DetachIsland()
    {
        SizeChanged -= OnIslandBarSizeChanged;
        IslandPill.SizeChanged -= OnIslandPillSizeChanged;
        IslandHost.SizeChanged -= OnIslandPopupSizeChanged;
        IslandHost.HubTabChanged -= OnIslandHubTabChanged;
        IslandHost.DetachWallpaper();
        if (_wallpaper is not null)
        {
            _wallpaper.HubApplied = null;
        }

        if (Monitor.IsPrimary && Application.Current is App detachApp)
        {
            detachApp.SetWallpaperSync(null);
        }

        IslandHost.DetachMedia();
        _engine.MonitorBrightness.BrightnessChanged -= OnIslandBrightnessChanged;

        if (_islandClock is not null)
        {
            _islandClock.Stop();
            _islandClock.Tick -= OnIslandClockTick;
            _islandClock = null;
        }

        _engine.Bluetooth.Changed -= OnBluetoothNotice;
        _engine.Bluetooth.StopWatching();
        CameraMirrorHost.Updated -= OnCameraMirrorUpdated;
        if (_vizTimer is not null)
        {
            _vizTimer.Stop();
            _vizTimer.Tick -= OnVisualizerTick;
            _vizTimer = null;
        }

        if (Island is { } island)
        {
            island.Changed -= OnIslandChanged;
            island.EntryAdded -= OnIslandEntryAdded;
        }

        ClearCaptureAffinity();
        IslandHost.Detach();
        _presence = null;
        _idleLabelSet = false;
        _appliedLowBattery = int.MinValue;
        _selectedSatellite = IslandSatellite.None;
        _idlePreviewSet = false;
        _appliedIdlePreview = null;
    }

    private void OnIslandChanged(object? sender, EventArgs e) => UpdateIslandPill();

    private void OnIslandEntryAdded(object? sender, EventArgs e)
    {
        if (_presence is null || !Monitor.IsPrimary || Island is not { } island || island.Entries.Count == 0)
        {
            return;
        }

        IslandEntry newest = island.Entries[0];
        if (newest.Kind != IslandEntryKind.Text)
        {
            return;
        }

        _presence.NoteNewClip(IslandPruner.FormatPreview(newest.Text), IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void UpdateIslandPill()
    {
        IslandService? island = Island;
        if (island is not { Config.Enabled: true } || _presence is null)
        {
            IslandPill.Visibility = Visibility.Collapsed;
            if (IslandPopup.IsOpen)
            {
                CloseIsland(animate: false);
            }

            _islandClock?.Stop();
            ApplyCaptureAffinity(default, pillShown: false);
            return;
        }

        IslandMusicMode mode = IslandNormalizer.IsTransientMusic(island.Config.MusicPresence)
            ? IslandMusicMode.Transient
            : IslandMusicMode.Persistent;
        _presence.SetMusicMode(mode, IslandNow());
        PushLowBattery(island.Config);
        PushIdleLabel(island.Config);
        PushColorMode(island.Config);
        if (Monitor.IsPrimary)
        {
            PushIdlePreview(island);
        }

        ApplyIslandChrome(focusMusic: false);
    }

    private void PulseIslandPill()
    {
        if (_islandPulseRunning || IslandPill.Visibility != Visibility.Visible)
        {
            return;
        }

        ScaleTransform transform = EnsurePillScale();
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        _islandPulseRunning = true;
        var pulse = new DoubleAnimation(1d, 1.06d, TimeSpan.FromMilliseconds(160))
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        pulse.Completed += (_, _) => _islandPulseRunning = false;
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
        transform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
    }

    private void OnIslandDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Monitor.IsPrimary && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnIslandPillDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!Monitor.IsPrimary || !e.Data.GetDataPresent(DataFormats.FileDrop) || Island is not { } island)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        string? displayName = null;
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
            {
                continue;
            }

            string name = Path.GetFileName(path);
            if (name.Length > 0)
            {
                displayName = name;
            }
        }

        island.AddDroppedFiles(paths);
        if (displayName is null)
        {
            return;
        }

        _presence?.NoteFileDrop(displayName, IslandNow());
        ApplyIslandChrome(focusMusic: false);
        PulseIslandPill();
    }

    private void PushLowBattery(IslandConfig? config)
    {
        int percent = IslandNormalizer.NormalizeLowBatteryPercent(
            config?.LowBatteryPercent ?? IslandConfig.DefaultLowBatteryPercent);
        if (_appliedLowBattery == percent)
        {
            return;
        }

        _appliedLowBattery = percent;
        _presence?.SetLowBatteryPercent(percent, IslandNow());
    }

    private void PushIdleLabel(IslandConfig config)
    {
        IslandIdleLabel label = IslandNormalizer.IsLastClipIdle(config.IdleLabel)
            ? IslandIdleLabel.LastClip
            : IslandIdleLabel.ClipboardName;
        if (_idleLabelSet && _appliedIdleLabel == label)
        {
            return;
        }

        _idleLabelSet = true;
        _appliedIdleLabel = label;
        _presence?.SetIdleLabel(label, IslandNow());
    }

    private void PushIdlePreview(IslandService island)
    {
        string? preview = LatestTextPreview(island.Entries);
        if (_idlePreviewSet && string.Equals(_appliedIdlePreview, preview, StringComparison.Ordinal))
        {
            return;
        }

        _idlePreviewSet = true;
        _appliedIdlePreview = preview;
        _presence?.SetIdlePreview(preview, IslandNow());
    }

    private static string? LatestTextPreview(IReadOnlyList<IslandEntry> entries)
    {
        foreach (IslandEntry entry in entries)
        {
            if (entry.Kind == IslandEntryKind.Text)
            {
                return IslandPruner.FormatPreview(entry.Text);
            }
        }

        return null;
    }

    private double ApplySatellites(IslandPresenceView snap)
    {
        if (_selectedSatellite != snap.FirstSatellite && _selectedSatellite != snap.SecondSatellite)
        {
            _selectedSatellite = IslandSatellite.None;
        }

        double width = PaintSatellite(IslandSatelliteFirst, IslandSatelliteFirstGlyph, snap.FirstSatellite);
        width += PaintSatellite(IslandSatelliteSecond, IslandSatelliteSecondGlyph, snap.SecondSatellite);
        return width;
    }

    private double PaintSatellite(Border host, TextBlock glyph, IslandSatellite satellite)
    {
        string? text = satellite switch
        {
            IslandSatellite.Clipboard => GlyphClipboard,
            IslandSatellite.Music => GlyphMusic,
            _ => null,
        };
        if (text is null)
        {
            host.BorderBrush = Brushes.Transparent;
            if (host.Tag is true && host.Visibility == Visibility.Visible)
            {
                // Closing: fade and shrink, then collapse. The pill width already morphs
                // to the smaller target, so both run together. Re-showing cancels this.
                host.Tag = false;
                HideSatelliteAnimated(host);
            }
            else if (host.Tag is not false)
            {
                host.Visibility = Visibility.Collapsed;
                host.Margin = new Thickness(0);
            }

            return 0;
        }

        glyph.Text = text;
        bool wasShown = host.Tag is true && host.Visibility == Visibility.Visible;
        host.Margin = new Thickness(8, 0, 0, 0);
        if (!wasShown)
        {
            host.Tag = true;
            host.Visibility = Visibility.Visible;
            ShowSatelliteAnimated(host);
        }

        bool selected = satellite == _selectedSatellite;
        host.BorderBrush = selected
            ? TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue
            : Brushes.Transparent;
        host.ToolTip = satellite == IslandSatellite.Music ? Strings.Island_Music : Strings.Island_Clipboard;
        return IslandSatelliteDip;
    }

    private static ScaleTransform SatelliteScale(Border host)
    {
        if (host.RenderTransform is ScaleTransform existing)
        {
            return existing;
        }

        var transform = new ScaleTransform(1, 1);
        host.RenderTransform = transform;
        return transform;
    }

    private static void ShowSatelliteAnimated(Border host)
    {
        ScaleTransform scale = SatelliteScale(host);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = 1;
        scale.ScaleY = 1;
        host.Opacity = 1;
        double ms = IslandMotion.Gate(IslandMotion.SatelliteInMs, SystemParameters.ClientAreaAnimation);
        FadeFrom(host, 0, ms, scale, IslandMotion.SatelliteFromScale);
    }

    private static void HideSatelliteAnimated(Border host)
    {
        ScaleTransform scale = SatelliteScale(host);
        double ms = IslandMotion.Gate(IslandMotion.SatelliteOutMs, SystemParameters.ClientAreaAnimation);
        void Collapse()
        {
            // A re-show in the meantime flips Tag back to true and owns the host again.
            if (host.Tag is false)
            {
                host.Tag = null;
                host.Visibility = Visibility.Collapsed;
                host.Margin = new Thickness(0);
            }

            host.Opacity = 1;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }

        if (ms <= 0)
        {
            Collapse();
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(host.Opacity, 0, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        fade.Completed += (_, _) =>
        {
            if (host.Tag is false)
            {
                host.BeginAnimation(UIElement.OpacityProperty, null);
                Collapse();
            }
        };
        host.BeginAnimation(UIElement.OpacityProperty, fade);
        var shrink = new DoubleAnimation(scale.ScaleX, IslandMotion.SatelliteFromScale, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    private double ApplyIslandExtras(IslandPresenceView snap)
    {
        bool camera = snap.Face == IslandPillFace.Camera;
        IslandCamera.Visibility = camera ? Visibility.Visible : Visibility.Collapsed;
        IslandCamera.Source = camera ? CameraMirrorHost.Preview?.Frame : null;
        bool bars = VisualizerBarsWanted(snap);
        IslandBars.Visibility = bars ? Visibility.Visible : Visibility.Collapsed;
        double cameraDip = camera && IslandCamera.Source is not null ? 44 : 0;
        return cameraDip + (bars ? IslandBarsDip : 0);
    }

    /// <summary>
    /// Bars follow the music face, not SMTC IsPlaying — some players keep a
    /// session while playback status lags; peak stays flat when audio is quiet.
    /// </summary>
    private bool VisualizerBarsWanted(IslandPresenceView snap)
        => snap.Face == IslandPillFace.NowPlaying
           && Island?.Config.Visualizer == true
           && _engine.Media.Snapshot.HasSession;

    private void OnBluetoothNotice(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnBluetoothNotice(sender, e));
            return;
        }

        if (_presence is null || Island?.Config.BluetoothNotice != true)
        {
            return;
        }

        IReadOnlyList<BluetoothDeviceInfo> current = _engine.Bluetooth.Devices;
        foreach (string name in BluetoothConnect.NewlyConnected(_btPrevious, current, _btSeeded))
        {
            _presence.NoteBluetooth(name, IslandNow());
        }

        _btPrevious = current;
        _btSeeded = true;
        ApplyIslandChrome(focusMusic: false);
    }

    private void OnCameraMirrorUpdated(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnCameraMirrorUpdated(sender, e));
            return;
        }

        if (_presence is null || !Monitor.IsPrimary)
        {
            return;
        }

        CameraPreview? preview = CameraMirrorHost.Preview;
        _presence.SetCamera(preview is not null, preview?.Status, IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void SyncBluetoothWatch()
    {
        if (!Monitor.IsPrimary)
        {
            return;
        }

        if (Island?.Config.BluetoothNotice == true)
        {
            _engine.Bluetooth.WatchConnections();
            return;
        }

        _engine.Bluetooth.StopWatching();
    }

    private bool VisualizerWanted()
        => Monitor.IsPrimary
           && _presence is not null
           && VisualizerBarsWanted(_presence.View);

    private void SyncVisualizerTimer()
    {
        if (!VisualizerWanted())
        {
            if (_vizTimer is { IsEnabled: true })
            {
                _vizTimer.Stop();
                VisualizerBars.Reset();
            }

            return;
        }

        if (_vizTimer is null)
        {
            _vizTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _vizTimer.Tick += OnVisualizerTick;
        }

        if (!_vizTimer.IsEnabled)
        {
            VisualizerBars.Reset();
            _vizTimer.Start();
        }
    }

    private void OnVisualizerTick(object? sender, EventArgs e)
    {
        if (!VisualizerWanted())
        {
            _vizTimer?.Stop();
            VisualizerBars.Reset();
            if (IslandBars.Visibility == Visibility.Visible)
            {
                ApplyIslandChrome(focusMusic: false);
            }

            return;
        }

        float peak = 0f;
        if (_engine.Volume.Snapshot is { IsMuted: true })
        {
            // Endpoint peak is pre-master-volume; mute still means a quiet chip.
            peak = 0f;
        }
        else
        {
            _ = AudioPeak.TryRead(out peak);
        }

        // Bars are fixed 14 DIP tall and scaled with a RenderTransform: no layout pass per tick,
        // and a tick whose scales did not change (silence) touches nothing at all.
        float[] levels = VisualizerBars.Step(peak);
        Border[] bars = [IslandBar0, IslandBar1, IslandBar2, IslandBar3, IslandBar4];
        for (int i = 0; i < bars.Length; i++)
        {
            double scale = IslandMotion.BarScale(levels[i], VisualizerBars.MaxDip);
            if (Math.Abs(scale - _barScales[i]) < 0.01)
            {
                continue;
            }

            _barScales[i] = scale;
            if (bars[i].RenderTransform is ScaleTransform transform)
            {
                transform.ScaleY = scale;
            }
        }

        if (IslandBars.Visibility != Visibility.Visible)
        {
            ApplyIslandChrome(focusMusic: false);
        }
    }

    private static bool FaceHasTitle(IslandPillFace face)
        => face is IslandPillFace.NowPlaying or IslandPillFace.Idle or IslandPillFace.Clip or IslandPillFace.Drop
            or IslandPillFace.Bluetooth or IslandPillFace.Camera;

    private string BatteryTip(int percent)
    {
        string tip = string.Format(CultureInfo.CurrentCulture, Strings.Island_BatteryFormat, percent);
        return _engine.Status.Snapshot.IsBatteryCharging ? string.Format(CultureInfo.CurrentCulture, Strings.Island_BatteryChargingFormat, tip) : tip;
    }

    private static string IslandAccessName(IslandPresenceView snap)
    {
        return snap.Face switch
        {
            IslandPillFace.NowPlaying => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_Music : snap.Title,
            IslandPillFace.Volume => snap.MeterMuted ? Strings.Island_VolumeMuted : string.Format(CultureInfo.CurrentCulture, Strings.Island_VolumeFormat, snap.MeterPercent),
            IslandPillFace.Brightness => string.Format(CultureInfo.CurrentCulture, Strings.Island_BrightnessFormat, snap.MeterPercent),
            IslandPillFace.Battery => string.Format(CultureInfo.CurrentCulture, Strings.Island_BatteryFormat, snap.MeterPercent),
            IslandPillFace.Clip or IslandPillFace.Drop => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_Clipboard : snap.Title,
            IslandPillFace.Bluetooth => string.IsNullOrWhiteSpace(snap.Title) ? "Bluetooth" : snap.Title,
            IslandPillFace.Camera => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_Camera : snap.Title,
            _ => string.IsNullOrWhiteSpace(snap.Title) ? Strings.Island_Clipboard : snap.Title,
        };
    }

    /// <summary>
    /// Last-clip preview parks the pill in its own window and drops only that HWND
    /// from capture. The top bar stays in the shot. A plain "Pano" label, music, and
    /// meters stay in the bar.
    /// </summary>
    private void ApplyCaptureAffinity(IslandPresenceView snap, bool pillShown)
    {
        bool park = PillCapture.ShouldPark(
            snap.Face,
            pillShown,
            snap.Title,
            IslandNormalizer.IsLastClipIdle(Island?.Config.IdleLabel));
        if (park)
        {
            ParkPill();
            WindowCapture.SetExcludedFromCapture(_source?.Handle ?? IntPtr.Zero, false);
            return;
        }

        ReturnPill();
        if (_captureExcluded)
        {
            _captureExcluded = false;
            WindowCapture.SetExcludedFromCapture(_source?.Handle ?? IntPtr.Zero, false);
        }
    }

    private void ParkPill()
    {
        if (_pillParked)
        {
            Dispatcher.BeginInvoke(PlaceShot, DispatcherPriority.Render);
            return;
        }

        if (IslandPill.Parent is not Panel home)
        {
            return;
        }

        _pillHome = home;
        _pillHomeIndex = home.Children.IndexOf(IslandPill);
        _pillMargin = IslandPill.Margin;
        _pillAlignH = IslandPill.HorizontalAlignment;
        _pillAlignV = IslandPill.VerticalAlignment;
        var slot = new Border
        {
            Width = IslandPill.ActualWidth,
            Height = Math.Max(IslandPill.ActualHeight, 1),
            Margin = _pillMargin,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Background = Brushes.Transparent,
        };
        home.Children.Remove(IslandPill);
        home.Children.Insert(_pillHomeIndex, slot);
        _pillSlot = slot;

        IslandPill.Margin = new Thickness(0);
        IslandPill.HorizontalAlignment = HorizontalAlignment.Center;
        IslandPill.VerticalAlignment = VerticalAlignment.Center;
        _shot ??= new IslandShotWindow(this);
        _shot.Host.Children.Add(IslandPill);
        _pillParked = true;
        _captureExcluded = false;
        IslandPill.SizeChanged += OnParkedPillSize;
        Dispatcher.BeginInvoke(PlaceShot, DispatcherPriority.Render);
    }

    private void OnParkedPillSize(object sender, SizeChangedEventArgs e)
    {
        if (_pillSlot is not null)
        {
            _pillSlot.Width = IslandPill.ActualWidth;
            _pillSlot.Height = Math.Max(IslandPill.ActualHeight, 1);
        }

        Dispatcher.BeginInvoke(PlaceShot, DispatcherPriority.Render);
    }

    private void PlaceShot()
    {
        if (!_pillParked || _shot is null || _pillSlot is null || _source is null)
        {
            return;
        }

        Point origin;
        try
        {
            origin = _pillSlot.PointToScreen(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        uint dpi = Display.GetDpi(_source.Handle);
        int width = Display.DipToPixels(Math.Max(IslandPill.ActualWidth, 1), dpi);
        int height = Display.DipToPixels(Math.Max(IslandPill.ActualHeight, 1), dpi);
        _shot.Place((int)Math.Round(origin.X), (int)Math.Round(origin.Y), width, height);
    }

    private void ReturnPill()
    {
        if (!_pillParked)
        {
            return;
        }

        IslandPill.SizeChanged -= OnParkedPillSize;
        _shot?.Host.Children.Remove(IslandPill);
        IslandPill.Margin = _pillMargin;
        IslandPill.HorizontalAlignment = _pillAlignH;
        IslandPill.VerticalAlignment = _pillAlignV;
        if (_pillHome is not null)
        {
            if (_pillSlot is not null)
            {
                _pillHome.Children.Remove(_pillSlot);
            }

            if (!ReferenceEquals(IslandPill.Parent, _pillHome))
            {
                int index = Math.Clamp(_pillHomeIndex, 0, _pillHome.Children.Count);
                _pillHome.Children.Insert(index, IslandPill);
            }
        }

        _pillSlot = null;
        _pillParked = false;
        _shot?.Conceal();
    }

    private void ClearCaptureAffinity()
    {
        ReturnPill();
        _captureExcluded = false;
        WindowCapture.SetExcludedFromCapture(_source?.Handle ?? IntPtr.Zero, false);
    }
}
