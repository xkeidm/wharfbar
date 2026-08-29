namespace WharfBar.Native;

/// <summary>
/// Circuit breaker for <c>SPI_SETWORKAREA</c>. Windows 11 25H2 (build 26200) rejects every
/// call with <c>ERROR_INVALID_PARAMETER</c> (87) — measured live, all <c>fWinIni</c> flag
/// combinations, taskbar hidden and visible — so the keep-hidden watch must stop hammering
/// a dead API every second. The circuit re-probes after <see cref="ReprobeInterval"/> and
/// <see cref="Reset"/> runs when the shell restarts (<c>TaskbarCreated</c>), so a future OS
/// fix (or older Windows) resumes normal expansion automatically.
/// </summary>
internal sealed class WorkAreaExpandPolicy
{
    /// <summary>Process-wide breaker; SPI calls come from several threads (watch, restore).</summary>
    internal static readonly WorkAreaExpandPolicy Shared = new();

    /// <summary>25H2 rejection signature for <c>SPI_SETWORKAREA</c>.</summary>
    internal const int ErrorInvalidParameter = 87;

    /// <summary>Consecutive 87 failures before the circuit opens.</summary>
    internal const int FailureThreshold = 2;

    /// <summary>How long the circuit stays open before the next single re-probe.</summary>
    internal static readonly TimeSpan ReprobeInterval = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private int _consecutiveInvalidParameter;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;

    /// <summary>True when the SPI call may be attempted at <paramref name="now"/>.</summary>
    internal bool ShouldAttempt(DateTimeOffset now)
    {
        lock (_gate)
        {
            return now >= _openUntil;
        }
    }

    /// <summary>True while attempts are skipped (diagnostics / settings hint).</summary>
    internal bool IsCircuitOpen(DateTimeOffset now)
    {
        lock (_gate)
        {
            return now < _openUntil;
        }
    }

    /// <summary>Feeds one call outcome. Other error codes are treated as transient.</summary>
    internal void RecordResult(bool ok, int win32Error, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (ok)
            {
                _consecutiveInvalidParameter = 0;
                _openUntil = DateTimeOffset.MinValue;
                return;
            }

            if (win32Error != ErrorInvalidParameter)
            {
                return;
            }

            _consecutiveInvalidParameter++;
            if (_consecutiveInvalidParameter >= FailureThreshold)
            {
                _openUntil = now + ReprobeInterval;
                _consecutiveInvalidParameter = 0;
            }
        }
    }

    /// <summary>Shell restart (<c>TaskbarCreated</c>) or manual re-arm.</summary>
    internal void Reset()
    {
        lock (_gate)
        {
            _consecutiveInvalidParameter = 0;
            _openUntil = DateTimeOffset.MinValue;
        }
    }
}
