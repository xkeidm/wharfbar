using System.Diagnostics;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Captures the Windows taskbar baseline on startup, hides it while WharfBar runs,
/// and restores on every exit/crash path (same discipline as AppBar.Remove).
/// </summary>
internal sealed class TaskbarVisibilityService
{
    private uint _baselineState;
    private int _captured; // 0 = not captured
    private int _restored; // 0 = not restored
    private int _hooksAttached;
    private bool _hidden;

    internal bool IsHidden => _hidden;

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
    }
}
