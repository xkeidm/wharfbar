using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// How a <c>.lnk</c> pin launches, when the stored path is still the shortcut.
/// A directory target must not bind to a process image (a pinned folder stays its own button).
/// </summary>
internal readonly record struct PinMatchKey(string Path, string? AppUserModelId, bool IsDirectory);

/// <summary>
/// Merges taskbar pins, the dock's own pin list, the live running-app list, and
/// minimized windows into one ordered strip of <see cref="DockSection"/>s.
/// </summary>
internal sealed class DockAppsComposer : IDisposable
{
    private readonly DockItemStore _store;
    private readonly RunningAppsSource _running;
    private readonly MinimizedWindowsSource _minimized;
    private readonly TaskbarPinnedAppsSource? _taskbarPins;
    private readonly DockConfig _config;
    private List<DockAppItem> _items = [];

    internal DockAppsComposer(
        DockItemStore store,
        RunningAppsSource running,
        MinimizedWindowsSource minimized,
        DockConfig config,
        TaskbarPinnedAppsSource? taskbarPins = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(minimized);
        ArgumentNullException.ThrowIfNull(config);

        _store = store;
        _running = running;
        _minimized = minimized;
        _taskbarPins = taskbarPins;
        _config = config;

        _store.Changed += OnSourceChanged;
        _running.Changed += OnSourceChanged;
        _minimized.Changed += OnSourceChanged;
        if (_taskbarPins is not null)
        {
            _taskbarPins.Changed += OnSourceChanged;
        }

        Rebuild();
    }

    internal IReadOnlyList<DockAppItem> Items => _items;

    internal event EventHandler? Changed;

    /// <summary>
    /// Pure merge. The left group is the taskbar pin order, then dock pins that are not
    /// already in that group. A running app binds to the first pin that is the same target
    /// and does not appear again. Closed pins stay, with <see cref="DockAppItem.IsRunning"/>
    /// false. Unpinned running apps follow on the right — one tile per app.
    /// </summary>
    internal static IReadOnlyList<DockAppItem> Compose(
        IReadOnlyList<PinnedApp> pins,
        IReadOnlyList<RunningAppInfo> running,
        IReadOnlyList<AppWindowInfo> minimized,
        bool showRunning,
        Func<string, PinMatchKey?>? resolveShortcut = null,
        IReadOnlyList<PinnedApp>? taskbarPins = null)
    {
        resolveShortcut ??= ResolveStoredShortcut;
        pins = MergePins(taskbarPins ?? [], pins, resolveShortcut);

        var next = new List<DockAppItem>(pins.Count + running.Count);
        var matched = new bool[running.Count];

        foreach (PinnedApp pin in pins)
        {
            PinMatchKey? shortcut = ShortcutKey(pin.Path, resolveShortcut);
            int hit = -1;
            for (int i = 0; i < running.Count; i++)
            {
                if (!matched[i] && PinMatches(pin, running[i], shortcut))
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

        var right = new List<DockAppItem>();
        var representedPids = new HashSet<uint>();
        for (int i = 0; i < running.Count; i++)
        {
            // Matched or not, this process already has a tile (the pin, or the one added below).
            RememberPid(running[i].MainWindowHandle, representedPids);
            if (matched[i])
            {
                continue;
            }

            RunningAppInfo app = running[i];
            right.Add(new DockAppItem(
                app.Id, app.Path, string.Empty, app.Label,
                IsPinned: false, IsRunning: true, app.MainWindowHandle,
                DockSection.Running, string.Empty));
        }

        // One tile per process that the running list never claimed. Title-then-handle order
        // picks a stable representative: the scan is re-enumerated every 800 ms.
        var minimizedOnly = new Dictionary<uint, AppWindowInfo>();
        foreach (AppWindowInfo window in minimized
            .OrderBy(static w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static w => w.Handle))
        {
            if (window.Pid == 0 || representedPids.Contains(window.Pid))
            {
                continue;
            }

            minimizedOnly.TryAdd(window.Pid, window);
        }

        foreach (AppWindowInfo window in minimizedOnly.Values)
        {
            right.Add(new DockAppItem(
                $"pid:{window.Pid}",
                string.Empty,
                string.Empty,
                string.IsNullOrEmpty(window.Title) ? "App" : window.Title,
                IsPinned: false,
                IsRunning: true,
                window.Handle,
                DockSection.Running,
                string.Empty));
        }

        right.Sort(static (a, b) =>
        {
            int byLabel = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            return byLabel != 0
                ? byLabel
                : a.MainWindowHandle.ToInt64().CompareTo(b.MainWindowHandle.ToInt64());
        });

        next.AddRange(right);
        return next;
    }

    /// <summary>
    /// Taskbar pins first, in the order given. A dock pin that is the same target is dropped.
    /// An empty taskbar list leaves <paramref name="dockPins"/> untouched.
    /// </summary>
    internal static IReadOnlyList<PinnedApp> MergePins(
        IReadOnlyList<PinnedApp> taskbarPins,
        IReadOnlyList<PinnedApp> dockPins,
        Func<string, PinMatchKey?> resolveShortcut)
    {
        if (taskbarPins.Count == 0)
        {
            return dockPins;
        }

        var merged = new List<PinnedApp>(taskbarPins.Count + dockPins.Count);
        var keys = new List<PinMatchKey?>(taskbarPins.Count + dockPins.Count);
        AppendUnique(merged, keys, taskbarPins, resolveShortcut);
        AppendUnique(merged, keys, dockPins, resolveShortcut);
        return merged;
    }

    private static void AppendUnique(
        List<PinnedApp> merged,
        List<PinMatchKey?> keys,
        IReadOnlyList<PinnedApp> pins,
        Func<string, PinMatchKey?> resolveShortcut)
    {
        foreach (PinnedApp pin in pins)
        {
            if (string.IsNullOrWhiteSpace(pin.Path))
            {
                continue;
            }

            PinMatchKey? key = ShortcutKey(pin.Path, resolveShortcut);
            bool duplicate = false;
            for (int i = 0; i < merged.Count; i++)
            {
                if (SameTarget(merged[i], keys[i], pin, key))
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                continue;
            }

            merged.Add(pin);
            keys.Add(key);
        }
    }

    private static bool SameTarget(PinnedApp a, PinMatchKey? aKey, PinnedApp b, PinMatchKey? bKey)
    {
        bool aDirectory = IsDirectoryPin(a, aKey);
        bool bDirectory = IsDirectoryPin(b, bKey);
        if (aDirectory || bDirectory)
        {
            return aDirectory
                && bDirectory
                && PathsMatch(PinTarget(a, aKey), PinTarget(b, bKey));
        }

        if (PathsMatch(PinTarget(a, aKey), PinTarget(b, bKey)))
        {
            return true;
        }

        string? aumidA = AumidOf(a, aKey);
        string? aumidB = AumidOf(b, bKey);
        return aumidA is not null && aumidB is not null && IdEquals(aumidA, aumidB);
    }

    private static string PinTarget(PinnedApp pin, PinMatchKey? key) =>
        key is { Path.Length: > 0 } resolved ? resolved.Path : pin.Path;

    private static bool IsDirectoryPin(PinnedApp pin, PinMatchKey? key)
    {
        if (key is { IsDirectory: true })
        {
            return true;
        }

        string target = PinTarget(pin, key);
        return Path.IsPathRooted(target) && Directory.Exists(target);
    }

    private static string? AumidOf(PinnedApp pin, PinMatchKey? key)
    {
        if (key is { AppUserModelId: { Length: > 0 } id } && IsAppUserModelId(id))
        {
            return id;
        }

        return IsAppUserModelId(pin.Path) ? pin.Path : null;
    }

    /// <summary>
    /// A pin and a running app are the same target when their full paths match, when the
    /// pin is stored as that app's AppUserModelId, or when a still-unresolved shortcut
    /// resolves to that path or AppUserModelId. Directory pins never bind: a pinned folder
    /// stays distinct from <c>explorer.exe</c>. File names alone never match.
    /// </summary>
    private static bool PinMatches(PinnedApp pin, RunningAppInfo app, PinMatchKey? shortcut)
    {
        if (shortcut is { IsDirectory: true })
        {
            return false;
        }

        if (PathsMatch(pin.Path, app.Path))
        {
            return true;
        }

        if (IsAppUserModelId(app.Id) && IdEquals(pin.Path, app.Id))
        {
            return true;
        }

        if (shortcut is not { } key)
        {
            return false;
        }

        if (PathsMatch(key.Path, app.Path))
        {
            return true;
        }

        return IsAppUserModelId(app.Id)
            && !string.IsNullOrWhiteSpace(key.AppUserModelId)
            && IdEquals(key.AppUserModelId, app.Id);
    }

    private static PinMatchKey? ShortcutKey(string path, Func<string, PinMatchKey?> resolve)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return resolve(path);
    }

    private static PinMatchKey? ResolveStoredShortcut(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        string? aumid = null;
        if (AppShortcut.TryGetAppUserModelId(path, out string? read) && !string.IsNullOrWhiteSpace(read))
        {
            aumid = read;
        }

        bool haveTarget = ShortcutResolver.TryResolve(path, out string target, out _, out _);
        string full = haveTarget ? DockItemStore.Normalize(target) : string.Empty;
        if (full.Length == 0 && string.IsNullOrEmpty(aumid))
        {
            return null;
        }

        bool isDirectory = full.Length > 0 && Directory.Exists(full);
        return new PinMatchKey(full, aumid, isDirectory);
    }

    private static void RememberPid(IntPtr hwnd, HashSet<uint> pids)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // The return value is the owning thread id; the out-param is the process id.
        uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (threadId != 0 && pid != 0)
        {
            pids.Add(pid);
        }
    }

    private static bool IsAppUserModelId(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && !id.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase)
        && !Path.IsPathRooted(id);

    private static bool IdEquals(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

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

    private static bool PathsMatch(string a, string b)
    {
        string left = CanonicalPath(a);
        string right = CanonicalPath(b);
        return left.Length > 0
            && right.Length > 0
            && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Full path, not file name. Non-rooted strings (an AppUserModelId, <c>hwnd:</c>) are
    /// compared as text so <see cref="Path.GetFullPath"/> cannot turn them into a cwd path.
    /// </summary>
    private static string CanonicalPath(string? path)
    {
        string trimmed = path?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || !Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        return DockItemStore.Normalize(trimmed);
    }

    private void OnSourceChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        IReadOnlyList<DockAppItem> next = Compose(
            _store.Items,
            _running.Apps,
            _minimized.Minimized,
            _config.ShowRunningApps,
            taskbarPins: _taskbarPins?.Pins);

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
        if (_taskbarPins is not null)
        {
            _taskbarPins.Changed -= OnSourceChanged;
        }
    }
}
