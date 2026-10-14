using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// One application the dock can show. <see cref="WindowCount"/> and <see cref="ProcessIds"/>
/// cover every top-level window that collapsed into this identity, not only the representative.
/// </summary>
internal sealed record RunningAppInfo(
    string Id,
    string Path,
    string Label,
    IntPtr MainWindowHandle,
    uint ProcessId = 0,
    int WindowCount = 1)
{
    /// <summary>Every process that belongs to this app. Empty when only <see cref="ProcessId"/> is known.</summary>
    internal IReadOnlyList<uint> ProcessIds { get; init; } = [];

    /// <summary>AppUserModelIDs seen on any window in the group, besides a path-shaped <see cref="Id"/>.</summary>
    internal IReadOnlyList<string> MatchIds { get; init; } = [];

    /// <summary>Executable paths seen on any window in the group.</summary>
    internal IReadOnlyList<string> MatchPaths { get; init; } = [];
}

/// <summary>One top-level window, already resolved off the UI thread, before identity collapse.</summary>
internal readonly record struct RunningWindowSnapshot(
    IntPtr Hwnd,
    string Title,
    uint Pid,
    string Path,
    string? AppUserModelId);

internal sealed class RunningAppsSource : IDisposable
{
    private static readonly string SystemAppsDir = Path.GetFullPath(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps"));

    private readonly Dispatcher _dispatcher;
    private readonly WindowCensus _census;
    private readonly List<RunningAppInfo> _apps = [];
    private bool _disposed;

    internal RunningAppsSource(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _census = WindowCensus.Acquire();
        _census.Subscribe(OnCensus);
    }

    internal IReadOnlyList<RunningAppInfo> Apps => _apps;
    internal event EventHandler? Changed;

    internal void Refresh() => _census.RequestScan();

    private void OnCensus(IReadOnlyList<CensusWindow> windows)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            List<RunningAppInfo> next = FromCensus(windows);
            if (_disposed)
            {
                return;
            }

            _ = _dispatcher.BeginInvoke(() => ApplyScan(next));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar running-apps scan failed: {ex.Message}");
        }
    }

    private void ApplyScan(List<RunningAppInfo> next)
    {
        if (_disposed)
        {
            return;
        }

        if (AppsEqual(_apps, next))
        {
            return;
        }

        _apps.Clear();
        _apps.AddRange(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<RunningAppInfo> FromCensus(IReadOnlyList<CensusWindow> windows)
    {
        var snapshots = new List<RunningWindowSnapshot>(windows.Count);
        foreach (CensusWindow window in windows)
        {
            snapshots.Add(new RunningWindowSnapshot(
                window.Hwnd,
                window.Title,
                window.Pid,
                window.Path,
                window.AppUserModelId));
        }

        return [.. FromSnapshots(snapshots)];
    }

    /// <summary>
    /// One dock identity per application. Windows link when they share a canonical exe path,
    /// an AppUserModelId, or a process id, and the link is transitive: an AUMID window and a
    /// later window of the same exe that never set an AUMID are one app. File names alone do
    /// not link. Each input window is kept — a second process is not dropped on the floor.
    /// </summary>
    internal static IReadOnlyList<RunningAppInfo> FromSnapshots(IReadOnlyList<RunningWindowSnapshot> windows)
    {
        if (windows is null || windows.Count == 0)
        {
            return [];
        }

        var entries = new List<RunningAppInfo>(windows.Count);
        foreach (RunningWindowSnapshot window in windows)
        {
            string path = window.Path ?? string.Empty;
            if (IsSystemUtilityHost(path))
            {
                continue;
            }

            bool hasAumid = !string.IsNullOrWhiteSpace(window.AppUserModelId);
            string label = ResolveLabel(window.Title, path);
            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(label) && !hasAumid)
            {
                continue;
            }

            string id = hasAumid
                ? window.AppUserModelId!.Trim()
                : !string.IsNullOrEmpty(path) ? path : $"hwnd:{window.Hwnd}";
            entries.Add(new RunningAppInfo(id, path, label, window.Hwnd, window.Pid, WindowCount: 1));
        }

        return Collapse(entries);
    }

    /// <summary>
    /// Merges entries that are the same application. A single entry is returned as-is.
    /// </summary>
    internal static IReadOnlyList<RunningAppInfo> Collapse(IReadOnlyList<RunningAppInfo>? apps)
    {
        if (apps is null || apps.Count == 0)
        {
            return [];
        }

        if (apps.Count == 1)
        {
            return apps;
        }

        int n = apps.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++)
        {
            parent[i] = i;
        }

        int Find(int index)
        {
            while (parent[index] != index)
            {
                parent[index] = parent[parent[index]];
                index = parent[index];
            }

            return index;
        }

        void Union(int a, int b)
        {
            int ra = Find(a);
            int rb = Find(b);
            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }

        var byPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byAumid = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byPid = new Dictionary<uint, int>();

        for (int i = 0; i < n; i++)
        {
            Link(i, RootedKeys(apps[i]), byPath, Union);
            Link(i, AumidKeys(apps[i]), byAumid, Union);
            foreach (uint pid in PidKeys(apps[i]))
            {
                if (byPid.TryGetValue(pid, out int other))
                {
                    Union(i, other);
                }
                else
                {
                    byPid[pid] = i;
                }
            }
        }

        var groups = new Dictionary<int, List<int>>();
        var order = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!groups.TryGetValue(root, out List<int>? members))
            {
                members = [];
                groups[root] = members;
                order.Add(root);
            }

            members.Add(i);
        }

        var result = new List<RunningAppInfo>(order.Count);
        foreach (int root in order)
        {
            result.Add(Merge(apps, groups[root]));
        }

        return result;
    }

    private static void Link(
        int index,
        IEnumerable<string> keys,
        Dictionary<string, int> indexByKey,
        Action<int, int> union)
    {
        foreach (string key in keys)
        {
            if (indexByKey.TryGetValue(key, out int other))
            {
                union(index, other);
            }
            else
            {
                indexByKey[key] = index;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _census.Unsubscribe(OnCensus);
        _census.Release();
    }

    private static RunningAppInfo Merge(IReadOnlyList<RunningAppInfo> apps, List<int> members)
    {
        RunningAppInfo first = apps[members[0]];
        if (members.Count == 1)
        {
            return WithCollectedIds(first);
        }

        string label = first.Label;
        IntPtr hwnd = first.MainWindowHandle;
        string path = first.Path;
        string? aumid = IsAumid(first.Id) ? first.Id.Trim() : null;
        uint processId = first.ProcessId;
        int windows = 0;
        var pids = new List<uint>();
        var ids = new List<string>();
        var paths = new List<string>();

        foreach (int index in members)
        {
            RunningAppInfo app = apps[index];
            windows += Math.Max(1, app.WindowCount);
            if (string.IsNullOrEmpty(label) && !string.IsNullOrEmpty(app.Label))
            {
                label = app.Label;
            }

            if (hwnd == IntPtr.Zero && app.MainWindowHandle != IntPtr.Zero)
            {
                hwnd = app.MainWindowHandle;
            }

            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(app.Path))
            {
                path = app.Path;
            }

            if (processId == 0 && app.ProcessId != 0)
            {
                processId = app.ProcessId;
            }

            if (aumid is null && IsAumid(app.Id))
            {
                aumid = app.Id.Trim();
            }

            AddPid(pids, app.ProcessId);
            foreach (uint pid in app.ProcessIds)
            {
                AddPid(pids, pid);
            }

            AddAumid(ids, app.Id);
            foreach (string id in app.MatchIds)
            {
                AddAumid(ids, id);
            }

            AddPath(paths, app.Path);
            foreach (string extra in app.MatchPaths)
            {
                AddPath(paths, extra);
            }
        }

        string identity = aumid ?? (!string.IsNullOrEmpty(path) ? path : first.Id);
        return new RunningAppInfo(identity, path, label, hwnd, processId, windows)
        {
            ProcessIds = pids,
            MatchIds = ids,
            MatchPaths = paths,
        };
    }

    /// <summary>
    /// A one-window entry still needs its pid and path on the match lists, so a later
    /// collapse and the dock's pin check see the same identity a multi-window group would.
    /// </summary>
    private static RunningAppInfo WithCollectedIds(RunningAppInfo app)
    {
        var pids = new List<uint>();
        AddPid(pids, app.ProcessId);
        foreach (uint pid in app.ProcessIds)
        {
            AddPid(pids, pid);
        }

        var ids = new List<string>();
        AddAumid(ids, app.Id);
        foreach (string id in app.MatchIds)
        {
            AddAumid(ids, id);
        }

        var paths = new List<string>();
        AddPath(paths, app.Path);
        foreach (string extra in app.MatchPaths)
        {
            AddPath(paths, extra);
        }

        if (pids.Count == app.ProcessIds.Count
            && ids.Count == app.MatchIds.Count
            && paths.Count == app.MatchPaths.Count
            && app.WindowCount >= 1)
        {
            return app;
        }

        return app with
        {
            WindowCount = Math.Max(1, app.WindowCount),
            ProcessIds = pids,
            MatchIds = ids,
            MatchPaths = paths,
        };
    }

    private static IEnumerable<string> RootedKeys(RunningAppInfo app)
    {
        string? path = RootedKey(app.Path);
        if (path is not null)
        {
            yield return path;
        }

        foreach (string extra in app.MatchPaths)
        {
            string? key = RootedKey(extra);
            if (key is not null)
            {
                yield return key;
            }
        }
    }

    private static IEnumerable<string> AumidKeys(RunningAppInfo app)
    {
        if (IsAumid(app.Id))
        {
            yield return app.Id.Trim();
        }

        foreach (string id in app.MatchIds)
        {
            if (IsAumid(id))
            {
                yield return id.Trim();
            }
        }
    }

    private static IEnumerable<uint> PidKeys(RunningAppInfo app)
    {
        if (app.ProcessId != 0)
        {
            yield return app.ProcessId;
        }

        foreach (uint pid in app.ProcessIds)
        {
            if (pid != 0)
            {
                yield return pid;
            }
        }
    }

    private static string? RootedKey(string? path)
    {
        string trimmed = path?.Trim().Trim('"') ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return null;
        }

        bool extended = trimmed.StartsWith(@"\\?\", StringComparison.Ordinal)
            || trimmed.StartsWith(@"\??\", StringComparison.Ordinal);
        if (!extended && !Path.IsPathRooted(trimmed))
        {
            return null;
        }

        string full = DockItemStore.Normalize(trimmed);
        return full.Length == 0 || !Path.IsPathRooted(full) ? null : full;
    }

    private static bool IsAumid(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && !id.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase)
        && !Path.IsPathRooted(id);

    private static void AddPid(List<uint> pids, uint pid)
    {
        if (pid != 0 && !pids.Contains(pid))
        {
            pids.Add(pid);
        }
    }

    private static void AddAumid(List<string> ids, string? id)
    {
        if (!IsAumid(id))
        {
            return;
        }

        string trimmed = id!.Trim();
        for (int i = 0; i < ids.Count; i++)
        {
            if (string.Equals(ids[i], trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        ids.Add(trimmed);
    }

    private static void AddPath(List<string> paths, string? path)
    {
        string? key = RootedKey(path);
        if (key is null)
        {
            return;
        }

        for (int i = 0; i < paths.Count; i++)
        {
            if (string.Equals(paths[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        paths.Add(key);
    }

    private static bool IsSystemUtilityHost(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        string full = Path.GetFullPath(path);
        return full.StartsWith(SystemAppsDir, StringComparison.OrdinalIgnoreCase)
            && full.Length > SystemAppsDir.Length
            && full[SystemAppsDir.Length] == Path.DirectorySeparatorChar;
    }

    private static string ResolveLabel(string title, string path) =>
        AppDisplayName.Choose(ShellDisplayName.TryGet(path), title, path);

    private static bool AppsEqual(List<RunningAppInfo> a, List<RunningAppInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Id, b[i].Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Path, b[i].Path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Label, b[i].Label, StringComparison.Ordinal)
                || a[i].MainWindowHandle != b[i].MainWindowHandle
                || a[i].ProcessId != b[i].ProcessId
                || a[i].WindowCount != b[i].WindowCount
                || !SamePids(a[i].ProcessIds, b[i].ProcessIds)
                || !SameTexts(a[i].MatchIds, b[i].MatchIds)
                || !SameTexts(a[i].MatchPaths, b[i].MatchPaths))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SamePids(IReadOnlyList<uint>? a, IReadOnlyList<uint>? b)
    {
        int an = a?.Count ?? 0;
        int bn = b?.Count ?? 0;
        if (an != bn)
        {
            return false;
        }

        for (int i = 0; i < an; i++)
        {
            if (a![i] != b![i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameTexts(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        int an = a?.Count ?? 0;
        int bn = b?.Count ?? 0;
        if (an != bn)
        {
            return false;
        }

        for (int i = 0; i < an; i++)
        {
            if (!string.Equals(a![i], b![i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
