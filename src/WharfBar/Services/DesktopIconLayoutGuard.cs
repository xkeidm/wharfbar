using System.Diagnostics;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Keeps the desktop icon arrangement stable while WharfBar owns the top/bottom work-area edges.
/// </summary>
/// <remarks>
/// Windows 11 re-lays-out the desktop icons whenever a monitor's work area changes: explorer
/// re-expresses the layout for the new grid (25H2 also changes the icon pitch) and, on a
/// multi-change startup such as WharfBar's (taskbar strip release, top AppBar, dock AppBar), it can
/// re-pack the icons into columns. The user's arrangement is then lost for the whole session.
///
/// The guard captures the layout at launch (before the first work-area change), as monitor + grid
/// cell instead of raw pixels, and re-projects it onto the live work areas once the shell settles
/// after every change. It also re-captures small user edits (dragging an icon) so its model stays
/// the user's current layout, and it repairs the original layout on exit when the shell failed to
/// restore it by itself.
///
/// Only documented shell interfaces are used (<see cref="DesktopIconView"/>); nothing is read from
/// or written to explorer's process memory. The shell work runs on a dedicated STA thread so the
/// short COM calls never stall the WPF UI thread.
/// </remarks>
internal sealed class DesktopIconLayoutGuard : IDisposable
{
    private const int WatchIntervalMs = 250;
    private const int SettleDelayMs = 800;

    /// <summary>Cadence of the live desktop-icon read while the user is present.</summary>
    internal const int LiveRefreshMs = 3000;

    /// <summary>Cadence of the live desktop-icon read while the session has been idle.</summary>
    internal const int LiveRefreshIdleMs = 30_000;

    /// <summary>Idle time after which the live desktop-icon read drops to <see cref="LiveRefreshIdleMs"/>.</summary>
    internal const int IdleBeforeSlowRefreshMs = 60_000;

    private const int VerifyDelayMs = 400;
    private const int MaxWriteAttempts = 3;
    private const int MaxReapplyFailures = 3;
    private const int UnavailableRetryMs = 60_000;
    private const int RestoreSettleMs = 600;
    private const int RestoreWaitMs = 5_000;

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(8);

    private readonly ManualResetEventSlim _started = new(false);
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _timer;
    private volatile bool _disposed;

    private DesktopIconView? _view;
    private DesktopIconLayoutSnapshot? _baseline;
    private DesktopIconLayoutSnapshot? _desired;
    private List<MonitorWorkArea> _lastMonitors = new();
    private long _monitorSignatureChangedAt;
    private long _nextReapplyAt = -1;
    private long _lastIdleRefreshAt;
    private long _unavailableUntil;
    private int _openFailures;
    private int _reapplyFailures;
    private bool _disabled;

    /// <summary>
    /// Starts the watcher and captures the layout. Returns only after the first capture (or the
    /// capture timeout), so the caller can mutate the work area immediately afterwards.
    /// </summary>
    internal void Start()
    {
        if (_thread is not null || _disposed)
        {
            return;
        }

        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "WharfBar desktop icon layout",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ = _started.Wait(StartTimeout);
    }

    private void ThreadMain()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            InitializeCore();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icons: init failed — {ex.Message}");
        }
        finally
        {
            _started.Set();
        }

        if (_disposed)
        {
            return;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(WatchIntervalMs),
        };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        Dispatcher.Run();
    }

    private void InitializeCore()
    {
        if (!TryEnsureView())
        {
            _openFailures++;
            _unavailableUntil = Environment.TickCount64 + (_openFailures < 3 ? 1_000 : UnavailableRetryMs);
            return;
        }

        List<MonitorWorkArea> monitors = ReadMonitors();
        DesktopIconLayoutSnapshot? baseline = CaptureSnapshot(monitors, out bool unsupported);
        if (baseline is null)
        {
            if (unsupported)
            {
                _disabled = true;
                Debug.WriteLine(
                    "WharfBar desktop icons: layout is not grid-aligned (or auto-arranged); "
                    + "leaving the shell's desktop layout alone");
            }
            else
            {
                _unavailableUntil = Environment.TickCount64 + 1_000;
            }

            return;
        }

        _baseline = baseline;
        _desired = baseline;
        _lastMonitors = monitors;
        _monitorSignatureChangedAt = Environment.TickCount64;
        _lastIdleRefreshAt = Environment.TickCount64;
        Debug.WriteLine(
            $"WharfBar desktop icons: captured {baseline.Count} icons on {baseline.Monitors.Count} monitor(s), "
            + $"grid {baseline.PitchX}×{baseline.PitchY}");
    }

    private bool TryEnsureView()
    {
        if (_view is not null)
        {
            return true;
        }

        if (!DesktopIconView.TryOpen(out DesktopIconView view))
        {
            return false;
        }

        _view = view;
        _openFailures = 0;
        return true;
    }

    private void InvalidateView()
    {
        _view?.Dispose();
        _view = null;
    }

    private void OnTick()
    {
        long now = Environment.TickCount64;
        if (_disposed || _disabled)
        {
            return;
        }

        if (_view is null)
        {
            if (now < _unavailableUntil)
            {
                return;
            }

            if (!TryEnsureView())
            {
                _openFailures++;
                _unavailableUntil = now + (_openFailures < 3 ? 1_000 : UnavailableRetryMs);
                return;
            }

            // Reconnected after an explorer restart: the desktop that exists now is the user's
            // desktop, so it becomes the baseline again.
            List<MonitorWorkArea> reconnected = ReadMonitors();
            DesktopIconLayoutSnapshot? snapshot = CaptureSnapshot(reconnected, out bool unsupported);
            if (snapshot is not null)
            {
                _baseline = snapshot;
                _desired = snapshot;
                _lastMonitors = reconnected;
                _monitorSignatureChangedAt = now;
            }
            else
            {
                if (unsupported)
                {
                    _disabled = true;
                }

                _unavailableUntil = now + (unsupported ? UnavailableRetryMs : 1_000);
            }

            return;
        }

        List<MonitorWorkArea> monitors = ReadMonitors();
        if (!SameMonitors(monitors, _lastMonitors))
        {
            // The layout the user has right now (including drags made seconds before the change)
            // must survive: adopt small divergences before the shell's re-layout is repaired.
            _ = TryAdoptSmallChanges(monitors);
            _lastMonitors = monitors;
            _monitorSignatureChangedAt = now;
            _lastIdleRefreshAt = now;
            _nextReapplyAt = -1;
            return;
        }

        // Let the shell finish its re-layout before writing anything back.
        if (now - _monitorSignatureChangedAt < SettleDelayMs)
        {
            return;
        }

        if (_nextReapplyAt >= 0)
        {
            if (now < _nextReapplyAt)
            {
                return;
            }

            _nextReapplyAt = -1;
            Reapply(monitors);
            return;
        }

        // Detect the user's own drags. The COM folder-view read runs every 3 s while the user is
        // around and backs off to 30 s once the session has been idle for a minute; input brings
        // the fast interval back on the next watch tick.
        if (now - _lastIdleRefreshAt >= LiveRefreshIntervalMs(TrayAutoHideReveal.UserIdleTime()))
        {
            _lastIdleRefreshAt = now;
            RefreshDesiredFromLive(monitors);
        }
    }

    /// <summary>
    /// Interval between live desktop-icon reads: the COM folder-view read runs every 3 s while the
    /// user is present, but a session idle for over a minute only needs the 30 s cadence (a drag
    /// needs input). The 250 ms monitor check stays unchanged.
    /// </summary>
    internal static int LiveRefreshIntervalMs(TimeSpan userIdle)
        => userIdle.TotalMilliseconds > IdleBeforeSlowRefreshMs ? LiveRefreshIdleMs : LiveRefreshMs;

    private DesktopIconLayoutSnapshot? CaptureSnapshot(
        IReadOnlyList<MonitorWorkArea> monitors,
        out bool unsupported)
    {
        unsupported = false;
        if (_view is null || !_view.TryRead(out List<DesktopIconPoint> items, out int pitchX, out int pitchY))
        {
            InvalidateView();
            return null;
        }

        if (_view.IsAutoArrange())
        {
            unsupported = true;
            return null;
        }

        DesktopIconLayoutSnapshot? snapshot = DesktopIconLayoutSnapshot.Create(items, monitors, pitchX, pitchY);
        if (snapshot is null)
        {
            unsupported = true;
        }

        return snapshot;
    }

    /// <summary>Re-projects the captured arrangement onto the current work areas.</summary>
    private void Reapply(IReadOnlyList<MonitorWorkArea> monitors)
    {
        // A drag that happened just before this work-area change is the user's layout, not a
        // reflow artefact: adopt it before repairing.
        _ = TryAdoptSmallChanges(monitors);
        DesktopIconLayoutSnapshot? desired = _desired;
        if (desired is null || _view is null)
        {
            return;
        }

        if (!_view.TryRead(out List<DesktopIconPoint> live, out int pitchX, out int pitchY))
        {
            InvalidateView();
            return;
        }

        if (pitchX <= 0)
        {
            pitchX = desired.PitchX;
        }

        if (pitchY <= 0)
        {
            pitchY = desired.PitchY;
        }

        HashSet<string> liveNames = live
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<DesktopIconPoint> targets = DesktopIconLayoutMap.Map(desired, monitors, pitchX, pitchY, liveNames);
        if (targets.Count == 0)
        {
            return;
        }

        var liveByName = new Dictionary<string, DesktopIconPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (DesktopIconPoint item in live)
        {
            liveByName[item.Name] = item;
        }

        targets.RemoveAll(target => liveByName.TryGetValue(target.Name, out DesktopIconPoint current)
            && current.X == target.X
            && current.Y == target.Y);
        if (targets.Count == 0)
        {
            return;
        }

        for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
        {
            if (!_view.TryWrite(targets))
            {
                // The view died (explorer restart) — reopen on the next tick.
                InvalidateView();
                return;
            }

            Thread.Sleep(VerifyDelayMs);
            if (!_view.TryRead(out List<DesktopIconPoint> after, out _, out _))
            {
                InvalidateView();
                return;
            }

            var afterByName = new Dictionary<string, DesktopIconPoint>(StringComparer.OrdinalIgnoreCase);
            foreach (DesktopIconPoint item in after)
            {
                afterByName[item.Name] = item;
            }

            int offTarget = 0;
            foreach (DesktopIconPoint target in targets)
            {
                if (afterByName.TryGetValue(target.Name, out DesktopIconPoint actual)
                    && (actual.X != target.X || actual.Y != target.Y))
                {
                    offTarget++;
                }
            }

            if (offTarget == 0)
            {
                _reapplyFailures = 0;
                _lastIdleRefreshAt = Environment.TickCount64;
                return;
            }
        }

        // The shell refused the projection (work area not settled, a monitor in flux, an icon that
        // cannot leave a reserved strip). Never fight it forever: after a few attempts adopt the
        // live state so the desktop stops churning.
        _reapplyFailures++;
        _lastIdleRefreshAt = Environment.TickCount64;
        Debug.WriteLine(
            $"WharfBar desktop icons: {targets.Count} icon(s) did not take the re-projected position "
            + $"(attempt {_reapplyFailures})");
        if (_reapplyFailures >= MaxReapplyFailures
            && _view.TryRead(out List<DesktopIconPoint> settled, out int settledPitchX, out int settledPitchY))
        {
            DesktopIconLayoutSnapshot? adopted = DesktopIconLayoutSnapshot.Create(
                settled,
                monitors,
                settledPitchX > 0 ? settledPitchX : desired.PitchX,
                settledPitchY > 0 ? settledPitchY : desired.PitchY);
            if (adopted is not null)
            {
                _desired = adopted;
                _reapplyFailures = 0;
                Debug.WriteLine("WharfBar desktop icons: adopted the shell's settled layout to stop churn");
            }
        }
    }

    /// <summary>
    /// Adopts a live layout that differs from the model only in a few icons (a user drag). A
    /// wholesale difference is a re-layout and is left to <see cref="Reapply"/>.
    /// </summary>
    private bool TryAdoptSmallChanges(IReadOnlyList<MonitorWorkArea> monitors)
    {
        DesktopIconLayoutSnapshot? desired = _desired;
        if (desired is null || _view is null)
        {
            return false;
        }

        if (!_view.TryRead(out List<DesktopIconPoint> live, out int pitchX, out int pitchY))
        {
            return false;
        }

        if (pitchX <= 0)
        {
            pitchX = desired.PitchX;
        }

        if (pitchY <= 0)
        {
            pitchY = desired.PitchY;
        }

        HashSet<string> liveNames = live.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<DesktopIconPoint> expected = DesktopIconLayoutMap.Map(desired, monitors, pitchX, pitchY, liveNames);
        var expectedByName = new Dictionary<string, DesktopIconPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (DesktopIconPoint item in expected)
        {
            expectedByName[item.Name] = item;
        }

        int changed = live.Count(item => !expectedByName.TryGetValue(item.Name, out DesktopIconPoint target)
            || target.X != item.X
            || target.Y != item.Y);
        if (changed == 0)
        {
            return false;
        }

        int tolerance = Math.Max(2, live.Count / 4);
        if (changed > tolerance)
        {
            return false;
        }

        DesktopIconLayoutSnapshot? updated = DesktopIconLayoutSnapshot.Create(live, monitors, pitchX, pitchY);
        if (updated is null)
        {
            return false;
        }

        _desired = updated;
        Debug.WriteLine($"WharfBar desktop icons: adopted {changed} user-moved icon(s)");
        return true;
    }

    /// <summary>
    /// Detects the user's own edits. A handful of changed icons is a user drag (and becomes the new
    /// desired layout); a wholesale change means a work-area reflow slipped past the signature
    /// watcher, so the captured arrangement is re-applied instead.
    /// </summary>
    private void RefreshDesiredFromLive(IReadOnlyList<MonitorWorkArea> monitors)
    {
        DesktopIconLayoutSnapshot? desired = _desired;
        if (desired is null || _view is null)
        {
            return;
        }

        if (!_view.TryRead(out List<DesktopIconPoint> live, out int pitchX, out int pitchY))
        {
            InvalidateView();
            return;
        }

        if (pitchX <= 0)
        {
            pitchX = desired.PitchX;
        }

        if (pitchY <= 0)
        {
            pitchY = desired.PitchY;
        }

        HashSet<string> liveNames = live.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<DesktopIconPoint> expected = DesktopIconLayoutMap.Map(desired, monitors, pitchX, pitchY, liveNames);
        var expectedByName = new Dictionary<string, DesktopIconPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (DesktopIconPoint item in expected)
        {
            expectedByName[item.Name] = item;
        }

        int changed = live.Count(item => !expectedByName.TryGetValue(item.Name, out DesktopIconPoint target)
            || target.X != item.X
            || target.Y != item.Y);
        if (changed == 0)
        {
            return;
        }

        int tolerance = Math.Max(2, live.Count / 4);
        if (changed > tolerance)
        {
            // Likely an unannounced reflow; repair from the known-good arrangement.
            _nextReapplyAt = Environment.TickCount64 + 50;
            return;
        }

        DesktopIconLayoutSnapshot? updated = DesktopIconLayoutSnapshot.Create(live, monitors, pitchX, pitchY);
        if (updated is not null)
        {
            _desired = updated;
            Debug.WriteLine($"WharfBar desktop icons: adopted {changed} moved icon(s)");
        }
    }

    /// <summary>
    /// Repairs the desktop before the process exits: after the bars and taskbar releases put the
    /// work areas back, the shell normally restores the launch layout by itself; if it did not,
    /// the captured arrangement is written back explicitly.
    /// </summary>
    internal void RestoreOnExit()
    {
        Dispatcher? dispatcher = _dispatcher;
        if (dispatcher is null || _disposed)
        {
            return;
        }

        try
        {
            dispatcher.Invoke(
                new Action(RestoreCore),
                DispatcherPriority.Send,
                CancellationToken.None,
                RestoreTimeout);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icons: exit restore failed — {ex.Message}");
        }
    }

    private void RestoreCore()
    {
        DesktopIconLayoutSnapshot? baseline = _baseline;
        DesktopIconLayoutSnapshot? desired = _desired ?? baseline;
        if (_disabled || baseline is null || desired is null)
        {
            return;
        }

        // Wait for the shell to see the launch work areas again (bars removed, taskbar restored).
        long deadline = Environment.TickCount64 + RestoreWaitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (SameMonitors(ReadMonitors(), baseline.Monitors.ToList()))
            {
                break;
            }

            Thread.Sleep(100);
        }

        Thread.Sleep(RestoreSettleMs);
        if (!TryEnsureView())
        {
            return;
        }

        if (!_view!.TryRead(out List<DesktopIconPoint> live, out int pitchX, out int pitchY))
        {
            InvalidateView();
            return;
        }

        if (pitchX <= 0)
        {
            pitchX = baseline.PitchX;
        }

        if (pitchY <= 0)
        {
            pitchY = baseline.PitchY;
        }

        // The shell normally restores the launch layout itself when the work area returns; carry
        // the user's own edits made during the session on top of it (the guard adopted them).
        List<MonitorWorkArea> monitors = ReadMonitors();
        HashSet<string> liveNames = live.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<DesktopIconPoint> targets = DesktopIconLayoutMap.Map(desired, monitors, pitchX, pitchY, liveNames);
        if (targets.Count == 0)
        {
            return;
        }

        var liveByName = new Dictionary<string, DesktopIconPoint>(StringComparer.OrdinalIgnoreCase);
        foreach (DesktopIconPoint item in live)
        {
            liveByName[item.Name] = item;
        }

        targets.RemoveAll(target => liveByName.TryGetValue(target.Name, out DesktopIconPoint current)
            && current.X == target.X
            && current.Y == target.Y);
        if (targets.Count == 0)
        {
            return;
        }

        if (_view.TryWrite(targets))
        {
            Thread.Sleep(VerifyDelayMs);
            Debug.WriteLine($"WharfBar desktop icons: restored {targets.Count} icon position(s) on exit");
        }
    }

    private static List<MonitorWorkArea> ReadMonitors()
    {
        var monitors = new List<MonitorWorkArea>(2);
        bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT rect, IntPtr data)
        {
            var info = new MONITORINFOEX { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (MonitorNative.GetMonitorInfo(hMonitor, ref info) && info.rcWork.Width > 0 && info.rcWork.Height > 0)
            {
                monitors.Add(new MonitorWorkArea(
                    info.szDevice ?? string.Empty,
                    new WorkAreaRect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom)));
            }

            return true;
        }

        _ = MonitorNative.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        monitors.Sort((a, b) => string.CompareOrdinal(a.DeviceName, b.DeviceName));
        return monitors;
    }

    private static bool SameMonitors(IReadOnlyList<MonitorWorkArea> a, IReadOnlyList<MonitorWorkArea> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].DeviceName, b[i].DeviceName, StringComparison.OrdinalIgnoreCase)
                || a[i].WorkArea != b[i].WorkArea)
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _timer?.Stop();
            _timer = null;
            _dispatcher?.InvokeShutdown();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icons: shutdown failed — {ex.Message}");
        }

        // The STA thread calls _started.Set() from ThreadMain. Only dispose the event once the
        // thread is known to be gone; a timed-out join can leave it about to call Set(), and
        // disposing underneath it would throw ObjectDisposedException on that thread. When the
        // join times out the event is simply left for the GC.
        bool joined = false;
        try
        {
            joined = _thread?.Join(TimeSpan.FromSeconds(2)) ?? true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar desktop icons: thread join failed — {ex.Message}");
        }

        _thread = null;
        _dispatcher = null;
        InvalidateView();
        if (joined)
        {
            _started.Dispose();
        }
    }
}


