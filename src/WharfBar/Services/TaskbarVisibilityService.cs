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
    private bool _stealthPeek;
    private int _expandingWorkArea;
    private int _pendingBroadcastExpand;
    private int _watchBusy;
    private readonly object _reservationLock = new();
    private readonly Dictionary<string, int> _reservedBottomByMonitor = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _keepHiddenTimer;
    private long _lastWorkAreaCheckMs = -1;

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

        AttachShutdownHooks();
        // First announcement must reach already-maximized windows (broadcast).
        ApplyHide(broadcast: true);
        StartKeepHiddenWatch();
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
            app.Exit += (_, _) => Restore();
            app.SessionEnding += (_, _) => Restore();
            app.DispatcherUnhandledException += (_, _) => Restore();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Restore();

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
                Restore();
                return true; // restored; system still ends the process shortly
            case NativeMethods.CTRL_C_EVENT:
            case NativeMethods.CTRL_BREAK_EVENT:
                Restore();
                return false; // allow default terminate
            default:
                return false;
        }
    }
}
