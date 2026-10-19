using System.Diagnostics;
using System.Windows.Threading;

namespace WharfBar.Services;

/// <summary>
/// Fills the icon pool from the cached catalog while the user is idle, a few icons
/// per pass at Background priority, so the pad's first frame already has every tile.
/// Shell extraction must run from any thread; WPF imaging is created on the UI
/// thread here because that is where the face consumes the store.
/// </summary>
internal static class LaunchpadWarmup
{
    private const int Batch = 8;
    private const int MaxIcons = 64;

    private static Dispatcher? _dispatcher;
    private static int _cursor;

    internal static void Begin(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        // Subscribe before the load starts: the event would otherwise fire before
        // anyone listens. Warm() is a no-op once a load is already running.
        LaunchpadCatalogCache.Shared.SnapshotChanged -= OnSnapshot;
        LaunchpadCatalogCache.Shared.SnapshotChanged += OnSnapshot;
        LaunchpadCatalogCache.Shared.Warm();
    }

    private static void OnSnapshot()
    {
        Dispatcher? dispatcher = _dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _cursor = 0;
        dispatcher.BeginInvoke(DispatcherPriority.Background, WarmSlice);
    }

    private static void WarmSlice()
    {
        if (!LaunchpadCatalogCache.Shared.TryGet(out LaunchpadCatalogSnapshot? snapshot)
            || _cursor >= snapshot.Apps.Count
            || _cursor >= MaxIcons)
        {
            return;
        }

        int end = Math.Min(_cursor + Batch, Math.Min(snapshot.Apps.Count, MaxIcons));
        for (int i = _cursor; i < end; i++)
        {
            string path = snapshot.Apps[i].ShortcutPath;
            try
            {
                LaunchpadIcons.Shared.GetIcon(path, 64);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar launchpad warmup: {path}: {ex.Message}");
            }
        }

        _cursor = end;
        if (_cursor < snapshot.Apps.Count && _cursor < MaxIcons && _dispatcher is { } dispatcher)
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, WarmSlice);
        }
    }
}
