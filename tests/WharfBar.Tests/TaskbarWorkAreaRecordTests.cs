using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// <c>TaskbarNative.RecordSetWorkAreaResult</c>'ın verdiği hüküm ve devre kesicisini besleme
/// kararı. Gerçek Win32 turu test edilemez, ama hükümün kendisi ve "hangi çağrı devre
/// kesicisini açabilir" kuralı tamamen saftır.
/// </summary>
public class TaskbarWorkAreaRecordTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Gerçek bir okuma asla üretmeyecek bir alt kenar: test ortamına bağımsız.</summary>
    private const int MismatchedBottom = -12345;

    [Fact]
    public void EffectApplied_RequiresApiSuccessAndMatchingReadBack()
    {
        var policy = new WorkAreaExpandPolicy();

        Assert.True(TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: 1080, expectedBottom: 1080,
            feedsBreaker: true, now: T0, policy: policy));

        // 25H2'nin imzası: TRUE döndü, iş bir şey yapmadı.
        Assert.False(TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: 1032, expectedBottom: 1080,
            feedsBreaker: true, now: T0, policy: policy));

        // Okuma tamamen başarısız: "etki uygulandı" denemez.
        Assert.False(TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: false, readBackBottom: 0, expectedBottom: 0,
            feedsBreaker: true, now: T0, policy: policy));

        // API reddetti: doğru okuma bile etkiyi kanıtlamaz.
        Assert.False(TaskbarNative.RecordSetWorkAreaResult(
            ok: false, error: WorkAreaExpandPolicy.ErrorInvalidParameter,
            readBackOk: true, readBackBottom: 1080, expectedBottom: 1080,
            feedsBreaker: true, now: T0, policy: policy));
    }

    /// <summary>
    /// O1'in çekirdeği: ikincil monitör genişletmesinin doğrulanamayan sonucu devre kesicisini
    /// AÇMAMALI. Devre açıkken <c>TrySetPrimaryWorkArea</c> erken çıkıp çıkıştaki work-area
    /// geri yüklemesini düşürür — <c>boundaries.md</c> "Asla" listesi.
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
    /// Aynı girdiler, bu kez devreyi besleyen çağrı: yukarıdaki testin tek farkı budur, yani
    /// ayrım gerçekten <c>feedsBreaker</c>'dan geliyor (koruma tesadüf değil).
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

    /// <summary>Beslenmeyen bir çağrı, önceden açık devreyi de kapatmamalı (sessiz geçersiz kılma).</summary>
    [Fact]
    public void NonBreakerFeedingCall_DoesNotCloseAnOpenCircuit()
    {
        var policy = new WorkAreaExpandPolicy();
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0);
        policy.RecordResult(false, WorkAreaExpandPolicy.ErrorInvalidParameter, false, T0.AddSeconds(1));
        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));

        // İkincil monitör genişlemesi başarılı olsa bile devre kapanmaz: yalnız birincil
        // monitörün gözlemi devrenin durumunu belirler.
        TaskbarNative.RecordSetWorkAreaResult(
            ok: true, error: 0, readBackOk: true, readBackBottom: 1080, expectedBottom: 1080,
            feedsBreaker: false, now: T0.AddSeconds(2), policy: policy);

        Assert.True(policy.IsCircuitOpen(T0.AddSeconds(2)));
    }

    /// <summary>Doğrulanmış etki, besleyen çağrıda başarı sayılır (devre sıfırlanır).</summary>
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
