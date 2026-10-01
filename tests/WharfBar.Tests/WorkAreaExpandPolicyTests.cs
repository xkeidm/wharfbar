using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class WorkAreaExpandPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 29, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldAttempt_Initially_True()
    {
        var policy = new WorkAreaExpandPolicy();
        Assert.True(policy.ShouldAttempt(T0));
        Assert.False(policy.IsCircuitOpen(T0));
    }

    [Fact]
    public void SingleInvalidParameterFailure_KeepsCircuitClosed()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        Assert.True(policy.ShouldAttempt(T0.AddSeconds(1)));
        Assert.False(policy.IsCircuitOpen(T0.AddSeconds(1)));
    }

    [Fact]
    public void ConsecutiveInvalidParameterFailures_OpenCircuit()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0.AddSeconds(1));
        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));
        Assert.False(policy.ShouldAttempt(T0.AddSeconds(2)));
    }

    [Fact]
    public void CircuitReopensAfterReprobeInterval()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0.AddSeconds(1));
        DateTimeOffset afterInterval = T0.AddSeconds(1).Add(WorkAreaExpandPolicy.ReprobeInterval);
        Assert.True(policy.ShouldAttempt(afterInterval));
        Assert.False(policy.IsCircuitOpen(afterInterval));
    }

    [Fact]
    public void OtherErrorCodes_DoNotOpenCircuit()
    {
        var policy = new WorkAreaExpandPolicy();
        for (int i = 0; i < 10; i++)
        {
            policy.RecordResult(false, 5, false, T0.AddSeconds(i));
        }

        Assert.False(policy.IsCircuitOpen(T0.AddMinutes(1)));
    }

    [Fact]
    public void Success_ResetsFailureCountAndCircuit()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0.AddSeconds(1));
        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));

        // A later re-probe (after the interval) succeeded â€” the shell accepts the API again.
        policy.RecordResult(true, 0, true, T0.AddSeconds(2).Add(WorkAreaExpandPolicy.ReprobeInterval));
        Assert.False(policy.IsCircuitOpen(T0.AddMinutes(30)));
        Assert.True(policy.ShouldAttempt(T0.AddMinutes(30)));
    }

    [Fact]
    public void Reset_ClosesCircuitAndClearsCounters()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0.AddSeconds(1));
        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));

        policy.Reset();
        Assert.False(policy.IsCircuitOpen(T0.AddSeconds(2)));
        Assert.True(policy.ShouldAttempt(T0.AddSeconds(2)));
    }

    [Fact]
    public void FailureAfterReprobe_RestartsCountingFromOne()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0.AddSeconds(1));

        // Re-probe after the interval fails again: the count restarts, so the circuit
        // must not open from the stale pre-probe count alone.
        DateTimeOffset reprobe = T0.AddSeconds(1).Add(WorkAreaExpandPolicy.ReprobeInterval);
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, reprobe);
        Assert.False(policy.IsCircuitOpen(reprobe.AddSeconds(1)));

        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, reprobe.AddSeconds(2));
        Assert.True(policy.IsCircuitOpen(reprobe.AddSeconds(3)));
    }

    /// <summary>
    /// 25H2's real signature: the API answers TRUE and leaves the work area exactly where it
    /// was. Keyed on the error code alone this never tripped, so the code kept believing the
    /// strip was being released.
    /// </summary>
    [Fact]
    public void SuccessWithoutEffect_OpensCircuit()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(true, 0, effectApplied: false, T0);
        Assert.False(policy.IsCircuitOpen(T0.AddSeconds(1)));

        policy.RecordResult(true, 0, effectApplied: false, T0.AddSeconds(1));
        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));
        Assert.False(policy.ShouldAttempt(T0.AddSeconds(2)));
    }

    [Fact]
    public void SuccessWithEffect_DoesNotOpenCircuit()
    {
        var policy = new WorkAreaExpandPolicy();
        for (int i = 0; i < 10; i++)
        {
            policy.RecordResult(true, 0, effectApplied: true, T0.AddSeconds(i));
        }

        Assert.False(policy.IsCircuitOpen(T0.AddMinutes(1)));
    }

    [Fact]
    public void SilentNoOp_CountsAsFailureAfterPriorHardError()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        // The next probe "succeeds" but still does nothing — it must complete the threshold.
        policy.RecordResult(true, 0, effectApplied: false, T0.AddSeconds(1));
        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));
    }
}
