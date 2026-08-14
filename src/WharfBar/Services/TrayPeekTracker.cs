namespace WharfBar.Services;

/// <summary>
/// Tracks tray-peek nesting with a bounded lifetime. A peek protects <c>Shell_TrayWnd</c>
/// from the keep-hidden watch only while it is fresh; once past
/// <see cref="IsStale"/> the watch force-ends an orphaned peek (a missed
/// <see cref="TaskbarVisibilityService.EndTrayPeek"/>) so the taskbar can never be left
/// visible forever. Thread-safe: peeks start on a worker thread and end on the dispatcher.
/// </summary>
internal sealed class TrayPeekTracker
{
    private readonly object _sync = new();
    private readonly TimeSpan _maxDuration;
    private int _depth;
    private DateTime _startedUtc;

    internal TrayPeekTracker(TimeSpan maxDuration)
    {
        _maxDuration = maxDuration;
    }

    /// <summary>True while at least one peek is open (whether fresh or stale).</summary>
    internal bool IsActive
    {
        get
        {
            lock (_sync)
            {
                return _depth > 0;
            }
        }
    }

    /// <summary>
    /// True when a peek is open but its protected window has elapsed — the keep-hidden
    /// watch may force-end it. Never true for an inactive tracker.
    /// </summary>
    internal bool IsStale(DateTime nowUtc)
    {
        lock (_sync)
        {
            return _depth > 0 && nowUtc - _startedUtc >= _maxDuration;
        }
    }

    /// <summary>Opens a peek (refcounted). The deadline window starts on the first open.</summary>
    internal void Begin(DateTime nowUtc)
    {
        lock (_sync)
        {
            if (_depth == 0)
            {
                _startedUtc = nowUtc;
            }

            _depth++;
        }
    }

    /// <summary>
    /// Closes one peek. Returns true when the last one closed (tracker now inactive);
    /// callers that open without a matching close will leave the tracker stale.
    /// </summary>
    internal bool End()
    {
        lock (_sync)
        {
            if (_depth > 0)
            {
                _depth--;
            }

            return _depth == 0;
        }
    }

    /// <summary>Force-closes every peek regardless of nesting (orphan recovery).</summary>
    internal void Reset()
    {
        lock (_sync)
        {
            _depth = 0;
        }
    }
}