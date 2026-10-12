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

    private static readonly TimeSpan TimelinePollInterval = TimeSpan.FromSeconds(1);

    private IslandService? _service;
    private MediaSessionService? _media;
    private readonly List<IslandEntry> _rows = [];
    private int _selectedIndex = -1;
    private int _bodySwapId;
    private DispatcherTimer? _timelineTimer;
    private bool _timelineArmed;

    internal event EventHandler? ClipboardRequested;

    internal event EventHandler? MusicRequested;

    public IslandFlyout()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Servisi bağlar; önceki bağlantıyı koparır (idempotent). Listeyi kurmaz: tek çağıran
    /// <c>OnIslandPopupOpened</c> hemen ardından <see cref="OnPanelOpened"/>'ı çağırıyor,
    /// kurulum zaten orada yapılıyor.
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

    /// <summary>Panel açıldı: pano gövdesi. Müzik gövdesi <see cref="PresentMusic"/> ile gelir.</summary>
    internal void OnPanelOpened() => PresentClipboard(showBack: false);

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
        ClipboardAffordance.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Scaled surface. This is not the popup child: WPF aligns a popup on the child's
    /// transformed corners, so a scale on <see cref="IslandFlyout"/> itself shifts the
    /// panel off the pill for the whole open.
    /// </summary>
    internal FrameworkElement PanelSurface => FlyoutRoot;

    /// <summary>
    /// Arranged panel width in DIP. Zero until layout; the XAML <c>Width</c> is not a fallback.
    /// </summary>
    internal double PanelWidthDip
    {
        get
        {
            double arranged = FlyoutRoot.ActualWidth > 1 ? FlyoutRoot.ActualWidth : ActualWidth;
            return IslandPopupAlign.WidthForPlacement(arranged);
        }
    }

    /// <summary>Hap ve panel aynı dolguyu paylaşsın diye boyun kapağı da boyanır.</summary>
    internal void ApplySurface()
    {
        var brush = FlyoutSurface.Create();
        FlyoutRoot.Background = brush;
        NeckCover.Background = brush;
    }

    /// <summary>Panelin üst çizgisini hapın altında kapatan şerit. Genişlik hapla aynı.</summary>
    internal void SetNeck(double pillDip) => NeckCover.Width = Math.Max(0, pillDip);

    /// <summary>Aynı popup'ın müzik gövdesi. <paramref name="focus"/> yalnız sabitleyen tıklamada.</summary>
    internal void PresentMusic(bool focus, bool crossfade = false)
    {
        ApplySurface();
        if (MusicCover.Clip is null)
        {
            MusicCover.Clip = new RectangleGeometry(new Rect(0, 0, 56, 56), 8, 8);
        }

        ApplyMusic();
        RevealBody(MusicBody, ClipboardBody, crossfade);
        ArmTimelinePoll();
        if (!focus)
        {
            return;
        }

        Focusable = true;
        MusicPlayButton.Focus();
    }

    /// <summary>Aynı popup'ın pano gövdesi. Geri düğmesi yalnız müzikten geçildiyse durur.</summary>
    internal void PresentClipboard(bool showBack, bool focus = true, bool crossfade = false)
    {
        ApplySurface();
        StopTimelinePoll();
        BackToMusicButton.Visibility = showBack ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Clear();
        _selectedIndex = 0;
        Rebuild();
        RevealBody(ClipboardBody, MusicBody, crossfade);
        if (focus)
        {
            FocusClipboardSearch();
        }
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

    private void OnMediaSnapshotChanged(object? sender, EventArgs e)
    {
        if (MusicBody.Visibility == Visibility.Visible)
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

        ApplyTimeline(snapshot);
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

    private void OnClipboardAffordanceClick(object sender, RoutedEventArgs e)
        => ClipboardRequested?.Invoke(this, EventArgs.Empty);

    private void OnBackToMusicClick(object sender, RoutedEventArgs e)
        => MusicRequested?.Invoke(this, EventArgs.Empty);

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
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(8),
            Background = restingBrush,
            Cursor = Cursors.Hand,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = entry.Kind == IslandEntryKind.File ? FileGlyph : TextGlyph,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var text = new TextBlock
        {
            Text = IslandPruner.FormatPreview(entry.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var meta = new TextBlock
        {
            Text = BuildMeta(entry),
            Opacity = 0.6,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(meta, 2);
        grid.Children.Add(meta);

        row.Child = grid;

        // Dosya satırında yalnız ad görünür; tam yol tooltip'te (kopyanın nerede olduğu
        // listeden anlaşılmıyor).
        if (entry.Kind == IslandEntryKind.File && entry.FilePath is { Length: > 0 } path)
        {
            row.ToolTip = path;
        }

        row.MouseEnter += (_, _) => row.Background = selectedBrush;
        row.MouseLeave += (_, _) => row.Background = restingBrush;
        row.MouseLeftButtonUp += (_, _) => Activate(entry);
        return row;
    }

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
            HintText.Text = SearchBox.Text.Length == 0 ? "Kayıt yok." : "Eşleşen kayıt yok.";
            HintText.Visibility = Visibility.Visible;
            return;
        }

        if (total > shown)
        {
            HintText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "… {0} kayıt daha var (arama ile daralt)",
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
