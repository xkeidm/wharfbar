namespace WharfBar.Services;

/// <summary>
/// Named-mutex single-instance guard. A second instance would register a second AppBar and
/// reserve the work area twice, so startup is refused instead.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    // Local\ scopes the mutex to the interactive session, which is what we want:
    // one bar per logged-on user, not one per machine.
    private const string MutexName = @"Local\WharfBar.SingleInstance.{0F4C6A1E-9C4B-4C39-9E1B-6B4E2E1D7A31}";

    private readonly Mutex _mutex;
    private bool _isDisposed;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns null when another instance already owns the mutex.</summary>
    internal static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        if (createdNew)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread (shutdown raced onto another thread); disposing still frees the handle.
        }

        _mutex.Dispose();
    }
}
