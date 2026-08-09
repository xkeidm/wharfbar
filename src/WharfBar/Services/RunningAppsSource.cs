using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed record RunningAppInfo(string Id, string Path, string Label, IntPtr MainWindowHandle);

internal sealed class RunningAppsSource : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly Dispatcher _dispatcher;
    private readonly List<RunningAppInfo> _apps = [];
    private DispatcherTimer? _poll;
    private bool _disposed;

    internal RunningAppsSource(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Refresh();
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => Refresh();
        _poll.Start();
    }

    internal IReadOnlyList<RunningAppInfo> Apps => _apps;
    internal event EventHandler? Changed;

    internal void Refresh()
    {
        var grouped = new Dictionary<string, RunningAppInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hwnd, title, pid) in WindowEnumeration.GetTopLevelCandidates())
        {
            bool hasAumid = AppUserModelId.TryGet(hwnd, out string aumid);
            string id = hasAumid ? aumid : ResolveIdFromProcess(hwnd, pid);
            if (grouped.ContainsKey(id))
            {
                continue;
            }

            string path = TryGetProcessPath(pid);
            string label = ResolveLabel(title, path);

            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(label) && !hasAumid)
            {
                continue;
            }

            grouped[id] = new RunningAppInfo(id, path, label, hwnd);
        }

        var next = grouped.Values.ToList();
        if (AppsEqual(_apps, next))
        {
            return;
        }

        _apps.Clear();
        _apps.AddRange(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _poll?.Stop();
        _poll = null;
    }

    private static string ResolveIdFromProcess(IntPtr hwnd, uint pid)
    {
        string path = TryGetProcessPath(pid);
        if (!string.IsNullOrEmpty(path))
        {
            return path;
        }

        return $"hwnd:{hwnd}";
    }

    private static string TryGetProcessPath(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.MainModule?.FileName ?? string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar process path lookup failed (pid {pid}): {ex.Message}");
            return string.Empty;
        }
    }

    private static string ResolveLabel(string title, string path)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        if (!string.IsNullOrEmpty(path))
        {
            return Path.GetFileName(path);
        }

        return string.Empty;
    }

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
                || a[i].MainWindowHandle != b[i].MainWindowHandle)
            {
                return false;
            }
        }

        return true;
    }
}
