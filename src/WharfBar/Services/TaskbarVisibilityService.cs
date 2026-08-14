using System.Diagnostics;
using System.Windows.Threading;
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
    private int _peekDepth;
    private bool _stealthPeek;
    private int _expandingWorkArea;
    private DispatcherTimer? _keepHiddenTimer;
    private long _lastWorkAreaCheckMs = -1;

    internal bool IsHidden => _hidden;

    /// <summary>True while a tray-overflow / mirror peek has temporarily shown <c>Shell_TrayWnd</c>.</summary>
    internal bool IsTrayPeekActive => Volatile.Read(ref _peekDepth) > 0;

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
        ApplyHide();
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
    /// Re-expands the primary work-area bottom after AppBar <c>SETPOS</c> (shell may restore
    /// the phantom taskbar strip). No-op when not hidden.
    /// </summary>
    internal void EnsureWorkAreaExpanded()
    {
        if (!_hidden || Volatile.Read(ref _restored) != 0 || Volatile.Read(ref _captured) == 0)
        {
            return;
        }

        ExpandWorkAreaBottom();
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
            Interlocked.Increment(ref _peekDepth);
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
                TaskbarNative.SetTrayVisibleStealth();
                _stealthPeek = true;
            }
            else
            {
                TaskbarNative.SetTrayVisible(true);
                _stealthPeek = false;
            }

            Interlocked.Exchange(ref _peekDepth, 1);
            // Keep maximized apps from jumping when the shell reclaims the bottom strip.
            ExpandWorkAreaBottom();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar tray peek show failed: {ex.Message}");
            Interlocked.Exchange(ref _peekDepth, 0);
            _stealthPeek = false;
            return false;
        }
    }

    /// <summary>Hides the taskbar again after a tray peek (still respects restore). Refcounted.</summary>
    internal void EndTrayPeek()
    {
        if (!IsTrayPeekActive)
        {
            return;
        }

        if (Interlocked.Decrement(ref _peekDepth) > 0)
        {
            return;
        }

        Interlocked.Exchange(ref _peekDepth, 0);
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

        Interlocked.Exchange(ref _peekDepth, 0);
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

    private void ApplyHide()
    {
        if (Volatile.Read(ref _restored) != 0)
        {
            return;
        }

        try
        {
            bool trayWasVisible = TaskbarNative.IsAnyTrayVisible();
            if (trayWasVisible)
            {
                TaskbarNative.SetTrayVisible(false);
            }

            _hidden = true;
            ExpandWorkAreaBottom();

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

        Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _keepHiddenTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(AppBarPlacement.KeepHiddenTrayPollMs),
        };
        _keepHiddenTimer.Tick += OnKeepHiddenTick;
        _keepHiddenTimer.Start();
    }

    private void StopKeepHiddenWatch()
    {
        DispatcherTimer? timer = _keepHiddenTimer;
        if (timer is null)
        {
            return;
        }

        _keepHiddenTimer = null;
        timer.Tick -= OnKeepHiddenTick;
        _lastWorkAreaCheckMs = -1;
        try
        {
            timer.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar keep-hidden timer stop failed: {ex.Message}");
        }
    }

    private void OnKeepHiddenTick(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _captured) == 0 || Volatile.Read(ref _restored) != 0)
        {
            return;
        }

        if (IsTrayPeekActive)
        {
            return;
        }

        try
        {
            if (TaskbarNative.IsAnyTrayVisible())
            {
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
    }

    private void ExpandWorkAreaBottom()
    {
        if (Interlocked.Exchange(ref _expandingWorkArea, 1) != 0)
        {
            return;
        }

        try
        {
            // SPI_SETWORKAREA with SPIF_SENDCHANGE broadcasts to every top-level window
            // and can block the caller for ~1s on some systems. Run it off the UI thread;
            // keep the guard so at most one expansion is in flight.
            ThreadPool.QueueUserWorkItem(RunExpandWorkAreaBottom);
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
            bool restoreRaced = Volatile.Read(ref _restored) != 0;
            if (!restoreRaced && TaskbarNative.TryExpandPrimaryWorkAreaBottom())
            {
                return;
            }

            // Restore may have run while the expansion was queued or in flight; undo it so
            // exit never leaves the work area stuck expanded past the baseline.
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
