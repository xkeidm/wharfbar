using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Windows SMTC (GlobalSystemMediaTransportControlsSessionManager) üzerinden çalan medyayı
/// izler: snapshot (başlık/sanatçı/kapak/durum) + oynat/duraklat/ileri/geri + oturum seçici.
/// Tüm olaylar UI thread'e (ctor'un kurulduğu Dispatcher) marshal edilir.
/// Kurulum hatası → 10 sn sonra yeniden dener (SMTC hizmeti gelene kadar).
/// </summary>
internal sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MediaSessionSelector _selector = new();
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageSource> _thumbnails = new(StringComparer.Ordinal);
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private MediaSnapshot _snapshot = MediaSnapshot.Idle;
    private DispatcherTimer? _retryTimer;
    private bool _disposed;

    internal MediaSessionService()
    {
        // UI thread'de kurulur (TopBarWindow.OnSourceInitialized).
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    internal event EventHandler? Changed;

    internal event EventHandler? SessionsChanged;

    internal MediaSnapshot Snapshot => _snapshot;

    internal bool HasActiveSession => _selector.Current is not null;

    internal IReadOnlyList<string> SessionIds => _selector.Sessions;

    internal async Task InitializeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += OnSessionsChanged;
            ReconcileSessions();
            RaiseSessions();
            if (_selector.Current is not null)
            {
                await ReloadCurrentAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] SMTC init başarısız: {ex.Message}");
            ScheduleRetry();
        }
    }

    internal void TogglePlayPause() => SendCommand(static s => s.TryTogglePlayPauseAsync());

    internal void Next() => SendCommand(static s => s.TrySkipNextAsync());

    internal void Previous() => SendCommand(static s => s.TrySkipPreviousAsync());

    internal void SelectNext() => SelectRelative(1);

    internal void SelectPrevious() => SelectRelative(-1);

    public void Dispose()
    {
        _disposed = true;

        _retryTimer?.Stop();
        _retryTimer = null;

        if (_manager is not null)
        {
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager = null;
        }

        foreach (string id in _sessions.Keys.ToList())
        {
            RemoveSessionInternal(id);
        }

        _thumbnails.Clear();
    }

    private void SelectRelative(int delta)
    {
        if (_selector.MoveCurrent(delta) is null)
        {
            return;
        }

        RaiseSessions();
        _ = ReloadCurrentAsync();
    }

    private void SendCommand(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        string? id = _selector.Current;
        if (id is null || !_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        _ = InvokeCommandAsync(command, session);
    }

    private static async Task InvokeCommandAsync(
        Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command,
        GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            await command(session);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] komut başarısız (desteklenmeyen komut olabilir): {ex.Message}");
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            ReconcileSessions();
            RaiseSessions();
            if (_selector.Current is not null)
            {
                _ = ReloadCurrentAsync();
            }
            else
            {
                _snapshot = MediaSnapshot.Idle;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            string id = GetSessionId(sender);
            string? before = _selector.Current;
            SyncSelectorForSession(id);
            RaiseSessions();
            if (id == before || _selector.Current != before)
            {
                _ = ReloadCurrentAsync();
            }
        });
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (GetSessionId(sender) == _selector.Current)
            {
                _ = ReloadCurrentAsync();
            }
        });
    }

    /// <summary>Manager'daki oturumları mevcut kümeyle eşitler: yenileri abone et + selector'a ekle, gidenleri çıkar.</summary>
    private void ReconcileSessions()
    {
        if (_manager is null)
        {
            return;
        }

        var live = _manager.GetSessions();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in live)
        {
            string id = GetSessionId(session);
            seenIds.Add(id);
            if (_sessions.ContainsKey(id))
            {
                continue;
            }

            session.PlaybackInfoChanged += OnPlaybackChanged;
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _sessions[id] = session;
            SyncSelectorForSession(id);
        }

        foreach (string id in _sessions.Keys.Where(id => !seenIds.Contains(id)).ToList())
        {
            RemoveSessionInternal(id);
        }
    }

    private void SyncSelectorForSession(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        GlobalSystemMediaTransportControlsSessionPlaybackStatus status;
        try
        {
            status = session.GetPlaybackInfo().PlaybackStatus;
        }
        catch (Exception ex)
        {
            // Oturum kapanıyor veya COM hatası veriyor — selector'dan çıkar.
            Debug.WriteLine($"[media] playback bilgisi alınamadı, oturum çıkarılıyor: {ex.Message}");
            _selector.RemoveSession(id);
            return;
        }

        if (status is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
            or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
        {
            _selector.AddSession(id);
            _selector.MarkActive(id);
        }
        else
        {
            _selector.RemoveSession(id);
        }
    }

    private void RemoveSessionInternal(string id)
    {
        if (_sessions.Remove(id, out var session))
        {
            session.PlaybackInfoChanged -= OnPlaybackChanged;
            session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _thumbnails.Remove(id);
            _selector.RemoveSession(id);
        }
    }

    private async Task ReloadCurrentAsync()
    {
        string? id = _selector.Current;
        if (id is null)
        {
            _snapshot = MediaSnapshot.Idle;
            _thumbnails.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        string appName = SimplifyAppName(id);
        string title = string.Empty;
        string artist = string.Empty;
        IRandomAccessStreamReference? thumbnailRef = null;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            title = props.Title ?? string.Empty;
            artist = props.Artist ?? string.Empty;
            thumbnailRef = props.Thumbnail;
        }
        catch (Exception ex)
        {
            // Metadata alınamadı (uygulama yanıt vermiyor) — app adı fallback'i kullanılır.
            Debug.WriteLine($"[media] metadata başarısız: {ex.Message}");
        }

        if (id != _selector.Current)
        {
            return;
        }

        ImageSource? thumb = null;
        if (thumbnailRef is not null && !_thumbnails.TryGetValue(id, out thumb))
        {
            thumb = await TryDecodeThumbnailAsync(thumbnailRef);
            if (thumb is not null)
            {
                _thumbnails[id] = thumb;
            }
        }

        if (id != _selector.Current)
        {
            return;
        }

        bool isPlaying = false;
        try
        {
            isPlaying = session.GetPlaybackInfo().PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] playback bilgisi alınamadı: {ex.Message}");
        }

        _snapshot = new MediaSnapshot(id, appName, title, artist, isPlaying, thumb);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<ImageSource?> TryDecodeThumbnailAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var stream = await reference.OpenReadAsync();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream.AsStreamForRead();
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] kapak çözülemedi: {ex.Message}");
            return null;
        }
    }

    /// <summary>AUMID'i okunabilir uygulama adına sadeleştirir: "..."!LastSegment[.exe]</summary>
    private static string SimplifyAppName(string appUserModelId)
    {
        string name = appUserModelId;
        int bang = name.LastIndexOf('!');
        if (bang >= 0 && bang < name.Length - 1)
        {
            name = name[(bang + 1)..];
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>Oturum anahtarı: AUMID (uygulama başına tek oturum). Boşsa "session" fallback.</summary>
    private static string GetSessionId(GlobalSystemMediaTransportControlsSession session)
    {
        string id = session.SourceAppUserModelId;
        return string.IsNullOrWhiteSpace(id) ? "session" : id;
    }

    private void RaiseSessions() => SessionsChanged?.Invoke(this, EventArgs.Empty);

    private void ScheduleRetry()
    {
        _retryTimer?.Stop();
        _retryTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _retryTimer.Tick += (_, _) =>
        {
            _retryTimer?.Stop();
            _retryTimer = null;
            _ = InitializeAsync();
        };
        _retryTimer.Start();
    }
}
