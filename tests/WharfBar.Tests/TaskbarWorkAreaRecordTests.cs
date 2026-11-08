using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The verdict returned by <c>TaskbarNative.RecordSetWorkAreaResult</c> and the decision to feed
/// the circuit breaker. The real Win32 round trip cannot be tested, but the verdict itself and
/// the rule for "which call may open the circuit breaker" are entirely pure.
/// </summary>
public class TaskbarWorkAreaRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A bottom edge that a real read would never produce: independent of the test environment.</summary>
    private const int MismatchedBottom = -12345;

    [Fact]
    public void EffectApplied_RequiresApiSuccessAndMatchingReadBack()
    {
        var policy = new WorkAreaExpandPolicy();

        Assert.True(TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: 1080, expectedBottom: 1080,
            feedsBreaker: true, now: T0, policy: policy));

        // The 25H2 signature: it returned TRUE but did nothing.
        Assert.False(TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: 1032, expectedBottom: 1080,
            feedsBreaker: true, now: T0, policy: policy));

        // The read failed entirely: we cannot claim "the effect was applied".
        Assert.False(TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: false, readBackBottom: 0, expectedBottom: 0,
            feedsBreaker: true, now: T0, policy: policy));

        // The API refused: even a correct read does not prove the effect.
        Assert.False(TaskbarNative.RecordSetWorkAreaResult(
            ok: false, error: WorkAreaExpandPolicy.ErrorInvalidParameter,
            readBackOk: true, readBackBottom: 1080, expectedBottom: 1080,
            feedsBreaker: true, now: T0, policy: policy));
    }

    /// <summary>
    /// The core of O1: the unverifiable result of a secondary monitor expansion must NOT open the
    /// circuit breaker. With the circuit open <c>TrySetPrimaryWorkArea</c> returns early and skips the
    /// work-area restore on exit — the "Never" list in <c>boundaries.md</c>.
    /// </summary>
    [Fact]
    public void NonBreakerFeedingCall_NeverOpensTheCircuit()
    {
        var policy = new WorkAreaExpandPolicy();

        for (int i = 0; i < 20; i++)
        {
            Assert.False(TaskbarNative.RecordSetWorkAreaResult(
                ok: true, error: 0, readBackOk: true, readBackBottom: MismatchedBottom,
                expectedBottom: 1080, feedsBreaker: false, now: T0.AddSeconds(i), policy: policy));
        }

        Assert.False(policy.IsCircuitOpen(T0.AddMinutes(1)));
        Assert.True(policy.ShouldAttempt(T0.AddMinutes(1)));
    }

    /// <summary>
    /// The same inputs, but this time a call that feeds the breaker: that is the only difference
    /// from the test above, so the distinction really comes from <c>feedsBreaker</c> (the protection is not a coincidence).
    /// </summary>
    [Fact]
    public void BreakerFeedingCall_WithUnverifiedEffect_OpensTheCircuit()
    {
        var policy = new WorkAreaExpandPolicy();

        TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: MismatchedBottom,
            expectedBottom: 1080, feedsBreaker: true, now: T0, policy: policy);
        Assert.False(policy.IsCircuitOpen(T0.AddSeconds(1)));

        TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: MismatchedBottom,
            expectedBottom: 1080, feedsBreaker: true, now: T0.AddSeconds(1), policy: policy);
        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));
    }

    /// <summary>A call that does not feed the breaker must not close an already open circuit either (silent override).</summary>
    [Fact]
    public void NonBreakerFeedingCall_DoesNotCloseAnOpenCircuit()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0.AddSeconds(1));
        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));

        // Even if the secondary monitor expansion succeeds the circuit does not close: only the primary
        // monitor's observation determines the circuit state.
        TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: 1080, expectedBottom: 1080,
            feedsBreaker: false, now: T0.AddSeconds(2), policy: policy);

        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));
    }

    /// <summary>A verified effect counts as success on a feeding call (the circuit is reset).</summary>
    [Fact]
    public void BreakerFeedingCall_WithVerifiedEffect_KeepsCircuitClosed()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);

        Assert.True(TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: 1080, expectedBottom: 1080,
            feedsBreaker: true, now: T0.AddSeconds(1), policy: policy));

        Assert.False(policy.IsCircuitOpen(T0.AddSeconds(2)));
    }
}
