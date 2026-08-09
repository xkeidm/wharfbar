using System.Diagnostics;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Captures the Windows taskbar baseline on startup, hides it while WharfBar runs,
/// and restores on every exit/crash path (same discipline as AppBar.Remove).
/// </summary>
internal sealed class TaskbarVisibilityService
{
    // Keep alive for SetConsoleCtrlHandler — GC collecting the delegate unregisters the handler.
    private NativeMethods.ConsoleCtrlHandlerRoutine? _consoleCtrlHandler;

    private uint _baselineState;
    private int _captured; // 0 = not captured
    private int _restored; // 0 = not restored
    private int _hooksAttached;
    private bool _hidden;

    internal bool IsHidden => _hidden;

    /// <summary>True while a tray-overflow peek has temporarily shown <c>Shell_TrayWnd</c>.</summary>
    internal bool IsTrayPeekActive { get; private set; }

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
        Interlocked.Exchange(ref _restored, 0);

        AttachShutdownHooks();
        ApplyHide();
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
    /// Temporarily shows the real taskbar so native overflow / Win+B can work.
    /// Pair with <see cref="EndTrayPeek"/>. No-op if we are not in the hidden session.
    /// </summary>
    /// <returns>True when a peek was started (caller must end it).</returns>
    internal bool BeginTrayPeek()
    {
        if (Volatile.Read(ref _captured) == 0 || Volatile.Read(ref _restored) != 0)
        {
            return false;
        }

        if (IsTrayPeekActive)
        {
            TaskbarNative.SetTrayVisible(true);
            return true;
        }

        try
        {
            TaskbarNative.SetTrayVisible(true);
            IsTrayPeekActive = true;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar tray peek show failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Hides the taskbar again after a tray-overflow peek (still respects restore).</summary>
    internal void EndTrayPeek()
    {
        if (!IsTrayPeekActive)
        {
            return;
        }

        IsTrayPeekActive = false;

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

        IsTrayPeekActive = false;

        try
        {
            // Always show after we hid — visibility at capture is unreliable (auto-hide).
            TaskbarNative.SetTrayVisible(true);
            TaskbarNative.SetState(_baselineState);
            _hidden = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar taskbar restore failed: {ex.Message}");
        }
    }

    private void ApplyHide()
    {
        try
        {
            TaskbarNative.SetTrayVisible(false);
            _hidden = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar taskbar hide failed: {ex.Message}");
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
