using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;
using Wpf.Ui.Appearance;

namespace WharfBar.Views;

/// <summary>
/// Island panel: search box + a single unified timeline (clipboard text and dropped
/// files). The project has no MVVM; rows are built by hand as <see cref="Border"/>.
/// </summary>
public sealed partial class IslandFlyout : UserControl
{
    /// <summary>
    /// Row ceiling built per pass. <c>MaxItems</c> can go up to 5000 and the list is
    /// not virtualized: rebuilding 5000 rows on every keystroke hangs the top bar,
    /// which is a registered AppBar. The number of entries not built is reported in the
    /// footer; search still covers all entries (order is "newest first", so the first 200 matches are the newest).
    /// </summary>
    private const int MaxRenderedRows = 200;

    /// <summary>Dropped-file entry — no emoji, no font dependency (an MDL2 code is never written without verification).</summary>
    internal const string FileGlyph = "\uD83D\uDCC4";

    /// <summary>Clipboard text entry.</summary>
    internal const string TextGlyph = "\u2261";

    /// <summary>Row delete button (MDL2 Delete).</summary>
    private const string DeleteGlyph = "\uE74D";

    /// <summary>Folder badge (Segoe MDL2 Assets "Folder"; drawn with <c>AppIconFontFamily</c>).</summary>
    private const string FolderGlyph = "\uE8B7";

    /// <summary>Accessibility name of the hover "×" button — the same action as in the menu.</summary>
    private const string RemoveAccessName = "Remove from list";

    /// <summary>
    /// Timeline refresh cadence. While a track is playing the media service interpolates the
    /// position locally, so the panel polls it at ~250 ms; a paused track only needs the slower
    /// cadence. The poll is armed only while the Music body is visible.
    /// </summary>
    private static readonly TimeSpan TimelinePollPlaying = TimeSpan.FromMilliseconds(250);

    /// <summary>Slower cadence while a session is paused (the position is not advancing).</summary>
    private static readonly TimeSpan TimelinePollPaused = TimeSpan.FromSeconds(1);

    /// <summary>Duration of the album tint fade (panel gradient, outline, cover bloom, play circle).</summary>
    private const double TintFadeMs = 260;

    /// <summary>
    /// Alpha of the panel tint at the pill join line (the panel's top edge). The top bar fills the
    /// joined pill with <see cref="FlyoutSurface.CreateTinted"/> at this same amount, so the pill
    /// fill and the first panel gradient stops resolve to the same color and the join has no seam.
    /// </summary>
    internal const byte PanelJoinTintAlpha = IslandPalette.PanelTintJoinAlpha;

    /// <summary>Hover lift of the play circle when it carries the album tint (share of white).</summary>
    private const double PlayButtonHoverLift = 0.14;

    /// <summary>Duration of the cover art crossfade.</summary>
    private const double CoverFadeMs = 220;

    /// <summary>Duration of the play/pause glyph crossfade.</summary>
    private const double GlyphFadeMs = 140;

    /// <summary>Reference width of the thumb drawn over the seek track (matches the Slider thumb).</summary>
    private const double SeekThumbDip = 13;

    private IslandService? _service;
    private MediaSessionService? _media;
    private readonly List<IslandEntry> _rows = [];
    private int _selectedIndex = -1;
    private int _bodySwapId;
    private DispatcherTimer? _timelineTimer;
    private bool _timelineArmed;

    /// <summary>Transparent padding around the panel so the shadow is not clipped by the popup window.</summary>
    internal const double ShadowPadDip = 24;

    /// <summary>Height of the concave fillets above the panel top edge.</summary>
    internal const double FilletDip = 10;

    /// <summary>Panel corner radius.</summary>
    internal const double CornerDip = 24;

    private double _pillWidthDip;
    private double _pillXDip;
    private Brush _silhouetteFill = Brushes.Transparent;

    /// <summary>Album tint state: animated gradient stops for the panel tint and the outline rim.</summary>
    private LinearGradientBrush? _panelTintBrush;
    private LinearGradientBrush? _outlineBrush;
    private bool _tintApplied;
    private Color? _appliedTint;

    /// <summary>Last tint painted on the play/pause circle (null = system accent).</summary>
    private Color? _playButtonTint;

    /// <summary>Seek ratio last painted on the designed track (0..1).</summary>
    private double _progressRatio;

    /// <summary>Last play/pause state the glyphs were swapped to.</summary>
    private bool? _glyphPlaying;

    /// <summary>Last play state the header dot was painted for.</summary>
    private bool? _dotPlaying;

    private bool _playPulseRunning;
    private int _coverFadeId;

    public IslandFlyout()
    {
        InitializeComponent();
        FlyoutRoot.SizeChanged += (_, _) => RebuildSilhouette();
        HubTabs.SizeChanged += OnHubTabStripSizeChanged;
        _tabStripButtons = [TabClipboard, TabWallpaper, TabMusic, TabShelf, TabPrompter, TabCamera, TabColor];
        _tabStripLabels = [TabClipboardLabel, TabWallpaperLabel, TabMusicLabel, TabShelfLabel, TabPrompterLabel, TabCameraLabel, TabColorLabel];
        CameraPreviewHost.SizeChanged += OnCameraPreviewSizeChanged;
        MusicCover.SizeChanged += OnMusicCoverSizeChanged;
        MusicProgressTrack.SizeChanged += (_, _) => PositionProgressThumb();

        // The album tint and the rim are animated in place: both brushes are created once and
        // their gradient stops are retargeted when the track changes, so no brush is rebuilt per tick.
        _panelTintBrush = CreatePanelTintBrush();
        SilhouetteTint.Fill = _panelTintBrush;
        _outlineBrush = CreateOutlineBrush();
        SilhouetteOutline.Stroke = _outlineBrush;

        // Let the shelf background menu open across the entire body (including the empty space
        // below the chips): without a transparent brush the Grid is outside hit testing, so
        // the menu would be unreachable in the real two-file case. A chip carries its own menu,
        // so the chip menu wins on a chip.
        ShelfBody.Background = Brushes.Transparent;
    }

    /// <summary>
    /// With all seven body tabs visible, the labelled strip does not fit inside the panel
    /// (rough measure: seven labelled tabs ~515 dip, panel inner width ~448). The moment
    /// the labels overflow they fold — names stay in ToolTip and automation names — and they
    /// come back when room is made. The Phase 1 look of five or fewer tabs never changes.
    /// </summary>
    private bool _tabStripCompact;

    /// <summary>Label widths are captured BEFORE folding; the expand calculation uses them.</summary>
    private readonly double[] _tabLabelWidths = new double[7];

    private readonly Button[] _tabStripButtons;
    private readonly TextBlock[] _tabStripLabels;

    private void OnHubTabStripSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTabStripCompactness();

    /// <summary>
    /// A tab-visibility change does not change HubTabs' real size (the panel inner width is
    /// always the same, ActualWidth does not change), so SizeChanged does not fire; the
    /// decision is deferred until after a layout pass.
    /// </summary>
    private void ScheduleTabStripCompactness()
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateTabStripCompactness));
    }

    private void UpdateTabStripCompactness()
    {
        double available = HubTabs.ActualWidth;
        if (available <= 1)
        {
            return;
        }

        if (!_tabStripCompact)
        {
            if (HubTabs.DesiredSize.Width > available)
            {
                // Capture the label widths BEFORE folding; the expand calculation needs them.
                for (int i = 0; i < _tabStripLabels.Length; i++)
                {
                    _tabLabelWidths[i] = _tabStripLabels[i].ActualWidth;
                }

                _tabStripCompact = true;
                FoldTabStripLabels(true);
                HubTabs.InvalidateMeasure();
            }

            return;
        }

        // Folded measure + widths of the visible labels: the labels come back if that still fits.
        double expandNeed = HubTabs.DesiredSize.Width;
        for (int i = 0; i < _tabStripLabels.Length; i++)
        {
            if (_tabStripButtons[i].Visibility == Visibility.Visible)
            {
                expandNeed += 6 + Math.Max(0, _tabLabelWidths[i]);
            }
        }

        if (expandNeed <= available)
        {
            _tabStripCompact = false;
            FoldTabStripLabels(false);
            // The next layout pass brings the labelled measure; the decision closes in one pass.
            HubTabs.InvalidateMeasure();

            // The estimate can be wrong (on the first fold a hidden tab's label had width
            // 0). Re-check with the real labelled DesiredSize after expanding: if it
            // fits the transition ends quietly, if it overflows the strip folds again — no loop.
            ScheduleTabStripCompactness();
        }
    }

    private void FoldTabStripLabels(bool fold)
    {
        foreach (TextBlock label in _tabStripLabels)
        {
            label.Visibility = fold ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>
    /// Attaches the service and detaches the previous one (idempotent). Does not build
    /// the list; the opening setup is done by <see cref="PresentHub"/>.
    /// </summary>
    internal void Attach(IslandService service)
    {
        Detach();
        _service = service;
        service.Changed += OnChanged;
    }

    internal void Detach()
    {
        // Invariant: an unbound panel never plays. The next tick stops itself
        // anyway, but the pin release should be unconditional here.
        StopPrompterPlayback();
        StopCameraBodyLifecycle();

        if (_service is not null)
        {
            _service.Changed -= OnChanged;
            _service = null;
        }

        _rows.Clear();
        _selectedIndex = -1;
        EntryListPanel.Children.Clear();
    }

    internal void AttachMedia(MediaSessionService service)
    {
        if (_media is not null)
        {
            _media.Changed -= OnMediaSnapshotChanged;
            _media.SessionsChanged -= OnMediaSnapshotChanged;
        }

        _media = service;
        _media.Changed += OnMediaSnapshotChanged;
        _media.SessionsChanged += OnMediaSnapshotChanged;
    }

    internal void DetachMedia()
    {
        StopTimelinePoll();
        StopPlayPulse();
        if (_media is not null)
        {
            _media.Changed -= OnMediaSnapshotChanged;
            _media.SessionsChanged -= OnMediaSnapshotChanged;
            _media = null;
        }
    }

    /// <summary>No clipboard button on the secondary monitor; the list is not attached either.</summary>
    internal void SetClipboardChrome(bool available)
    {
        TabClipboard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        TabWallpaper.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        // Strip invariant: secondary bars carry Music only, so even when the config is on,
        // body tabs (Color, Shelf, Camera) are hidden by this surface gate. The Color body
        // also locks screen sampling to the primary's work area (ScreenSampler is single-schedule);
        // the Shelf body looks at the drop service, which is attached only on the primary.
        _primarySurface = available;
        TabColor.Visibility = available && _pickerTabWanted ? Visibility.Visible : Visibility.Collapsed;
        TabShelf.Visibility = available && _shelfTabWanted ? Visibility.Visible : Visibility.Collapsed;
        TabPrompter.Visibility = available && _prompterTabWanted ? Visibility.Visible : Visibility.Collapsed;
        TabCamera.Visibility = available && _cameraTabWanted ? Visibility.Visible : Visibility.Collapsed;
        // The music body's "clipboard history" affordance exists only where there is a clipboard
        // to show (primary); the XAML declares it collapsed because that is the secondary default.
        ClipboardAffordance.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        ScheduleTabStripCompactness();
    }

    /// <summary>
    /// Surface gate for body tabs: because only the primary bar's hub can carry screen
    /// sampling (Color) and the drop service (Shelf), while <see cref="SetClipboardChrome"/>
    /// is small both tabs are hidden independently of config.
    /// </summary>
    private bool _primarySurface;

    /// <summary>Wanted state of the body tab: the bool setting is combined with the primary surface gate.</summary>
    private bool _pickerTabWanted = IslandConfig.DefaultColorPickerTab;

    /// <summary>Wanted state of the Shelf tab: the bool setting is combined with the primary surface gate.</summary>
    private bool _shelfTabWanted = IslandConfig.DefaultShelfTab;

    /// <summary>Wanted state of the Prompter tab: the bool setting is combined with the primary surface gate.</summary>
    private bool _prompterTabWanted = IslandConfig.DefaultPrompterTab;

    /// <summary>Wanted state of the Camera tab: the bool setting is combined with the primary surface gate.</summary>
    private bool _cameraTabWanted = IslandConfig.DefaultCameraTab;

    /// <summary>Shows or hides the Color picker tab; gated by <c>IslandConfig.ColorPickerTab</c>.</summary>
    internal void SetColorPickerTabVisible(bool visible)
    {
        _pickerTabWanted = visible;
        TabColor.Visibility = visible && _primarySurface ? Visibility.Visible : Visibility.Collapsed;
        ScheduleTabStripCompactness();
    }

    /// <summary>
    /// Scaled surface. This is not the popup child: WPF aligns a popup on the child's
    /// transformed corners, so a scale on <see cref="IslandFlyout"/> itself shifts the
    /// panel off the pill for the whole open.
    /// </summary>
    internal FrameworkElement PanelSurface => SurfaceRoot;

    /// <summary>
    /// Arranged panel width in DIP. Zero until layout; the XAML <c>Width</c> is not a fallback.
    /// </summary>
    internal double PanelWidthDip
    {
        get
        {
            double arranged = FlyoutRoot.ActualWidth;
            return IslandPopupAlign.WidthForPlacement(arranged);
        }
    }

    /// <summary>
    /// True when a point (in this control's coordinates) is inside the visible panel surface
    /// (<see cref="FlyoutRoot"/>), i.e. not in the transparent shadow pad around it.
    /// </summary>
    internal bool IsPointInsidePanel(Point point)
    {
        if (FlyoutRoot.ActualWidth <= 1 || FlyoutRoot.ActualHeight <= 1)
        {
            return false;
        }

        Point origin = FlyoutRoot.TranslatePoint(new Point(0, 0), this);
        return point.X >= origin.X && point.X <= origin.X + FlyoutRoot.ActualWidth
            && point.Y >= origin.Y && point.Y <= origin.Y + FlyoutRoot.ActualHeight;
    }

    /// <summary>The silhouette is painted with this fill so the pill and the panel share it.</summary>
    internal void ApplySurface()
    {
        _silhouetteFill = FlyoutSurface.Create();
        SilhouetteFill.Fill = _silhouetteFill;
        RebuildSilhouette();
    }

    /// <summary>
    /// Tells the silhouette where the pill sits: its width and the X of its left edge in panel
    /// coordinates (negative when the pill starts left of the panel; the panel can be clamped
    /// to the work area so this is not always (panel - pill) / 2).
    /// </summary>
    internal void SetJoin(double pillWidthDip, double pillXInPanelDip)
    {
        _pillWidthDip = Math.Max(0, pillWidthDip);
        _pillXDip = pillXInPanelDip;
        RebuildSilhouette();
    }

    private void RebuildSilhouette()
    {
        double w = FlyoutRoot.ActualWidth;
        double h = FlyoutRoot.ActualHeight;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        IslandSilhouetteShape shape = IslandSilhouette.Build(w, h, _pillXDip, _pillWidthDip, CornerDip, FilletDip);
        StreamGeometry fill = IslandSilhouetteGeometry.Create(shape.Fill, filled: true);
        SilhouetteFill.Data = fill;

        // The tint paints the very same frozen geometry, so it can never spill past the panel or
        // the concave fillets beside the pill (it is the panel-local silhouette, start to finish).
        SilhouetteTint.Data = fill;
        SilhouetteOutline.Data = IslandSilhouetteGeometry.Create(shape.Outline, filled: false);
        if (SilhouetteFill.Fill is null)
        {
            SilhouetteFill.Fill = _silhouetteFill;
        }

        SilhouetteOutline.Stroke = _outlineBrush;
    }

    /// <summary>
    /// Panel tint gradient: the album color starts right at the top of the joined surface (fillets
    /// and tab strip included) and strengthens toward the bottom. The stops are animated in place;
    /// the brush is never frozen. The top stop alpha is shared with the pill fill through
    /// <see cref="PanelJoinTintAlpha"/>, so the join line is one continuous color, not a seam.
    /// </summary>
    private static LinearGradientBrush CreatePanelTintBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        // The gradient box starts at the fillet tips (10 DIP above the panel top). Stop 0 sits at
        // that tip and stop 1 (0.06) on the panel's top edge / pill join line; both are painted at
        // the join alpha so the joined pill fill (same color + same alpha composite) continues the
        // surface with no seam. Stop positions below walk down the panel. All stops are transparent
        // until ApplyTint targets them, so the neutral surface has no accidental tint.
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.06));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.22));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0.60));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        return brush;
    }

    /// <summary>
    /// Top-lit rim alpha while a tint is active. Kept close to the pill's own stroke so the
    /// rim reading does not jump where the fillet meets the pill side; <see cref="JoinRimColor"/>
    /// hands the same top color to the pill border (TopBarWindow) so the outline is continuous.
    /// </summary>
    private const byte OutlineTopAlphaTinted = 0x55;
    private const byte OutlineBottomAlphaTinted = 0x1A;
    private const byte OutlineTopAlphaNeutral = 0x30;
    private const byte OutlineBottomAlphaNeutral = 0x0C;

    /// <summary>
    /// Top-lit rim on the neutral (untinted) surface. A white hairline is the dark-theme reading;
    /// on the light theme's near-white panel it disappears, so the rim flips to a dark hairline.
    /// High contrast uses the system window-text tone, which is black on HCWhite and white on
    /// HCBlack.
    /// </summary>
    private static Color NeutralOutlineColor(byte alpha)
        => AppThemePreference.Resolve() switch
        {
            ApplicationTheme.Light => Color.FromArgb(alpha, 0x00, 0x00, 0x00),
            ApplicationTheme.HighContrast => Color.FromArgb(
                alpha,
                SystemColors.WindowTextColor.R,
                SystemColors.WindowTextColor.G,
                SystemColors.WindowTextColor.B),
            _ => Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF),
        };

    private static Color OutlineTopColor(Color? tint)
        => tint is Color c
            ? Color.FromArgb(OutlineTopAlphaTinted, c.R, c.G, c.B)
            : NeutralOutlineColor(OutlineTopAlphaNeutral);

    private static Color OutlineBottomColor(Color? tint)
        => tint is Color c
            ? Color.FromArgb(OutlineBottomAlphaTinted, c.R, c.G, c.B)
            : NeutralOutlineColor(OutlineBottomAlphaNeutral);

    /// <summary>
    /// Top rim color for the joined pill's border. The panel's outline starts at the fillet tips
    /// with this same color, so the pill stroke and the panel rim read as one continuous edge.
    /// </summary>
    internal Color JoinRimColor => OutlineTopColor(_appliedTint);

    /// <summary>
    /// Album tint currently painted on the panel (null = neutral). The top bar reads it to fill
    /// the joined pill with the same tinted surface instead of a flat theme color.
    /// </summary>
    internal Color? SurfaceTint => _appliedTint;

    /// <summary>
    /// Tint amount the joined pill fill must use to match the panel's top edge: the same alpha the
    /// first panel gradient stops are painted with (<see cref="PanelJoinTintAlpha"/>).
    /// </summary>
    internal static double JoinTintAmount => PanelJoinTintAlpha / 255d;

    /// <summary>
    /// Raised after the album tint changed (track/tab/mode). The window uses it to re-tint the
    /// joined pill's border so the rim stays continuous with the panel.
    /// </summary>
    internal event EventHandler? SurfaceTintChanged;

    /// <summary>Top-lit rim: bright at the top, fading toward the bottom (animated to the tint when active).</summary>
    private static LinearGradientBrush CreateOutlineBrush()
        => new(
            NeutralOutlineColor(OutlineTopAlphaNeutral),
            NeutralOutlineColor(OutlineBottomAlphaNeutral),
            new Point(0, 0),
            new Point(0, 1));

    /// <summary>
    /// Re-applies the neutral / tinted rim for the current theme. The panel is its own popup window
    /// and never receives the bar's theme messages, so an open panel has to be told when the theme
    /// dictionary was swapped underneath it.
    /// </summary>
    internal void RefreshThemeRim() => ApplyOutlineTint(_appliedTint);

    /// <summary>
    /// Rebuilds the code-built hub rows after a light/dark (or accent) swap. Row and chip fills are
    /// single brushes read from the theme at build time — the hover/selection handlers exchange
    /// local brush values, so a live reference would be overwritten by the interaction. The rows are
    /// therefore rebuilt on the theme-change path instead (the panel is a popup window and never
    /// sees the bar's theme messages by itself). Selection is preserved by the rebuild methods.
    /// </summary>
    internal void RefreshThemeRows()
    {
        if (ShelfBody.Visibility == Visibility.Visible)
        {
            RebuildShelf();
        }

        if (ColorPickerBody.Visibility == Visibility.Visible)
        {
            RebuildPicker();
        }

        Rebuild();
    }

    /// <summary>Currently shown hub tab.</summary>
    internal IslandHubTab CurrentHubTab { get; private set; } = IslandHubTab.Clipboard;

    /// <summary>Raised only when the user clicks a hub tab (not when a tab is restored on open).</summary>
    internal event EventHandler<IslandHubTab>? HubTabChanged;

    private WallpaperService? _wallpaper;

    internal void AttachWallpaper(WallpaperService service)
    {
        _wallpaper = service;
        WallpaperHost.Attach(service);
    }

    internal void DetachWallpaper() => _wallpaper = null;

    /// <summary>Shows or hides the Music tab.</summary>
    internal void SetMusicTabVisible(bool visible)
    {
        TabMusic.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ScheduleTabStripCompactness();
    }

    /// <summary>Shows or hides the Shelf tab; gated by <c>IslandConfig.ShelfTab</c> and the primary surface.</summary>
    internal void SetShelfTabVisible(bool visible)
    {
        _shelfTabWanted = visible;
        TabShelf.Visibility = visible && _primarySurface ? Visibility.Visible : Visibility.Collapsed;
        ScheduleTabStripCompactness();
    }

    /// <summary>Shows or hides the Prompter tab; gated by <c>IslandConfig.PrompterTab</c> and the primary surface.</summary>
    internal void SetPrompterTabVisible(bool visible)
    {
        _prompterTabWanted = visible;
        TabPrompter.Visibility = visible && _primarySurface ? Visibility.Visible : Visibility.Collapsed;
        ScheduleTabStripCompactness();
    }

    /// <summary>Shows or hides the Camera tab; gated by <see cref="IslandConfig.CameraTab"/> and the primary surface.</summary>
    internal void SetCameraTabVisible(bool visible)
    {
        _cameraTabWanted = visible;
        TabCamera.Visibility = visible && _primarySurface ? Visibility.Visible : Visibility.Collapsed;
        ScheduleTabStripCompactness();
    }

    /// <summary>
    /// The "Pick screen color" click. The top bar handles this event: the island panel closes
    /// first, then the picker overlay covering the whole work area opens (the
    /// <see cref="ColorPickerWindow"/> overlay is the only topmost exception) and the hub comes
    /// back when the pick is done — the panel is managed by
    /// <c>TopBarWindow.Island</c> in the same class, so the ordering comes from there.
    /// </summary>
    internal event EventHandler? ScreenPickerRequested;

    /// <summary>
    /// Request from the on/off button in the camera row (true = on, false = off). The body does
    /// nothing beyond this event: <see cref="CameraMirrorHost.Apply(bool)"/> is called only from
    /// the top bar's handler — the single path. The answer comes back via
    /// <see cref="CameraMirrorHost.Updated"/> and refreshes the body.
    /// </summary>
    internal event EventHandler<bool>? CameraMirrorRequested;

    /// <summary>
    /// The microphone row is display-only: while the body is visible the mute state is
    /// refreshed every 2000 ms (Phase 2 timer lifetime pattern: set up on one decision, release on one decision).
    /// </summary>
    private DispatcherTimer? _micTimer;

    /// <summary>
    /// While the camera body is visible, live tracking is set up: the host's
    /// <see cref="CameraMirrorHost.Updated"/> subscription + the microphone timer. On a tab
    /// change and on panel close paths <see cref="StopCameraBodyLifecycle"/>
    /// releases it; both are idempotent, setting it up again is harmless.
    /// </summary>
    private void StartCameraBodyLifecycle()
    {
        CameraMirrorHost.Updated -= OnCameraMirrorHostUpdated;
        CameraMirrorHost.Updated += OnCameraMirrorHostUpdated;
        _micTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        _micTimer.Tick -= OnMicTimerTick;
        _micTimer.Tick += OnMicTimerTick;
        _micTimer.Start();
    }

    /// <summary>If the body is not visible, the subscription + timer are released; no tick is carried while hidden.</summary>
    private void StopCameraBodyLifecycle()
    {
        CameraMirrorHost.Updated -= OnCameraMirrorHostUpdated;
        _micTimer?.Stop();
    }

    /// <summary>Preview updates are raised from a background thread: the body refresh is moved to the UI.</summary>
    private void OnCameraMirrorHostUpdated(object? sender, EventArgs e)
        => _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshCameraBody));

    private void OnMicTimerTick(object? sender, EventArgs e) => ReadMicStatus();

    /// <summary>
    /// Refreshes the body from the host state. "Off" when there is no host; the live
    /// stream ("Camera" status) says "Active" in body language, and other
    /// states such as "No camera" are shown as-is. The button only mirrors the host
    /// state — there is no write path other than its own
    /// <see cref="CameraMirrorRequested"/> event.
    /// </summary>
    private void RefreshCameraBody()
    {
        CameraPreview? preview = CameraMirrorHost.Preview;
        if (preview is null)
        {
            CameraPreviewImage.Source = null;
            CameraPreviewPlaceholder.Visibility = Visibility.Visible;
            CameraStatusText.Text = Strings.Qs_Off;
            CameraToggleButton.IsChecked = false;
            return;
        }

        CameraPreviewImage.Source = preview.Frame;
        CameraPreviewPlaceholder.Visibility = preview.Frame is null ? Visibility.Visible : Visibility.Collapsed;
        CameraStatusText.Text = preview.Status == "Camera" ? "Active" : preview.Status;
        CameraToggleButton.IsChecked = preview.IsRunning;
    }

    /// <summary>Microphone status text: no answer → "Unknown", muted → "Muted", otherwise "Active".</summary>
    private void ReadMicStatus()
    {
        bool known = AudioEndpointVolume.TryGetCaptureMute(out bool muted);
        MicStatusText.Text = known ? (muted ? "Muted" : "Active") : "Unknown";
    }

    /// <summary>
    /// Camera on/off click: the body only RAISES the event; on its own it does
    /// NOT CALL <see cref="CameraMirrorHost.Apply(bool)"/> — the single path for Apply is the top bar
    /// handler: SetEnabled + Apply + record, and the answer comes back via Updated.
    /// </summary>
    private void OnCameraToggleClick(object sender, RoutedEventArgs e)
        => CameraMirrorRequested?.Invoke(this, CameraToggleButton.IsChecked ?? false);

    /// <summary>
    /// Clips the preview frame to corners of radius 14 (MusicCover clip pattern, scaled
    /// to size — if the clip had stayed without a radius the square corners would spill past the Border's rounding).
    /// </summary>
    private void OnCameraPreviewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        CameraPreviewHost.Clip = new RectangleGeometry(
            new Rect(0, 0, CameraPreviewHost.ActualWidth, CameraPreviewHost.ActualHeight), 14, 14);
    }

    /// <summary>
    /// Body Tag ids: returns the tab's XAML Tag string (<c>Tag="shelf"</c> etc.).
    /// An unknown value falls back to Clipboard, like <see cref="IslandHub.Normalize"/>.
    /// </summary>
    internal static string TabIdForBody(IslandHubTab tab) => tab switch
    {
        IslandHubTab.Wallpaper => "wallpaper",
        IslandHubTab.Music => "music",
        IslandHubTab.Shelf => "shelf",
        IslandHubTab.Prompter => "prompter",
        IslandHubTab.Camera => "camera",
        IslandHubTab.ColorPicker => "colorpicker",
        _ => "clipboard",
    };

    private FrameworkElement BodyFor(IslandHubTab tab) => tab switch
    {
        IslandHubTab.Wallpaper => WallpaperBody,
        IslandHubTab.Music => MusicBody,
        IslandHubTab.Shelf => ShelfBody,
        IslandHubTab.Prompter => PrompterBody,
        IslandHubTab.Camera => CameraBody,
        IslandHubTab.ColorPicker => ColorPickerBody,
        _ => ClipboardBody,
    };

    /// <summary>Same popup, one body of several. <paramref name="focus"/> only on a pinning click.</summary>
    internal void PresentHub(IslandHubTab tab, bool focus, bool crossfade = false)
    {
        ApplySurface();
        SetMusicTabVisible(_media?.Snapshot.HasSession ?? false);
        SetShelfTabVisible(_service?.Config.ShelfTab ?? IslandConfig.DefaultShelfTab);
        SetColorPickerTabVisible(_service?.Config.ColorPickerTab ?? IslandConfig.DefaultColorPickerTab);
        SetPrompterTabVisible(_service?.Config.PrompterTab ?? IslandConfig.DefaultPrompterTab);
        SetCameraTabVisible(_service?.Config.CameraTab ?? IslandConfig.DefaultCameraTab);
        CurrentHubTab = tab;
        _clipboardShown = tab != IslandHubTab.Music;
        UpdateTabChrome(tab);

        // Shelf selection lives only in the Shelf body: leaving the body (tab click, setting
        // change or body hide) drops the selection. Returning to the Shelf sets it up
        // without selection anyway; the rebuild itself <b>preserves</b> the selection.
        if (tab != IslandHubTab.Shelf)
        {
            ClearShelfSelection();
            ShowShelfDropTarget(false);
            // The drop result will not be rendered on another body: do not carry the owed
            // arrival animation into an unrelated shelf rebuild later.
            CancelShelfDrop();
        }

        // Don't tick once the body is hidden: stop playback when the tab changes (the pin stays
        // released; the scroll position is kept on Paused and resumes from where it left off).
        if (tab != IslandHubTab.Prompter && _prompterState == PrompterMotion.Playback.Playing)
        {
            SetPrompterPlayback(PrompterMotion.Playback.Paused);
        }

        // While the camera body is not visible no live source tracking is carried: on a tab change
        // the Updated subscription + microphone timer are released; the camera path sets them up on return.
        if (tab != IslandHubTab.Camera)
        {
            StopCameraBodyLifecycle();
        }

        // The play-button pulse belongs to the open Music body only; any other tab releases it.
        if (tab != IslandHubTab.Music)
        {
            StopPlayPulse();
        }

        switch (tab)
        {
            case IslandHubTab.Music:
                ApplyMusic();
                break;
            case IslandHubTab.Wallpaper:
                StopTimelinePoll();
                // The album tint belongs to the SMTC session, not to the hub tab: the panel keeps
                // its color when the user browses another body (docs: "The panel tints while an
                // SMTC session exists"). ApplyMusic is the only other tint caller and covers Music.
                ApplySessionTint();
                WallpaperHost.Refresh();
                break;
            case IslandHubTab.Shelf:
                StopTimelinePoll();
                ApplySessionTint();
                RebuildShelf();
                break;
            case IslandHubTab.Prompter:
                StopTimelinePoll();
                ApplySessionTint();
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    // Decide with the body visible + layout settled: extent/offset should not come back zero.
                    HookPrompterScroll();
                    RefreshPrompterPlayAvailability();
                }));
                break;
            case IslandHubTab.Camera:
                StopTimelinePoll();
                ApplySessionTint();
                RefreshCameraBody();
                ReadMicStatus();
                StartCameraBodyLifecycle();
                break;
            case IslandHubTab.ColorPicker:
                StopTimelinePoll();
                ApplySessionTint();
                RebuildPicker();
                break;
            default:
                StopTimelinePoll();
                ApplySessionTint();
                SearchBox.Clear();
                _selectedIndex = 0;
                Rebuild();
                break;
        }

        RevealHubBody(tab, crossfade);

        switch (tab)
        {
            case IslandHubTab.Music:
                if (!crossfade && MusicNow.Visibility == Visibility.Visible)
                {
                    Stagger(MusicCover, (UIElement)MusicTitle.Parent, MusicTransport);
                }

                ArmTimelinePoll();
                // The body is visible now, so the play pulse may run (it is suppressed while hidden).
                RefreshPlayPulse();
                if (focus)
                {
                    Focusable = true;
                    MusicPlayButton.Focus();
                }

                break;
            case IslandHubTab.Clipboard:
                if (focus)
                {
                    FocusClipboardSearch();
                }

                break;
            default:
                if (focus)
                {
                    Focusable = true;
                    Focus();
                }

                break;
        }
    }

    private void UpdateTabChrome(IslandHubTab tab)
    {
        foreach (Button b in new[] { TabClipboard, TabWallpaper, TabMusic, TabShelf, TabPrompter, TabCamera, TabColor })
        {
            if (b.Tag as string == IslandHub.ToConfig(tab))
            {
                // Subtle fill, not ControlFillColorSecondary: the latter is a 50 % white wash in the
                // light theme and the selected tab would be invisible on the near-white panel.
                b.SetResourceReference(BackgroundProperty, "SubtleFillColorSecondaryBrush");
            }
            else
            {
                b.ClearValue(BackgroundProperty);
            }
        }
    }

    private void OnHubTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        IslandHubTab tab = IslandHub.Normalize(tag);
        PresentHub(tab, focus: true, crossfade: true);
        HubTabChanged?.Invoke(this, tab);
    }

    private void RevealHubBody(IslandHubTab tab, bool crossfade)
    {
        FrameworkElement show = BodyFor(tab);
        FrameworkElement? hide = null;
        foreach (FrameworkElement body in new FrameworkElement[] { MusicBody, ClipboardBody, WallpaperBody, ShelfBody, PrompterBody, CameraBody, ColorPickerBody })
        {
            if (ReferenceEquals(body, show))
            {
                continue;
            }

            if (hide is null && body.Visibility == Visibility.Visible)
            {
                hide = body;
            }
            else
            {
                body.Visibility = Visibility.Collapsed;
            }
        }

        hide ??= ReferenceEquals(show, MusicBody) ? ClipboardBody : MusicBody;
        RevealBody(show, hide, crossfade);
    }

    /// <summary>Focuses the search box for the click that pins a hover-opened panel.</summary>
    internal void FocusClipboardSearch()
    {
        if (SearchHost.Visibility != Visibility.Visible)
        {
            Focusable = true;
            Focus();
            return;
        }

        SearchBox.Focus();
        if (!SearchBox.IsFocused)
        {
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => SearchBox.Focus()));
        }
    }

    /// <summary>
    /// Music and clipboard crossfade over ~150 ms inside the same popup. The height animates along;
    /// on first open the panel animation is enough and <paramref name="crossfade"/> stays off.
    /// </summary>
    private void RevealBody(FrameworkElement show, FrameworkElement hide, bool crossfade)
    {
        bool swap = crossfade
            && hide.Visibility == Visibility.Visible
            && show.Visibility != Visibility.Visible
            && SystemParameters.ClientAreaAnimation;
        if (!swap)
        {
            _bodySwapId++;
            StopBodyMotion(show, hide);
            hide.Visibility = Visibility.Collapsed;
            show.Opacity = 1;
            show.Visibility = Visibility.Visible;
            BodyHost.Height = double.NaN;
            return;
        }

        int id = ++_bodySwapId;
        double fromHeight = BodyHost.ActualHeight;
        if (fromHeight < 1)
        {
            fromHeight = hide.ActualHeight;
        }

        BodyHost.BeginAnimation(FrameworkElement.HeightProperty, null);
        BodyHost.Height = fromHeight;
        show.BeginAnimation(OpacityProperty, null);
        hide.BeginAnimation(OpacityProperty, null);
        show.Opacity = 0;
        show.Visibility = Visibility.Visible;
        show.Measure(new Size(380, double.PositiveInfinity));
        double toHeight = Math.Max(show.DesiredSize.Height, 1);

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(150);
        show.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        hide.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration) { EasingFunction = ease });

        var height = new DoubleAnimation(fromHeight, toHeight, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        height.Completed += (_, _) =>
        {
            if (id != _bodySwapId)
            {
                return;
            }

            StopBodyMotion(show, hide);
            hide.Visibility = Visibility.Collapsed;
            show.Opacity = 1;
            BodyHost.Height = double.NaN;
        };
        BodyHost.BeginAnimation(FrameworkElement.HeightProperty, height);
    }

    private void StopBodyMotion(FrameworkElement show, FrameworkElement hide)
    {
        show.BeginAnimation(OpacityProperty, null);
        hide.BeginAnimation(OpacityProperty, null);
        hide.Opacity = 1;
        BodyHost.BeginAnimation(FrameworkElement.HeightProperty, null);
    }

    /// <summary>
    /// Fades and lifts each part in turn. With system animations off nothing runs and the
    /// parts keep their final state.
    /// </summary>
    private static void Stagger(params UIElement[] parts)
    {
        bool animate = SystemParameters.ClientAreaAnimation;
        for (int i = 0; i < parts.Length; i++)
        {
            UIElement part = parts[i];
            if (!animate)
            {
                part.BeginAnimation(UIElement.OpacityProperty, null);
                part.ClearValue(UIElement.OpacityProperty);
                part.RenderTransform = Transform.Identity;
                continue;
            }

            var lift = new TranslateTransform(0, 6);
            part.RenderTransform = lift;
            TimeSpan begin = TimeSpan.FromMilliseconds(IslandMotion.StaggerDelayMs(i, animate));
            TimeSpan length = TimeSpan.FromMilliseconds(IslandMotion.Gate(180, animate));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            part.Opacity = 0;
            var opacityAnim = new DoubleAnimation(0, 1, length)
            {
                BeginTime = begin,
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd,
            };
            opacityAnim.Completed += (_, _) =>
            {
                if (!ReferenceEquals(part.RenderTransform, lift))
                {
                    return;
                }

                part.BeginAnimation(UIElement.OpacityProperty, null);
                part.ClearValue(UIElement.OpacityProperty);
                lift.BeginAnimation(TranslateTransform.YProperty, null);
                part.RenderTransform = Transform.Identity;
            };
            part.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            lift.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(6, 0, length) { BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
        }
    }

    private void OnMediaSnapshotChanged(object? sender, EventArgs e)
    {
        SetMusicTabVisible(_media?.Snapshot.HasSession ?? false);
        if (MusicBody.Visibility == Visibility.Visible && !_clipboardShown)
        {
            ApplyMusic();
        }

        // The panel tint is session-wide, so a session end / track change must retint the panel
        // even while a non-Music body is shown (ApplyMusic above only covers the Music body).
        ApplySessionTint();
        UpdateTimelineInterval();
    }

    private void ApplyMusic()
    {
        MediaSnapshot snapshot = _media?.Snapshot ?? MediaSnapshot.Idle;
        bool hasSession = snapshot.HasSession;
        MusicNow.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        MusicTransport.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        MusicEmpty.Visibility = hasSession ? Visibility.Collapsed : Visibility.Visible;

        // The source-app row stays visible with one session too (it names the app); the chevrons
        // only become usable when there is more than one SMTC session to switch between.
        bool multipleSessions = _media is { SessionIds.Count: > 1 };
        MusicSessions.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        MusicPreviousSessionButton.IsEnabled = multipleSessions;
        MusicNextSessionButton.IsEnabled = multipleSessions;
        // Clipboard-history affordance: only where a clipboard list exists (primary bar). The XAML
        // default is collapsed for the secondary bars; SetClipboardChrome also writes this state.
        ClipboardAffordance.Visibility = _primarySurface ? Visibility.Visible : Visibility.Collapsed;

        if (!hasSession)
        {
            if (MusicTitle.Text.Length > 0)
            {
                MusicTitle.Text = string.Empty;
            }

            if (MusicArtist.Text.Length > 0)
            {
                MusicArtist.Text = string.Empty;
            }

            SetCoverArt(null, animate: false);
            MusicTimeline.Visibility = Visibility.Collapsed;
            UpdatePlayGlyph(false);
            UpdatePlayingDot(false);
            RefreshPlayPulse();
            ApplyTint(null);
            return;
        }

        string title = string.IsNullOrEmpty(snapshot.Title) ? snapshot.AppName : snapshot.Title;
        if (MusicTitle.Text != title)
        {
            MusicTitle.Text = title;
        }

        if (MusicArtist.Text != snapshot.Artist)
        {
            MusicArtist.Text = snapshot.Artist;
        }

        if (MusicSessionName.Text != snapshot.AppName)
        {
            MusicSessionName.Text = snapshot.AppName;
        }

        SetCoverArt(snapshot.Thumbnail, animate: true);
        UpdatePlayGlyph(snapshot.IsPlaying);
        UpdatePlayingDot(snapshot.IsPlaying);
        RefreshPlayPulse();

        // A paused track keeps its album tint: the panel follows the loaded track, not the play state.
        ApplyTint(IslandTintSource.Resolve(ColorMode, snapshot.Thumbnail, hasSession));
        ApplyTimeline(snapshot);
    }

    /// <summary>Canonical <c>island.colorMode</c>; set by the owning top bar from config.</summary>
    internal string ColorMode
    {
        get => _colorMode;
        set
        {
            if (_colorMode == value)
            {
                return;
            }

            _colorMode = value;
            // The tint follows the SMTC session, not the visible hub tab: a mode change must
            // retint (or neutralize) the panel even while another body is shown.
            ApplySessionTint();
        }
    }

    private string _colorMode = IslandConfig.DefaultColorMode;
    private bool _clipboardShown;

    /// <summary>
    /// Tint the whole panel follows: the album color of the current SMTC session (also when
    /// paused), resolved by the same extractor as the closed capsule. Null when there is no
    /// session or <c>island.colorMode</c> is neutral/system-without-accent-resolution. This is
    /// deliberately independent of the hub tab, so the panel does not lose its color when the
    /// user browses Clipboard/Wallpaper/Shelf/Prompter/Camera/Color.
    /// </summary>
    private Color? SessionTint()
    {
        MediaSnapshot snapshot = _media?.Snapshot ?? MediaSnapshot.Idle;
        return snapshot.HasSession
            ? IslandTintSource.Resolve(ColorMode, snapshot.Thumbnail, musicPlaying: true)
            : null;
    }

    /// <summary>Re-applies the session tint to the panel surface (see <see cref="SessionTint"/>).</summary>
    private void ApplySessionTint() => ApplyTint(SessionTint());

    /// <summary>
    /// Paints the album tint on the panel silhouette, the rim, the cover bloom, the seek fill and
    /// the play/pause circle. Null restores the neutral theme look. Retargeting the existing
    /// gradient stops makes a track change a smooth fade; the call is a no-op when the tint did not
    /// change (the timeline poll raises the media snapshot several times per second).
    /// </summary>
    internal void ApplyTint(Color? tint)
    {
        if (_tintApplied && tint == _appliedTint)
        {
            return;
        }

        _tintApplied = true;
        _appliedTint = tint;
        ApplyPanelTint(tint);
        ApplyOutlineTint(tint);
        ApplyCoverGlow(tint);
        ApplyProgressTint(tint);
        ApplyPlayButtonTint(tint);
        SurfaceTintChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyPanelTint(Color? tint)
    {
        if (_panelTintBrush is null)
        {
            return;
        }

        IReadOnlyList<Color> stops = PanelTintStops(tint);
        for (int i = 0; i < stops.Count && i < _panelTintBrush.GradientStops.Count; i++)
        {
            AnimateGradientStop(_panelTintBrush.GradientStops[i], stops[i], TintFadeMs);
        }
    }

    /// <summary>
    /// The five panel-tint stops. The first two share the join alpha (fillets, panel top edge and
    /// the joined pill fill all resolve to the same composite), then the wash strengthens over the
    /// tab strip/header and gets unmistakable through the body, with the last stop deepened toward
    /// black so the panel keeps depth instead of a flat color. Alphas are chosen against the dark
    /// theme surface: even the lightest cover tint keeps primary text at AA contrast in the body
    /// band (asserted by IslandTintTests).
    /// </summary>
    private static Color[] PanelTintStops(Color? tint)
    {
        if (tint is not Color c)
        {
            return [Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent, Colors.Transparent];
        }

        var color = new IslandRgb(c.R, c.G, c.B);
        IslandRgb deep = IslandPalette.Blend(color, new IslandRgb(0, 0, 0), IslandPalette.PanelTintDeepShade);
        return
        [
            Color.FromArgb(IslandPalette.PanelTintJoinAlpha, c.R, c.G, c.B),
            Color.FromArgb(IslandPalette.PanelTintJoinAlpha, c.R, c.G, c.B),
            Color.FromArgb(IslandPalette.PanelTintHeaderAlpha, c.R, c.G, c.B),
            Color.FromArgb(IslandPalette.PanelTintBodyAlpha, c.R, c.G, c.B),
            Color.FromArgb(IslandPalette.PanelTintBottomAlpha, deep.R, deep.G, deep.B),
        ];
    }

    private void ApplyOutlineTint(Color? tint)
    {
        if (_outlineBrush is null)
        {
            return;
        }

        AnimateGradientStop(_outlineBrush.GradientStops[0], OutlineTopColor(tint), TintFadeMs);
        AnimateGradientStop(_outlineBrush.GradientStops[1], OutlineBottomColor(tint), TintFadeMs);
    }

    /// <summary>Soft album-color bloom behind the rounded cover; hidden when there is no tint.</summary>
    private void ApplyCoverGlow(Color? tint)
    {
        if (tint is not Color c)
        {
            MusicCoverGlow.BeginAnimation(UIElement.OpacityProperty, null);
            MusicCoverGlow.Opacity = 0;
            return;
        }

        var bloom = new RadialGradientBrush(
            Color.FromArgb(0x8C, c.R, c.G, c.B),
            Color.FromArgb(0x00, c.R, c.G, c.B))
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        bloom.Freeze();
        MusicCoverGlow.Background = bloom;
        AnimateOpacity(MusicCoverGlow, 1, TintFadeMs);
    }

    /// <summary>Seek fill, thumb rim and the playing dot follow the active tint (accent when neutral).</summary>
    private void ApplyProgressTint(Color? tint)
    {
        if (tint is Color c)
        {
            var solid = new SolidColorBrush(c);
            solid.Freeze();
            MusicProgressFill.Background = solid;
            MusicProgressThumb.BorderBrush = solid;
            MusicPlayingDot.Background = solid;
            return;
        }

        MusicProgressFill.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
        MusicProgressThumb.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
        MusicPlayingDot.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
    }

    /// <summary>
    /// Play/pause circle follows the active album tint, with the system accent as the fallback
    /// (neutral color mode / no tint). The glyph is switched to a readable color from the actual
    /// fill, and the ring pulse around the circle takes the same hue.
    /// </summary>
    private void ApplyPlayButtonTint(Color? tint)
    {
        _playButtonTint = tint;
        UpdatePlayButtonFill();
    }

    /// <summary>
    /// Repaints the play circle for <see cref="_playButtonTint"/> and the current pointer state.
    /// With a tint the fill is the album color (lifted slightly on hover), so the circle no longer
    /// reads as the beige system accent; without one the theme accent + its secondary hover fill
    /// stay exactly as the style defined them.
    /// </summary>
    private void UpdatePlayButtonFill(bool? hover = null)
    {
        bool over = hover ?? MusicPlayButton.IsMouseOver;
        if (_playButtonTint is not Color c)
        {
            MusicPlayButton.SetResourceReference(
                Button.BackgroundProperty,
                over ? "AccentFillColorSecondaryBrush" : "AccentFillColorDefaultBrush");
            MusicPlayButton.SetResourceReference(Control.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
            MusicPlayPulse.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
            return;
        }

        var tint = new IslandRgb(c.R, c.G, c.B);
        IslandRgb fill = over
            ? IslandPalette.Blend(tint, new IslandRgb(0xFF, 0xFF, 0xFF), PlayButtonHoverLift)
            : tint;
        IslandRgb glyph = IslandPalette.ReadableForeground(fill);
        MusicPlayButton.Background = FrozenBrush(Color.FromRgb(fill.R, fill.G, fill.B));
        MusicPlayButton.Foreground = FrozenBrush(Color.FromRgb(glyph.R, glyph.G, glyph.B));
        MusicPlayPulse.BorderBrush = FrozenBrush(c);
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private bool _writingSeek;

    /// <summary>
    /// The designed track is shown only when SMTC gives a position and a duration. The slider is
    /// the interaction layer only while <see cref="MediaSnapshot.CanSeek"/>; otherwise the same
    /// track is a read-only bar. The label shows the real clock, never an invented percentage.
    /// </summary>
    private void ApplyTimeline(MediaSnapshot snapshot)
    {
        string? clock = snapshot.TimelineText();
        if (clock is null || snapshot.Position is not TimeSpan position || snapshot.Duration is not TimeSpan duration
            || duration <= TimeSpan.Zero)
        {
            MusicTimeline.Visibility = Visibility.Collapsed;
            MusicSeek.Visibility = Visibility.Collapsed;
            MusicProgressThumb.Visibility = Visibility.Collapsed;
            MusicProgressTrack.Visibility = Visibility.Collapsed;
            return;
        }

        if (MusicTime.Text != clock)
        {
            MusicTime.Text = clock;
        }

        System.Windows.Automation.AutomationProperties.SetName(MusicTime, clock);
        MusicTimeline.Visibility = Visibility.Visible;
        MusicProgressTrack.Visibility = Visibility.Visible;

        bool seekable = snapshot.CanSeek;
        MusicSeek.Visibility = seekable ? Visibility.Visible : Visibility.Collapsed;
        MusicProgressThumb.Visibility = seekable ? Visibility.Visible : Visibility.Collapsed;
        if (seekable)
        {
            double maximum = duration.TotalMilliseconds;
            if (Math.Abs(MusicSeek.Maximum - maximum) > 1)
            {
                MusicSeek.Maximum = maximum;
            }

            if (!MusicSeek.IsMouseCaptureWithin)
            {
                _writingSeek = true;
                MusicSeek.Value = Math.Clamp(position.TotalMilliseconds, 0, maximum);
                _writingSeek = false;
            }

            double ratio = MusicSeek.IsMouseCaptureWithin && maximum > 0
                ? MusicSeek.Value / maximum
                : position.TotalMilliseconds / maximum;
            UpdateProgressVisual(ratio);
            return;
        }

        UpdateProgressVisual(position.TotalMilliseconds / duration.TotalMilliseconds);
    }

    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_writingSeek || MusicSeek.Maximum <= 0)
        {
            return;
        }

        UpdateProgressVisual(e.NewValue / MusicSeek.Maximum);
    }

    /// <summary>
    /// Paints the fill and the thumb from a 0..1 ratio. The thumb is centered on the fill edge
    /// (half a thumb inset at both ends), which is also how the invisible Slider thumb is
    /// arranged, so dragging and painting stay aligned.
    /// </summary>
    private void UpdateProgressVisual(double ratio)
    {
        _progressRatio = Math.Clamp(ratio, 0, 1);
        double width = MusicProgressTrack.ActualWidth;
        if (width <= 1)
        {
            // Not laid out yet; PositionProgressThumb re-runs from SizeChanged with a real width.
            return;
        }

        double center = (SeekThumbDip / 2) + (_progressRatio * Math.Max(0, width - SeekThumbDip));
        double fraction = Math.Clamp(center / width, 0, 1);
        MusicProgressDone.Width = new GridLength(fraction, GridUnitType.Star);
        MusicProgressRest.Width = new GridLength(Math.Max(0, 1 - fraction), GridUnitType.Star);
        Canvas.SetLeft(MusicProgressThumb, center - (SeekThumbDip / 2));
    }

    private void PositionProgressThumb()
    {
        if (MusicProgressTrack.ActualWidth > 1)
        {
            UpdateProgressVisual(_progressRatio);
        }
    }

    private void OnSeekPointerUp(object sender, MouseButtonEventArgs e) => CommitSeek();

    private void CommitSeek()
    {
        if (_writingSeek || MusicSeek.Visibility != Visibility.Visible)
        {
            return;
        }

        _media?.TrySeek(TimeSpan.FromMilliseconds(MusicSeek.Value));
    }

    private void ArmTimelinePoll()
    {
        _timelineArmed = true;
        _timelineTimer ??= new DispatcherTimer { Interval = TimelinePollPlaying };
        _timelineTimer.Tick -= OnTimelineTick;
        _timelineTimer.Tick += OnTimelineTick;
        UpdateTimelineInterval();
        _timelineTimer.Start();
        _media?.RefreshTimeline();
    }

    /// <summary>~250 ms while playing (the service interpolates); the slower cadence while paused.</summary>
    private void UpdateTimelineInterval()
    {
        if (_timelineTimer is null)
        {
            return;
        }

        TimeSpan interval = _media?.Snapshot.IsPlaying == true ? TimelinePollPlaying : TimelinePollPaused;
        if (_timelineTimer.Interval != interval)
        {
            _timelineTimer.Interval = interval;
        }
    }

    private void StopTimelinePoll()
    {
        _timelineArmed = false;
        _timelineTimer?.Stop();
    }

    private void OnTimelineTick(object? sender, EventArgs e)
    {
        if (!_timelineArmed || MusicBody.Visibility != Visibility.Visible)
        {
            StopTimelinePoll();
            return;
        }

        UpdateTimelineInterval();
        _media?.RefreshTimeline();
    }

    /// <summary>
    /// Swaps the play/pause glyphs with a short crossfade. Reduced motion sets the final opacities
    /// instantly; the glyph pair never re-runs for the same state (the snapshot changes per tick).
    /// </summary>
    private void UpdatePlayGlyph(bool playing)
    {
        if (_glyphPlaying == playing)
        {
            return;
        }

        _glyphPlaying = playing;
        AnimateOpacity(MusicPlayGlyph, playing ? 0 : 1, GlyphFadeMs);
        AnimateOpacity(MusicPauseGlyph, playing ? 1 : 0, GlyphFadeMs);
    }

    /// <summary>Bright dot while playing, dim while paused; no churn on the per-tick snapshot updates.</summary>
    private void UpdatePlayingDot(bool playing)
    {
        if (_dotPlaying == playing)
        {
            return;
        }

        _dotPlaying = playing;
        AnimateOpacity(MusicPlayingDot, playing ? 1 : 0.35, 180);
    }

    /// <summary>Cover art crossfade: the new bitmap fades in over the old one on a second image layer.</summary>
    private void SetCoverArt(ImageSource? source, bool animate)
    {
        ImageSource? current = MusicArt.Source;
        if (ReferenceEquals(current, source))
        {
            if (MusicArtFade.Source is not null)
            {
                // A pending crossfade still points at an older bitmap: drop it, the base already matches.
                _coverFadeId++;
                MusicArtFade.BeginAnimation(UIElement.OpacityProperty, null);
                MusicArtFade.Opacity = 0;
                MusicArtFade.Source = null;
                MusicArtFade.Visibility = Visibility.Collapsed;
            }

            MusicPlaceholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
            MusicArt.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
            return;
        }

        if (source is null)
        {
            // No artwork: drop both layers at once instead of fading to the placeholder.
            _coverFadeId++;
            MusicArtFade.BeginAnimation(UIElement.OpacityProperty, null);
            MusicArtFade.Opacity = 0;
            MusicArtFade.Source = null;
            MusicArtFade.Visibility = Visibility.Collapsed;
            MusicArt.Source = null;
            MusicArt.Visibility = Visibility.Collapsed;
            MusicPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        MusicPlaceholder.Visibility = Visibility.Collapsed;
        MusicArt.Visibility = Visibility.Visible;
        int id = ++_coverFadeId;
        bool crossfade = animate && current is not null && SystemParameters.ClientAreaAnimation;
        if (!crossfade)
        {
            MusicArtFade.BeginAnimation(UIElement.OpacityProperty, null);
            MusicArtFade.Opacity = 0;
            MusicArtFade.Source = null;
            MusicArtFade.Visibility = Visibility.Collapsed;
            MusicArt.Source = source;
            return;
        }

        MusicArtFade.Source = source;
        MusicArtFade.Visibility = Visibility.Visible;
        AnimateOpacity(MusicArtFade, 1, CoverFadeMs, () =>
        {
            if (id != _coverFadeId)
            {
                return;
            }

            MusicArt.Source = source;
            MusicArt.Visibility = Visibility.Visible;
            MusicArtFade.BeginAnimation(UIElement.OpacityProperty, null);
            MusicArtFade.Opacity = 0;
            MusicArtFade.Source = null;
            MusicArtFade.Visibility = Visibility.Collapsed;
        });
    }

    private void OnMusicCoverSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // WPF Border does not clip its child, so the rounded cover needs its own clip (scaled to size).
        if (MusicCover.ActualWidth <= 0 || MusicCover.ActualHeight <= 0)
        {
            return;
        }

        MusicCover.Clip = new RectangleGeometry(
            new Rect(0, 0, MusicCover.ActualWidth, MusicCover.ActualHeight), 18, 18);
    }

    /// <summary>Starts the ring pulse only while playing, visible and with client-area animation on.</summary>
    private void RefreshPlayPulse()
    {
        if (MusicBody.Visibility == Visibility.Visible
            && !_clipboardShown
            && _media?.Snapshot.IsPlaying == true
            && SystemParameters.ClientAreaAnimation)
        {
            StartPlayPulse();
            return;
        }

        StopPlayPulse();
    }

    private void StartPlayPulse()
    {
        if (_playPulseRunning)
        {
            return;
        }

        _playPulseRunning = true;
        var scale = new ScaleTransform(0.86, 0.86);
        MusicPlayPulse.RenderTransformOrigin = new Point(0.5, 0.5);
        MusicPlayPulse.RenderTransform = scale;
        var length = TimeSpan.FromMilliseconds(1500);
        var ease = new SineEase { EasingMode = EasingMode.EaseOut };
        MusicPlayPulse.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.55, 0, length) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.86, 1.18, length) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.86, 1.18, length) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
    }

    private void StopPlayPulse()
    {
        if (!_playPulseRunning && MusicPlayPulse.Opacity <= 0)
        {
            return;
        }

        _playPulseRunning = false;
        MusicPlayPulse.BeginAnimation(UIElement.OpacityProperty, null);
        MusicPlayPulse.Opacity = 0;
        if (MusicPlayPulse.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }
    }

    private void OnTransportHover(object sender, MouseEventArgs e)
    {
        AnimateControlScale(sender as FrameworkElement, 1.06, 140);
        // The play circle's hover fill is code-driven (IslandPlayButton has no accent hover
        // trigger), so a tinted circle would otherwise keep its resting fill while hovered.
        if (ReferenceEquals(sender, MusicPlayButton))
        {
            UpdatePlayButtonFill(hover: true);
        }
    }

    private void OnTransportLeave(object sender, MouseEventArgs e)
    {
        AnimateControlScale(sender as FrameworkElement, 1.0, 200);
        if (ReferenceEquals(sender, MusicPlayButton))
        {
            UpdatePlayButtonFill(hover: false);
        }
    }

    private void OnTransportPress(object sender, MouseButtonEventArgs e)
        => AnimateControlScale(sender as FrameworkElement, 0.92, 80);

    private void OnTransportRelease(object sender, MouseButtonEventArgs e)
        => AnimateControlScale(sender as FrameworkElement, sender is UIElement { IsMouseOver: true } ? 1.06 : 1.0, 180);

    /// <summary>Hover/press scale for the transport buttons; reduced motion makes it an instant set.</summary>
    private static void AnimateControlScale(FrameworkElement? element, double to, double ms)
    {
        if (element is null)
        {
            return;
        }

        if (element.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = scale;
        }

        double duration = IslandMotion.Gate(ms, SystemParameters.ClientAreaAnimation);
        if (duration <= 0)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = to;
            scale.ScaleY = to;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var length = TimeSpan.FromMilliseconds(duration);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, length) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, length) { EasingFunction = ease });
    }

    /// <summary>
    /// Animated opacity change in the "set the local value, animate toward it" style: the final
    /// value is the local base, so a stopped or completed clock never snaps back. With client-area
    /// animation off the value is set instantly.
    /// </summary>
    private static void AnimateOpacity(UIElement element, double to, double ms, Action? completed = null)
    {
        double from = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = to;
        double duration = IslandMotion.Gate(ms, SystemParameters.ClientAreaAnimation);
        if (duration <= 0 || Math.Abs(from - to) < 0.001)
        {
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(duration))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        element.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>Retargets a gradient stop: base value first, then a FillBehavior.Stop animation from the live value.</summary>
    private static void AnimateGradientStop(GradientStop stop, Color target, double ms)
    {
        Color from = stop.Color;
        stop.BeginAnimation(GradientStop.ColorProperty, null);
        stop.Color = target;
        double duration = IslandMotion.Gate(ms, SystemParameters.ClientAreaAnimation);
        if (duration <= 0 || from == target)
        {
            return;
        }

        stop.BeginAnimation(
            GradientStop.ColorProperty,
            new ColorAnimation(from, target, TimeSpan.FromMilliseconds(duration))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop,
            });
    }

    private void OnPreviousTrackClick(object sender, RoutedEventArgs e) => _media?.Previous();

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _media?.TogglePlayPause();

    private void OnNextTrackClick(object sender, RoutedEventArgs e) => _media?.Next();

    private void OnPreviousSessionClick(object sender, RoutedEventArgs e) => _media?.SelectPrevious();

    private void OnNextSessionClick(object sender, RoutedEventArgs e) => _media?.SelectNext();

    /// <summary>
    /// Panel closed. The live subscription is released: rebuilding the list on every copy
    /// while hidden is not free work for the top bar, which is a registered AppBar.
    /// </summary>
    internal void OnPanelClosed()
    {
        StopTimelinePoll();
        StopPlayPulse();
        StopPrompterPlayback();
        Detach();

        // Body closed: the shelf selection does not carry into the next open (the chips are rebuilt anyway).
        ClearShelfSelection();
        // The drag preview glow and any owed drop animation belong to the open panel only.
        ShowShelfDropTarget(false);
        CancelShelfDrop();
    }

    /// <summary>
    /// <see cref="IslandService.Changed"/> is raised only on the UI dispatcher
    /// (<c>IslandService.RaiseChanged</c>); no hop is needed.
    /// </summary>
    private void OnChanged(object? sender, EventArgs e)
    {
        if (_service is { Config.ShelfTab: false })
        {
            // Tab turned off in settings: it drops out of the strip (same principle as the Music update).
            // If its body is still open it is not deferred: it returns to the Clipboard body immediately.
            SetShelfTabVisible(false);
            if (ShelfBody.Visibility == Visibility.Visible)
            {
                PresentHub(IslandHubTab.Clipboard, focus: false, crossfade: true);
            }
        }
        else if (ShelfBody.Visibility == Visibility.Visible)
        {
            RebuildShelf();
        }

        if (_service is { Config.ColorPickerTab: false })
        {
            SetColorPickerTabVisible(false);
            if (ColorPickerBody.Visibility == Visibility.Visible)
            {
                PresentHub(IslandHubTab.Clipboard, focus: false, crossfade: true);
            }
        }
        else if (ColorPickerBody.Visibility == Visibility.Visible)
        {
            RebuildPicker();
        }

        if (_service is { Config.PrompterTab: false })
        {
            SetPrompterTabVisible(false);
            if (PrompterBody.Visibility == Visibility.Visible)
            {
                PresentHub(IslandHubTab.Clipboard, focus: false, crossfade: true);
            }
        }

        if (_service is { Config.CameraTab: false })
        {
            // Camera tab turned off in settings: it drops out of the strip like Shelf/Color/Prompter and
            // returns to Clipboard immediately if the body is open (PresentHub releases the lifecycle too).
            SetCameraTabVisible(false);
            if (CameraBody.Visibility == Visibility.Visible)
            {
                PresentHub(IslandHubTab.Clipboard, focus: false, crossfade: true);
            }
        }

        Rebuild();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        if (_service is null)
        {
            return;
        }

        bool historyEmpty = _service.Entries.Count == 0 && SearchBox.Text.Length == 0;
        ClipboardEmpty.Visibility = historyEmpty ? Visibility.Visible : Visibility.Collapsed;
        SearchHost.Visibility = historyEmpty ? Visibility.Collapsed : Visibility.Visible;
        ListScroll.Visibility = historyEmpty ? Visibility.Collapsed : Visibility.Visible;

        string? query = SearchBox.Text;
        List<IslandEntry> matches = _service.Entries
            .Where(entry => IslandPruner.Matches(entry, query))
            .ToList();

        int shown = Math.Min(matches.Count, MaxRenderedRows);
        if (_selectedIndex >= shown)
        {
            _selectedIndex = shown - 1;
        }

        _rows.Clear();
        for (int i = 0; i < shown; i++)
        {
            _rows.Add(matches[i]);
        }

        // The theme brush is resolved once per rebuild, not per row setup: looking up a resource
        // 200 times for 200 rows is unnecessary.
        Brush selectedBrush = ResolveSelectedBrush();

        EntryListPanel.Children.Clear();
        for (int i = 0; i < _rows.Count; i++)
        {
            EntryListPanel.Children.Add(BuildRow(_rows[i], i == _selectedIndex, selectedBrush));
        }

        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 && !historyEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (historyEmpty)
        {
            HintText.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateHint(matches.Count, shown);
        ScrollSelectedIntoView();
    }

    private Border BuildRow(IslandEntry entry, bool selected, Brush selectedBrush)
    {
        Brush restingBrush = selected ? selectedBrush : Brushes.Transparent;
        var row = new Border
        {
            Padding = new Thickness(8, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(12),
            Background = restingBrush,
            Cursor = Cursors.Hand,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FontFamily iconFont = ResolveIconFont();

        var tile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = ResolveTileBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = entry.Kind == IslandEntryKind.File ? FileGlyph : TextGlyph,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        grid.Children.Add(tile);

        var stack = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = IslandPruner.FormatPreview(entry.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = BuildMeta(entry),
            FontSize = 11,
            Opacity = 0.6,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        var delete = new Button
        {
            Content = new TextBlock
            {
                Text = DeleteGlyph,
                FontFamily = iconFont,
                FontSize = 12,
            },
            ToolTip = "Delete",
            Focusable = false,
            Visibility = Visibility.Hidden,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(delete, "Delete");
        if (TryFindResource("IslandIconButton") is Style iconButtonStyle)
        {
            delete.Style = iconButtonStyle;
        }

        delete.Click += (_, e) =>
        {
            e.Handled = true;
            // Rebuild() shortens _selectedIndex itself (same as the Delete key path).
            _service?.Remove(entry);
        };
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);

        row.Child = grid;

        // Only the name is visible on a file row; the full path is in the tooltip (where the copy
        // lives is not obvious from the list).
        if (entry.Kind == IslandEntryKind.File && entry.FilePath is { Length: > 0 } path)
        {
            row.ToolTip = path;
        }

        row.MouseEnter += (_, _) =>
        {
            row.Background = selectedBrush;
            delete.Visibility = Visibility.Visible;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = restingBrush;
            delete.Visibility = Visibility.Hidden;
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            // Button already handles MouseLeftButtonUp; still, the event coming from the delete
            // button should not activate the row.
            if (e.OriginalSource is DependencyObject source && IsInside(source, delete))
            {
                return;
            }

            Activate(entry);
        };
        return row;
    }

    private static bool IsInside(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return false;
    }

    private static FontFamily ResolveIconFont() =>
        Application.Current?.TryFindResource("AppIconFontFamily") as FontFamily
        ?? new FontFamily("Segoe MDL2 Assets");
    /// <summary>The "source app · relative time" label at the right of the row.</summary>
    private static string BuildMeta(IslandEntry entry)
    {
        string relative = IslandPruner.FormatRelativeTime(entry.CapturedAtUtc, DateTimeOffset.UtcNow);
        if (string.IsNullOrEmpty(entry.SourceApp))
        {
            return relative;
        }

        string app = entry.SourceApp.Length > IslandPruner.SourceAppMaxChars
            ? entry.SourceApp[..IslandPruner.SourceAppMaxChars]
            : entry.SourceApp;
        return string.Format(CultureInfo.CurrentCulture, "{0} · {1}", app, relative);
    }

    /// <summary>
    /// Hover tile behind a row glyph. <c>ControlFillColorSecondary</c> is a white wash in the light
    /// theme (#80F9F9F9) and disappears on the near-white panel; the subtle fill is black-alpha in
    /// light and white-alpha in dark, i.e. it stays visible in both.
    /// </summary>
    private static Brush ResolveTileBrush()
    {
        if (Application.Current?.TryFindResource("SubtleFillColorSecondaryBrush") is Brush brush)
        {
            return brush;
        }

        var fallback = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        fallback.Freeze();
        return fallback;
    }

    /// <summary>Hover / selection fill for rows and chips (same light-theme reason as <see cref="ResolveTileBrush"/>).</summary>
    private static Brush ResolveSelectedBrush()
    {
        if (Application.Current?.TryFindResource("SubtleFillColorSecondaryBrush") is Brush brush)
        {
            return brush;
        }

        // Don't let the selection become invisible if the theme dictionary is not loaded (first-open race).
        var fallback = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        fallback.Freeze();
        return fallback;
    }

    /// <summary>Footer line: the empty list, or the number of entries not built because of the limit.</summary>
    private void UpdateHint(int total, int shown)
    {
        if (total == 0)
        {
            HintText.Text = SearchBox.Text.Length == 0 ? Strings.Island_NoEntries : Strings.Island_NoMatchingEntries;
            HintText.Visibility = Visibility.Visible;
            return;
        }

        if (total > shown)
        {
            HintText.Text = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Island_MoreEntriesFormat,
                total - shown);
            HintText.Visibility = Visibility.Visible;
            return;
        }

        HintText.Visibility = Visibility.Collapsed;
    }

    private void ScrollSelectedIntoView()
    {
        if (_selectedIndex < 0 || _selectedIndex >= EntryListPanel.Children.Count)
        {
            return;
        }

        // While navigating with the keyboard the selected row can fall outside the scroll area.
        if (EntryListPanel.Children[_selectedIndex] is FrameworkElement row)
        {
            row.BringIntoView();
        }
    }

    private void Activate(IslandEntry entry)
    {
        if (_service is null)
        {
            return;
        }

        if (entry.Kind == IslandEntryKind.File)
        {
            OpenFile(entry.FilePath);
            return;
        }

        // The text entry is put back on the clipboard. PutBack does not re-capture its own copy
        // (it writes CanIncludeInClipboardHistory=0) — otherwise the same entry would be listed twice.
        _service.PutBack(entry);
    }

    /// <summary>
    /// Opens the shelf entry with the default application (a file <b>or</b> a folder); the file is not deleted.
    /// </summary>
    /// <remarks>
    /// The path comes from <c>island.json</c>, i.e. it is an <b>untrusted</b> input. Opening goes
    /// through the <see cref="IslandService.OpenShelfFile"/> gate: a real folder is accepted
    /// (<c>UseShellExecute</c> opens Explorer); a device path, a folder with a reparse point and an
    /// escape component are rejected on the <see cref="IslandService.CanOpenShelfFile"/> side; launch
    /// failures are logged there too and return <c>false</c>.
    /// </remarks>
    private void OpenFile(string? path)
    {
        if (_service is null || path is null)
        {
            return;
        }

        _ = _service.OpenShelfFile(path);
    }

    #region Shelf body

    /// <summary>
    /// Selected shelf chips — the entry <b>id</b> is kept, not the chip order: the selection must
    /// survive the rebuilds triggered by clipboard/media notifications. The id of an entry that
    /// drops out of the list or is pruned is cleared silently once it is not found in the fresh list.
    /// </summary>
    private readonly HashSet<long> _shelfSelection = [];

    /// <summary>Anchor of the Shift range selection (entry id); <c>-1</c> when there is no selection.</summary>
    private long _shelfAnchorId = -1;

    /// <summary>
    /// Entry ids seen by the last shelf rebuild. New ids — a drop that just landed — can slide in
    /// without re-animating the chips an unrelated rebuild already painted.
    /// </summary>
    private readonly HashSet<long> _shelfKnownIds = [];

    /// <summary>An entry drop just landed: the next rebuild animates the chips it adds.</summary>
    private bool _shelfDropPulse;

    /// <summary>Extra start delay (ms) for the drop animation while the panel is still opening.</summary>
    private double _shelfDropDelayMs;

    /// <summary>Only entries with an id above this threshold are animated as fresh drop records.</summary>
    private long _shelfDropMinNewId;

    /// <summary>The orange shelf drop-target glow is lit (a file drag hovers the pill).</summary>
    private bool _shelfDropTargetActive;

    /// <summary>Drop-target glow pulse: bright/dim endpoints and half-period.</summary>
    private const double ShelfDropGlowMin = 0.35;
    private const double ShelfDropGlowMax = 1.0;
    private const double ShelfDropGlowPulseMs = 720;

    /// <summary>
    /// Display order of the last rebuild (chip order = <see cref="IslandService.Entries"/> order).
    /// Range selection and the batch drag payload follow this order.
    /// </summary>
    private readonly List<IslandEntry> _shelfRows = [];

    /// <summary>
    /// Swallows the first release so the release that ends a drag does not collapse the selection to
    /// one chip. The release is handed to the source even when it ends the drag
    /// (<c>_suppressNextClick</c> in DockWindow exists for the same reason), so a batch drag that
    /// ends with Esc or by dropping on the shelf must not scatter the selection.
    /// <para>
    /// Lifetime: cleared on every new chip press (a drag whose release never arrived must not
    /// eat the next click's collapse), set unconditionally when <see cref="DragDrop.DoDragDrop"/>
    /// returns — a rejected drag is still a drag — and consumed on the first release.
    /// </para>
    /// </summary>
    private bool _suppressNextShelfCollapse;

    /// <summary>
    /// Lights the shelf as the drop target while a file drag hovers the pill. The glow is an
    /// overlay above the list, so it never shifts the chips; the pulse is the "drag is alive"
    /// animation and stays a static lit state when the user has animations off.
    /// </summary>
    internal void ShowShelfDropTarget(bool active)
    {
        if (_shelfDropTargetActive == active)
        {
            return;
        }

        _shelfDropTargetActive = active;
        ShelfDropGlow.BeginAnimation(UIElement.OpacityProperty, null);
        if (!active)
        {
            ShelfDropGlow.Opacity = 0;
            return;
        }

        ShelfDropGlow.Opacity = ShelfDropGlowMin;
        double ms = IslandMotion.Gate(ShelfDropGlowPulseMs, SystemParameters.ClientAreaAnimation);
        if (ms <= 0)
        {
            ShelfDropGlow.Opacity = ShelfDropGlowMax;
            return;
        }

        var pulse = new DoubleAnimation(ShelfDropGlowMin, ShelfDropGlowMax, TimeSpan.FromMilliseconds(ms))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        ShelfDropGlow.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    /// <summary>
    /// Arms the new-chip entry animation for the rebuild a landed drop triggers.
    /// <paramref name="panelStillOpening"/> adds the reveal delay so a drop that also opens the
    /// panel does not play the chip motion behind the open animation.
    /// <paramref name="newestEntryIdBefore"/> is the newest record id before the drop: only ids
    /// above it are fresh, so a detached flyout's first shelf build does not animate old chips.
    /// </summary>
    internal void PrepareShelfDrop(bool panelStillOpening = false, long newestEntryIdBefore = 0)
    {
        _shelfDropPulse = true;
        _shelfDropMinNewId = newestEntryIdBefore;
        _shelfDropDelayMs = panelStillOpening
            ? IslandMotion.Gate(IslandMotion.ShelfArrivalAfterRevealMs, SystemParameters.ClientAreaAnimation)
            : 0;
    }

    /// <summary>A drop that landed nothing (or a non-Shelf body): no entry animation is owed.</summary>
    internal void CancelShelfDrop()
    {
        _shelfDropPulse = false;
        _shelfDropDelayMs = 0;
        _shelfDropMinNewId = 0;
    }

    /// <summary>
    /// Drop-result motion for a new chip: a short fade + downward slide, staggered for a batch.
    /// The base state is the START (hidden/offset) so a BeginTime delay does not flash the final
    /// frame first; the clocks hold the end state and are removed in Completed, leaving the chip
    /// at its resting local values with no animation clock behind.
    /// </summary>
    private static void AnimateShelfChipIn(Border chip, int index, double extraDelayMs)
    {
        double ms = IslandMotion.Gate(IslandMotion.ContentFadeMs, SystemParameters.ClientAreaAnimation);
        if (ms <= 0)
        {
            return;
        }

        var slide = new TranslateTransform(0, -10);
        chip.RenderTransform = slide;
        chip.Opacity = 0;
        double delayMs = extraDelayMs + IslandMotion.StaggerDelayMs(Math.Min(index, 6), true);
        var delay = TimeSpan.FromMilliseconds(delayMs);
        var duration = new Duration(TimeSpan.FromMilliseconds(ms));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(1, duration)
        {
            BeginTime = delay,
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        fade.Completed += (_, _) =>
        {
            chip.BeginAnimation(UIElement.OpacityProperty, null);
            chip.Opacity = 1;
            slide.BeginAnimation(TranslateTransform.YProperty, null);
            slide.Y = 0;
        };
        chip.BeginAnimation(UIElement.OpacityProperty, fade);
        slide.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, duration)
            {
                BeginTime = delay,
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    /// <summary>
    /// Shelf body: dropped file entries as single-line chips — extension badge + file
    /// name. Order matches <see cref="IslandService.Entries"/> (newest first); the row
    /// ceiling of the clipboard body (<see cref="MaxRenderedRows"/>) applies here too.
    /// </summary>
    private void RebuildShelf()
    {
        if (_service is null)
        {
            return;
        }

        List<IslandEntry> files = _service.Entries
            .Where(entry => entry.Kind == IslandEntryKind.File)
            .ToList();

        // Selection is tied to the id: an entry not in the fresh list (removed/pruned) drops out, the
        // remaining selection is kept. Rebuilds frequently triggered by background notifications do
        // not eat the selection — it is cleared only on tab/body close and by this id filter.
        HashSet<long> liveIds = [.. files.Select(entry => entry.Id)];
        _shelfSelection.IntersectWith(liveIds);
        if (_shelfAnchorId >= 0 && !liveIds.Contains(_shelfAnchorId))
        {
            _shelfAnchorId = -1;
        }

        bool empty = files.Count == 0;
        ShelfEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ShelfScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        ShelfList.Children.Clear();
        _shelfRows.Clear();
        bool animateNew = _shelfDropPulse;
        double arrivalDelay = _shelfDropDelayMs;
        long minNewId = _shelfDropMinNewId;
        _shelfDropPulse = false;
        _shelfDropDelayMs = 0;
        _shelfDropMinNewId = 0;
        int shown = Math.Min(files.Count, MaxRenderedRows);
        for (int i = 0; i < shown; i++)
        {
            IslandEntry entry = files[i];
            _shelfRows.Add(entry);

            // The folder badge is read from on-disk metadata. The query is made ONCE per chip, here:
            // RebuildShelf runs on every clipboard/media notification, and leaking two separate
            // Directory.Exists calls (badge + menu resolution) into the chip builder would double the hot path.
            bool isFolder = entry.FilePath is { Length: > 0 } folderPath && Directory.Exists(folderPath);
            Border chip = BuildShelfChip(entry, isFolder);
            if (animateNew && entry.Id > minNewId && !_shelfKnownIds.Contains(entry.Id))
            {
                AnimateShelfChipIn(chip, i, arrivalDelay);
            }

            ShelfList.Children.Add(chip);
        }

        _shelfKnownIds.Clear();
        _shelfKnownIds.UnionWith(liveIds);

        // The background menu is lazy: the threshold comes from the entry count, not from disk (no
        // path query on rebuild). The menu object is created once when first needed; the payload
        // is resolved from the live list when the drag actually starts (see GetShelfBatchMenu).
        ShelfBody.ContextMenu = files.Count >= 2 ? GetShelfBatchMenu() : null;
    }

    /// <summary>
    /// Badge text: the first three letters of the extension in upper case (<c>report.PDF → PDF</c>). For
    /// a file without an extension the first three letters of the name are read — an empty badge is
    /// never left. For a folder entry <see cref="FolderGlyph"/> is drawn instead of badge text
    /// (see <see cref="BuildShelfChip"/>).
    /// </summary>
    private static string ShelfBadge(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        ext = ext.Length > 0 ? ext[1..] : fileName;
        return (ext.Length <= 3 ? ext : ext[..3]).ToUpperInvariant();
    }

    /// <summary>
    /// Chip row: same metrics as the clipboard body's <see cref="BuildRow"/> pattern (tile 32,
    /// corner 12, 2 between rows) — the only difference: the second column holds the name, not meta;
    /// plus selection click, right-click menu and drag-out. Files open from the menu or from the
    /// row in the clipboard body.
    /// </summary>
    private Border BuildShelfChip(IslandEntry entry, bool isFolder)
    {
        var chip = new Border
        {
            Padding = new Thickness(8, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent,
            // The border is 1 px on every chip; on selection the brush switches to accent. If the
            // thickness changed at selection time the chip would grow 2 px and the row height would jump on selection.
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand,
            // Spec B2: keyboard focus + removal path with Delete/Enter/Space (the
            // PreviewKeyDown below). The focus visual is the accent border; mouse focus does no harm.
            Focusable = true,
            Tag = entry,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Folder badge: a folder glyph instead of the three-letter extension. The entry kind is not
        // separate for a folder (IslandEntryKind.File); whether it is a folder is read once per entry
        // in RebuildShelf and passed in here (the disk query is not repeated in this builder).
        var badge = new Border
        {
            Width = 36,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = ResolveTileBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Child = isFolder
                ? new TextBlock
                {
                    Text = FolderGlyph,
                    FontFamily = ResolveIconFont(),
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                }
                : new TextBlock
                {
                    Text = ShelfBadge(entry.Text),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
        };
        grid.Children.Add(badge);

        var name = new TextBlock
        {
            Text = entry.Text,
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        // Hover "×": in the third (Auto) column, visible while the mouse is over the chip (or while
        // the chip has keyboard focus). The click drops the entry from the list; if the entry's own path
        // is under Drop the old copy is deleted, while the original file outside Drop is left untouched
        // (<see cref="IslandService.Remove"/> contract). The background is <c>Transparent</c>
        // (not null): it puts the 16×16 area into hit testing, i.e. the area around the "×" glyph
        // is clickable too.
        var removeButton = new Border
        {
            Width = 16,
            Height = 16,
            Margin = new Thickness(2, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            ToolTip = RemoveAccessName,
            Child = new TextBlock
            {
                Text = "×",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(removeButton, 2);
        grid.Children.Add(removeButton);
        chip.Child = grid;

        // Spec B2 accessibility: the chip and "×" are announced by name to screen readers.
        System.Windows.Automation.AutomationProperties.SetName(chip, entry.Text);
        System.Windows.Automation.AutomationProperties.SetName(removeButton, RemoveAccessName);

        // The file name is in the list; the copy's full path is in the tooltip (same placement as in the clipboard body).
        if (entry.FilePath is { Length: > 0 } fullPath)
        {
            chip.ToolTip = fullPath;
        }

        Brush restingBrush = Brushes.Transparent;
        Brush hoverBrush = ResolveSelectedBrush();
        chip.MouseEnter += (_, _) =>
        {
            chip.Background = hoverBrush;
            removeButton.Visibility = Visibility.Visible;
        };
        chip.MouseLeave += (_, _) =>
        {
            // Keyboard focus keeps "×" open: the Enter/Space removal path must not close
            // silently just because the mouse left (the focus visual stays on the focus itself too).
            removeButton.Visibility = chip.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
            // A selected chip keeps the hover fill on leave too; the selection frame should not be left alone.
            chip.Background = _shelfSelection.Contains(entry.Id) ? hoverBrush : restingBrush;
        };

        // Keyboard focus: the accent border shows, "×" opens. On focus loss the visual and the button
        // return to the normal (selection/hover) state.
        chip.GotKeyboardFocus += (_, _) =>
        {
            chip.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
            removeButton.Visibility = Visibility.Visible;
        };
        chip.LostKeyboardFocus += (_, _) =>
        {
            if (!chip.IsMouseOver)
            {
                removeButton.Visibility = Visibility.Collapsed;
            }

            UpdateShelfChipSelection(chip, entry);
        };

        // The selection click is independent of the drag capture: an entry without a path is selected too.
        chip.MouseLeftButtonDown += (_, _) => OnShelfChipPressed(entry);
        chip.MouseLeftButtonUp += (_, _) => OnShelfChipReleased(entry);

        UpdateShelfChipSelection(chip, entry);
        Action? cancelShelfDrag = AttachShelfChipDrag(chip, entry);

        // The "×" click never enters the chip's selection/drag paths: once the preview (tunnel) event
        // is marked handled here, the bubbling <see cref="UIElement.MouseDownEvent"/> does not reach
        // the chip — neither the selection changes nor a drag capture is set up. Still, the cancel
        // action is called to release a possibly stale capture.
        removeButton.PreviewMouseLeftButtonDown += (_, args) => args.Handled = true;
        removeButton.PreviewMouseLeftButtonUp += (_, args) =>
        {
            args.Handled = true;
            RemoveShelfEntry(entry, cancelShelfDrag);
        };

        // Keyboard removal (spec B2): Delete always; Enter/Space while "×" is visible (focus or
        // hover) runs the same entry-only path. Cancelling the press releases a stale mouse capture,
        // so keyboard removal never starts a drag.
        chip.PreviewKeyDown += (_, args) =>
        {
            bool remove = args.Key == Key.Delete
                || ((args.Key == Key.Enter || args.Key == Key.Space)
                    && removeButton.Visibility == Visibility.Visible);
            if (!remove)
            {
                return;
            }

            args.Handled = true;
            RemoveShelfEntry(entry, cancelShelfDrag);
        };

        // The menu is created lazily: RebuildShelf runs on every clipboard/media notification; creating
        // it here would mean TryGetExistingShelfPath + CanOpenShelfFile (Exists + GetAttributes +
        // component walk) on every chip. It is created when the right click actually opens; behavior is the same.
        chip.ContextMenuOpening += OnShelfChipContextMenuOpening;
        return chip;
    }

    /// <summary>
    /// Chip press manages the selection: Ctrl toggles one by one, Shift selects a range from the anchor, a
    /// plain press drops the selection. On a plain press a <b>selected</b> chip is not dropped at once: if the
    /// press turns into a drag the selection payload is carried; if it ends as a click it collapses to a single
    /// chip on release (<see cref="OnShelfChipReleased"/>).
    /// </summary>
    private void OnShelfChipPressed(IslandEntry entry)
    {
        // Every new press clears the stale "drag finished" flag: a drag whose release never arrived
        // must not eat the next click's selection collapse.
        _suppressNextShelfCollapse = false;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            if (!_shelfSelection.Remove(entry.Id))
            {
                _shelfSelection.Add(entry.Id);
            }

            _shelfAnchorId = entry.Id;
        }
        else if ((modifiers & ModifierKeys.Shift) != 0)
        {
            ExtendShelfSelectionTo(entry);
        }
        else if (!_shelfSelection.Contains(entry.Id))
        {
            ClearShelfSelection();
        }

        RefreshShelfChipSelection();
    }

    /// <summary>
    /// Release of a plain click: if a selected chip in a multi-selection was pressed without dragging, the
    /// selection collapses to that chip (the drag never reaches here; the capture is released inside
    /// <see cref="AttachShelfChipDrag"/>).
    /// </summary>
    private void OnShelfChipReleased(IslandEntry entry)
    {
        // The release that ends a drag lands here too: if the batch drag ended with Esc or by dropping
        // on the shelf, the selection must not collapse to one chip (only the first release is swallowed).
        if (_suppressNextShelfCollapse)
        {
            _suppressNextShelfCollapse = false;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None
            || _shelfSelection.Count <= 1
            || !_shelfSelection.Contains(entry.Id))
        {
            return;
        }

        _shelfSelection.Clear();
        _shelfSelection.Add(entry.Id);
        _shelfAnchorId = entry.Id;
        RefreshShelfChipSelection();
    }

    /// <summary>Selects the range in display order from the anchor up to the clicked chip.</summary>
    private void ExtendShelfSelectionTo(IslandEntry entry)
    {
        int clicked = IndexOfShelfRow(entry.Id);
        if (clicked < 0)
        {
            return;
        }

        int anchor = IndexOfShelfRow(_shelfAnchorId);
        if (anchor < 0)
        {
            // If the anchor dropped out of the list, the range starts at the clicked chip.
            anchor = clicked;
            _shelfAnchorId = entry.Id;
        }

        int from = Math.Min(anchor, clicked);
        int to = Math.Max(anchor, clicked);
        _shelfSelection.Clear();
        for (int i = from; i <= to; i++)
        {
            _shelfSelection.Add(_shelfRows[i].Id);
        }
    }

    private int IndexOfShelfRow(long id)
    {
        for (int i = 0; i < _shelfRows.Count; i++)
        {
            if (_shelfRows[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Applies the selection visuals on a single chip: accent border + hover fill while the mouse
    /// is over it or while it is selected. On rebuild <see cref="BuildShelfChip"/> goes through this
    /// gate too, so the selection carries over to the new chips by itself.
    /// </summary>
    private void UpdateShelfChipSelection(Border chip, IslandEntry entry)
    {
        chip.BorderThickness = new Thickness(1);
        if (_shelfSelection.Contains(entry.Id))
        {
            chip.SetResourceReference(Border.BorderBrushProperty, "AccentFillColorDefaultBrush");
            chip.Background = ResolveSelectedBrush();
            return;
        }

        chip.BorderBrush = Brushes.Transparent;
        chip.Background = chip.IsMouseOver ? ResolveSelectedBrush() : Brushes.Transparent;
    }

    /// <summary>Refreshes every chip's visual when the selection changes (chip count is bounded by the row ceiling).</summary>
    private void RefreshShelfChipSelection()
    {
        foreach (UIElement child in ShelfList.Children)
        {
            if (child is Border chip && chip.Tag is IslandEntry entry)
            {
                UpdateShelfChipSelection(chip, entry);
            }
        }
    }

    /// <summary>
    /// Clears the selection. It is called only on tab/body close and for ids that leave the list;
    /// an ordinary <see cref="RebuildShelf"/> preserves the selection.
    /// </summary>
    private void ClearShelfSelection()
    {
        _shelfSelection.Clear();
        _shelfAnchorId = -1;
    }

    /// <summary>
    /// Converts the selected entries into a <see cref="DataFormats.FileDrop"/> payload: only paths still
    /// present on disk (a file <b>or</b> a folder). If none remain, <see langword="null"/> —
    /// the caller does not start a drag.
    /// </summary>
    private static string[]? BuildShelfFileDropPayload(IEnumerable<IslandEntry> entries)
    {
        List<string> paths = [];
        foreach (IslandEntry entry in entries)
        {
            if (entry.FilePath is { Length: > 0 } path && (File.Exists(path) || Directory.Exists(path)))
            {
                paths.Add(path);
            }
        }

        return paths.Count == 0 ? null : [.. paths];
    }

    /// <summary>
    /// Shelf background menu: a single item, "Drag all out" (independent of the selection, <b>all</b>
    /// shelf entries). The menu object is a <b>single instance</b> and lazy: the threshold is the entry
    /// count rather than disk (<see cref="RebuildShelf"/>), so no per-entry path query runs on every
    /// clipboard/media notification. The payload, however, is resolved from the live list when the drag
    /// actually starts: a path dropped from disk while the menu is open is not carried; if no path
    /// remains, the drag does not start.
    /// </summary>
    private ContextMenu GetShelfBatchMenu()
    {
        if (_shelfBatchMenu is not null)
        {
            return _shelfBatchMenu;
        }

        var menu = new ContextMenu();
        // A click cannot start a drag (the button must be held): the hint says the item has to be
        // pressed and dragged — a plain click closes the menu and starts no drag.
        var dragAll = new MenuItem
        {
            Header = "Drag all out",
            ToolTip = "Hold and drag to a folder",
        };

        // Dragging requires a held button in OLE: Click fires after the mouse release, the first
        // QueryContinueDrag does not see the button held and WPF cancels the drag. That is why the
        // item is only "armed" on press (position), and the drag starts once the threshold is crossed —
        // while the button is still held — after closing the menu. The payload is rebuilt at the
        // threshold (freshness at click time); a plain click without dragging starts no drag and the menu closes normally.
        // Movement is tracked from the menu: while the menu is open the mouse is captured by the popup, so
        // the event reaches the menu even when the cursor leaves the item (if bound to the item only,
        // the first movement outside the bar would be missed).
        Point? armedOrigin = null;
        dragAll.PreviewMouseLeftButtonDown += (_, args) => armedOrigin = args.GetPosition(ShelfBody);
        menu.AddHandler(
            UIElement.MouseMoveEvent,
            new MouseEventHandler(OnBatchMenuItemMouseMove),
            handledEventsToo: true);

        void OnBatchMenuItemMouseMove(object sender, MouseEventArgs args)
        {
            if (armedOrigin is not { } origin || args.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Vector delta = args.GetPosition(ShelfBody) - origin;
            if (Math.Abs(delta.X) <= SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) <= SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            armedOrigin = null;
            IEnumerable<IslandEntry> live = _service is { } service
                ? service.Entries.Where(entry => entry.Kind == IslandEntryKind.File)
                : [];
            string[]? paths = BuildShelfFileDropPayload(live);
            // While the popup is open the OLE loop cannot close the menu: menu first, then the drag.
            menu.IsOpen = false;
            if (paths is null)
            {
                return;
            }

            var payload = new DataObject(DataFormats.FileDrop, paths);
            _ = DragDrop.DoDragDrop(ShelfBody, payload, DragDropEffects.Copy | DragDropEffects.Move);

            // Drag finished: the release that ended it must not collapse the selection to one chip (see field).
            _suppressNextShelfCollapse = true;
        }

        menu.Items.Add(dragAll);
        _shelfBatchMenu = menu;
        return menu;
    }

    /// <summary>Single instance of the background menu (the menu looks at the live list, not at entries).</summary>
    private ContextMenu? _shelfBatchMenu;

    /// <summary>
    /// Drags the chip into a folder window: <see cref="IslandEntry.FilePath"/> is carried as
    /// <see cref="DataFormats.FileDrop"/>. If the chip is part of a multi-selection the payload is
    /// <b>the whole selection</b> (only the paths still on disk; if none remain the drag does not start).
    /// The drag starts on <b>actual movement</b>: the left button is
    /// captured on <see cref="UIElement.MouseLeftButtonDownEvent"/>, and once the threshold is crossed the capture is
    /// released and <see cref="DragDrop.DoDragDrop"/> is called — that call runs blocking until the user
    /// releases the drag (the UI thread is STA anyway).
    /// </summary>
    /// <returns>
    /// The action that cancels the press (clears the flag, releases the capture) — returned so the hover "×" click
    /// does not start a drag; <see langword="null"/> when no drag was attached.
    /// </returns>
    private Action? AttachShelfChipDrag(FrameworkElement chip, IslandEntry entry)
    {
        if (entry.FilePath is not { Length: > 0 } path)
        {
            return null;
        }

        bool pressed = false;
        Point origin = default;
        void CancelPress()
        {
            if (!pressed)
            {
                return;
            }

            pressed = false;
            chip.ReleaseMouseCapture();
        }

        chip.MouseLeftButtonDown += (_, args) =>
        {
            pressed = true;
            origin = args.GetPosition(ShelfList);
            _ = chip.CaptureMouse();
        };
        chip.MouseMove += (_, args) =>
        {
            if (!pressed || args.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Vector delta = args.GetPosition(ShelfList) - origin;
            if (Math.Abs(delta.X) <= SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) <= SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            // DoDragDrop is blocking: the flag is cleared first so that MouseMove events queued
            // after the call returns do not start the drag again.
            pressed = false;
            chip.ReleaseMouseCapture();

            // If a selected chip is dragged the payload is the batch; a chip outside the selection carries a single path (current behavior).
            bool batch = _shelfSelection.Count > 1 && _shelfSelection.Contains(entry.Id);
            string[]? paths = batch
                ? BuildShelfFileDropPayload(_shelfRows.Where(row => _shelfSelection.Contains(row.Id)))
                : [path];
            if (paths is null)
            {
                // No path in the batch payload is still on disk: a ghost drag is not started with a stale path.
                return;
            }

            var payload = new DataObject(DataFormats.FileDrop, paths);
            _ = DragDrop.DoDragDrop(chip, payload, DragDropEffects.Copy | DragDropEffects.Move);

            // Drag finished: the release that ended it must not collapse the selection to one chip (see field).
            _suppressNextShelfCollapse = true;
        };
        chip.MouseLeftButtonUp += (_, _) => CancelPress();
        return CancelPress;
    }

    /// <summary>
    /// Removal on the chip ("×" click or Delete/Enter/Space from the keyboard): drops the entry from
    /// the list and removes it from the selection too. If the entry's <b>own path</b> is under <c>Drop</c>
    /// the old copy is deleted; the original file outside <c>Drop</c> is left untouched (the deletion boundary is
    /// in the <see cref="IslandService.Remove"/> contract). The click changes no selection and
    /// starts no drag: the press event never reaches the chip (the <see cref="BuildShelfChip"/> preview
    /// gate), and the cancel action releases a stale capture as well.
    /// </summary>
    private void RemoveShelfEntry(IslandEntry entry, Action? cancelShelfDrag)
    {
        cancelShelfDrag?.Invoke();
        _shelfSelection.Remove(entry.Id);
        if (_shelfAnchorId == entry.Id)
        {
            _shelfAnchorId = -1;
        }

        // Changed is raised synchronously on the UI thread: the chips are rebuilt here, so the selection
        // frame and the row order are not refreshed separately.
        _service?.Remove(entry);
    }

    /// <summary>
    /// Builds the chip menu only when it actually opens (no disk query on the hot rebuild path). Because
    /// the menu is assigned at this moment, <c>ContextMenuService</c>'s class handler cannot see
    /// it; so the event is marked <c>Handled</c> and the menu is opened here. Placement
    /// is the same as WPF's automatic opening: mouse cursor, keyboard (Shift+F10/Apps) center.
    /// </summary>
    private void OnShelfChipContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Border chip || chip.Tag is not IslandEntry entry)
        {
            return;
        }

        ContextMenu menu = BuildShelfChipMenu(entry);
        chip.ContextMenu = menu;
        menu.PlacementTarget = chip;
        menu.Placement = e.CursorLeft < 0 ? PlacementMode.Center : PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// Chip menu. File actions ("Show file location", "Copy path", "Copy
    /// file") at the top; they appear only when <see cref="ResolveShelfMenuPath"/> resolves a path and
    /// passes it through the <see cref="IslandService.CanOpenShelfFile"/> gate.
    /// "Copy file" is <b>absent</b> for a folder entry and for a path that resolves to a folder (B4 scope:
    /// copying from the menu is only for real files). "Open with" is visible only when
    /// <see cref="IslandService.CanOpenShelfFile"/> allows it; every chip has
    /// "Remove from list" — the entry drops from the list, the file is untouched (deletion boundaries are
    /// in the <see cref="IslandService.Remove"/> and <see cref="IslandService.DeleteDroppedFiles"/>
    /// contracts). "Empty shelf" is a single button under the body; it is not repeated
    /// in the menu.
    /// </summary>
    private ContextMenu BuildShelfChipMenu(IslandEntry entry)
    {
        var menu = new ContextMenu();
        string? resolved = ResolveShelfMenuPath(entry);
        if (resolved is not null)
        {
            var reveal = new MenuItem { Header = "Open file location" };
            reveal.Click += (_, _) => RevealInExplorer(resolved);
            menu.Items.Add(reveal);

            var copyPath = new MenuItem { Header = "Copy path" };
            copyPath.Click += (_, _) => _ = ClipboardNative.TryWriteText(resolved);
            menu.Items.Add(copyPath);

            // Hidden for a folder: copying a folder from the menu is out of scope in this iteration (B4).
            if (File.Exists(resolved) && !Directory.Exists(resolved))
            {
                var copyFile = new MenuItem { Header = "Copy file" };
                copyFile.Click += (_, _) => _ = ClipboardNative.TryWriteFileDropList([resolved]);
                menu.Items.Add(copyFile);
            }
        }

        if (_service is not null && _service.CanOpenShelfFile(entry.FilePath))
        {
            var open = new MenuItem { Header = "Open with app" };
            open.Click += (_, _) => _ = _service?.OpenShelfFile(entry.FilePath!);
            menu.Items.Add(open);
        }

        var remove = new MenuItem { Header = "Remove from list" };
        remove.Click += (_, _) => _service?.Remove(entry);
        menu.Items.Add(remove);
        return menu;
    }

    /// <summary>
    /// Gives the <b>resolved and gated</b> path for the chip menu's file actions: if the entry's own
    /// path (file or folder) exists that one, otherwise the copy with the same name under the <c>Drop</c>
    /// root. Because the path comes from <c>island.json</c> it is <b>untrusted</b>; revealing and
    /// copying require the <see cref="IslandService.CanOpenShelfFile"/> gate just like opening
    /// (device path, folder with a reparse point, escape component are rejected). If the gate is not
    /// passed, <see langword="null"/> is returned and the actions are never added to the menu.
    /// </summary>
    private string? ResolveShelfMenuPath(IslandEntry entry)
    {
        if (_service is null || !_service.TryGetExistingShelfPath(entry.FilePath, out string resolved))
        {
            return null;
        }

        return _service.CanOpenShelfFile(resolved) ? resolved : null;
    }

    /// <summary>
    /// Opens Explorer with <paramref name="path"/> selected. The arguments are produced by
    /// <see cref="IslandService.BuildRevealArguments"/> and added to
    /// <see cref="ProcessStartInfo.ArgumentList"/> one by one: quoting is not escaped by hand,
    /// and a path with a trailing separator still passes correctly (the old
    /// <c>Arguments = $"/select,\"{path}\""</c> form opened the default folder in Explorer).
    /// The path has passed the gate (<see cref="ResolveShelfMenuPath"/>); even so the shell
    /// launch can fail — the error is logged and the menu click does not crash the app.
    /// </summary>
    private static void RevealInExplorer(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo("explorer.exe");
            foreach (string argument in IslandService.BuildRevealArguments(path))
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Debug.WriteLine($"[WharfBar] island reveal failed: {ex.Message}");
        }
    }

    /// <summary>
    /// "Empty shelf": asks for confirmation before emptying the list and the copies under the
    /// <c>Drop</c> root (copies deleted in one go are irreversible). The clipboard body's
    /// <see cref="IslandService.Clear"/> path does not come here; if an entry's path is outside <c>Drop</c>
    /// the entry only leaves the list and the original file is untouched.
    /// </summary>
    private void OnShelfClearClick(object sender, RoutedEventArgs e)
    {
        if (_service is null)
        {
            return;
        }

        const string title = "Empty shelf";
        const string text = "The shelf list will be cleared; the copies under %AppData%\\WharfBar\\Drop will be deleted too. Continue?";
        Window? owner = Window.GetWindow(this);
        MessageBoxResult choice = owner is null
            ? MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(owner, text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        _service.DeleteDroppedFiles();
    }

    #endregion

    #region Color picker body

    private DispatcherTimer? _pickerFeedbackTimer;

    /// <summary>
    /// Color body: pick-click button + recent colors strip + large color card. The colors come
    /// from the session-scoped <see cref="IslandPickedColors"/> store (same metrics as the clipboard
    /// body's <see cref="Rebuild"/> rebuild pattern).
    /// </summary>
    private void RebuildPicker()
    {
        IReadOnlyList<Color> colors = IslandPickedColors.Items;
        bool any = colors.Count > 0;
        Color current = any ? colors[0] : Color.FromArgb(0, 0, 0, 0);
        _pickerSelected = current;
        ApplyPickerSelection(current);
        PickerCopyButton.IsEnabled = any;

        // A new presentation also clears the old feedback flash (no stale "Copied" left behind).
        _pickerFeedbackTimer?.Stop();
        PickerFeedback.Opacity = 0;

        PickerRecents.Children.Clear();
        Brush tileBrush = ResolveTileBrush();
        for (int i = 0; i < colors.Count; i++)
        {
            PickerRecents.Children.Add(BuildPickerChip(colors[i], tileBrush));
        }
    }

    /// <summary>Pulls the large card to the given color; an empty color returns the default look.</summary>
    private void ApplyPickerSelection(Color color)
    {
        if (color.A == 0)
        {
            // Returns to the XAML default (set instead of ClearValue, which would drop the DynamicResource link).
            PickerSwatch.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            PickerHex.Text = "—";
            System.Windows.Automation.AutomationProperties.SetName(PickerHex, "No color selected yet");
            return;
        }

        var fill = new SolidColorBrush(color);
        fill.Freeze();
        PickerSwatch.Background = fill;
        PickerHex.Text = ColorMath.ToHex(color.R, color.G, color.B);
        System.Windows.Automation.AutomationProperties.SetName(PickerHex, PickerHex.Text);
    }

    /// <summary>Small tile in the recent colors strip: click → the selection returns to it. The HEX is in the tooltip.</summary>
    private FrameworkElement BuildPickerChip(Color color, Brush tileBrush)
    {
        string hex = ColorMath.ToHex(color.R, color.G, color.B);
        Brush fill = ResolveSwatchBrush(color);
        Brush hoverBrush = ResolveSelectedBrush();
        var chip = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = tileBrush,
            Background = fill,
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            ToolTip = hex,
        };
        System.Windows.Automation.AutomationProperties.SetName(chip, hex);
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ApplyPickerSelection(color);
            _pickerSelected = color;
        };
        chip.MouseEnter += (_, _) => chip.Background = hoverBrush;
        chip.MouseLeave += (_, _) => chip.Background = fill;
        return chip;
    }

    private Brush ResolveSwatchBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Copy: the selected color's HEX is written to the clipboard (Task 7 path); with a result notification.</summary>
    private void OnPickerCopyClick(object sender, RoutedEventArgs e)
    {
        if (_service is null || _pickerSelected.A == 0)
        {
            return;
        }

        string hex = ColorMath.ToHex(_pickerSelected.R, _pickerSelected.G, _pickerSelected.B);
        // The write goes through the same Task 7 gate: the gate path is synchronously false, success arrives
        // together with BeginInvoke — both timings write the same feedback.
        _service.PutOnClipboard(hex, completed => ShowPickerCopyFeedback(completed));
    }

    /// <summary>Copy feedback flash ("Copied" / "Could not copy"): a silent false is not left.</summary>
    internal void ShowPickerCopyFeedback(bool written)
    {
        ShowPickerFeedback(written ? "Copied" : "Could not copy", written);
    }

    private void ShowPickerFeedback(string text, bool ok)
    {
        PickerFeedback.Text = text;
        PickerFeedback.Opacity = ok ? 0.75 : 0.95;
        _pickerFeedbackTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        _pickerFeedbackTimer.Tick -= OnPickerFeedbackTick;
        _pickerFeedbackTimer.Tick += OnPickerFeedbackTick;
        _pickerFeedbackTimer.Stop();
        _pickerFeedbackTimer.Start();
    }

    private void OnPickerFeedbackTick(object? sender, EventArgs e)
    {
        _pickerFeedbackTimer?.Stop();
        PickerFeedback.Opacity = 0;
    }

    /// <summary>Selected color: the last selection on body build, that color on chip clicks.</summary>
    private Color _pickerSelected;

    private void OnPickerScreenClick(object sender, RoutedEventArgs e)
    {
        if (_service is null)
        {
            return;
        }

        ScreenPickerRequested?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region Prompter body

    /// <summary>
    /// Prompter playback pin: true = playback on (locks the Task 5 top bar side), false =
    /// free. Raised only on real state changes (silent on the Editing→Paused transition).
    /// </summary>
    internal event Action<bool>? PrompterPinnedChanged;

    /// <summary>Playback glyphs (documented MDL2): Play E768, Pause E769.</summary>
    private const string PrompterPlayGlyphText = "\uE768";
    private const string PrompterPauseGlyphText = "\uE769";

    /// <summary>
    /// Reading tick interval ~16 ms. Single decision: <see cref="DispatcherTimer"/>,
    /// NO <c>CompositionTarget.Rendering</c> hook — a from-scratch creation that can be stopped in a
    /// hidden body and follows the same lifetime pattern as the existing <c>_timelineTimer</c> idiom.
    /// </summary>
    private static readonly TimeSpan PrompterTickInterval = TimeSpan.FromMilliseconds(16);

    private PrompterMotion.Playback _prompterState = PrompterMotion.Playback.Editing;
    private DispatcherTimer? _prompterTick;
    private DateTime _prompterLastTickUtc;
    private double _prompterSpeed = 1.0;
    private double _prompterEditingFontSize;

    private bool _prompterDragging;
    private double _prompterDragStartOffset;
    private Point _prompterDragOrigin;
    private bool _prompterScrollHooked;

    private void OnPrompterPlayPauseClick(object sender, RoutedEventArgs e) =>
        SetPrompterPlayback(PrompterMotion.Toggle(_prompterState));

    /// <summary>
    /// Reading in a single pass. Playing: editor read-only, reading font
    /// <see cref="PrompterMotion.ReadingFontDip"/>, the tick runs; Editing/Paused: editable and
    /// the tick off. The pin (true/false) only fires when playback turns on or off.
    /// </summary>
    private void SetPrompterPlayback(PrompterMotion.Playback incoming)
    {
        // The playback gate in one decision: if there is nowhere to scroll (the text fits the editor or
        // the scroll rests at the end) the request silently returns to Editing — the button side stays
        // disabled by the same decision (<see cref="RefreshPrompterPlayAvailability"/>).
        ScrollViewer? sv = incoming == PrompterMotion.Playback.Playing ? PrompterInnerScroll(PrompterEditor) : null;
        PrompterMotion.Playback state = incoming;
        if (state == PrompterMotion.Playback.Playing
            && (sv is null || !PrompterMotion.CanStartPlayback(sv.ScrollableHeight, sv.VerticalOffset)))
        {
            state = PrompterMotion.Playback.Editing;
        }

        bool wasPlaying = _prompterState == PrompterMotion.Playback.Playing;
        _prompterState = state;
        bool nowPlaying = state == PrompterMotion.Playback.Playing;

        PrompterEditor.IsReadOnly = nowPlaying;
        if (nowPlaying && !wasPlaying)
        {
            _prompterEditingFontSize = PrompterEditor.FontSize;
            PrompterEditor.FontSize = PrompterMotion.ReadingFontDip;
        }
        else if (!nowPlaying && wasPlaying && _prompterEditingFontSize > 0)
        {
            // The editing font from XAML is captured on the first entry into Playing; it is put back here.
            PrompterEditor.FontSize = _prompterEditingFontSize;
        }

        PrompterPlayGlyph.Text = nowPlaying ? PrompterPauseGlyphText : PrompterPlayGlyphText;
        PrompterPlayLabel.Text = nowPlaying ? "Pause" : "Play";
        PrompterPlayPause.IsChecked = nowPlaying;

        if (nowPlaying)
        {
            StartPrompterTick();
        }
        else
        {
            StopPrompterTick();
        }

        if (nowPlaying != wasPlaying)
        {
            PrompterPinnedChanged?.Invoke(nowPlaying);
        }

        RefreshPrompterPlayAvailability();
    }

    private void OnPrompterTick(object? sender, EventArgs e)
    {
        // Gate 1: while the body is hidden no tick returns (the second wall is <see cref="OnPanelClosed"/>).
        if (PrompterBody.Visibility != Visibility.Visible)
        {
            StopPrompterTick();
            return;
        }

        ScrollViewer? sv = PrompterInnerScroll(PrompterEditor);
        if (sv is null || sv.ScrollableHeight <= 0)
        {
            SetPrompterPlayback(PrompterMotion.Playback.Editing);
            return;
        }

        DateTime now = DateTime.UtcNow;
        double elapsed = (now - _prompterLastTickUtc).TotalSeconds;
        _prompterLastTickUtc = now;

        double current = sv.VerticalOffset;
        double updated = PrompterMotion.UpdateOffset(
            current, elapsed, _prompterSpeed, SystemParameters.ClientAreaAnimation);
        double clamped = PrompterMotion.ClampOffset(updated, sv.ExtentHeight, sv.ViewportHeight);
        sv.ScrollToVerticalOffset(clamped);

        // Reaching the end only counts on real progress: if animations are off the speed is 0
        // (<see cref="PrompterMotion.SpeedDipPerSec"/>) and reading waits where it is.
        if (updated - current > PrompterMotion.EndEpsilonDip
            && clamped >= sv.ScrollableHeight - PrompterMotion.EndEpsilonDip)
        {
            SetPrompterPlayback(PrompterMotion.Playback.Editing);
        }
    }

    private void StartPrompterTick()
    {
        _prompterTick ??= new DispatcherTimer { Interval = PrompterTickInterval };
        _prompterTick.Tick -= OnPrompterTick;
        _prompterTick.Tick += OnPrompterTick;
        _prompterLastTickUtc = DateTime.UtcNow;
        _prompterTick.Start();
    }

    private void StopPrompterTick() => _prompterTick?.Stop();

    /// <summary>Panel closed: the tick stops and playback returns to Editing (the pin is released).</summary>
    private void StopPrompterPlayback()
    {
        _prompterDragging = false;
        if (_prompterState == PrompterMotion.Playback.Playing)
        {
            SetPrompterPlayback(PrompterMotion.Playback.Editing);
        }
        else
        {
            StopPrompterTick();
        }
    }

    /// <summary>
    /// The scroll source is the TextBox's INNER ScrollViewer (the <c>PART_ContentHost</c> in its
    /// template): the engine reads the offset from there and writes back to it — there is no external
    /// scrollbar. Null if the template has not been applied.
    /// </summary>
    private static ScrollViewer? PrompterInnerScroll(TextBox editor) =>
        editor.Template is null
            ? null
            : editor.Template.FindName("PART_ContentHost", editor) as ScrollViewer;

    /// <summary>
    /// The Play button's enabled state is derived from the editor's INNER ScrollViewer: typing,
    /// manual scrolling, file/clipboard load and the font switch (Playing ↔ Editing) are all
    /// refreshed from the single <c>ScrollChanged</c> hook. Because during Playing the button acts
    /// as Pause, a per-tick refresh is not needed (it is kept off).
    /// </summary>
    private void HookPrompterScroll()
    {
        if (_prompterScrollHooked || PrompterInnerScroll(PrompterEditor) is not ScrollViewer sv)
        {
            return;
        }

        _prompterScrollHooked = true;
        sv.ScrollChanged += (_, _) =>
        {
            if (_prompterState != PrompterMotion.Playback.Playing)
            {
                RefreshPrompterPlayAvailability();
            }
        };
    }

    /// <summary>
    /// "The button that does not lie": when there is nowhere to scroll, Play is disabled and the tooltip
    /// states the reason — a short text and the end of reading are no longer a silent dead end. During Playing
    /// the button is Pause, so it is always enabled. While the body is hidden the measure comes back zero;
    /// the decision is not made then (the first open is refreshed deferred by <see cref="PresentHub"/>).
    /// </summary>
    private void RefreshPrompterPlayAvailability()
    {
        if (PrompterBody.Visibility != Visibility.Visible)
        {
            return;
        }

        ScrollViewer? sv = PrompterInnerScroll(PrompterEditor);
        double scrollable = sv?.ScrollableHeight ?? 0;
        double offset = sv?.VerticalOffset ?? 0;
        bool canToggle = _prompterState == PrompterMotion.Playback.Playing
            || PrompterMotion.CanStartPlayback(scrollable, offset);
        PrompterPlayPause.IsEnabled = canToggle;
        PrompterPlayPause.ToolTip = canToggle
            ? "Play/Pause"
            : scrollable <= 0
                ? "Nothing to scroll; a longer text that does not fit the editor is required."
                : "Scroll is at the bottom; use Back to start to start over.";
    }

    private void OnPrompterEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        PrompterEditorPlaceholder.Visibility = PrompterEditor.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Dragging the text by holding it while it is being read: the vertical mouse delta drags the same
    /// VerticalOffset — playback does not pause. In Editing/Paused the TextBox
    /// goes back to its own text selection, we do not interfere.
    /// </summary>
    private void OnPrompterEditorPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // The scrollbar has its own path: let it work before the text drag takes it over.
        if (_prompterState != PrompterMotion.Playback.Playing
            || e.OriginalSource is System.Windows.Controls.Primitives.ScrollBar
            || PrompterInnerScroll(PrompterEditor) is not ScrollViewer sv)
        {
            return;
        }

        _prompterDragging = true;
        _prompterDragOrigin = e.GetPosition(PrompterEditor);
        _prompterDragStartOffset = sv.VerticalOffset;
        _ = PrompterEditor.CaptureMouse();
        e.Handled = true;
    }

    private void OnPrompterEditorPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_prompterDragging
            || e.LeftButton != MouseButtonState.Pressed
            || PrompterInnerScroll(PrompterEditor) is not ScrollViewer sv)
        {
            return;
        }

        double dy = e.GetPosition(PrompterEditor).Y - _prompterDragOrigin.Y;

        // The hand holding the text: if the text goes down (dy > 0) reading goes back (the offset shrinks).
        double offset = _prompterDragStartOffset - dy;
        sv.ScrollToVerticalOffset(Math.Clamp(offset, 0, sv.ScrollableHeight));
        e.Handled = true;
    }

    private void OnPrompterEditorPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_prompterDragging)
        {
            return;
        }

        _prompterDragging = false;
        PrompterEditor.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnPrompterResetClick(object sender, RoutedEventArgs e)
    {
        // Reset does not touch the text: the editor's text stays the same, the offset starts from the top.
        PrompterInnerScroll(PrompterEditor)?.ScrollToHome();
    }

    private void OnPrompterFromClipboardClick(object sender, RoutedEventArgs e)
    {
        if (_service is null)
        {
            return;
        }

        // Polite no-op: if there is no text in history the editor and placeholder stay as they are; no popup/chat.
        if (_service.TryLastClipboardText(out string text))
        {
            LoadPrompterText(text);
        }
    }

    private void OnPrompterLoadFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Text file (*.txt)|*.txt" };
        bool? chosen = dialog.ShowDialog(Window.GetWindow(this));
        if (chosen != true)
        {
            return;
        }

        try
        {
            LoadPrompterText(File.ReadAllText(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            // Corrupt/inaccessible file is a polite no-op: the editor keeps the current text, silent log.
            Debug.WriteLine($"[WharfBar] prompter txt load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// New text in a single path: playback returns to Editing (the pin is free) and scrolling
    /// restarts from the top — "Take from clipboard" and ".txt" use the same gate.
    /// </summary>
    private void LoadPrompterText(string text)
    {
        StopPrompterPlayback();
        PrompterEditor.Text = text;
        PrompterInnerScroll(PrompterEditor)?.ScrollToHome();
    }

    private void OnPrompterSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _prompterSpeed = PrompterMotion.ClampMultiplier(e.NewValue);

        // The label may not be built yet during XAML load (the Slider comes before the label).
        if (PrompterSpeedLabel is not null)
        {
            PrompterSpeedLabel.Text = _prompterSpeed.ToString("F1", CultureInfo.InvariantCulture) + "×";
        }
    }

    #endregion

    /// <summary>
    /// Panel keyboard navigation. It arrives from <c>PreviewKeyDown</c> while the search box has focus; the key
    /// is forwarded to the service directly here, no fake <see cref="KeyEventArgs"/> is constructed.
    /// </summary>
    /// <returns><c>true</c> for a consumed key (the caller sets <c>e.Handled</c>).</returns>
    internal bool HandleNavigationKey(Key key)
    {
        // While the search box has focus the arrows stay with the text caret. In the Music body left/right change track.
        if (!SearchBox.IsKeyboardFocused
            && MusicBody.Visibility == Visibility.Visible
            && ClipboardBody.Visibility != Visibility.Visible)
        {
            switch (key)
            {
                case Key.Left:
                    _media?.Previous();
                    return true;
                case Key.Right:
                    _media?.Next();
                    return true;
                default:
                    return false;
            }
        }

        if (CurrentHubTab is IslandHubTab.Wallpaper or IslandHubTab.Shelf or IslandHubTab.Prompter or IslandHubTab.Camera or IslandHubTab.ColorPicker)
        {
            // Wallpaper, shelf, prompter and camera bodies carry no list navigation; strip keys fall back to the bottom bar.
            return false;
        }

        switch (key)
        {
            case Key.Up:
            case Key.Down:
                if (_rows.Count == 0)
                {
                    return false;
                }

                _selectedIndex = key == Key.Down
                    ? Math.Min(_rows.Count - 1, _selectedIndex + 1)
                    : Math.Max(0, _selectedIndex - 1);
                Rebuild();
                return true;

            case Key.Enter:
                if (_selectedIndex < 0 || _selectedIndex >= _rows.Count)
                {
                    return false;
                }

                Activate(_rows[_selectedIndex]);
                return true;

            case Key.Delete:
                if (_selectedIndex < 0 || _selectedIndex >= _rows.Count)
                {
                    return false;
                }

                _service?.Remove(_rows[_selectedIndex]);
                return true;

            default:
                return false;
        }
    }
}
