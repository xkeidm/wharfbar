using System.Diagnostics;
using System.Linq;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Captures the Windows taskbar baseline on startup, hides it while WharfBar runs,
/// and restores on every exit/crash path (same discipline as AppBar.Remove).
/// Also expands the primary work-area bottom after hide — <c>SW_HIDE</c> alone leaves a
/// phantom taskbar strip that maximized apps still respect.
/// Explorer can re-show <c>Shell_TrayWnd</c> after AppBar <c>SETPOS</c> or auto-hide
/// edge reveal; a keep-hidden watch re-applies hide until restore.
/// </summary>
internal sealed class TaskbarVisibilityService
{
    // Keep alive for SetConsoleCtrlHandler — GC collecting the delegate unregisters the handler.
    private NativeMethods.ConsoleCtrlHandlerRoutine? _consoleCtrlHandler;

    private uint _baselineState;
    private RECT _baselineWorkArea;
    private bool _haveBaselineWorkArea;
    private int _captured; // 0 = not captured
    private int _restored; // 0 = not restored
    private int _hooksAttached;
    private bool _hidden;
    private readonly TrayPeekTracker _peekTracker = new(AppBarPlacement.TrayPeekMaxDuration);
    private readonly TaskbarWatchdogStateStore _watchdogStore = new(TaskbarWatchdog.DefaultDirectory());
    private bool _stealthPeek;
    private int _expandingWorkArea;
    private int _pendingBroadcastExpand;
    private int _watchBusy;
    private readonly object _reservationLock = new();
    private readonly Dictionary<string, int> _reservedBottomByMonitor = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _keepHiddenTimer;
    private long _lastWorkAreaCheckMs = -1;
    private Task? _restoreTask;

    /// <summary>
    /// Restore tamamlandığından emin olmak için çıkış yollarında beklenen üst sınır.
    /// Ölçülen en kötü blok ~13 sn idi; tavan iki katından fazla.
    /// </summary>
    internal static readonly TimeSpan RestoreWaitTimeout = TimeSpan.FromSeconds(30);

    internal bool IsHidden => _hidden;

    /// <summary>
    /// Updates the dock reservation for <paramref name="monitor"/> and re-announces that
    /// monitor's work area so maximized apps resize to the new bottom edge.
    /// </summary>
    internal void SetDockReservation(MonitorInfo monitor, int bottomPx)
    {
        lock (_reservationLock)
        {
            int current = _reservedBottomByMonitor.GetValueOrDefault(monitor.DeviceId);
            if (current == bottomPx)
            {
                return;
            }

            _reservedBottomByMonitor[monitor.DeviceId] = Math.Max(0, bottomPx);
        }

        ExpandWorkAreaBottom(monitor, broadcast: true);
    }

    /// <summary>True while a tray peek has temporarily shown <c>Shell_TrayWnd</c>.</summary>
    internal bool IsTrayPeekActive => _peekTracker.IsActive;

    /// <summary>True when the active peek parked the tray off-screen (no visible flash).</summary>
    internal bool IsStealthPeekActive => IsTrayPeekActive && _stealthPeek;

    internal void CaptureAndHide()
    {
        if (Interlocked.Exchange(ref _captured, 1) != 0)
        {
            ReapplyHide();
            return;
        }

        // Self-heal: a leftover state file means the previous run died without restoring
        // (hard crash — corrupting-state exceptions skip managed handlers). Show the tray
        // BEFORE capturing so the baseline reflects the user's real taskbar and this
        // startup repairs the desktop even if the watchdog itself failed.
        TaskbarWatchdogState? stale = _watchdogStore.Load();
        if (stale is not null)
        {
            try
            {
                TaskbarNative.SetTrayVisible(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar stale taskbar heal failed: {ex.Message}");
            }
        }

        // Capture AppBar state only. Do not trust IsWindowVisible for restore:
        // auto-hide taskbars often report false when slid away, which would
        // permanently leave the tray hidden after exit.
        _baselineState = TaskbarNative.GetState();
        if (TaskbarNative.TryGetPrimaryWorkArea(out RECT work))
        {
            _baselineWorkArea = work;
            _haveBaselineWorkArea = true;
        }

        Interlocked.Exchange(ref _restored, 0);

        SaveWatchdogState();

        AttachShutdownHooks();
        // First announcement must reach already-maximized windows (broadcast).
        ApplyHide(broadcast: true);
        StartKeepHiddenWatch();
        SpawnWatchdog();
    }

    /// <summary>
    /// Persists the taskbar baseline for the watchdog process. File present = taskbar
    /// hidden by this process generation and not yet restored.
    /// </summary>
    private void SaveWatchdogState()
    {
        try
        {
            _watchdogStore.Save(new TaskbarWatchdogState
            {
                Pid = Environment.ProcessId,
                ProcessStartTime = Process.GetCurrentProcess().StartTime,
                BaselineState = _baselineState,
                WorkAreaLeft = _baselineWorkArea.Left,
                WorkAreaTop = _baselineWorkArea.Top,
                WorkAreaRight = _baselineWorkArea.Right,
                WorkAreaBottom = _baselineWorkArea.Bottom,
                HaveWorkArea = _haveBaselineWorkArea,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar watchdog state save failed: {ex.Message}");
        }
    }

    /// <summary>Starts the out-of-process taskbar watchdog (best effort).</summary>
    private void SpawnWatchdog()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return;
            }

            _ = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--taskbar-watchdog",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar watchdog spawn failed: {ex.Message}");
        }
    }

    internal void ReapplyHide()
    {
        if (Volatile.Read(ref _captured) == 0 || Volatile.Read(ref _restored) != 0)
        {
            return;
        }

        // Do not yank the tray closed mid-overflow; the peek timer will EndTrayPeek.
        if (IsTrayPeekActive)
        {
            return;
        }

        ApplyHide();
    }

    /// <summary>
    /// Re-expands the work-area bottom for every reserved monitor after AppBar
    /// <c>SETPOS</c> (shell may restore the phantom taskbar strip). No-op when not hidden.
    /// </summary>
    internal void EnsureWorkAreaExpanded()
    {
        if (!_hidden || Volatile.Read(ref _restored) != 0 || Volatile.Read(ref _captured) == 0)
        {
            return;
        }

        KeyValuePair<string, int>[] snapshot;
        lock (_reservationLock)
        {
            snapshot = _reservedBottomByMonitor.ToArray();
        }

        foreach (KeyValuePair<string, int> pair in snapshot)
        {
            MonitorInfo? monitor = MonitorNative.Enumerate().FirstOrDefault(m =>
                string.Equals(m.DeviceId, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (monitor is not null)
            {
                ExpandWorkAreaBottom(monitor);
            }
        }
    }

    /// <summary>
    /// Temporarily shows the real taskbar so native overflow / Win+B / UIA can work.
    /// Pair with <see cref="EndTrayPeek"/>. Nested peeks are refcounted.
    /// </summary>
    /// <param name="stealth">
    /// When true, parks <c>Shell_TrayWnd</c> below the monitor so background mirror polls
    /// do not flash the taskbar. Visible peeks (overflow / click) pass false.
    /// </param>
    /// <returns>True when a peek was started (caller must end it).</returns>
    internal bool BeginTrayPeek(bool stealth = false)
    {
        if (Volatile.Read(ref _captured) == 0 || Volatile.Read(ref _restored) != 0)
        {
            return false;
        }

        if (IsTrayPeekActive)
        {
            _peekTracker.Begin(DateTime.UtcNow);
            // Upgrade stealth → visible if a user-facing peek nests over a mirror poll.
            if (!stealth && _stealthPeek)
            {
                try
                {
                    TaskbarNative.UnparkTrayOnScreen();
                    _stealthPeek = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WharfBar tray peek upgrade failed: {ex.Message}");
                }
            }

            return true;
        }

        try
        {
            if (stealth)
            {
                // Cloak refused → a parked+shown tray could flash when explorer re-asserts
                // position. Skip this read cycle instead of gambling on visibility.
                if (!TaskbarNative.TrySetTrayVisibleStealth())
                {
                    return false;
                }

                _stealthPeek = true;
            }
            else
            {
                TaskbarNative.SetTrayVisible(true);
                _stealthPeek = false;
            }

            _peekTracker.Begin(DateTime.UtcNow);
            // Keep maximized apps from jumping when the shell reclaims the bottom strip.
            MonitorInfo? primary = MonitorNative.Enumerate().FirstOrDefault(m => m.IsPrimary);
            if (primary is not null)
            {
                ExpandWorkAreaBottom(primary);
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar tray peek show failed: {ex.Message}");
            _peekTracker.Reset();
            _stealthPeek = false;
            return false;
        }
    }

    /// <summary>Hides the taskbar again after a tray peek (still respects restore). Refcounted.</summary>
    internal void EndTrayPeek()
    {
        if (!_peekTracker.IsActive)
        {
            return;
        }

        if (!_peekTracker.End())
        {
            return;
        }

        _stealthPeek = false;

        if (Volatile.Read(ref _restored) != 0 || Volatile.Read(ref _captured) == 0)
        {
            return;
        }

        ApplyHide();
    }

    internal void Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
        {
            return;
        }

        if (Volatile.Read(ref _captured) == 0)
        {
            return;
        }

        // Shell calls inside RestoreCore can block for many seconds when some top-level
        // window answers the SPI_SETWORKAREA broadcast slowly (measured 12.6 s once).
        // Running them on the UI thread stalled the still-visible bar window until the
        // broadcast finished — the "closes late" symptom. Kick to background; exit paths
        // wait for completion explicitly (WaitForRestore / RestoreBlocking).
        _restoreTask = Task.Run(RestoreCore);
    }

    /// <summary>
    /// Crash/exit paths where nothing is visible anymore and the process is going away:
    /// run the restore synchronously (a background thread might be killed mid-restore).
    /// </summary>
    internal void RestoreBlocking()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
        {
            WaitForRestore(RestoreWaitTimeout);
            return;
        }

        if (Volatile.Read(ref _captured) == 0)
        {
            return;
        }

        RestoreCore();
    }

    /// <summary>Waits until the background restore finishes (no-op when not started).</summary>
    internal void WaitForRestore(TimeSpan timeout)
    {
        try
        {
            _restoreTask?.Wait(timeout);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: WaitForRestore failed — {ex.Message}");
        }
    }

    private void RestoreCore()
    {
        _peekTracker.Reset();
        _stealthPeek = false;
        StopKeepHiddenWatch();

        try
        {
            // Always show after we hid — visibility at capture is unreliable (auto-hide).
            TaskbarNative.SetTrayVisible(true);
            TaskbarNative.SetState(_baselineState);
            if (_haveBaselineWorkArea)
            {
                _ = TaskbarNative.TrySetPrimaryWorkArea(_baselineWorkArea);
            }

            _hidden = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar taskbar restore failed: {ex.Message}");
        }
        finally
        {
            // Watchdog polls this file: gone → parent restored, watchdog exits.
            _watchdogStore.Delete();
        }
    }

    private void ApplyHide(bool broadcast = false)
    {
        if (Volatile.Read(ref _restored) != 0)
        {
            return;
        }

        try
        {
            // Persistent stealth styling first: any show (ours or explorer's async sync)
            // stays invisible while WharfBar runs. Idempotent; covers new secondaries.
            TaskbarNative.EnsureTrayStealthStyles();

            bool trayWasVisible = TaskbarNative.IsAnyTrayVisible();
            if (trayWasVisible)
            {
                TaskbarNative.SetTrayVisible(false);
            }

            _hidden = true;
            // Silent expansion: a SPIF_SENDCHANGE broadcast makes the shell re-assert the
            // hidden taskbar's phantom reservation, which needs another expansion — an
            // endless grow/shrink loop for maximized windows. Only startup announces.
            MonitorInfo? primary = MonitorNative.Enumerate().FirstOrDefault(m => m.IsPrimary);
            if (primary is not null)
            {
                ExpandWorkAreaBottom(primary, broadcast);
            }

            // Re-notifying on every no-op hide cancels dock auto-hide animation
            // (WorkAreaChanged → ApplyPlacement snaps Y back to Hidden).
            if (trayWasVisible)
            {
                WorkAreaChanged.Notify();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar taskbar hide failed: {ex.Message}");
        }
    }

    private void StartKeepHiddenWatch()
    {
        if (_keepHiddenTimer is not null)
        {
            return;
        }

        // Background thread, not the WPF dispatcher — a busy or starving UI thread must
        // never block re-hiding the taskbar (Background-priority DispatcherTimer starves
        // under constant render/input load and leaves Shell_TrayWnd visible).
        _keepHiddenTimer = new Timer(
            OnKeepHiddenTick,
            null,
            AppBarPlacement.KeepHiddenTrayPollMs,
            AppBarPlacement.KeepHiddenTrayPollMs);
    }

    private void StopKeepHiddenWatch()
    {
        Timer? timer = _keepHiddenTimer;
        _keepHiddenTimer = null;
        _lastWorkAreaCheckMs = -1;
        try
        {
            timer?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar keep-hidden timer stop failed: {ex.Message}");
        }
    }

    private void OnKeepHiddenTick(object? state)
    {
        if (Interlocked.Exchange(ref _watchBusy, 1) != 0)
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref _captured) == 0 || Volatile.Read(ref _restored) != 0)
            {
                return;
            }

            if (_peekTracker.IsActive)
            {
                // A fresh peek (overflow flyout in use) protects the tray from re-hide.
                // An orphaned peek (missed EndTrayPeek) would disable this watch forever
                // and leave the taskbar visible — force-end it once it is stale.
                if (!_peekTracker.IsStale(DateTime.UtcNow))
                {
                    return;
                }

                _peekTracker.Reset();
                _stealthPeek = false;
            }

            if (TaskbarNative.IsAnyTrayVisible())
            {
                TaskbarNative.EnsureTrayStealthStyles();
                TaskbarNative.SetTrayVisible(false);
            }

            // HWND can stay hidden while explorer re-SETPOS the taskbar AppBar (~48px
            // phantom strip). Re-expand on a slow cadence — 10 Hz SPI_GETWORKAREA
            // plus SET+SENDCHANGE fights explorer and stalls the UI thread.
            long nowMs = Environment.TickCount64;
            if (AppBarPlacement.IsIntervalDue(
                    nowMs,
                    _lastWorkAreaCheckMs,
                    AppBarPlacement.KeepHiddenWorkAreaPollMs))
            {
                _lastWorkAreaCheckMs = nowMs;
                EnsureWorkAreaExpanded();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar keep-hidden failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _watchBusy, 0);
        }
    }

    private void ExpandWorkAreaBottom(MonitorInfo monitor, bool broadcast = false)
    {
        if (Interlocked.Exchange(ref _expandingWorkArea, 1) != 0)
        {
            if (broadcast)
            {
                Interlocked.Exchange(ref _pendingBroadcastExpand, 1);
            }

            return;
        }

        try
        {
            ThreadPool.QueueUserWorkItem(RunExpandWorkAreaBottom, (monitor, broadcast));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar work area expand queue failed: {ex.Message}");
            Interlocked.Exchange(ref _expandingWorkArea, 0);
        }
    }

    private void RunExpandWorkAreaBottom(object? state)
    {
        try
        {
            var (monitor, broadcast) = ((MonitorInfo, bool))state!;
            bool restoreRaced = Volatile.Read(ref _restored) != 0;
            if (!restoreRaced)
            {
                int reserved;
                lock (_reservationLock)
                {
                    reserved = _reservedBottomByMonitor.GetValueOrDefault(monitor.DeviceId);
                }

                if (TaskbarNative.TryExpandWorkAreaBottom(monitor, reserved, broadcast))
                {
                    return;
                }
            }

            if (restoreRaced || Volatile.Read(ref _restored) != 0)
            {
                if (_haveBaselineWorkArea)
                {
                    _ = TaskbarNative.TrySetPrimaryWorkArea(_baselineWorkArea);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar work area expand failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _expandingWorkArea, 0);
            if (Interlocked.Exchange(ref _pendingBroadcastExpand, 0) == 1)
            {
                MonitorInfo? monitor = MonitorNative.Enumerate().FirstOrDefault(m => m.IsPrimary);
                if (monitor is not null)
                {
                    ExpandWorkAreaBottom(monitor, broadcast: true);
                }
            }
        }
    }

    private void AttachShutdownHooks()
    {
        if (Interlocked.Exchange(ref _hooksAttached, 1) != 0)
        {
            return;
        }

        if (System.Windows.Application.Current is { } app)
        {
            app.Exit += (_, _) => RestoreBlocking();
            app.SessionEnding += (_, _) => RestoreBlocking();
            app.DispatcherUnhandledException += (_, _) => RestoreBlocking();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreBlocking();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreBlocking();

        // Closing the console that owns `dotnet run` does not run WPF OnExit reliably;
        // CTRL_CLOSE_EVENT does — restore here before the process is torn down.
        _consoleCtrlHandler = OnConsoleCtrl;
        if (!NativeMethods.SetConsoleCtrlHandler(_consoleCtrlHandler, add: true))
        {
            Debug.WriteLine("WharfBar: SetConsoleCtrlHandler failed (no console is fine for WinExe start)");
        }
    }

    private bool OnConsoleCtrl(uint ctrlType)
    {
        switch (ctrlType)
        {
            case NativeMethods.CTRL_CLOSE_EVENT:
            case NativeMethods.CTRL_LOGOFF_EVENT:
            case NativeMethods.CTRL_SHUTDOWN_EVENT:
                RestoreBlocking();
                return true; // restored; system still ends the process shortly
            case NativeMethods.CTRL_C_EVENT:
            case NativeMethods.CTRL_BREAK_EVENT:
                RestoreBlocking();
                return false; // allow default terminate
            default:
                return false;
        }
    }
}
