using System.Collections.Generic;

namespace WharfBar.Services;

/// <summary>
/// Session selection policy: the most recently active session comes first. Pure list logic — unit tested.
/// Only playing/paused sessions are kept in the list (the caller manages that).
/// </summary>
internal sealed class MediaSessionSelector
{
    private readonly List<string> _sessions = new();
    // Insertion-order ring — chevron swipe order; independent of the MRU order.
    private readonly List<string> _ring = new();

    /// <summary>Session ids, most recently active first.</summary>
    internal IReadOnlyList<string> Sessions => _sessions;

    /// <summary>The most recently active session; null if none.</summary>
    internal string? Current => _sessions.Count > 0 ? _sessions[0] : null;

    /// <summary>Appends an unknown id to the end; leaves existing ones untouched.</summary>
    internal void AddSession(string id)
    {
        if (!_sessions.Contains(id))
        {
            _sessions.Add(id);
            _ring.Add(id);
        }
    }

    /// <summary>Moves a known id to the front; an unknown one is a no-op.</summary>
    internal void MarkActive(string id) => MoveToFront(id);

    internal void RemoveSession(string id)
    {
        _sessions.Remove(id);
        _ring.Remove(id);
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
