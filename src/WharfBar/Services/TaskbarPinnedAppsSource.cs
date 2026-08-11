using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using WharfBar.Models;

namespace WharfBar.Services;

internal sealed class TaskbarPinnedAppsSource : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly Dispatcher _dispatcher;
    private readonly List<PinnedApp> _pins = [];
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private DispatcherTimer? _poll;
    private bool _disposed;

    internal TaskbarPinnedAppsSource(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Refresh();
        StartWatcher();
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => Refresh();
        _poll.Start();
    }

    internal IReadOnlyList<PinnedApp> Pins => _pins;
    internal bool HasLivePins => _pins.Count > 0;
    internal event EventHandler? Changed;

    internal void Refresh()
    {
        string dir = GetTaskBarPinnedDirectory();
        var next = new List<PinnedApp>();

        try
        {
            if (Directory.Exists(dir))
            {
                foreach (string lnk in Directory.EnumerateFiles(dir, "*.lnk")
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    if (ShortcutResolver.TryResolve(lnk, out string path, out string args, out string label))
                    {
                        next.Add(new PinnedApp(path, args, label));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar pin scan failed: {ex.Message}");
        }

        if (PinsEqual(_pins, next))
        {
            return;
        }

        _pins.Clear();
        _pins.AddRange(next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _poll?.Stop();
        _poll = null;
        _debounce?.Stop();
        _debounce = null;
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void StartWatcher()
    {
        string dir = GetTaskBarPinnedDirectory();
        if (!Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, "*.lnk")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
        };
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Changed += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            _debounce ??= new DispatcherTimer { Interval = Debounce };
            _debounce.Stop();
            _debounce.Tick -= OnDebounce;
            _debounce.Tick += OnDebounce;
            _debounce.Start();
        });
    }

    private void OnDebounce(object? sender, EventArgs e)
    {
        _debounce?.Stop();
        Refresh();
    }

    internal static string GetTaskBarPinnedDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    private static bool PinsEqual(List<PinnedApp> a, List<PinnedApp> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Path, b[i].Path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Args, b[i].Args, StringComparison.Ordinal)
                || !string.Equals(a[i].Label, b[i].Label, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
