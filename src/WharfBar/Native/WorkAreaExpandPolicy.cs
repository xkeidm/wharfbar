namespace WharfBar.Native;

/// <summary>
/// Circuit breaker for the <c>SPI_SETWORKAREA</c> backup path. That API is dead on Windows 11
/// 25H2 (build 26200, measured live): every call returns <c>TRUE</c> and changes nothing, and
/// only some call shapes report <c>ERROR_INVALID_PARAMETER</c> (87). Keying the breaker on the
/// error code alone therefore never trips — the shell kept "succeeding" and the code believed
/// the work area was expanding. The breaker counts a call as failed when it either errors with
/// 87 <b>or</b> reports success without moving the work area (see <see cref="RecordResult"/>).
///
/// This is only the backup path. The bottom strip is actually released by
/// <c>ABM_SETSTATE(ABS_AUTOHIDE)</c> on the registered appbar (ADR-005 note 5), which parks the
/// taskbar at its reveal sliver and drops the reservation — so a dead SPI costs nothing on 25H2
/// as long as the auto-hide state holds.
///
/// Note: <c>ABM_SETSTATE</c> only takes effect when the caller is a registered appbar. Probing it
/// from an unregistered process returns success and changes nothing — do not "measure" it that
/// way (this mistake was made on 2026-09-30).
///
/// The keep-hidden watch must stop hammering a dead API. The circuit re-probes after
/// <see cref="ReprobeInterval"/> and <see cref="Reset"/> runs when the shell restarts
/// (<c>TaskbarCreated</c>), so a future OS fix (or older Windows) resumes normal expansion
/// automatically.
/// </summary>
internal sealed class WorkAreaExpandPolicy
{
    /// <summary>Process-wide breaker; SPI calls come from several threads (watch, restore).</summary>
    internal static readonly WorkAreaExpandPolicy Shared = new();

    /// <summary>25H2 rejection signature for <c>SPI_SETWORKAREA</c>.</summary>
    internal const int ErrorInvalidParameter = 87;

    /// <summary>Consecutive dead-API results before the circuit opens.</summary>
    internal const int FailureThreshold = 2;

    /// <summary>How long the circuit stays open before the next single re-probe.</summary>
    internal static readonly TimeSpan ReprobeInterval = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private int _consecutiveFailures;
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

    /// <summary>
    /// Feeds one call outcome. <paramref name="ok"/> is the API's own return value and
    /// <paramref name="effectApplied"/> is whether the work area actually moved. 25H2 does both
    /// at once: <c>ok=true</c> with <c>effectApplied=false</c> is still a dead API. Other error
    /// codes are treated as transient.
    /// </summary>
    internal void RecordResult(bool ok, int win32Error, bool effectApplied, DateTimeOffset now)
    {
        lock (_gate)
        {
            bool rejected = !ok && win32Error == ErrorInvalidParameter;
            bool silentNoOp = ok && !effectApplied;
            if (!rejected && !silentNoOp)
            {
                _consecutiveFailures = 0;
                _openUntil = DateTimeOffset.MinValue;
                return;
            }

            _consecutiveFailures++;
            if (_consecutiveFailures >= FailureThreshold)
            {
                _openUntil = now + ReprobeInterval;
                _consecutiveFailures = 0;
            }
        }
    }

    /// <summary>Shell restart (<c>TaskbarCreated</c>) or manual re-arm.</summary>
    internal void Reset()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openUntil = DateTimeOffset.MinValue;
        }
    }
}
