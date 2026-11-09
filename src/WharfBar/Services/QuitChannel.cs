using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WharfBar.Services;

/// <summary>
/// Cross-process quit channel behind <c>WharfBar.exe --quit</c>.
/// </summary>
/// <remarks>
/// An installer or updater must never force-kill WharfBar: a hard kill skips the AppBar
/// <c>ABM_REMOVE</c> and leaves the reserved strip on screen until Explorer restarts (see
/// docs/04-gotchas.md). <c>--quit</c> instead asks the running instance to shut down through the
/// same path as the tray Quit item — taskbar restore + AppBar removal — and waits until that
/// instance has released the single-instance mutex, which is released last in
/// <see cref="App"/>'s exit path.
/// </remarks>
internal static class QuitChannel
{
    // Local\ scopes the channel to the interactive session, matching SingleInstanceGuard.
    private const string EventName = @"Local\WharfBar.Quit.{7B0D2B5E-3F0A-4F6E-9A63-2C6D5A1E8B44}";

    private static readonly TimeSpan QuitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Running-instance side: creates the signal event and invokes <paramref name="onQuit"/> on a
    /// thread-pool thread when another process asks for a quit. Never throws; null means the
    /// channel could not be created and the app still runs (only <c>--quit</c> is unavailable).
    /// </summary>
    internal static IDisposable? Listen(Action onQuit)
    {
        EventWaitHandle? signal = null;
        try
        {
            signal = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                EventName);

            RegisteredWaitHandle registration = ThreadPool.RegisterWaitForSingleObject(
                signal,
                (_, _) => onQuit(),
                state: null,
                Timeout.Infinite,
                executeOnlyOnce: true);

            return new Listener(signal, registration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            signal?.Dispose();
            Debug.WriteLine($"[WharfBar] quit channel unavailable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// <c>--quit</c> side. Returns <c>0</c> when no instance is running or the running instance
    /// quit, <c>1</c> when an instance is running but did not quit (an older build without
    /// <c>--quit</c>, or a quit that timed out), and <c>2</c> on an unexpected error.
    /// </summary>
    internal static int RequestQuit()
    {
        try
        {
            if (!Mutex.TryOpenExisting(SingleInstanceGuard.MutexName, out Mutex? mutex))
            {
                return 0; // No instance is running: nothing to quit.
            }

            using (mutex)
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(EventName);
                    signal.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    // A WharfBar older than this command line is running and cannot be asked.
                    return 1;
                }

                try
                {
                    if (!mutex.WaitOne(QuitTimeout))
                    {
                        return 1; // Still running after the timeout.
                    }

                    mutex.ReleaseMutex();
                    return 0;
                }
                catch (AbandonedMutexException)
                {
                    // The instance died while quitting. Restoring its shell state is the
                    // watchdog's job; from an installer's point of view it is gone.
                    return 0;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine($"[WharfBar] quit request failed: {ex.Message}");
            return 2;
        }
    }

    private sealed class Listener : IDisposable
    {
        private readonly EventWaitHandle _signal;
        private readonly RegisteredWaitHandle _registration;

        internal Listener(EventWaitHandle signal, RegisteredWaitHandle registration)
        {
            _signal = signal;
            _registration = registration;
        }

        public void Dispose()
        {
            // Unregister first: the callback never touches the event, so the order only
            // avoids a disposed handle still being watched.
            _registration.Unregister(null);
            _signal.Dispose();
        }
    }
}
