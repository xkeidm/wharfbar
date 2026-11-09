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
/// The timeline position is interpolated locally (<see cref="MediaTimeline"/>) because SMTC's raw
/// <c>Position</c> is frozen between app reports. All events are marshaled to the UI thread (the
/// Dispatcher captured by the ctor). Setup failure → retries after 10 s (until the SMTC service is available).
/// </summary>
/// <remarks>
/// Selection policy: the current session changes only when a session starts playing, when the user picks
/// another one with the panel chevrons (<see cref="SelectNext"/>/<see cref="SelectPrevious"/>), or when
/// the current session disappears. A paused/stopped status never steals the panel nor removes a session
/// from the selector — otherwise a paused leftover (e.g. a paused music app) would win over the player
/// that is really playing whenever the latter briefly changes status during a track skip.
/// </remarks>
internal sealed class MediaSessionService : IDisposable
{
    /// <summary>
    /// Grace window after an optimistic play/pause flip during which the panel poll does not override the
    /// flipped state: the player may still report the pre-command status until it has applied it.
    /// A <c>PlaybackInfoChanged</c> event (or this window expiring) publishes the authoritative state.
    /// </summary>
    private static readonly TimeSpan OptimisticPlaybackWindow = TimeSpan.FromSeconds(1);

    private readonly Dispatcher _dispatcher;
    private readonly MediaSessionSelector _selector = new();
    private readonly MediaAppNameResolver _appNames = new();
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>Last decoded cover per session (one entry per session; bounded by the session count).</summary>
    private readonly Dictionary<string, CachedArtwork> _artwork = new(StringComparer.Ordinal);

    /// <summary>
    /// Per-session artwork revision. It is bumped when the app republishes its media properties, so a
    /// changed cover with the same title/artist is decoded instead of being served from the cache.
    /// </summary>
    private readonly Dictionary<string, int> _artworkVersions = new(StringComparer.Ordinal);

    /// <summary>Monotonic reload counter; older async completions must not overwrite a newer track.</summary>
    private int _reloadGeneration;

    /// <summary>Session whose play/pause state was flipped optimistically (null when none).</summary>
    private string? _optimisticSessionId;

    /// <summary>Playback state published optimistically for <see cref="_optimisticSessionId"/>.</summary>
    private bool _optimisticIsPlaying;

    /// <summary>UTC instant after which the optimistic value is replaced by what SMTC reports.</summary>
    private DateTimeOffset _optimisticUntil;

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

    /// <summary>
    /// Instant play/pause feedback: the snapshot flips and <see cref="Changed"/> is raised before the
    /// command is sent, then reverts if SMTC reports the command failed. The panel poll does not undo the
    /// flip during <see cref="OptimisticPlaybackWindow"/>; a real <c>PlaybackInfoChanged</c> wins earlier.
    /// </summary>
    internal void TogglePlayPause()
    {
        if (_disposed)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _ = _dispatcher.BeginInvoke(TogglePlayPause);
            return;
        }

        string? id = _selector.Current;
        if (id is null || !_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        // The snapshot may still describe the previous session (a reload is in flight); then there is
        // nothing to flip optimistically and the next publish carries the real state.
        bool previous = _snapshot.IsPlaying;
        bool optimistic = string.Equals(_snapshot.SessionId, id, StringComparison.Ordinal);
        if (optimistic)
        {
            _snapshot = _snapshot with { IsPlaying = !previous };
            _optimisticSessionId = id;
            _optimisticIsPlaying = !previous;
            _optimisticUntil = DateTimeOffset.UtcNow + OptimisticPlaybackWindow;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        _ = TogglePlayPauseAsync(session, id, previous, optimistic);
    }

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
        ClearOptimisticPlayback();

        // Any in-flight metadata read / artwork decode belongs to the past now.
        unchecked
        {
            _reloadGeneration++;
        }

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

        _artwork.Clear();
        _artworkVersions.Clear();
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

    /// <summary>
    /// Sends the play/pause command and reverts the optimistic flip when SMTC reports it failed. If an
    /// authoritative publish already replaced the optimistic value, there is nothing to revert.
    /// </summary>
    private async Task TogglePlayPauseAsync(
        GlobalSystemMediaTransportControlsSession session,
        string sessionId,
        bool wasPlaying,
        bool wasOptimistic)
    {
        bool toggled;
        try
        {
            toggled = await session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] play/pause command failed: {ex.Message}");
            toggled = false;
        }

        if (toggled || !wasOptimistic || _disposed)
        {
            return;
        }

        void Revert()
        {
            if (_disposed
                || _optimisticUntil == default
                || !string.Equals(_optimisticSessionId, sessionId, StringComparison.Ordinal)
                || !string.Equals(_selector.Current, sessionId, StringComparison.Ordinal)
                || !string.Equals(_snapshot.SessionId, sessionId, StringComparison.Ordinal))
            {
                return;
            }

            // A newer toggle (or an authoritative publish) owns the state now; reverting would fight it.
            if (_optimisticIsPlaying == wasPlaying)
            {
                return;
            }

            ClearOptimisticPlayback();
            _snapshot = _snapshot with { IsPlaying = wasPlaying };
            Changed?.Invoke(this, EventArgs.Empty);
        }

        if (_dispatcher.CheckAccess())
        {
            Revert();
        }
        else
        {
            _ = _dispatcher.BeginInvoke(Revert);
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            string? before = _selector.Current;
            ReconcileSessions();
            string? after = _selector.Current;
            RaiseSessions();

            if (after is not null
                && string.Equals(before, after, StringComparison.Ordinal)
                && string.Equals(_snapshot.SessionId, after, StringComparison.Ordinal))
            {
                // The selected session survived the reconcile: only its playback state may have changed.
                PublishPlaybackNow(authoritative: false);
                return;
            }

            // A session left, appeared or got promoted: reload title/artwork. With no current session
            // left this publishes the idle snapshot and invalidates older reload completions.
            _ = ReloadCurrentAsync();
        });
    }

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            string id = GetSessionId(sender);
            string? before = _selector.Current;
            bool selectorChanged = SyncSelectorForSession(id);
            string? after = _selector.Current;

            if (selectorChanged)
            {
                RaiseSessions();
            }

            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                // The selection policy picked another session (it started playing, or the current one
                // closed): only now is a metadata/artwork reload needed.
                _ = ReloadCurrentAsync();
                return;
            }

            if (id == after)
            {
                // Same session: publish playback state and timeline immediately; metadata is unchanged.
                PublishPlaybackNow();
            }
        });
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = _dispatcher.BeginInvoke(() =>
        {
            string id = GetSessionId(sender);

            // The app republished its metadata (a new cover or a track change): a cached cover for this
            // session is no longer trustworthy, even when title/artist look the same.
            BumpArtworkVersion(id);

            if (id == _selector.Current)
            {
                _ = ReloadCurrentAsync();
            }
        });
    }

    /// <summary>
    /// Reconciles the manager's sessions with the current set: subscribe new ones and add them to the
    /// selector, re-sync the known ones, and remove the ones that left. A newly appearing playing session
    /// becomes current when the selected session is not itself playing (or nothing is selected) — a paused
    /// fallback must not keep the panel, while an already playing session is never stolen from.
    /// </summary>
    private void ReconcileSessions()
    {
        if (_manager is null)
        {
            return;
        }

        var live = _manager.GetSessions();
        string? newlyPlaying = null;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in live)
        {
            string id = GetSessionId(session);
            if (!seenIds.Add(id))
            {
                continue;
            }

            if (_sessions.ContainsKey(id))
            {
                // Catch up on a status change whose event was missed; the same selection policy applies.
                SyncSelectorForSession(id);
                continue;
            }

            session.PlaybackInfoChanged += OnPlaybackChanged;
            session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _sessions[id] = session;

            // Seed the observed state so the first PlaybackInfoChanged is not mistaken for a transition.
            MediaPlaybackState state = MediaPlaybackState.Unknown;
            try
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus status =
                    session.GetPlaybackInfo().PlaybackStatus;
                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
                {
                    RemoveSessionInternal(id);
                    continue;
                }

                state = ToPlaybackState(status);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[media] could not get playback info for a new session: {ex.Message}");
            }

            // A new id is appended without promotion; the caller decides below whether it becomes current.
            _selector.SetPlaybackState(id, state);
            if (state == MediaPlaybackState.Playing && newlyPlaying is null)
            {
                newlyPlaying = id;
            }
        }

        foreach (string id in _sessions.Keys.Where(id => !seenIds.Contains(id)).ToList())
        {
            RemoveSessionInternal(id);
        }

        // A newly appearing playing session takes the panel when the current session is not playing (or
        // nothing is selected): a paused fallback must not keep the panel while music really plays. When
        // the shown session is already playing, a second player that starts must not steal it.
        if (newlyPlaying is not null && _selector.ShouldPromoteNewlyPlaying(newlyPlaying))
        {
            _selector.MarkActive(newlyPlaying);
        }
    }

    /// <summary>
    /// Applies one session's playback status to the selector (policy lives in
    /// <see cref="MediaSessionSelector"/>: only a transition into Playing promotes; a paused status keeps
    /// the session listed without taking the selection; a closed/broken session leaves the list). Returns
    /// true when the selection or the represented session set changed.
    /// </summary>
    private bool SyncSelectorForSession(string id)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            return false;
        }

        GlobalSystemMediaTransportControlsSessionPlaybackStatus status;
        try
        {
            status = session.GetPlaybackInfo().PlaybackStatus;
        }
        catch (Exception ex)
        {
            // A transient COM failure (e.g. while a player skips a track) must not steal or drop the
            // selection; the session is still in the manager's set and the next change re-syncs it.
            Debug.WriteLine($"[media] could not get playback info: {ex.Message}");
            return false;
        }

        bool wasListed = _selector.Sessions.Contains(id);
        string? before = _selector.Current;

        if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
        {
            if (!wasListed)
            {
                return false;
            }

            _selector.RemoveSession(id);
            return true;
        }

        _selector.SetPlaybackState(id, ToPlaybackState(status));
        return !wasListed || !string.Equals(before, _selector.Current, StringComparison.Ordinal);
    }

    /// <summary>
    /// SMTC status → the state the selection policy cares about. "Changing" (a track skip in progress)
    /// counts as playing so a skip cannot look like a fresh transition into Playing.
    /// </summary>
    internal static MediaPlaybackState ToPlaybackState(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
        => status switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened
                or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Paused,
            _ => MediaPlaybackState.Unknown,
        };

    /// <summary>
    /// Publishes the current session's playback state and interpolated timeline immediately, without the
    /// async metadata read + artwork decode a full reload does. <see cref="Changed"/> is raised only when
    /// a published value actually changed. <paramref name="authoritative"/> is true for a
    /// <c>PlaybackInfoChanged</c> callback (it ends an optimistic play/pause window); a session-list
    /// change only refreshes the fallback state and leaves a fresh optimistic flip alone.
    /// </summary>
    private void PublishPlaybackNow(bool authoritative = true)
    {
        if (_disposed)
        {
            return;
        }

        string? id = _selector.Current;
        if (id is null || !_sessions.TryGetValue(id, out var session))
        {
            return;
        }

        if (!string.Equals(_snapshot.SessionId, id, StringComparison.Ordinal))
        {
            // The snapshot still belongs to another session; the in-flight reload publishes this one.
            return;
        }

        (bool reportedPlaying, double playbackRate, bool canSeek, bool playbackReadOk) = ReadPlayback(session);
        if (!playbackReadOk)
        {
            // A transient read failure must not publish a fake "paused" state.
            return;
        }

        (TimeSpan? position, TimeSpan? duration, bool timelineReadOk) =
            ReadTimeline(session, reportedPlaying, playbackRate);
        if (!timelineReadOk)
        {
            // Keep the timeline that is already on screen instead of blanking it on a transient failure.
            position = _snapshot.Position;
            duration = _snapshot.Duration;
        }

        bool isPlaying;
        if (authoritative)
        {
            // A PlaybackInfoChanged event is authoritative: it ends an optimistic play/pause window.
            ClearOptimisticPlayback();
            isPlaying = reportedPlaying;
        }
        else
        {
            isPlaying = ApplyOptimisticPlayback(id, reportedPlaying);
        }

        if (!MediaTimeline.ShouldRaiseTimeline(
                _snapshot.Position,
                _snapshot.Duration,
                _snapshot.CanSeek,
                _snapshot.IsPlaying,
                position,
                duration,
                canSeek,
                isPlaying))
        {
            return;
        }

        _snapshot = _snapshot with
        {
            IsPlaying = isPlaying,
            Position = position,
            Duration = duration,
            CanSeek = canSeek,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The playback state to publish: an optimistic toggle wins inside its grace window (the player may
    /// still report the pre-command status); the window expiry restores the authoritative SMTC value.
    /// </summary>
    private bool ApplyOptimisticPlayback(string sessionId, bool reportedPlaying)
    {
        if (_optimisticUntil == default)
        {
            return reportedPlaying;
        }

        if (DateTimeOffset.UtcNow >= _optimisticUntil)
        {
            ClearOptimisticPlayback();
            return reportedPlaying;
        }

        return string.Equals(_optimisticSessionId, sessionId, StringComparison.Ordinal)
            ? _optimisticIsPlaying
            : reportedPlaying;
    }

    private void ClearOptimisticPlayback()
    {
        _optimisticSessionId = null;
        _optimisticIsPlaying = false;
        _optimisticUntil = default;
    }

    private void RemoveSessionInternal(string id)
    {
        if (_sessions.Remove(id, out var session))
        {
            session.PlaybackInfoChanged -= OnPlaybackChanged;
            session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _artwork.Remove(id);
            _artworkVersions.Remove(id);
            _selector.RemoveSession(id);
        }
    }

    private async Task ReloadCurrentAsync()
    {
        int generation = unchecked(++_reloadGeneration);
        string? id = _selector.Current;
        if (id is null)
        {
            ClearOptimisticPlayback();
            _snapshot = MediaSnapshot.Idle;
            _artwork.Clear();
            _artworkVersions.Clear();
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
        bool metadataRead;

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            title = props.Title ?? string.Empty;
            artist = props.Artist ?? string.Empty;
            thumbnailRef = props.Thumbnail;
            metadataRead = true;
        }
        catch (Exception ex)
        {
            // Metadata could not be read (the app is not responding) — the last known values are kept,
            // otherwise the app-name fallback is used.
            Debug.WriteLine($"[media] metadata failed: {ex.Message}");
            metadataRead = false;
            if (string.Equals(_snapshot.SessionId, id, StringComparison.Ordinal))
            {
                title = _snapshot.Title;
                artist = _snapshot.Artist;
            }
        }

        if (!IsCurrentReload(generation, id))
        {
            return;
        }

        ImageSource? thumb = null;
        if (thumbnailRef is not null)
        {
            int artworkVersion = ArtworkVersion(id);
            if (_artwork.TryGetValue(id, out CachedArtwork? cached)
                && cached.Version == artworkVersion
                && string.Equals(cached.Title, title, StringComparison.Ordinal)
                && string.Equals(cached.Artist, artist, StringComparison.Ordinal))
            {
                thumb = cached.Image;
            }
            else
            {
                thumb = await TryDecodeThumbnailAsync(thumbnailRef);
                if (!IsCurrentReload(generation, id))
                {
                    return;
                }

                if (thumb is not null)
                {
                    _artwork[id] = new CachedArtwork(title, artist, artworkVersion, thumb);
                }
            }
        }
        else if (!metadataRead && string.Equals(_snapshot.SessionId, id, StringComparison.Ordinal))
        {
            // A transient metadata failure must not blank the cover that is already on screen.
            thumb = _snapshot.Thumbnail;
        }

        if (!IsCurrentReload(generation, id))
        {
            return;
        }

        (bool isPlaying, double playbackRate, bool canSeek, _) = ReadPlayback(session);
        (TimeSpan? position, TimeSpan? duration, _) = ReadTimeline(session, isPlaying, playbackRate);
        ClearOptimisticPlayback();
        _snapshot = new MediaSnapshot(id, appName, title, artist, isPlaying, thumb, position, duration, canSeek);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// True while an asynchronous reload completion (metadata read or artwork decode) still belongs to
    /// the newest reload and the same selected session, i.e. it may publish / cache its result.
    /// </summary>
    private bool IsCurrentReload(int generation, string sessionId)
        => !_disposed && MediaReloadGuard.IsCurrent(generation, _reloadGeneration, sessionId, _selector.Current);

    private int ArtworkVersion(string id) => _artworkVersions.TryGetValue(id, out int version) ? version : 0;

    private void BumpArtworkVersion(string id) => _artworkVersions[id] = unchecked(ArtworkVersion(id) + 1);

    /// <summary>
    /// Re-reads the timeline while the music panel is open and publishes the locally interpolated
    /// position (SMTC's raw <c>Position</c> is frozen between app reports). Artwork and title are not
    /// refreshed. <see cref="Changed"/> is raised only when the shown position advanced at least
    /// <see cref="MediaTimeline.RaisePositionDeltaMs"/>, the duration/seek capability changed, or the
    /// playback state changed (which also reconciles an optimistic play/pause flip), so a ~250 ms poll
    /// stays at roughly 4-5 raises per second.
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

        (bool reportedPlaying, double playbackRate, bool canSeek, bool playbackReadOk) = ReadPlayback(session);
        if (!playbackReadOk)
        {
            // A transient read failure must not flip the glyph to paused between two ticks.
            return;
        }

        // Resolve the playback state first: an expired optimistic window is dropped even if the
        // timeline read below fails (the next successful tick publishes the authoritative value).
        bool isPlaying = ApplyOptimisticPlayback(id, reportedPlaying);

        (TimeSpan? position, TimeSpan? duration, bool timelineReadOk) =
            ReadTimeline(session, reportedPlaying, playbackRate);
        if (!timelineReadOk)
        {
            return;
        }

        if (!MediaTimeline.ShouldRaiseTimeline(
                _snapshot.Position,
                _snapshot.Duration,
                _snapshot.CanSeek,
                _snapshot.IsPlaying,
                position,
                duration,
                canSeek,
                isPlaying))
        {
            return;
        }

        _snapshot = _snapshot with
        {
            IsPlaying = isPlaying,
            Position = position,
            Duration = duration,
            CanSeek = canSeek,
        };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Playback status + rate + seek capability in one <c>GetPlaybackInfo</c> call.
    /// A failed read (COM error, e.g. while a player restarts its audio pipeline) reports
    /// <c>ReadOk: false</c>: the caller must not publish the fallback values as if they were real.
    /// </summary>
    private static (bool IsPlaying, double PlaybackRate, bool CanSeek, bool ReadOk) ReadPlayback(
        GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionPlaybackInfo info = session.GetPlaybackInfo();
            // "Changing" (track skip in progress) still counts as playing: the glyph must not blink to
            // pause during a skip. WinRT projects PlaybackRate as nullable; "not reported" means 1x.
            GlobalSystemMediaTransportControlsSessionPlaybackStatus status = info.PlaybackStatus;
            return (
                status is GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                    or GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing,
                info.PlaybackRate ?? 1.0,
                info.Controls.IsPlaybackPositionEnabled,
                true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] could not get playback info: {ex.Message}");
            return (false, 1.0, false, false);
        }
    }

    /// <summary>
    /// SMTC timeline (interpolated to "now"). Both are null when there is no position or the duration
    /// (<c>EndTime - StartTime</c>) is zero or negative; <c>ReadOk: false</c> means the read threw, so a
    /// caller that only refreshes state must not blank an already published timeline.
    /// </summary>
    private static (TimeSpan? Position, TimeSpan? Duration, bool ReadOk) ReadTimeline(
        GlobalSystemMediaTransportControlsSession session,
        bool isPlaying,
        double playbackRate)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline = session.GetTimelineProperties();
            TimeSpan duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero)
            {
                return (null, null, true);
            }

            var sample = new MediaTimeline.MediaTimelineSample(
                timeline.Position,
                duration,
                timeline.LastUpdatedTime,
                playbackRate,
                isPlaying);
            return (MediaTimeline.Interpolate(sample, DateTimeOffset.Now), duration, true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[media] could not get timeline: {ex.Message}");
            return (null, null, false);
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

    /// <summary>
    /// A decoded cover for one session. It is only reusable while the track key and the artwork
    /// revision (bumped on every media-properties republish) still match.
    /// </summary>
    private sealed record CachedArtwork(string Title, string Artist, int Version, ImageSource Image);

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
