using WharfBar.Models;

namespace WharfBar.Services;

internal sealed class DockAppsComposer : IDisposable
{
    private readonly TaskbarPinnedAppsSource _pins;
    private readonly RunningAppsSource _running;
    private readonly IReadOnlyList<PinnedApp> _fallback;
    private List<DockAppItem> _items = [];

    internal DockAppsComposer(
        TaskbarPinnedAppsSource pins,
        RunningAppsSource running,
        IReadOnlyList<PinnedApp> configFallback)
    {
        _pins = pins;
        _running = running;
        _fallback = configFallback;
        _pins.Changed += OnSourceChanged;
        _running.Changed += OnSourceChanged;
        Rebuild();
    }

    internal IReadOnlyList<DockAppItem> Items => _items;
    internal event EventHandler? Changed;

    private void OnSourceChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        IReadOnlyList<PinnedApp> pinList = _pins.HasLivePins ? _pins.Pins : _fallback;
        IReadOnlyList<RunningAppInfo> running = _running.Apps;
        var matchedRunning = new HashSet<int>();
        var next = new List<DockAppItem>(pinList.Count + running.Count);

        foreach (PinnedApp pin in pinList)
        {
            RunningAppInfo? match = null;
            for (int i = 0; i < running.Count; i++)
            {
                if (matchedRunning.Contains(i))
                {
                    continue;
                }

                if (PathsMatch(pin.Path, running[i].Path))
                {
                    match = running[i];
                    matchedRunning.Add(i);
                    break;
                }
            }

            string id = match is not null ? match.Id : ResolvePinId(pin);
            next.Add(new DockAppItem(
                id,
                pin.Path,
                pin.Args,
                pin.Label,
                true,
                match is not null,
                match?.MainWindowHandle ?? IntPtr.Zero));
        }

        var unpinned = new List<RunningAppInfo>();
        for (int i = 0; i < running.Count; i++)
        {
            if (!matchedRunning.Contains(i))
            {
                unpinned.Add(running[i]);
            }
        }

        unpinned.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));

        foreach (RunningAppInfo app in unpinned)
        {
            next.Add(new DockAppItem(
                app.Id,
                app.Path,
                string.Empty,
                app.Label,
                false,
                true,
                app.MainWindowHandle));
        }

        if (ItemsEqual(_items, next))
        {
            return;
        }

        _items = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _pins.Changed -= OnSourceChanged;
        _running.Changed -= OnSourceChanged;
    }

    private static string ResolvePinId(PinnedApp pin) =>
        !string.IsNullOrWhiteSpace(pin.Path) ? pin.Path : pin.Label;

    private static bool PathsMatch(string a, string b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool ItemsEqual(List<DockAppItem> a, List<DockAppItem> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!ItemEqual(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ItemEqual(DockAppItem x, DockAppItem y) =>
        string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.Path, y.Path, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.Args, y.Args, StringComparison.Ordinal)
        && string.Equals(x.Label, y.Label, StringComparison.Ordinal)
        && x.IsPinned == y.IsPinned
        && x.IsRunning == y.IsRunning
        && x.MainWindowHandle == y.MainWindowHandle;
}
