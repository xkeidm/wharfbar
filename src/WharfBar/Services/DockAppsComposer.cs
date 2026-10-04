using System;
using System.Collections.Generic;
using System.Linq;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Merges the dock's own pinned list, the live running-app list, and minimized windows
/// into one ordered strip of <see cref="DockSection"/>s.
/// </summary>
internal sealed class DockAppsComposer : IDisposable
{
    private readonly DockItemStore _store;
    private readonly RunningAppsSource _running;
    private readonly MinimizedWindowsSource _minimized;
    private readonly DockConfig _config;
    private List<DockAppItem> _items = [];

    internal DockAppsComposer(
        DockItemStore store,
        RunningAppsSource running,
        MinimizedWindowsSource minimized,
        DockConfig config)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(minimized);
        ArgumentNullException.ThrowIfNull(config);

        _store = store;
        _running = running;
        _minimized = minimized;
        _config = config;

        _store.Changed += OnSourceChanged;
        _running.Changed += OnSourceChanged;
        _minimized.Changed += OnSourceChanged;
        Rebuild();
    }

    internal IReadOnlyList<DockAppItem> Items => _items;

    internal event EventHandler? Changed;

    /// <summary>
    /// Pure merge. Pinned order is authoritative; a running app binds to the first pin
    /// with a matching path; leftovers become the Running section; every minimized
    /// window becomes its own Minimized tile.
    /// </summary>
    internal static IReadOnlyList<DockAppItem> Compose(
        IReadOnlyList<PinnedApp> pins,
        IReadOnlyList<RunningAppInfo> running,
        IReadOnlyList<AppWindowInfo> minimized,
        bool showRunning)
    {
        var next = new List<DockAppItem>(pins.Count + running.Count + minimized.Count);
        var matched = new bool[running.Count];

        foreach (PinnedApp pin in pins)
        {
            int hit = -1;
            for (int i = 0; i < running.Count; i++)
            {
                if (!matched[i] && PathsMatch(pin.Path, running[i].Path))
                {
                    hit = i;
                    break;
                }
            }

            if (hit < 0)
            {
                next.Add(new DockAppItem(
                    pin.Path, pin.Path, pin.Args, pin.Label,
                    IsPinned: true, IsRunning: false, IntPtr.Zero,
                    DockSection.Pinned, string.Empty));
                continue;
            }

            matched[hit] = true;
            RunningAppInfo app = running[hit];
            next.Add(new DockAppItem(
                app.Id, pin.Path, pin.Args, pin.Label,
                IsPinned: true, IsRunning: true, app.MainWindowHandle,
                DockSection.Pinned, string.Empty));
        }

        if (!showRunning)
        {
            return next;
        }

        var unpinned = new List<RunningAppInfo>();
        for (int i = 0; i < running.Count; i++)
        {
            if (!matched[i])
            {
                unpinned.Add(running[i]);
            }
        }

        unpinned.Sort(static (a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        foreach (RunningAppInfo app in unpinned)
        {
            next.Add(new DockAppItem(
                app.Id, app.Path, string.Empty, app.Label,
                IsPinned: false, IsRunning: true, app.MainWindowHandle,
                DockSection.Running, string.Empty));
        }

        var byPid = new Dictionary<uint, RunningAppInfo>();
        foreach (RunningAppInfo app in running)
        {
            uint pid = NativeMethods.GetWindowThreadProcessId(app.MainWindowHandle, out uint resolved);
            if (pid != 0)
            {
                byPid.TryAdd(resolved, app);
            }
        }

        foreach (AppWindowInfo window in minimized
            .OrderBy(static w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static w => w.Handle))
        {
            byPid.TryGetValue(window.Pid, out RunningAppInfo? owner);
            next.Add(new DockAppItem(
                $"hwnd:{window.Handle}",
                owner?.Path ?? string.Empty,
                string.Empty,
                string.IsNullOrEmpty(owner?.Label) ? window.Title : owner.Label,
                IsPinned: false,
                IsRunning: true,
                window.Handle,
                DockSection.Minimized,
                window.Title));
        }

        return next;
    }

    internal static bool ItemsEqual(IReadOnlyList<DockAppItem> a, IReadOnlyList<DockAppItem> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Id, b[i].Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Path, b[i].Path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Args, b[i].Args, StringComparison.Ordinal)
                || !string.Equals(a[i].Label, b[i].Label, StringComparison.Ordinal)
                || !string.Equals(a[i].WindowTitle, b[i].WindowTitle, StringComparison.Ordinal)
                || a[i].IsPinned != b[i].IsPinned
                || a[i].IsRunning != b[i].IsRunning
                || a[i].MainWindowHandle != b[i].MainWindowHandle
                || a[i].Section != b[i].Section)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PathsMatch(string a, string b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void OnSourceChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        IReadOnlyList<DockAppItem> next = Compose(
            _store.Items, _running.Apps, _minimized.Minimized, _config.ShowRunningApps);

        if (ItemsEqual(_items, next))
        {
            return;
        }

        _items = [.. next];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _store.Changed -= OnSourceChanged;
        _running.Changed -= OnSourceChanged;
        _minimized.Changed -= OnSourceChanged;
    }
}
