using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace WharfBar.Services;

/// <summary>Apps + recent files, captured once and reused so the pad opens full-height.</summary>
internal sealed record LaunchpadCatalogSnapshot(
    IReadOnlyList<LaunchpadApp> Apps,
    IReadOnlyList<LaunchpadRecentItem> Recent,
    DateTimeOffset Timestamp);

/// <summary>
/// A process-wide cache of the Start Menu catalog. Filled once in the background
/// (at startup or after the first open), so <c>LaunchpadWindow</c> paints the full
/// list on the first frame instead of growing in slices.
/// </summary>
internal sealed class LaunchpadCatalogCache
{
    /// <summary>The one process-wide cache; warm-up and the launchpad window share it.</summary>
    internal static LaunchpadCatalogCache Shared { get; } = new();

    /// <summary>A scan older than this is refreshed in the background before reuse.</summary>
    internal static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    private readonly Func<LaunchpadCatalogSnapshot> _load;
    private volatile LaunchpadCatalogSnapshot? _snapshot;
    private long _started;

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
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        Task.Run(LoadSync);
    }

    /// <summary>The loader, synchronous. Safe to call from any thread.</summary>
    internal LaunchpadCatalogSnapshot LoadSync()
    {
        LaunchpadCatalogSnapshot snapshot = _load();
        _snapshot = snapshot;
        SnapshotChanged?.Invoke();
        return snapshot;
    }

    /// <summary>The shell scan used when no loader was injected. Never throws.</summary>
    internal static LaunchpadCatalogSnapshot DefaultLoad()
    {
        IReadOnlyList<LaunchpadApp> apps;
        IReadOnlyList<LaunchpadRecentItem> recent;
        try
        {
            apps = LaunchpadCatalog.List();
            recent = LaunchpadRecent.List(24);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar launchpad catalog: {ex.Message}");
            apps = [];
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
}
