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
/// Tracks playing media through Windows SMTC (GlobalSystemMediaTransportControlsSessionManager):
/// snapshot (title/artist/artwork/state) + play/pause/next/previous + session selector.
/// All events are marshaled to the UI thread (the Dispatcher captured by the ctor).
/// Setup failure → retries after 10 s (until the SMTC service is available).
/// </summary>
internal sealed class MediaSessionService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly MediaSessionSelector _selector = new();
    private readonly MediaAppNameResolver _appNames = new();
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Id, string Title, string Artist), ImageSource> _thumbnails = new();
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
            if (_manager is not null)
            {
                _manager.SessionsChanged -= OnSessionsChanged;
            }

            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_disposed)
            {
                return;
            }

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
            Debug.WriteLine($"[media] SMTC init failed: {ex.Message}");
            ScheduleRetry();
        }
    }

    internal void TogglePlayPause() => SendCommand(static s => s.TryTogglePlayPauseAsync());

    internal void Next() => SendCommand(static s => s.TrySkipNextAsync());

    internal void Previous() => SendCommand(static s => s.TrySkipPreviousAsync());

    /// <summary>Active session reports <c>IsPlaybackPositionEnabled</c>.</summary>
    internal bool CanSeek => _snapshot.CanSeek;

    /// <summary>
    /// Seeks when the active session allows position changes. Otherwise a no-op.
    /// <paramref name="position"/> is the offset shown on the island timeline.
    /// </summary>
    internal void TrySeek(TimeSpan position)
    {
        if (_disposed || !_snapshot.CanSeek)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(() => TrySeek(position));
            return;
        }

        string? id = _selector.Current;
        if (id is null || !_sessions.TryGetValue(id, out GlobalSystemMediaTransportControlsSession? session))
        {
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        if (_snapshot.Duration is TimeSpan duration && duration > TimeSpan.Zero && position > duration)
        {
            position = duration;
        }

        _ = SeekAsync(session, position.Ticks);
    }

    private static async Task SeekAsync(GlobalSystemMediaTransportControlsSession session, long ticks)
    {
        try
        {
            await session.TryChangePlaybackPositionAsync(ticks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] search failed: {ex.Message}");
        }
    }

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
            Debug.WriteLine($"[media] command failed (possibly an unsupported command): {ex.Message}");
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

    /// <summary>Reconciles the manager's sessions with the current set: subscribe new ones + add them to the selector, remove the ones that left.</summary>
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
            // The session is closing or throwing a COM error — remove it from the selector.
            Debug.WriteLine($"[media] could not get playback info, removing session: {ex.Message}");
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
            foreach (var key in _thumbnails.Keys.Where(k => k.Id == id).ToList())
            {
                _thumbnails.Remove(key);
            }

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

        string appName = _appNames.Resolve(id);
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
            // Metadata could not be read (the app is not responding) — the last known values are kept,
            // otherwise the app-name fallback is used.
            Debug.WriteLine($"[media] metadata failed: {ex.Message}");
            if (_snapshot.SessionId == id)
            {
                title = _snapshot.Title;
                artist = _snapshot.Artist;
            }
        }

        if (id != _selector.Current)
        {
            return;
        }

        var thumbnailKey = (id, title, artist);
        ImageSource? thumb = null;
        if (thumbnailRef is not null && !_thumbnails.TryGetValue(thumbnailKey, out thumb))
        {
            thumb = await TryDecodeThumbnailAsync(thumbnailRef);
            if (thumb is not null)
            {
                _thumbnails[thumbnailKey] = thumb;
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
            Debug.WriteLine($"[media] could not get playback info: {ex.Message}");
        }

        (TimeSpan? position, TimeSpan? duration) = ReadTimeline(session);
        bool canSeek = ReadCanSeek(session);
        _snapshot = new MediaSnapshot(id, appName, title, artist, isPlaying, thumb, position, duration, canSeek);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Re-reads position/duration while the music panel is open. Artwork and title are not refreshed.
    /// If the value did not change, <see cref="Changed"/> is not raised.
    /// </summary>
    internal void RefreshTimeline()
    {
        if (_disposed)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(RefreshTimeline);
            return;
        }

        string? id = _selector.Current;
        if (id is null || !_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        if (!string.Equals(_snapshot.SessionId, id, StringComparison.Ordinal))
        {
            return;
        }

        (TimeSpan? position, TimeSpan? duration) = ReadTimeline(session);
        bool canSeek = ReadCanSeek(session);
        if (_snapshot.Position == position && _snapshot.Duration == duration && _snapshot.CanSeek == canSeek)
        {
            return;
        }

        _snapshot = _snapshot with { Position = position, Duration = duration, CanSeek = canSeek };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// SMTC timeline. Both are null if there is no duration, it is 0 or less, or the read throws.
    /// </summary>
    private static (TimeSpan? Position, TimeSpan? Duration) ReadTimeline(
        GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline = session.GetTimelineProperties();
            TimeSpan duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero || timeline.Position < TimeSpan.Zero)
            {
                return (null, null);
            }

            return (timeline.Position, duration);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] could not get timeline: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>SMTC <c>IsPlaybackPositionEnabled</c>. A failed read is not seekable.</summary>
    private static bool ReadCanSeek(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] could not read seek support: {ex.Message}");
            return false;
        }
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
            Debug.WriteLine($"[media] could not resolve cover: {ex.Message}");
            return null;
        }
    }

    /// <summary>Session key: AUMID (one session per app). If empty, the "session" fallback.</summary>
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
