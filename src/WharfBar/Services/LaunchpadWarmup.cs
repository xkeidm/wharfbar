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

        // SnapshotChanged is raised on the scanner thread; touch the cursor only on the
        // dispatcher thread that also advances it.
        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _cursor = 0;
            WarmSlice();
        });
    }

    private static void WarmSlice()
    {
        if (!LaunchpadCatalogCache.Shared.TryGet(out LaunchpadCatalogSnapshot? snapshot)
            || _cursor >= snapshot.Apps.Count)
        {
            return;
        }

        // The whole catalog, not a fixed prefix: the pad can scroll to every app, and an
        // unwarmed icon is extracted synchronously on the UI thread on first paint.
        int end = Math.Min(_cursor + Batch, snapshot.Apps.Count);
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
        if (_cursor < snapshot.Apps.Count && _dispatcher is { } dispatcher)
        {
            dispatcher.BeginInvoke(DispatcherPriority.Background, WarmSlice);
        }
    }
}
