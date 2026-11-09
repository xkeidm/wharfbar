using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace WharfBar.Services;

/// <summary>Apps + recent files, captured once and reused so the pad opens full-height.</summary>
internal sealed record LaunchpadCatalogSnapshot(
    IReadOnlyList<LaunchpadApp> Apps,
    IReadOnlyList<LaunchpadRecentItem> Recent,
    DateTimeOffset Timestamp);

/// <summary>
/// A process-wide cache of the Start Menu catalog. Filled once in the background
/// (at startup or after the first open), so <c>LaunchpadWindow</c> paints the full
/// list on the first frame instead of growing in slices. Warm-up and the window share
/// one in-flight load; a stale snapshot is refreshed once, newer data wins.
/// </summary>
internal sealed class LaunchpadCatalogCache
{
    /// <summary>The one process-wide cache; warm-up and the launchpad window share it.</summary>
    internal static LaunchpadCatalogCache Shared { get; } = new();

    /// <summary>A scan older than this is refreshed in the background before reuse.</summary>
    internal static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    private readonly Func<LaunchpadCatalogSnapshot> _load;
    private readonly object _gate = new();
    private volatile LaunchpadCatalogSnapshot? _snapshot;
    private Task<LaunchpadCatalogSnapshot>? _inflight;
    private bool _started;

    internal LaunchpadCatalogCache(Func<LaunchpadCatalogSnapshot>? load = null) =>
        _load = load ?? DefaultLoad;

    internal LaunchpadCatalogSnapshot? Snapshot => _snapshot;

    /// <summary>Raised once per successful background load, after <see cref="Snapshot"/> is set.</summary>
    internal event Action? SnapshotChanged;

    internal bool TryGet([NotNullWhen(true)] out LaunchpadCatalogSnapshot? snapshot)
    {
        snapshot = _snapshot;
        return snapshot is not null;
    }

    internal void Publish(LaunchpadCatalogSnapshot snapshot) => _snapshot = snapshot;

    /// <summary>Loads in the background, once per instance. Later calls are no-ops.</summary>
    internal void Warm()
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _inflight = Task.Run(LoadNow);
        }
    }

    /// <summary>
    /// The loader, synchronous. Safe to call from any thread. When a warm-up scan is already
    /// in flight the caller waits for that scan instead of starting a second one.
    /// </summary>
    internal LaunchpadCatalogSnapshot LoadSync()
    {
        Task<LaunchpadCatalogSnapshot> task;
        lock (_gate)
        {
            if (_inflight is null)
            {
                _started = true;
                _inflight = Task.Run(LoadNow);
            }

            task = _inflight;
        }

        return task.GetAwaiter().GetResult();
    }

    /// <summary>The shell scan used when no loader was injected. Never throws.</summary>
    internal static LaunchpadCatalogSnapshot DefaultLoad()
    {
        // Each source degrades on its own: a locked Recent folder must not erase the app list.
        IReadOnlyList<LaunchpadApp> apps;
        IReadOnlyList<LaunchpadRecentItem> recent;
        try
        {
            apps = LaunchpadCatalog.List();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launchpad catalog: {ex.Message}");
            apps = [];
        }

        try
        {
            recent = LaunchpadRecent.List(24);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launchpad recent: {ex.Message}");
            recent = [];
        }

        return new LaunchpadCatalogSnapshot(apps, recent, DateTimeOffset.UtcNow);
    }

    /// <summary>A snapshot missing or older than the TTL must be refreshed before reuse.</summary>
    internal static bool IsStale(
        LaunchpadCatalogSnapshot? snapshot,
        DateTimeOffset now,
        TimeSpan ttl) =>
        snapshot is null || now - snapshot.Timestamp >= ttl;

    private LaunchpadCatalogSnapshot LoadNow()
    {
        LaunchpadCatalogSnapshot loaded;
        try
        {
            loaded = _load();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launchpad catalog load: {ex.Message}");
            lock (_gate)
            {
                _inflight = null;
            }

            return _snapshot ?? new LaunchpadCatalogSnapshot([], [], DateTimeOffset.UtcNow);
        }

        bool published;
        LaunchpadCatalogSnapshot result;
        lock (_gate)
        {
            _inflight = null;
            // Never let an older scan overwrite a newer one (warm-up vs a stale refresh).
            published = _snapshot is null || loaded.Timestamp >= _snapshot.Timestamp;
            if (published)
            {
                _snapshot = loaded;
            }

            result = _snapshot ?? loaded;
        }

        if (published)
        {
            RaiseChanged();
        }

        return result;
    }

    private void RaiseChanged()
    {
        Action? handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        // One broken subscriber must not stop the pad from receiving fresh data.
        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar launchpad snapshot handler: {ex.Message}");
            }
        }
    }
}
