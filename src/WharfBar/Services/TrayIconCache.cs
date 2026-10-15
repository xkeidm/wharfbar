namespace WharfBar.Services;

/// <summary>
/// Per-key tray icon cache keyed by <see cref="TrayIconSignature"/>. Pure (no Win32), generic so
/// tests can drive it without bitmaps.
/// <list type="bullet">
/// <item>A fresh capture with the cached signature returns the <b>cached instance</b> — the
/// snapshot stays reference-equal and the UI is not touched.</item>
/// <item>A failed capture never downgrades a real icon to a placeholder at once (no real → glyph
/// flicker). Only after captures kept failing past <see cref="StaleIconLimit"/> does the stale
/// pixel give way to the placeholder: a frozen icon must not pose as the live state forever.</item>
/// <item>Placeholders are cached too, so a glyph is built once and stays the same instance.</item>
/// </list>
/// </summary>
internal sealed class TrayIconCache<TIcon>
    where TIcon : class
{
    internal static readonly TimeSpan StaleIconLimit = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal int Count => _entries.Count;

    internal bool TryGet(string key, out TIcon icon)
    {
        if (_entries.TryGetValue(key, out Entry? entry))
        {
            icon = entry.Icon;
            return true;
        }

        icon = null!;
        return false;
    }

    internal bool IsPlaceholder(string key) => _entries.TryGetValue(key, out Entry? entry) && entry.IsPlaceholder;

    /// <summary>
    /// Stores a fresh capture. Returns the instance to publish: the cached one when the pixels
    /// are unchanged (<paramref name="changed"/> false), otherwise <paramref name="fresh"/>.
    /// </summary>
    internal TIcon Accept(string key, TIcon fresh, ulong signature, DateTime now, out bool changed)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        if (_entries.TryGetValue(key, out Entry? entry) && !entry.IsPlaceholder && entry.Signature == signature)
        {
            entry.ConfirmedAt = now;
            changed = false;
            return entry.Icon;
        }

        _entries[key] = new Entry(fresh, signature, isPlaceholder: false, now);
        changed = true;
        return fresh;
    }

    /// <summary>
    /// No fresh pixels for <paramref name="key"/>. <paramref name="captureAttempted"/> false means
    /// nothing was tried (tray hidden, no peek) — that is no evidence, the cached icon stays.
    /// A failed attempt keeps a real icon until it is <see cref="StaleIconLimit"/> old.
    /// </summary>
    internal TIcon Fallback(string key, Func<TIcon> placeholder, DateTime now, bool captureAttempted)
    {
        ArgumentNullException.ThrowIfNull(placeholder);
        if (_entries.TryGetValue(key, out Entry? entry)
            && (entry.IsPlaceholder || !captureAttempted || now - entry.ConfirmedAt < StaleIconLimit))
        {
            return entry.Icon;
        }

        TIcon glyph = placeholder();
        _entries[key] = new Entry(glyph, 0, isPlaceholder: true, now);
        return glyph;
    }

    internal void Retain(IEnumerable<string> keys)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var keep = new HashSet<string>(keys, StringComparer.Ordinal);
        List<string>? drop = null;
        foreach (string key in _entries.Keys)
        {
            if (!keep.Contains(key))
            {
                (drop ??= []).Add(key);
            }
        }

        if (drop is null)
        {
            return;
        }

        foreach (string key in drop)
        {
            _entries.Remove(key);
        }
    }

    internal void Clear() => _entries.Clear();

    private sealed class Entry(TIcon icon, ulong signature, bool isPlaceholder, DateTime confirmedAt)
    {
        internal TIcon Icon { get; } = icon;

        internal ulong Signature { get; } = signature;

        internal bool IsPlaceholder { get; } = isPlaceholder;

        internal DateTime ConfirmedAt { get; set; } = confirmedAt;
    }
}
