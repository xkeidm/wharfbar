using System.Collections.Generic;

namespace WharfBar.Services;

/// <summary>
/// Oturum seçim politikası: en son aktif oturum başta. Saf liste mantığı — unit testli.
/// Yalnızca playing/paused oturumlar listede tutulur (çağıran taraf yönetir).
/// </summary>
internal sealed class MediaSessionSelector
{
    private readonly List<string> _sessions = new();
    private readonly List<string> _ring = new();

    /// <summary>Oturum id'leri, en son aktif başta.</summary>
    internal IReadOnlyList<string> Sessions => _sessions;

    /// <summary>En son aktif oturum; yoksa null.</summary>
    internal string? Current => _sessions.Count > 0 ? _sessions[0] : null;

    /// <summary>Bilinmeyen id'yi sona ekler; mevcuda dokunmaz.</summary>
    internal void AddSession(string id)
    {
        if (!_sessions.Contains(id))
        {
            _sessions.Add(id);
            _ring.Add(id);
        }
    }

    /// <summary>Bilinen id'yi başa taşır; bilinmeyen no-op.</summary>
    internal void MarkActive(string id)
    {
        if (_sessions.Remove(id))
        {
            _sessions.Insert(0, id);
        }
    }

    internal void RemoveSession(string id)
    {
        _sessions.Remove(id);
        _ring.Remove(id);
    }

    /// <summary>Seçili oturumu delta kadar kaydırır (liste sonunda sarmalayıcı); yeni seçim döner.</summary>
    internal string? MoveCurrent(int delta)
    {
        if (_sessions.Count == 0)
        {
            return null;
        }

        int current = _ring.IndexOf(Current!);
        int next = ((current + delta) % _ring.Count + _ring.Count) % _ring.Count;
        string id = _ring[next];
        _sessions.Remove(id);
        _sessions.Insert(0, id);
        return id;
    }
}
