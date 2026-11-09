using System.Collections.Generic;

namespace WharfBar.Services;

/// <summary>
/// Playback state as far as the selection policy cares: only the transition into
/// <see cref="Playing"/> promotes a session; every other state merely keeps it selectable.
/// </summary>
internal enum MediaPlaybackState
{
    Unknown,
    Playing,
    Paused,
}

/// <summary>
/// Session selection policy: the most recently active session comes first. Pure list logic — unit tested.
/// The caller owns membership (the sessions SMTC currently reports). A paused/stopped session stays in
/// the list until it disappears: dropping it mid track-skip would hand the panel to another app.
/// </summary>
internal sealed class MediaSessionSelector
{
    private readonly List<string> _sessions = new();
    // Insertion-order ring — chevron swipe order; independent of the MRU order.
    private readonly List<string> _ring = new();
    private readonly Dictionary<string, MediaPlaybackState> _states = new(StringComparer.Ordinal);

    /// <summary>Session ids, most recently active first.</summary>
    internal IReadOnlyList<string> Sessions => _sessions;

    /// <summary>The most recently active session; null if none.</summary>
    internal string? Current => _sessions.Count > 0 ? _sessions[0] : null;

    /// <summary>
    /// Last observed playback state of a session; <see cref="MediaPlaybackState.Unknown"/> when the
    /// session is not tracked (or was never observed).
    /// </summary>
    internal MediaPlaybackState StateOf(string id)
        => _states.TryGetValue(id, out MediaPlaybackState state) ? state : MediaPlaybackState.Unknown;

    /// <summary>Appends an unknown id to the end; leaves existing ones untouched.</summary>
    internal void AddSession(string id)
    {
        if (_sessions.Contains(id))
        {
            return;
        }

        _sessions.Add(id);
        _ring.Add(id);
        _states[id] = MediaPlaybackState.Unknown;
    }

    /// <summary>
    /// Records an observed playback state. Turning Playing promotes the session to Current — that is the
    /// only automatic selection change. Any other state (paused/stopped/opened) never steals the
    /// selection. An unknown id is appended without promotion, so a newly appearing session cannot take
    /// the panel over either.
    /// </summary>
    internal void SetPlaybackState(string id, MediaPlaybackState state)
    {
        if (!_sessions.Contains(id))
        {
            AddSession(id);
            _states[id] = state;
            return;
        }

        MediaPlaybackState previous = StateOf(id);
        _states[id] = state;
        if (state == MediaPlaybackState.Playing && previous != MediaPlaybackState.Playing)
        {
            MoveToFront(id);
        }
    }

    /// <summary>Moves a known id to the front; an unknown one is a no-op.</summary>
    internal void MarkActive(string id) => MoveToFront(id);

    /// <summary>
    /// Whether a newly appearing playing session should take the selection: only while the current session
    /// is not itself playing (or nothing is selected). A paused fallback must not keep the panel when a
    /// player really starts, while a second player that starts next to an already playing session must not
    /// steal what the user is looking at. Returns false when <paramref name="id"/> already is current.
    /// </summary>
    internal bool ShouldPromoteNewlyPlaying(string id)
    {
        string? current = Current;
        return current is null
            || (!string.Equals(current, id, StringComparison.Ordinal)
                && StateOf(current) != MediaPlaybackState.Playing);
    }

    /// <summary>
    /// Removes a session. When the current one leaves, the fallback prefers a still-playing session over
    /// the MRU head, so a paused leftover never wins over something that is really playing.
    /// </summary>
    internal void RemoveSession(string id)
    {
        bool wasCurrent = string.Equals(Current, id, StringComparison.Ordinal);
        _sessions.Remove(id);
        _ring.Remove(id);
        _states.Remove(id);
        if (!wasCurrent)
        {
            return;
        }

        foreach (string candidate in _sessions)
        {
            if (StateOf(candidate) == MediaPlaybackState.Playing)
            {
                MoveToFront(candidate);
                return;
            }
        }
    }

    /// <summary>Advances the selection by delta in the ring (wrapping); the new selection is moved to the head of the MRU list; null if empty.</summary>
    internal string? MoveCurrent(int delta)
    {
        if (_sessions.Count == 0)
        {
            return null;
        }

        int current = _ring.IndexOf(Current!);
        int next = ((current + delta) % _ring.Count + _ring.Count) % _ring.Count;
        string id = _ring[next];
        MoveToFront(id);
        return id;
    }

    private void MoveToFront(string id)
    {
        if (_sessions.Remove(id))
        {
            _sessions.Insert(0, id);
        }
    }
}
