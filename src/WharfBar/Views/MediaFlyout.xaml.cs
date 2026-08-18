using System.Windows;
using System.Windows.Controls;
using WharfBar.Models;
using WharfBar.Services;

namespace WharfBar.Views;

public partial class MediaFlyout : UserControl
{
    private MediaSessionService? _service;

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
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_service is not null)
        {
            Apply(_service.Snapshot);
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

    private void OnPreviousSessionClick(object sender, RoutedEventArgs e) => _service?.SelectPrevious();

    private void OnNextSessionClick(object sender, RoutedEventArgs e) => _service?.SelectNext();

    private void OnPreviousClick(object sender, RoutedEventArgs e) => _service?.Previous();

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => _service?.TogglePlayPause();

    private void OnNextClick(object sender, RoutedEventArgs e) => _service?.Next();
}
