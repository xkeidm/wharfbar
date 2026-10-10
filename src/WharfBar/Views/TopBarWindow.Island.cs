using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class TopBarWindow
{
    private const double IslandWidthMs = 200;
    private const double IslandIdleDip = 108;
    private const double IslandHoverDip = 4;
    private const double IslandPadDip = 32;
    private const double IslandGlyphDip = 16;
    private const double IslandMeterDip = 72;
    // Centered slot outside the title: pill margin 24+24, padding 16+16, glyph, title gap.
    private const double IslandTitleChromeDip = 104;
    private const string GlyphClipboard = "\uE8C8";
    private const string GlyphMusic = "\uE90C";
    private const string GlyphBrightness = "\uE706";

    private bool _islandClosePending;
    private bool _islandAwaitingReveal;
    private bool _islandMeasuring;
    private bool _islandRevealPosted;
    private IslandPresence? _presence;
    private DispatcherTimer? _islandClock;
    private bool _islandFocusMusic;
    private IslandPanelBody _shownPanel;
    private bool _shownBack;
    private bool _islandPulseRunning;
    private string? _islandTip;
    private string _faceTitle = string.Empty;
    private double _faceBar = double.NaN;
    private double _faceLeft = double.NaN;
    private double _faceRight = double.NaN;
    private double _titleWidth;
    private double _meterFill;
    private double _pillWidth = double.NaN;

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
        if (_presence is null || Island is not { Config.Enabled: true })
        {
            return;
        }

        IslandPillCommand command = _presence.Click(IslandNow());
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

    private void OnIslandClipboardRequested(object? sender, EventArgs e)
    {
        if (_presence is null)
        {
            return;
        }

        _presence.ShowClipboard(IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void OnIslandMusicRequested(object? sender, EventArgs e)
    {
        if (_presence is null)
        {
            return;
        }

        _presence.BackToMusic(IslandNow());
        ApplyIslandChrome(focusMusic: true);
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
        // hapta ya da paneldeyse süreyi kapatmadan önce hover'ı geri yükle.
        long now = IslandNow();
        if (IslandPill.IsMouseOver)
        {
            _presence.PointerEnterPill(now);
        }
        else if (IslandPopup.IsOpen && IslandHost.IsMouseOver)
        {
            _presence.PointerEnterPanel(now);
        }

        _presence.Tick(now);
        if (IslandPill.IsMouseOver)
        {
            _presence.PointerEnterPill(IslandNow());
        }

        ApplyIslandChrome(focusMusic: false);
    }

    private void OnIslandBrightnessChanged(object? sender, EventArgs e) => NoteIslandBrightness(apply: true);

    private void NoteIslandVolume()
    {
        if (_presence is null || !Monitor.IsPrimary)
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
        if (_presence is null || !Monitor.IsPrimary)
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
        if (_presence is null || !Monitor.IsPrimary)
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
        ApplyIslandFace(snap);
        SyncIslandPopup(snap, focusMusic);
        ArmIslandClock();
    }

    private void ApplyIslandFace(IslandPresenceView snap)
    {
        IslandGlyph.Text = snap.Face switch
        {
            IslandPillFace.NowPlaying => GlyphMusic,
            IslandPillFace.Volume => VolumeHudGlyph(snap.MeterPercent, snap.MeterMuted),
            IslandPillFace.Brightness => GlyphBrightness,
            _ => GlyphClipboard,
        };

        string tip = snap.Face switch
        {
            IslandPillFace.NowPlaying => BuildMediaTooltip(_engine.Media.Snapshot),
            IslandPillFace.Volume => snap.MeterMuted ? "Ses (sessiz)" : $"Ses %{snap.MeterPercent}",
            IslandPillFace.Brightness => $"Parlaklık %{snap.MeterPercent}",
            _ => "Pano ve bırakılan dosyalar",
        };
        if (!string.Equals(tip, _islandTip, StringComparison.Ordinal))
        {
            _islandTip = tip;
            SetPlainToolTip(IslandPill, tip);
        }

        bool meter = snap.Face is IslandPillFace.Volume or IslandPillFace.Brightness;
        bool showTitle = false;
        double titleDip = 0;

        if (meter)
        {
            ShowMeter();
            double fill = IslandMeterDip * Math.Clamp(snap.MeterPercent, 0, 100) / 100d;
            if (snap.MeterMuted)
            {
                fill = 0;
            }

            AnimateWidth(IslandMeterFill, FrameworkElement.WidthProperty, fill, ref _meterFill);
        }
        else
        {
            HideMeter();
            showTitle = TryShowTitle(snap, out titleDip);
            if (!showTitle)
            {
                ShowIdleChrome(snap.Face == IslandPillFace.Idle);
            }
        }

        double content = meter
            ? IslandPadDip + IslandGlyphDip + 8 + IslandMeterDip
            : showTitle
                ? IslandPadDip + IslandGlyphDip + 8 + titleDip
                : IslandIdleDip;
        double hover = IslandPill.IsMouseOver ? IslandHoverDip : 0;
        AnimatePillWidth(Math.Max(IslandIdleDip, content) + hover);
    }

    private bool TryShowTitle(IslandPresenceView snap, out double titleDip)
    {
        titleDip = 0;
        if (snap.Face != IslandPillFace.NowPlaying || string.IsNullOrWhiteSpace(snap.Title))
        {
            return false;
        }

        bool sameLayout = snap.Title == _faceTitle
            && Math.Abs(_faceBar - ActualWidth) < 1
            && Math.Abs(_faceLeft - LeftCluster.ActualWidth) < 1
            && Math.Abs(_faceRight - StatusStrip.ActualWidth) < 1
            && IslandTitle.Visibility == Visibility.Visible;
        if (!sameLayout)
        {
            _faceTitle = snap.Title;
            _faceBar = ActualWidth;
            _faceLeft = LeftCluster.ActualWidth;
            _faceRight = StatusStrip.ActualWidth;

            IslandTitle.Text = snap.Title;
            IslandTitle.BeginAnimation(FrameworkElement.MaxWidthProperty, null);
            IslandTitle.MaxWidth = 1000;
            _titleWidth = IslandPresence.TitleMaxWidth(
                ActualWidth,
                LeftCluster.ActualWidth,
                StatusStrip.ActualWidth,
                IslandTitleChromeDip,
                IslandTitleMeasure.Dip(IslandTitle));
        }

        if (_titleWidth <= 0)
        {
            return false;
        }

        titleDip = _titleWidth;
        IslandIdleLabel.Visibility = Visibility.Collapsed;
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
        IslandTitle.BeginAnimation(FrameworkElement.MaxWidthProperty, null);
        IslandTitle.MaxWidth = 0;
        IslandTitle.Margin = new Thickness(0);
        IslandTitle.Visibility = Visibility.Collapsed;
        IslandIdleLabel.Visibility = showLabel ? Visibility.Visible : Visibility.Collapsed;
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

        double from = cached;
        cached = to;
        element.BeginAnimation(property, null);
        element.SetValue(property, from);
        element.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(IslandWidthMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd,
        });
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
            if (focusMusic && snap.Panel == IslandPanelBody.Music)
            {
                IslandHost.PresentMusic(focus: true);
            }
            else if (focusMusic && snap.Panel == IslandPanelBody.Clipboard)
            {
                IslandHost.FocusClipboardSearch();
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
        if (snap.Panel == IslandPanelBody.Music)
        {
            IslandHost.PresentMusic(focusMusic, crossfade);
            return;
        }

        IslandHost.PresentClipboard(snap.ShowClipboardBack, focusMusic, crossfade);
    }

    private void CloseIsland(bool animate = true)
    {
        if (Monitor.IsPrimary)
        {
            _presence?.Dismiss(IslandNow());
        }

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
        if (Island is { } island)
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
        ApplyIslandOffset(offset, force);
        IslandHost.SetNeck(pillDip);
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
            IslandHost.SetNeck(IslandPill.ActualWidth);
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
        AnimateChipFlyout(IslandHost.PanelSurface, opening, onDone, fromX, 0.05, IslandHost);
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

    private void AttachIsland()
    {
        if (!Monitor.IsPrimary)
        {
            return;
        }

        IslandService? island = Island;
        if (island is null)
        {
            UpdateIslandPill();
            return;
        }

        _presence = new IslandPresence();
        _islandClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1) };
        _islandClock.Tick += OnIslandClockTick;
        SizeChanged += OnIslandBarSizeChanged;
        IslandPill.SizeChanged += OnIslandPillSizeChanged;
        IslandHost.SizeChanged += OnIslandPopupSizeChanged;
        IslandHost.ClipboardRequested += OnIslandClipboardRequested;
        IslandHost.MusicRequested += OnIslandMusicRequested;
        IslandHost.AttachMedia(_engine.Media);
        _engine.MonitorBrightness.BrightnessChanged += OnIslandBrightnessChanged;

        NoteIslandVolume();
        NoteIslandBrightness(apply: false);
        NoteIslandMedia(apply: false);

        island.Changed += OnIslandChanged;
        island.EntryAdded += OnIslandEntryAdded;
        UpdateIslandPill();
    }

    private void DetachIsland()
    {
        SizeChanged -= OnIslandBarSizeChanged;
        IslandPill.SizeChanged -= OnIslandPillSizeChanged;
        IslandHost.SizeChanged -= OnIslandPopupSizeChanged;
        IslandHost.ClipboardRequested -= OnIslandClipboardRequested;
        IslandHost.MusicRequested -= OnIslandMusicRequested;
        IslandHost.DetachMedia();
        _engine.MonitorBrightness.BrightnessChanged -= OnIslandBrightnessChanged;

        if (_islandClock is not null)
        {
            _islandClock.Stop();
            _islandClock.Tick -= OnIslandClockTick;
            _islandClock = null;
        }

        if (Island is { } island)
        {
            island.Changed -= OnIslandChanged;
            island.EntryAdded -= OnIslandEntryAdded;
        }

        IslandHost.Detach();
        _presence = null;
    }

    private void OnIslandChanged(object? sender, EventArgs e) => UpdateIslandPill();

    private void OnIslandEntryAdded(object? sender, EventArgs e)
    {
        UpdateIslandPill();
        PulseIslandPill();
    }

    private void UpdateIslandPill()
    {
        IslandService? island = Island;
        bool available = island is not null && island.Config.Enabled && Monitor.IsPrimary;
        if (!available || island is null || _presence is null)
        {
            IslandPill.Visibility = Visibility.Collapsed;
            _islandClock?.Stop();
            if (IslandPopup.IsOpen)
            {
                CloseIsland(animate: false);
            }

            return;
        }

        IslandPill.Visibility = Visibility.Visible;
        IslandMusicMode mode = IslandNormalizer.IsTransientMusic(island.Config.MusicPresence)
            ? IslandMusicMode.Transient
            : IslandMusicMode.Persistent;
        _presence.SetMusicMode(mode, IslandNow());
        ApplyIslandChrome(focusMusic: false);
    }

    private void PulseIslandPill()
    {
        if (_islandPulseRunning || IslandPill.Visibility != Visibility.Visible)
        {
            return;
        }

        var transform = IslandPill.RenderTransform as System.Windows.Media.ScaleTransform;
        if (transform is null)
        {
            transform = new System.Windows.Media.ScaleTransform(1, 1);
            IslandPill.RenderTransformOrigin = new Point(0.5, 0.5);
            IslandPill.RenderTransform = transform;
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
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnIslandPillDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || Island is not { } island)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            island.AddDroppedFiles(paths);
            PulseIslandPill();
        }
    }
}
