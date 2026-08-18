using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class MediaFlyout : UserControl
{
    private const double SessionSwitchSlideDip = 14;
    private static readonly TimeSpan SessionSwitchDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SessionSwitchFadeDuration = TimeSpan.FromMilliseconds(180);

    private MediaSessionService? _service;
    private string? _currentSessionId;
    private int _sessionSwitchDirection;

    public MediaFlyout()
    {
        InitializeComponent();
    }

    internal void Attach(MediaSessionService service)
    {
        if (_service is not null)
        {
            _service.Changed -= OnChanged;
            _service.SessionsChanged -= OnSessionsChanged;
        }

        _service = service;
        _service.Changed += OnChanged;
        _service.SessionsChanged += OnSessionsChanged;
        Apply(_service.Snapshot);
        ApplySessions(_service);
    }

    internal void OnFlyoutOpened()
    {
        FlyoutRoot.Background = FlyoutSurface.Create();
        // Açılışta kendi giriş animasyonu oynar; ilk Apply'e oturum geçiş animasyonu yok.
        _currentSessionId = null;
        _sessionSwitchDirection = 0;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_service is not null)
        {
            Apply(_service.Snapshot);
            ApplySessions(_service);
        }
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        if (_service is not null)
        {
            ApplySessions(_service);
        }
    }

    private void Apply(MediaSnapshot snapshot)
    {
        bool hasSession = snapshot.HasSession;
        NowPlayingPanel.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        TransportPanel.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = hasSession ? Visibility.Collapsed : Visibility.Visible;

        // Oturum değişince içerik yönlü kaydırma + fade ile gelir; aynı oturumdaki
        // parça/oynat-durdur güncellemeleri animasyonsuz kalır.
        if (hasSession && _currentSessionId is not null && snapshot.SessionId != _currentSessionId)
        {
            AnimateSessionSwitch(_sessionSwitchDirection);
        }

        _sessionSwitchDirection = 0;
        _currentSessionId = snapshot.SessionId;

        if (!hasSession)
        {
            TitleText.Text = string.Empty;
            ArtistText.Text = string.Empty;
            ThumbnailImage.Visibility = Visibility.Collapsed;
            ThumbnailPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        // Metadata alınamadıysa uygulama adı fallback.
        TitleText.Text = string.IsNullOrEmpty(snapshot.Title) ? snapshot.AppName : snapshot.Title;
        ArtistText.Text = snapshot.Artist;

        if (snapshot.Thumbnail is not null)
        {
            ThumbnailImage.Source = snapshot.Thumbnail;
            ThumbnailImage.Visibility = Visibility.Visible;
            ThumbnailPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            ThumbnailImage.Visibility = Visibility.Collapsed;
            ThumbnailPlaceholder.Visibility = Visibility.Visible;
        }

        PlayPauseGlyph.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";
    }

    private void ApplySessions(MediaSessionService service)
    {
        SelectorPanel.Visibility = service.SessionIds.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;
        SessionNameText.Text = service.Snapshot.AppName;
    }

    private void OnPreviousSessionClick(object sender, RoutedEventArgs e)
    {
        _sessionSwitchDirection = -1;
        _service?.SelectPrevious();
    }

    private void OnNextSessionClick(object sender, RoutedEventArgs e)
    {
        _sessionSwitchDirection = 1;
        _service?.SelectNext();
    }

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _service?.Previous();

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _service?.TogglePlayPause();

    private void OnNextClick(object sender, RoutedEventArgs e) => _service?.Next();

    /// <summary>
    /// Oturum geçişinde içerik yönlü kayarak + fadelenerek gelir: sonraki (chevon ›) sağdan,
    /// önceki (‹) soldan; otomatik değişimde yönsüz fade. Flyout açılış stiliyle aynı
    /// CubicEase/EaseOut. Hızlı tekrarlarda önceki animasyon sıfırlanarak yeniden başlar.
    /// </summary>
    private void AnimateSessionSwitch(int direction)
    {
        var transform = NowPlayingPanel.RenderTransform as TranslateTransform ?? new TranslateTransform();
        NowPlayingPanel.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        NowPlayingPanel.BeginAnimation(UIElement.OpacityProperty, null);

        double fromX = direction switch
        {
            -1 => -SessionSwitchSlideDip,
            1 => SessionSwitchSlideDip,
            _ => 0,
        };

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        transform.X = fromX;
        NowPlayingPanel.Opacity = 0;
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, SessionSwitchDuration)
        {
            EasingFunction = ease,
        });
        NowPlayingPanel.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, SessionSwitchFadeDuration)
        {
            EasingFunction = ease,
        });
    }
}
