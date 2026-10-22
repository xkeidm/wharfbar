using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        HubTabs.SizeChanged += OnHubTabStripSizeChanged;
        _tabStripButtons = [TabClipboard, TabWallpaper, TabMusic, TabShelf, TabPrompter, TabColor];
        _tabStripLabels = [TabClipboardLabel, TabWallpaperLabel, TabMusicLabel, TabShelfLabel, TabPrompterLabel, TabColorLabel];
    }

    /// <summary>
    /// Altı gövde sekmesi aynı anda görünürken etiketli şerit panel içine sığmaz (ölçülmüş:
    /// 6 etiketli sekme ~447 dip, panel iç genişliği ~388). Etiketler taştığı anda katlanır
    /// — adlar ToolTip ve otomasyon adlarında kalır — ve yer açıldığında geri döner. Beş ve
    /// daha az sekmenin Faz 1 görüntüsü hiçbir durumda değişmez.
    /// </summary>
    private bool _tabStripCompact;

    /// <summary>Etiket genişlikleri katlanmadan ÖNCE yakalanır; tekrar açma hesabı bunu kullanır.</summary>
    private readonly double[] _tabLabelWidths = new double[6];

    private readonly Button[] _tabStripButtons;
    private readonly TextBlock[] _tabStripLabels;

    private void OnHubTabStripSizeChanged(object sender, SizeChangedEventArgs e) => UpdateTabStripCompactness();

    /// <summary>
    /// Sekme görünürlüğü değişimi HubTabs'ın gerçek boyutunu değiştirmez (panel iç genişliği
    /// hep aynıdır, ActualWidth değişmez), SizeChanged ateşlenmez; karar bir düzen geçişi
    /// sonrasına ertelenir.
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
                // Katlanmadan ÖNCE etiket genişliklerini yakala; geri açma hesabı için gerekir.
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

        // Katlanmış ölçü + görünen etiketlerin genişlikleri: yine taşmayacaksa etiketler döner.
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
            // Bir sonraki düzen geçişi etiketli ölçüyü getirir; karar tek geçişte kapanır.
            HubTabs.InvalidateMeasure();

            // Kestirim yanlış olabilir (ilk katlanmada gizli bir sekmenin etiketi genişlik
            // 0 almıştı). Açılımdan sonra gerçek etiketli DesiredSize'la yeniden doğrula:
            // sığıyorsa geçiş sessiz biter, taşarsa şerit yeniden katlanır — döngü kurmaz.
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
        // Değişmez: bağlı olmayan panel oynamaz. Bir sonraki tik zaten kendi kendini
        // durdurur ama pim serbest bırakımı burada koşulsuz olsun.
        StopPrompterPlayback();

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
        // Şerit invariantı: ikincil barlar yalnız Müzik taşır, yani config'i açık olsa bile
        // gövde sekmeleri (Renk, Raf) bu yüzey kapısıyla birlikte gizlenir. Renk gövdesi ayrıca
        // ekran örneklemesini birincilin çalışma alanına kilitler (ScreenSampler tek zamanlama);
        // Raf gövdesi yalnız birincilde bağlanan bırakma servisine bakar.
        _primarySurface = available;
        TabColor.Visibility = available && _pickerTabWanted ? Visibility.Visible : Visibility.Collapsed;
        TabShelf.Visibility = available && _shelfTabWanted ? Visibility.Visible : Visibility.Collapsed;
        TabPrompter.Visibility = available && _prompterTabWanted ? Visibility.Visible : Visibility.Collapsed;
        ScheduleTabStripCompactness();
    }

    /// <summary>
    /// Gövde sekmelerinin yüzey kapısı: yalnız birincil barın hub'ı ekran örneklemesi (Renk)
    /// ve bırakma servisi (Raf) taşıyabildiği için <see cref="SetClipboardChrome"/> küçükken
    /// iki sekme de config'ten bağımsız gizlenir.
    /// </summary>
    private bool _primarySurface;

    /// <summary>Gövde sekmesinin istenen durum: bool ayarı primary yüzey kapısıyla birleşir.</summary>
    private bool _pickerTabWanted = IslandConfig.DefaultColorPickerTab;

    /// <summary>Raf sekmesinin istenen durum: bool ayarı primary yüzey kapısıyla birleşir.</summary>
    private bool _shelfTabWanted = IslandConfig.DefaultShelfTab;

    /// <summary>Prompter sekmesinin istenen durum: bool ayarı primary yüzey kapısıyla birleşir.</summary>
    private bool _prompterTabWanted = IslandConfig.DefaultPrompterTab;

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

    /// <summary>
    /// "Ekran rengini seç" tıklaması. Üst bar bu olayı alır: ada paneli önce kapanır, sonra
    /// tam çalışma alanını kaplayan seçim örtüsü açılır (<see cref="ColorPickerWindow"/> örtüsü
    /// tek topmost istisnası) ve seçim bittiğinde hub geri döner — paneli yöneten
    /// <c>TopBarWindow.Island</c> tarafı aynı sınıfta olduğu için düzen oradan gelir.
    /// </summary>
    internal event EventHandler? ScreenPickerRequested;

    /// <summary>
    /// Gövde Tag kimlikleri: sekmenin XAML'deki Tag dizgisini verir (<c>Tag="shelf"</c> vb.).
    /// Bilinmeyen değer <see cref="IslandHub.Normalize"/> gibi Pano'ya döner.
    /// </summary>
    internal static string TabIdForBody(IslandHubTab tab) => tab switch
    {
        IslandHubTab.Wallpaper => "wallpaper",
        IslandHubTab.Music => "music",
        IslandHubTab.Shelf => "shelf",
        IslandHubTab.Prompter => "prompter",
        IslandHubTab.ColorPicker => "colorpicker",
        _ => "clipboard",
    };

    private FrameworkElement BodyFor(IslandHubTab tab) => tab switch
    {
        IslandHubTab.Wallpaper => WallpaperBody,
        IslandHubTab.Music => MusicBody,
        IslandHubTab.Shelf => ShelfBody,
        IslandHubTab.Prompter => PrompterBody,
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
        CurrentHubTab = tab;
        _clipboardShown = tab != IslandHubTab.Music;
        UpdateTabChrome(tab);

        // Gövde gizlenince tik atmasın: sekme değişirken oynatmayı durdur (pim serbest
        // kalır; kaydırma konumu Paused'ta korunur, geri dönüşte kaldığı yerden sürer).
        if (tab != IslandHubTab.Prompter && _prompterState == PrompterMotion.Playback.Playing)
        {
            SetPrompterPlayback(PrompterMotion.Playback.Paused);
        }

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
            case IslandHubTab.Shelf:
                StopTimelinePoll();
                ApplyTint(null);
                RebuildShelf();
                break;
            case IslandHubTab.Prompter:
                StopTimelinePoll();
                ApplyTint(null);
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    // Gövde görünür + layout oturmuş halde karar verilir: extent/ofset sıfır gelmesin.
                    HookPrompterScroll();
                    RefreshPrompterPlayAvailability();
                }));
                break;
            case IslandHubTab.ColorPicker:
                StopTimelinePoll();
                ApplyTint(null);
                RebuildPicker();
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
        foreach (Button b in new[] { TabClipboard, TabWallpaper, TabMusic, TabShelf, TabPrompter, TabColor })
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
        foreach (FrameworkElement body in new FrameworkElement[] { MusicBody, ClipboardBody, WallpaperBody, ShelfBody, PrompterBody, ColorPickerBody })
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
        StopPrompterPlayback();
        Detach();
    }

    /// <summary>
    /// <see cref="IslandService.Changed"/> yalnızca UI dispatcher'ında yükselir
    /// (<c>IslandService.RaiseChanged</c>); atlamaya gerek yok.
    /// </summary>
    private void OnChanged(object? sender, EventArgs e)
    {
        if (_service is { Config.ShelfTab: false })
        {
            // Sekme ayardan kapandı: şeritten düşer (Müzik'teki güncelleme ilkesiyle aynı).
            // Gövdesi hâlâ açıksa beklemeye alınmaz: anında Pano gövdesine döner.
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
    /// Yol <c>island.json</c>'dan gelir, yani <b>güvenilmez</b> bir girdidir. Açma
    /// <see cref="IslandService.OpenShelfFile"/> kapısından geçer: orijinal yolu tutan raf
    /// kaydında dizin/kaçış yolları <see cref="IslandService.CanOpenShelfFile"/> tarafında
    /// reddedilir; başlatma hataları da orada loglanıp <c>false</c> döner.
    /// </remarks>
    private void OpenFile(string? path)
    {
        if (_service is null || path is null)
        {
            return;
        }

        _ = _service.OpenShelfFile(path);
    }

    #region Şelf gövdesi

    /// <summary>
    /// Raf gövdesi: bırakılan dosya kayıtları tek satırlık çip olarak — uzantı rozeti + dosya
    /// adı. Sıra <see cref="IslandService.Entries"/> ile aynı (en yeni başta); pano gövdesinin
    /// satır tavanı (<see cref="MaxRenderedRows"/>) burada da geçerlidir.
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

        bool empty = files.Count == 0;
        ShelfEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ShelfScroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        ShelfList.Children.Clear();
        int shown = Math.Min(files.Count, MaxRenderedRows);
        for (int i = 0; i < shown; i++)
        {
            ShelfList.Children.Add(BuildShelfChip(files[i]));
        }
    }

    /// <summary>
    /// Rozet metni: uzantının ilk üç harfi büyük harfe (<c>report.PDF → PDF</c>). Uzantısız
    /// dosyada adın ilk üç harfi okunur — boş rozet bırakılmaz.
    /// </summary>
    private static string ShelfBadge(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        ext = ext.Length > 0 ? ext[1..] : fileName;
        return (ext.Length <= 3 ? ext : ext[..3]).ToUpperInvariant();
    }

    /// <summary>
    /// Çip satırı: pano gövdesinin <see cref="BuildRow"/> deseniyle aynı ölçüler (kutu 32,
    /// köşe 12, iki satır arası 2) — tek fark: ikinci kolonda meta değil ad, sağ tık menüsü ve
    /// sürükleyerek dışarı taşıma. Sol tık hiçbir şey yapmaz; dosya açma menüden ya da pano
    /// gövdesindeki satırdan.
    /// </summary>
    private Border BuildShelfChip(IslandEntry entry)
    {
        var chip = new Border
        {
            Padding = new Thickness(8, 8, 8, 8),
            Margin = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(12),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var badge = new Border
        {
            Width = 36,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            Background = ResolveTileBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
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
        chip.Child = grid;

        // Dosya adı listede; kopyanın tam yolu tooltip'te (pano gövdesindeki yerleşimle aynı).
        if (entry.FilePath is { Length: > 0 } fullPath)
        {
            chip.ToolTip = fullPath;
        }

        Brush restingBrush = Brushes.Transparent;
        Brush hoverBrush = ResolveSelectedBrush();
        chip.MouseEnter += (_, _) => chip.Background = hoverBrush;
        chip.MouseLeave += (_, _) => chip.Background = restingBrush;

        AttachShelfChipDrag(chip, entry);
        chip.ContextMenu = BuildShelfChipMenu(entry);
        return chip;
    }

    /// <summary>
    /// Çipi bir klasör penceresine sürükler: <see cref="DataFormats.FileDrop"/> biçiminde
    /// <see cref="IslandEntry.FilePath"/> taşınır. Sürkleme <b>gerçek taşınımda</b> başlar:
    /// sol tuş <see cref="UIElement.MouseLeftButtonDownEvent"/>'te yakalanır, eşik aşılınca
    /// yakalama bırakılıp <see cref="DragDrop.DoDragDrop"/> çağrılır — o çağrı kullanıcı
    /// sürüklemeyi bırakana kadar engelleyici çalışır (UI thread'i zaten STA'dır).
    /// </summary>
    private void AttachShelfChipDrag(FrameworkElement chip, IslandEntry entry)
    {
        if (entry.FilePath is not { Length: > 0 } path)
        {
            return;
        }

        bool pressed = false;
        Point origin = default;
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

            // DoDragDrop engelleyicidir: bayrak önce kapatılır ki çağrı bittikten sonra
            // kuyruğa düşen MouseMove'larda sürükleme yeniden başlamasın.
            pressed = false;
            chip.ReleaseMouseCapture();
            var payload = new DataObject(DataFormats.FileDrop, new[] { path });
            _ = DragDrop.DoDragDrop(chip, payload, DragDropEffects.Copy | DragDropEffects.Move);
        };
        chip.MouseLeftButtonUp += (_, _) =>
        {
            if (pressed)
            {
                pressed = false;
                chip.ReleaseMouseCapture();
            }
        };
    }

    /// <summary>
    /// Çip menüsü. "Uygulama ile aç" yalnız <see cref="IslandService.CanOpenShelfFile"/> izin
    /// verdiğinde görünür; her çipte "Listeden kaldır" vardır — kayıt listeden düşer, dosyaya
    /// dokunulmaz (silme sınırları <see cref="IslandService.Remove"/> ve
    /// <see cref="IslandService.DeleteDroppedFiles"/> kontratlarındadır). "Rafını boşalt"
    /// gövde altında tek düğmedir; menüye ikinci kez tekrarlanmaz.
    /// </summary>
    private ContextMenu BuildShelfChipMenu(IslandEntry entry)
    {
        var menu = new ContextMenu();
        if (_service is not null && _service.CanOpenShelfFile(entry.FilePath))
        {
            var open = new MenuItem { Header = "Uygulama ile aç" };
            open.Click += (_, _) => _ = _service?.OpenShelfFile(entry.FilePath!);
            menu.Items.Add(open);
        }

        var remove = new MenuItem { Header = "Listeden kaldır" };
        remove.Click += (_, _) => _service?.Remove(entry);
        menu.Items.Add(remove);
        return menu;
    }

    /// <summary>
    /// "Rafını boşalt": listeyi ve <c>Drop</c> kökündeki kopyaları boşaltmadan önce onay ister
    /// (tek seferde silinen kopyalar geri dönüşsüz). Pano gövdesinin
    /// <see cref="IslandService.Clear"/> yolu buraya değmez; kayıt yolu <c>Drop</c> dışındaysa
    /// kayıt yalnız listeden çıkar, orijinal dosyaya dokunulmaz.
    /// </summary>
    private void OnShelfClearClick(object sender, RoutedEventArgs e)
    {
        if (_service is null)
        {
            return;
        }

        const string title = "Rafını boşalt";
        const string text = "Raf listesi boşaltılır; %AppData%\\WharfBar\\Drop altındaki kopyalar da silinir. Devam edilsin mi?";
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

    #region Renk seçici gövdesi

    private DispatcherTimer? _pickerFeedbackTimer;

    /// <summary>
    /// Renk gövdesi: seçim tıklaması düğme + son renkler şeridi + büyük renk kartı. Renkler
    /// oturumluk <see cref="IslandPickedColors"/> deposundan gelir (pano gövdesinin
    /// <see cref="Rebuild"/> yeniden-kuruluş deseniyle aynı ölçüler).
    /// </summary>
    private void RebuildPicker()
    {
        IReadOnlyList<Color> colors = IslandPickedColors.Items;
        bool any = colors.Count > 0;
        Color current = any ? colors[0] : Color.FromArgb(0, 0, 0, 0);
        _pickerSelected = current;
        ApplyPickerSelection(current);
        PickerCopyButton.IsEnabled = any;

        // Yeni sunum eski geri bildirim flaşını da temizler (kalıcı "Kopyalandı" kalıntısı kalmasın).
        _pickerFeedbackTimer?.Stop();
        PickerFeedback.Opacity = 0;

        PickerRecents.Children.Clear();
        Brush tileBrush = ResolveTileBrush();
        for (int i = 0; i < colors.Count; i++)
        {
            PickerRecents.Children.Add(BuildPickerChip(colors[i], tileBrush));
        }
    }

    /// <summary>Büyük kartı verilen renge çeker; boş renk varsayılan görünümü döndürür.</summary>
    private void ApplyPickerSelection(Color color)
    {
        if (color.A == 0)
        {
            // XAML varsayılanına döner (DynamicResource bağını ClearValue sileceği için set edilir).
            PickerSwatch.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            PickerHex.Text = "—";
            System.Windows.Automation.AutomationProperties.SetName(PickerHex, "Henüz renk seçilmedi");
            return;
        }

        var fill = new SolidColorBrush(color);
        fill.Freeze();
        PickerSwatch.Background = fill;
        PickerHex.Text = ColorMath.ToHex(color.R, color.G, color.B);
        System.Windows.Automation.AutomationProperties.SetName(PickerHex, PickerHex.Text);
    }

    /// <summary>Son renkler şeridindeki küçük kutu: tıkla → seçim ona döner. HEX tooltip'te.</summary>
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

    /// <summary>Kopyala: seçili rengin HEX'i panoya yazılır (Task 7 yolu); sonuç bildirimi ile.</summary>
    private void OnPickerCopyClick(object sender, RoutedEventArgs e)
    {
        if (_service is null || _pickerSelected.A == 0)
        {
            return;
        }

        string hex = ColorMath.ToHex(_pickerSelected.R, _pickerSelected.G, _pickerSelected.B);
        // Yazma aynı Task 7 kapısından geçer: gate path'i senkron false, başarı BeginInvoke
        // ile birlikte gelir — iki zamanlama da aynı geri bildirimi yazar.
        _service.PutOnClipboard(hex, completed => ShowPickerCopyFeedback(completed));
    }

    /// <summary>Copy feedback flash ("Kopyalandı" / "Kopyalanamadı"): sessiz false kalmasın.</summary>
    internal void ShowPickerCopyFeedback(bool written)
    {
        ShowPickerFeedback(written ? "Kopyalandı" : "Kopyalanamadı", written);
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

    /// <summary>Seçili renk: gövde kuruluşunda son seçim, çip tıklamalarında o renk.</summary>
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

    #region Okur (Prompter) gövdesi

    /// <summary>
    /// Okur oynatma pimi: true = oynatma açık (Task 5 üst bar tarafını kilitler), false =
    /// serbest. Yalnız gerçek durum değişimlerinde atılır (Editing→Paused geçişinde sessiz).
    /// </summary>
    internal event Action<bool>? PrompterPinnedChanged;

    /// <summary>Oynatma simgeleri (MDL2 belgelenmiş): Play E768, Pause E769.</summary>
    private const string PrompterPlayGlyphText = "\uE768";
    private const string PrompterPauseGlyphText = "\uE769";

    /// <summary>
    /// Okuma tik aralığı ~16 ms. Tek karar: <see cref="DispatcherTimer"/>,
    /// <c>CompositionTarget.Rendering</c> kancası YOK — gizli gövdede durdurabilen ve
    /// mevcut <c>_timelineTimer</c> kalııyla aynı ömür desenine uyan baştan oluşturma.
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
    /// Okuma tek geçidi. Playing: editör salt-okunur, okuma fontu
    /// <see cref="PrompterMotion.ReadingFontDip"/>, tik çalışır; Editing/Paused: düzenlenebilir ve
    /// tik kapalı. Pim (true/false) yalnız oynatmanın açılıp kapandığında fırlar.
    /// </summary>
    private void SetPrompterPlayback(PrompterMotion.Playback incoming)
    {
        // Oynatma kapısı tek kararda: kaydırılacak yer yoksa (metin editöre tam sığar ya
        // da kaydırma uçta oturur) istek sessizce Editing'e döner — düğme tarafı aynı
        // kararla kapalı durur (<see cref="RefreshPrompterPlayAvailability"/>).
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
            // XAML'daki düzenleme fontu ilk Playing'e girişte yakalanır; buraya geri konur.
            PrompterEditor.FontSize = _prompterEditingFontSize;
        }

        PrompterPlayGlyph.Text = nowPlaying ? PrompterPauseGlyphText : PrompterPlayGlyphText;
        PrompterPlayLabel.Text = nowPlaying ? "Duraklat" : "Oynat";
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
        // Kapı 1: gövde gizliyken hiçbir tik dönmez (ikinci duvar <see cref="OnPanelClosed"/>).
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

        // Sonuna varış yalnız gerçek ilerlemede sayılır: animasyon kapalıysa hız 0'dır
        // (<see cref="PrompterMotion.SpeedDipPerSec"/>) ve okuma olduğumuz yerde bekler.
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

    /// <summary>Panel kapandı: tik durur, oynatma Editing'e döner (pim serbest kalır).</summary>
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
    /// Kaydırma kaynağı TextBox'ın İÇ ScrollViewer'ıdır (şablonundaki
    /// <c>PART_ContentHost</c>): motor ofseti buradan okur ve buraya yazar — dış bir
    /// kaydırıcı varmadır. Şablon uygulanmadıysa null.
    /// </summary>
    private static ScrollViewer? PrompterInnerScroll(TextBox editor) =>
        editor.Template is null
            ? null
            : editor.Template.FindName("PART_ContentHost", editor) as ScrollViewer;

    /// <summary>
    /// Oynat düğmesinin basılabilirliği editörün İÇ ScrollViewer'ından türetilir: yazma,
    /// elle kaydırma, dosya/pano yükleme ve font geçişi (Playing ↔ Editing) hepsi tek
    /// <c>ScrollChanged</c> kancasından tazelenir. Playing sırasında düğme Duraklat
    /// görevi gördüğünden tik başına tazeleme gerekmez (kapalı tutulur).
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
    /// "Yalan söylemeyen düğme": kaydırılacak yer yokken Oynat disabled olur ve tooltip
    /// gerekçeyi söyler — kısa metin ve okuma sonu artık sessiz ölü-uç değil. Playing'de
    /// düğme Duraklat olduğundan her zaman açıktır. Gövde gizliyken ölçüm sıfır gelir;
    /// karar o an verilmez (ilk açılışı <see cref="PresentHub"/> deferred tazeler).
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
            ? "Oynat/Duraklat"
            : scrollable <= 0
                ? "Kaydırılacak bölüm yok; editöre sığmayan daha uzun bir metin gerekli."
                : "Kaydırma en altta; Başa dön ile en baştan başla.";
    }

    private void OnPrompterEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        PrompterEditorPlaceholder.Visibility = PrompterEditor.Text.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Okunurken metni tutup sürükleme: dikey fare deltası aynı VerticalOffset'i
    /// sürükler — oynatma duraklamaz. Editing/Paused'ta TextBox
    /// kendi metin seçimine döner, biz müdahale etmeyiz.
    /// </summary>
    private void OnPrompterEditorPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Kaydırma çubuğunun kendi yolu var: metin sürükleme onu ele geçirmeden çalışsın.
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

        // Metni tutan el: metin aşağı inerse (dy > 0) okuma geriye döner (ofset küçülür).
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
        // Başa dön metne dokunmaz: editörün metni aynı kalır, ofset ilk üstten başlar.
        PrompterInnerScroll(PrompterEditor)?.ScrollToHome();
    }

    private void OnPrompterFromClipboardClick(object sender, RoutedEventArgs e)
    {
        if (_service is null)
        {
            return;
        }

        // Kibar-yok: geçmişte metin yoksa editör ve placeholder olduğu gibi kalır; popup/chat yok.
        if (_service.TryLastClipboardText(out string text))
        {
            LoadPrompterText(text);
        }
    }

    private void OnPrompterLoadFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Metin dosyası (*.txt)|*.txt" };
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
            // Bozuk/erişilemeyen dosya kibar-yok: editör mevcut metni tutar, sessiz log.
            Debug.WriteLine($"[WharfBar] prompter txt load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Yeni metin tek yol: oynatma Editing'e döner (pim serbest) ve kaydırma en baştan
    /// başlar — "Panodan al" ve ".txt" aynı kapıyı kullanır.
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

        // XAML yüklemesi sırasında etiket henüz kurulmamış olabilir (Slider etiketten önce).
        if (PrompterSpeedLabel is not null)
        {
            PrompterSpeedLabel.Text = _prompterSpeed.ToString("F1", CultureInfo.InvariantCulture) + "×";
        }
    }

    #endregion

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

        if (CurrentHubTab is IslandHubTab.Wallpaper or IslandHubTab.Shelf or IslandHubTab.Prompter or IslandHubTab.ColorPicker)
        {
            // Duvar kağıdı, raf ve okur gövdesi liste gezinmesi taşımaz; şerit tuşları alt bara döner.
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
