using System.Diagnostics;
using System.Text;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>One top-level window from the shared dock census.</summary>
internal readonly record struct CensusWindow(
    IntPtr Hwnd,
    string Title,
    uint Pid,
    bool IsMinimized,
    string Path,
    string? AppUserModelId);

/// <summary>
/// One <c>EnumWindows</c> pass for every dock. Running apps and the minimized-window
/// table used to scan on their own timers, and each monitor repeated both.
/// Process paths stay cached until the pid disappears.
/// </summary>
internal sealed class WindowCensus
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private static readonly object Gate = new();
    private static WindowCensus? _shared;

    private readonly object _listenerGate = new();
    private readonly List<Action<IReadOnlyList<CensusWindow>>> _listeners = [];
    private readonly Dictionary<uint, string> _paths = [];
    private Timer? _timer;
    private int _refs;
    private int _busy;
    private int _pending;
    private int _closed;

    private WindowCensus()
    {
    }

    internal static WindowCensus Acquire()
    {
        lock (Gate)
        {
            _shared ??= new WindowCensus();
            _shared._refs++;
            _shared._timer ??= new Timer(_ => _shared?.RequestScan(), null, Interval, Interval);
            return _shared;
        }
    }

    internal void Release()
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_shared, this))
            {
                return;
            }

            _refs--;
            if (_refs > 0)
            {
                return;
            }

            Volatile.Write(ref _closed, 1);
            _timer?.Dispose();
            _timer = null;
            _shared = null;
            lock (_listenerGate)
            {
                _listeners.Clear();
            }
        }
    }

    internal void Subscribe(Action<IReadOnlyList<CensusWindow>> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_listenerGate)
        {
            _listeners.Add(listener);
        }

        RequestScan();
    }

    internal void Unsubscribe(Action<IReadOnlyList<CensusWindow>> listener)
    {
        lock (_listenerGate)
        {
            _listeners.Remove(listener);
        }
    }

    internal void RequestScan()
    {
        if (Volatile.Read(ref _closed) != 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            Interlocked.Exchange(ref _pending, 1);
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => RunScan());
    }

    private void RunScan()
    {
        try
        {
            if (Volatile.Read(ref _closed) != 0)
            {
                return;
            }

            IReadOnlyList<CensusWindow> snapshot = Capture();
            Action<IReadOnlyList<CensusWindow>>[] listeners;
            lock (_listenerGate)
            {
                listeners = [.. _listeners];
            }

            foreach (Action<IReadOnlyList<CensusWindow>> listener in listeners)
            {
                try
                {
                    listener(snapshot);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WharfBar window census listener failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar window census failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            if (Interlocked.Exchange(ref _pending, 0) != 0 && Volatile.Read(ref _closed) == 0)
            {
                RequestScan();
            }
        }
    }

    private IReadOnlyList<CensusWindow> Capture()
    {
        IReadOnlyList<(IntPtr Hwnd, string Title, uint Pid)> candidates =
            WindowEnumeration.GetTopLevelCandidates();
        var seen = new HashSet<uint>();
        var result = new List<CensusWindow>(candidates.Count);
        foreach ((IntPtr hwnd, string title, uint pid) in candidates)
        {
            seen.Add(pid);
            bool hasAumid = AppUserModelId.TryGet(hwnd, out string aumid);
            result.Add(new CensusWindow(
                hwnd,
                title,
                pid,
                NativeMethods.IsIconic(hwnd),
                PathFor(pid),
                hasAumid ? aumid : null));
        }

        lock (_paths)
        {
            List<uint>? drop = null;
            foreach (uint pid in _paths.Keys)
            {
                if (!seen.Contains(pid))
                {
                    (drop ??= []).Add(pid);
                }
            }

            if (drop is not null)
            {
                foreach (uint pid in drop)
                {
                    _paths.Remove(pid);
                }
            }
        }

        return result;
    }

    private string PathFor(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        lock (_paths)
        {
            if (_paths.TryGetValue(pid, out string? cached))
            {
                return cached;
            }
        }

        string path = QueryProcessPath(pid);
        if (path.Length == 0)
        {
            return string.Empty;
        }

        lock (_paths)
        {
            _paths[pid] = path;
        }

        return path;
    }

    private static string QueryProcessPath(uint pid)
    {
        IntPtr handle = NativeMethods.OpenProcess(
            NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
            bInheritHandle: false,
            pid);
        if (handle == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size)
                ? sb.ToString()
                : string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar process path lookup failed (pid {pid}): {ex.Message}");
            return string.Empty;
        }
        finally
        {
            _ = NativeMethods.CloseHandle(handle);
        }
    }
}
