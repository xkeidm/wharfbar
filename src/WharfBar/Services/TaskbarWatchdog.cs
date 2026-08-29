using System.Diagnostics;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Watchdog mode (<c>WharfBar.exe --taskbar-watchdog</code>): watches the main process
/// from outside and restores the Windows taskbar if it dies without restoring. Needed
/// because corrupting-state exceptions (AccessViolationException) terminate the process
/// without running any managed handler, so in-process hooks cannot fire.
/// Runs without initializing WPF — no windows, no tray icon, exits on its own once the
/// state file is gone (parent restored normally) or after it has performed a restore.
/// </summary>
internal static class TaskbarWatchdog
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Restore decision, pure so PID reuse stays unit-testable: restore only when the
    /// dead process is the exact generation we captured (same pid and start time).
    /// </summary>
    internal static bool ShouldRestore(TaskbarWatchdogState? state, int pid, DateTime processStartTime)
    {
        if (state is null)
        {
            return false;
        }

        if (state.Pid != pid)
        {
            return false;
        }

        return Math.Abs((state.ProcessStartTime - processStartTime).TotalSeconds) < 1.0;
    }

    /// <summary>Watchdog main loop; never throws, always returns.</summary>
    internal static void Run()
    {
        try
        {
            RunCore();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar watchdog failed: {ex.Message}");
        }
    }

    private static void RunCore()
    {
        var store = new TaskbarWatchdogStateStore(DefaultDirectory());
        TaskbarWatchdogState? state = store.Load();
        if (state is null)
        {
            return;
        }

        int pid = state.Pid;
        Process? process = TryGetProcess(pid, state.ProcessStartTime);
        if (process is null)
        {
            // Parent is already gone (watchdog started late). Restore if the file is
            // still ours; a rewritten file belongs to a newer WharfBar generation.
            RestoreIfStillOurs(store, pid, state.ProcessStartTime);
            return;
        }

        while (true)
        {
            process.WaitForExit(PollInterval);

            if (!store.Exists())
            {
                // Parent restored normally and deleted the file — nothing to do.
                return;
            }

            if (!process.HasExited)
            {
                continue;
            }

            RestoreIfStillOurs(store, pid, state.ProcessStartTime);
            return;
        }
    }

    private static void RestoreIfStillOurs(TaskbarWatchdogStateStore store, int pid, DateTime startTime)
    {
        TaskbarWatchdogState? current = store.Load();
        if (!ShouldRestore(current, pid, startTime))
        {
            return;
        }

        try
        {
            TaskbarNative.SetTrayVisible(true);
            TaskbarNative.SetState(current!.BaselineState);
            if (current.HaveWorkArea)
            {
                _ = TaskbarNative.TrySetPrimaryWorkArea(new RECT
                {
                    Left = current.WorkAreaLeft,
                    Top = current.WorkAreaTop,
                    Right = current.WorkAreaRight,
                    Bottom = current.WorkAreaBottom,
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar watchdog restore failed: {ex.Message}");
        }
        finally
        {
            store.Delete();
        }
    }

    private static Process? TryGetProcess(int pid, DateTime expectedStart)
    {
        try
        {
            Process process = Process.GetProcessById(pid);
            if (Math.Abs((process.StartTime - expectedStart).TotalSeconds) >= 1.0)
            {
                return null; // PID reused by another process generation.
            }

            return process;
        }
        catch (Exception ex) when (ex is ArgumentException or SystemException)
        {
            return null; // Process already gone.
        }
    }

    internal static string DefaultDirectory()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return System.IO.Path.Combine(appData, "WharfBar");
    }
}
