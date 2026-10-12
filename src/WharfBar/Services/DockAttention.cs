namespace WharfBar.Services;

/// <summary>
/// Accent-pulse clock for a dock tile. A flash starts a 1.6s window; a second flash
/// restarts it; activation or expiry clears it. The cell paints the pulse — this type
/// only decides whether it is still running.
/// </summary>
internal sealed class DockAttentionTracker
{
    internal const long PulseMs = 1600;

    private readonly Dictionary<string, long> _until = new(StringComparer.OrdinalIgnoreCase);

    internal void StartOrRestart(string key, long nowMs)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        _until[key] = nowMs + PulseMs;
    }

    internal void Clear(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            _until.Remove(key);
        }
    }

    /// <summary>Drops every pulse whose window has ended.</summary>
    internal void Expire(long nowMs)
    {
        if (_until.Count == 0)
        {
            return;
        }

        List<string>? stale = null;
        foreach ((string key, long until) in _until)
        {
            if (nowMs >= until)
            {
                stale ??= [];
                stale.Add(key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (string key in stale)
        {
            _until.Remove(key);
        }
    }

    internal bool IsPulsing(string key, long nowMs)
    {
        if (string.IsNullOrEmpty(key) || !_until.TryGetValue(key, out long until))
        {
            return false;
        }

        if (nowMs >= until)
        {
            _until.Remove(key);
            return false;
        }

        return true;
    }

    internal long Until(string key) =>
        !string.IsNullOrEmpty(key) && _until.TryGetValue(key, out long until) ? until : 0;

    /// <summary>WharfBar's own windows are not attention targets.</summary>
    internal static bool ShouldNotice(uint windowPid, uint selfPid) =>
        windowPid != 0 && windowPid != selfPid;
}
