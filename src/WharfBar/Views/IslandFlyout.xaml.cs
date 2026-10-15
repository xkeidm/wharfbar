using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Island paneli: arama kutusu + tek birleşik zaman çizelgesi (pano metni ve bırakılan
/// dosyalar). Projede MVVM yok; satırlar <see cref="Border"/> olarak elle kurulur.
/// </summary>
public sealed partial class IslandFlyout : UserControl
{
    /// <summary>
    /// Tek seferde kurulan satır tavanı. <c>MaxItems</c> 5000'e kadar çıkabiliyor ve liste
    /// sanalize değil: her tuş vuruşunda 5000 satır yeniden kurmak kayıtlı bir AppBar olan
    /// üst barı takar. Kurulmayan kayıt sayısı alt bilgide bildirilir, arama yine tüm kayıtlara
    /// bakar (sıra "en yeni başta" olduğu için ilk 200 eşleşme en yenilerdir).
    /// </summary>
    private const int MaxRenderedRows = 200;

    /// <summary>Bırakılan dosya kaydı — emoji, font bağımlılığı yok (MDL2 kodu doğrulanmadan yazılmaz).</summary>
    internal const string FileGlyph = "\uD83D\uDCC4";

    /// <summary>Pano metni kaydı.</summary>
    internal const string TextGlyph = "\u2261";

    /// <summary>Satır silme düğmesi (MDL2 Delete).</summary>
    private const string DeleteGlyph = "\uE74D";

    private static readonly TimeSpan TimelinePollInterval = TimeSpan.FromSeconds(1);

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

    public IslandFlyout()
    {
        InitializeComponent();
        FlyoutRoot.SizeChanged += (_, _) => RebuildSilhouette();
    }

    /// <summary>
    /// Servisi bağlar; önceki bağlantıyı koparır (idempotent). Listeyi kurmaz;
    /// açılış kurulumu <see cref="PresentHub"/> ile yapılır.
    /// </summary>
    internal void Attach(IslandService service)
    {
        Detach();
        _service = service;
        service.Changed += OnChanged;
    }

    internal void Detach()
    {
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
        if (_media is not null)
        {
            _media.Changed -= OnMediaSnapshotChanged;
            _media.SessionsChanged -= OnMediaSnapshotChanged;
            _media = null;
        }
    }

    /// <summary>İkincil monitörde pano düğmesi yok; liste de bağlanmaz.</summary>
    internal void SetClipboardChrome(bool available)
    {
        TabClipboard.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        TabWallpaper.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>Hap ve panel aynı dolguyu paylaşsın diye silüet bu dolguyla boyanır.</summary>
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
        SilhouetteFill.Data = IslandSilhouetteGeometry.Create(shape.Fill, filled: true);
        SilhouetteOutline.Data = IslandSilhouetteGeometry.Create(shape.Outline, filled: false);
        if (SilhouetteFill.Fill is null)
        {
            SilhouetteFill.Fill = _silhouetteFill;
        }

        SilhouetteOutline.Stroke = HighlightOutline(_outlineTint);
    }

    private Color? _outlineTint;

    /// <summary>Top-lit rim: bright at the top, fading toward the bottom; tinted when a color is active.</summary>
    private static Brush HighlightOutline(Color? tint)
    {
        Color top = tint is Color c ? Color.FromArgb(0xA0, c.R, c.G, c.B) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
        Color bottom = tint is Color d ? Color.FromArgb(0x30, d.R, d.G, d.B) : Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF);
        var brush = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
        brush.Freeze();
        return brush;
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
    }

    private FrameworkElement BodyFor(IslandHubTab tab) => tab switch
    {
        IslandHubTab.Wallpaper => WallpaperBody,
        IslandHubTab.Music => MusicBody,
        _ => ClipboardBody,
    };

    /// <summary>Same popup, one of three bodies. <paramref name="focus"/> only on a pinning click.</summary>
    internal void PresentHub(IslandHubTab tab, bool focus, bool crossfade = false)
    {
        ApplySurface();
        SetMusicTabVisible(_media?.Snapshot.HasSession ?? false);
        CurrentHubTab = tab;
        _clipboardShown = tab != IslandHubTab.Music;
        UpdateTabChrome(tab);

        switch (tab)
        {
            case IslandHubTab.Music:
                if (MusicCover.Clip is null)
                {
                    MusicCover.Clip = new RectangleGeometry(new Rect(0, 0, 88, 88), 14, 14);
                }

                ApplyMusic();
                break;
            case IslandHubTab.Wallpaper:
                StopTimelinePoll();
                ApplyTint(null);
                WallpaperHost.Refresh();
                break;
            default:
                StopTimelinePoll();
                ApplyTint(null);
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
        foreach (Button b in new[] { TabClipboard, TabWallpaper, TabMusic })
        {
            if (b.Tag as string == IslandHub.ToConfig(tab))
            {
                b.SetResourceReference(BackgroundProperty, "ControlFillColorSecondaryBrush");
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
        foreach (FrameworkElement body in new FrameworkElement[] { MusicBody, ClipboardBody, WallpaperBody })
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

    /// <summary>Hover ile açılmış panoyu sabitleyen tıklama arama kutusuna odaklar.</summary>
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
    /// Müzik ve pano aynı popup içinde ~150 ms çapraz solar. Yükseklik birlikte akar;
    /// ilk açılışta panel animasyonu yeter, <paramref name="crossfade"/> kapalı kalır.
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
    }

    private void ApplyMusic()
    {
        MediaSnapshot snapshot = _media?.Snapshot ?? MediaSnapshot.Idle;
        bool hasSession = snapshot.HasSession;
        MusicNow.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        MusicTransport.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        MusicEmpty.Visibility = hasSession ? Visibility.Collapsed : Visibility.Visible;
        MusicSessions.Visibility = _media is { SessionIds.Count: > 1 }
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!hasSession)
        {
            MusicTitle.Text = string.Empty;
            MusicArtist.Text = string.Empty;
            MusicArt.Visibility = Visibility.Collapsed;
            MusicPlaceholder.Visibility = Visibility.Visible;
            MusicTimeline.Visibility = Visibility.Collapsed;
            ApplyTint(null);
            return;
        }

        MusicTitle.Text = string.IsNullOrEmpty(snapshot.Title) ? snapshot.AppName : snapshot.Title;
        MusicArtist.Text = snapshot.Artist;
        MusicSessionName.Text = snapshot.AppName;
        MusicPlayGlyph.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";

        if (snapshot.Thumbnail is not null)
        {
            MusicArt.Source = snapshot.Thumbnail;
            MusicArt.Visibility = Visibility.Visible;
            MusicPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            MusicArt.Visibility = Visibility.Collapsed;
            MusicPlaceholder.Visibility = Visibility.Visible;
        }

        ApplyTint(IslandTintSource.Resolve(ColorMode, snapshot.Thumbnail, snapshot.IsPlaying));
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
            if (_clipboardShown)
            {
                return;
            }

            MediaSnapshot snapshot = _media?.Snapshot ?? MediaSnapshot.Idle;
            ApplyTint(snapshot.HasSession
                ? IslandTintSource.Resolve(_colorMode, snapshot.Thumbnail, snapshot.IsPlaying)
                : null);
        }
    }

    private string _colorMode = IslandConfig.DefaultColorMode;
    private bool _clipboardShown;

    /// <summary>Tints the ambient glow and the progress fill; null restores the neutral look.</summary>
    internal void ApplyTint(Color? tint)
    {
        _outlineTint = tint;
        SilhouetteOutline.Stroke = HighlightOutline(tint);
        if (tint is not Color c)
        {
            AmbientGlow.Opacity = 0;
            MusicProgressFill.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            return;
        }

        var glow = new RadialGradientBrush(Color.FromArgb(0x59, c.R, c.G, c.B), Colors.Transparent)
        {
            Center = new Point(0.1, 0.0),
            GradientOrigin = new Point(0.1, 0.0),
            RadiusX = 0.9,
            RadiusY = 0.55,
        };
        glow.Freeze();
        AmbientGlow.Background = glow;
        AmbientGlow.Opacity = 1;

        var solid = new SolidColorBrush(c);
        solid.Freeze();
        MusicProgressFill.Background = solid;
    }

    private bool _writingSeek;

    /// <summary>
    /// Çubuk yalnız SMTC konum ve süre verdiğinde. Kaydırıcı ayrıca <see cref="MediaSnapshot.CanSeek"/> ister.
    /// Sayılar süre metnidir, yüzde değil.
    /// </summary>
    private void ApplyTimeline(MediaSnapshot snapshot)
    {
        string? clock = snapshot.TimelineText();
        if (clock is null || snapshot.Position is not TimeSpan position || snapshot.Duration is not TimeSpan duration
            || duration <= TimeSpan.Zero)
        {
            MusicTimeline.Visibility = Visibility.Collapsed;
            MusicSeek.Visibility = Visibility.Collapsed;
            MusicProgressTrack.Visibility = Visibility.Collapsed;
            return;
        }

        MusicTime.Text = clock;
        System.Windows.Automation.AutomationProperties.SetName(MusicTime, clock);
        MusicTimeline.Visibility = Visibility.Visible;

        if (snapshot.CanSeek)
        {
            MusicProgressTrack.Visibility = Visibility.Collapsed;
            MusicSeek.Visibility = Visibility.Visible;
            MusicSeek.Maximum = duration.TotalMilliseconds;
            if (!MusicSeek.IsMouseCaptureWithin)
            {
                _writingSeek = true;
                MusicSeek.Value = Math.Clamp(position.TotalMilliseconds, 0, duration.TotalMilliseconds);
                _writingSeek = false;
            }

            return;
        }

        MusicSeek.Visibility = Visibility.Collapsed;
        MusicProgressTrack.Visibility = Visibility.Visible;
        double ratio = Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
        MusicProgressDone.Width = new GridLength(ratio, GridUnitType.Star);
        MusicProgressRest.Width = new GridLength(Math.Max(0, 1 - ratio), GridUnitType.Star);
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
        _media?.RefreshTimeline();
        _timelineTimer ??= new DispatcherTimer { Interval = TimelinePollInterval };
        _timelineTimer.Tick -= OnTimelineTick;
        _timelineTimer.Tick += OnTimelineTick;
        _timelineTimer.Start();
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

        _media?.RefreshTimeline();
    }

    private void OnPreviousTrackClick(object sender, RoutedEventArgs e) => _media?.Previous();

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _media?.TogglePlayPause();

    private void OnNextTrackClick(object sender, RoutedEventArgs e) => _media?.Next();

    private void OnPreviousSessionClick(object sender, RoutedEventArgs e) => _media?.SelectPrevious();

    private void OnNextSessionClick(object sender, RoutedEventArgs e) => _media?.SelectNext();

    /// <summary>
    /// Panel kapandı. Canlı abonelik bırakılır: gizliyken her kopyalamada listeyi yeniden
    /// kurmak, kayıtlı bir AppBar olan üst bar için bedava iş değildir.
    /// </summary>
    internal void OnPanelClosed()
    {
        StopTimelinePoll();
        Detach();
    }

    /// <summary>
    /// <see cref="IslandService.Changed"/> yalnızca UI dispatcher'ında yükselir
    /// (<c>IslandService.RaiseChanged</c>); atlamaya gerek yok.
    /// </summary>
    private void OnChanged(object? sender, EventArgs e) => Rebuild();

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

        // Tema fırçası her kurulumda değil, her yeniden kurulumda bir kez çözülür: 200 satır
        // için 200 kez kaynak aramak gereksiz.
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
            ToolTip = "Sil",
            Focusable = false,
            Visibility = Visibility.Hidden,
            VerticalAlignment = VerticalAlignment.Center,
        };
        System.Windows.Automation.AutomationProperties.SetName(delete, "Sil");
        if (TryFindResource("IslandIconButton") is Style iconButtonStyle)
        {
            delete.Style = iconButtonStyle;
        }

        delete.Click += (_, e) =>
        {
            e.Handled = true;
            // Rebuild() _selectedIndex'i kendisi kısaltır (Delete tuşu yoluyla aynı).
            _service?.Remove(entry);
        };
        Grid.SetColumn(delete, 2);
        grid.Children.Add(delete);

        row.Child = grid;

        // Dosya satırında yalnız ad görünür; tam yol tooltip'te (kopyanın nerede olduğu
        // listeden anlaşılmıyor).
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
            // Button zaten MouseLeftButtonUp'u işler; yine de silme düğmesinden gelen olay
            // satırı etkinleştirmesin.
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
        Application.Current.TryFindResource("AppIconFontFamily") as FontFamily
        ?? new FontFamily("Segoe MDL2 Assets");
    /// <summary>Satır sağındaki "kaynak uygulama · göreli zaman" etiketi.</summary>
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

    private static Brush ResolveTileBrush()
    {
        if (Application.Current.TryFindResource("ControlFillColorSecondaryBrush") is Brush brush)
        {
            return brush;
        }

        var fallback = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        fallback.Freeze();
        return fallback;
    }

    private static Brush ResolveSelectedBrush()
    {
        if (Application.Current.TryFindResource("ControlFillColorDefaultBrush") is Brush brush)
        {
            return brush;
        }

        // Tema sözlüğü yüklenmemişse (ilk açılış yarışı) seçim görünmez olmasın.
        var fallback = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));
        fallback.Freeze();
        return fallback;
    }

    /// <summary>Alt bilgi satırı: boş liste ya da sınır nedeniyle kurulmayan kayıt sayısı.</summary>
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

        // Klavye ile gezinirken seçili satır kaydırma alanının dışında kalabiliyor.
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

        // Metin kaydı panoya geri konur. PutBack kendi kopyasını yeniden yakalamaz
        // (CanIncludeInClipboardHistory=0 yazıyor) — aksi halde aynı kayıt iki kez listelenirdi.
        _service.PutBack(entry);
    }

    /// <summary>
    /// Bırakılan kopyayı varsayılan uygulamayla açar; dosya silinmez.
    /// </summary>
    /// <remarks>
    /// Yol <c>island.json</c>'dan gelir, yani <b>güvenilmez</b> bir girdidir. Silme hâlâ
    /// <c>Drop</c> önekini zorunlu tutar. Açma <see cref="IslandService.CanOpenShelfFile"/>
    /// kapısından geçer: raf yolu kopyalamadan durabilir, dizin ve kaçış yolları reddedilir.
    /// </remarks>
    private void OpenFile(string? path)
    {
        if (_service is null || !_service.CanOpenShelfFile(path))
        {
            if (path is not null)
            {
                Debug.WriteLine($"[WharfBar] island open skipped, outside Drop: {path}");
            }

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Bozuk kısayol, erişim yok, varsayılan uygulama yok: kayıt listede kalır.
            Debug.WriteLine($"[WharfBar] island open failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Panel klavye gezinmesi. Arama kutusu odaktayken <c>PreviewKeyDown</c>'den gelir; tuş
    /// burada doğrudan servise iletilir, sahte <see cref="KeyEventArgs"/> kurulmaz.
    /// </summary>
    /// <returns>Tüketilen tuş için <c>true</c> (çağıran <c>e.Handled</c> yapar).</returns>
    internal bool HandleNavigationKey(Key key)
    {
        // Arama kutusu odaktayken oklar metin imlecinde kalır. Müzik gövdesinde sol/sağ parça değiştirir.
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

        if (CurrentHubTab == IslandHubTab.Wallpaper)
        {
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
