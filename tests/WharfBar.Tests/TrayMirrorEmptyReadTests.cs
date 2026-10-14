using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Boş UIA okumasında aynanın ne yapacağı. Donma "son iyi snapshot'ı tut" demekse de
/// <b>sınırlı</b> olmalı (blip tazelemesi 60 sn'de bir kısıtlı ve pencerelerden biri
/// maximize olduğu hiç çalışmıyor; sınırsız tolerans barı kalıcı bayat bırakıyor) — ama
/// <b>bir blip aralığını da aşmamalı</b>. Eşiğin ikisini birden yapması gerekiyor: normal
/// park/throttle boşluğunda ikonlar kaybolmamalı, gerçekten boş tray ise makul sürede
/// temizlenmeli. Saf mantık; Win32 yok.
/// </summary>
public class TrayMirrorEmptyReadTests
{
    /// <summary>Bir blip aralığına düşen okuma sayısı — grace'ın aşması gereken asgari pencere.</summary>
    private static int PollsPerBlipInterval =>
        (int)Math.Ceiling(TrayMirrorService.StateBlipMinInterval.TotalMilliseconds / TrayMirrorService.PollInterval.TotalMilliseconds);

    /// <summary>
    /// Regresyonun asıl kilidi: grace penceresi bir blip aralığından <b>uzun</b> olmalı
    /// (<c>3 × 1.5 sn = 4.5 sn</c> iken ikonlar 60 sn'lik boşluğun %7.5'inde görünür
    /// kalıyordu) ama "bayat kalıcı donma" tarafına da kaymamalı — iki okumalık toleransı
    /// aşan bir değer yalnız ölü ikon penceresini uzatır.
    /// </summary>
    [Fact]
    public void GraceWindow_CoversOneWholeBlipInterval_PlusTwoPolls()
    {
        TimeSpan grace = TrayMirrorService.PollInterval * TrayMirrorService.EmptyReadGraceReads;

        Assert.True(
            grace > TrayMirrorService.StateBlipMinInterval,
            $"grace {grace.TotalSeconds}s, bir state-blip aralığı ({TrayMirrorService.StateBlipMinInterval.TotalSeconds}s) boyunca sürmüyor — ayna her throttle boşluğunun ortasında temizlenir");
        Assert.True(
            grace <= TrayMirrorService.StateBlipMinInterval + (2 * TrayMirrorService.PollInterval),
            $"grace {grace.TotalSeconds}s, iki okumalık toleransı aşıyor — sınırsız donma yönünde kayıyor");
    }

    /// <summary>
    /// Ölçülen regresyonun birinci kriteri: park halinde doğrudan okuma <b>her</b> poll'da
    /// boş döner ve blip throttle edildiği için hiç kanıt gelmez. Bu boşluk boyunca ayna
    /// hiçbir zaman temizlenmemeli.
    /// </summary>
    [Fact]
    public void ThrottleGap_UpToOneBlipInterval_NeverBlanksTheMirror()
    {
        for (int poll = 1; poll <= PollsPerBlipInterval; poll++)
        {
            Assert.True(
                TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(
                    consecutiveEmptyReads: poll,
                    heldItemCount: 3,
                    trayConfirmedEmptyByBlip: false),
                $"t={poll * TrayMirrorService.PollInterval.TotalSeconds:F1}s: mirror blip aralığı dolmadan temizlendi");
        }
    }

    /// <summary>
    /// O2'nin asıl amacı korunuyor: grace tükenince ayna temizlenir, tıklama kapatılmış bir
    /// tray uygulamasının bayat UIA öğesine gitmez.
    /// </summary>
    [Fact]
    public void GraceEnd_ClearsTheMirror_WhenNoBlipEverConfirmed()
    {
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(
            TrayMirrorService.EmptyReadGraceReads - 1, heldItemCount: 3, trayConfirmedEmptyByBlip: false));
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(
            TrayMirrorService.EmptyReadGraceReads, heldItemCount: 3, trayConfirmedEmptyByBlip: false));
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(
            TrayMirrorService.EmptyReadGraceReads + 40, heldItemCount: 3, trayConfirmedEmptyByBlip: false));
    }

    /// <summary>
    /// "Taze blip de boş döndü" ayrı kanıt: tray gösterildi, yine de boş okundu. Grace
    /// harcanmaz — kullanıcı gerçekten her şeyi kapatmışsa birkaç saniyede temizlenir.
    /// </summary>
    [Fact]
    public void ConfirmedEmptyBlip_ClearsTheMirror_OnTheVeryFirstEmptyRead()
    {
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(
            consecutiveEmptyReads: 1, heldItemCount: 3, trayConfirmedEmptyByBlip: true));
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(
            consecutiveEmptyReads: 5, heldItemCount: 1, trayConfirmedEmptyByBlip: true));
    }

    [Fact]
    public void EmptyRead_NothingHeld_ReportsEmpty()
    {
        // Hiç iyi snapshot yok: tutulacak bir şey de yok, boş liste zaten doğru.
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(1, heldItemCount: 0, trayConfirmedEmptyByBlip: false));
    }

    /// <summary>
    /// Grace'in dayandığı ayrımın kaynağı: yalnız <b>gösterilmiş</b> bir tray'in boş
    /// okunması kanıttır. Throttle, tıklamada ya da maximize pencere yüzünden denenmeyen
    /// blip ve açılmadı/istisna veren peek kanıt DEĞİLDİR — bu yüzden grace sürer.
    /// </summary>
    [Theory]
    // Tray hiç gösterilmedi (peek açılmadı).
    [InlineData(false, false, 0)]
    // Tray gösterildi ama UIA gezintisi patladı — hiçbir şey okunmadı.
    [InlineData(true, true, 0)]
    [InlineData(true, true, 7)]
    public void BlipThatNeverReadTheTray_IsNotEvidence(bool trayShown, bool readWalkFailed, int readCount)
    {
        TrayMirrorService.TrayBlipOutcome outcome =
            TrayMirrorService.ClassifyBlipRead(trayShown, readWalkFailed, readCount);

        Assert.Equal(TrayMirrorService.TrayBlipOutcome.Inconclusive, outcome);
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(
            consecutiveEmptyReads: 1, heldItemCount: 3, trayConfirmedEmptyByBlip: false));
    }

    [Fact]
    public void BlipOverAShownTray_ReadsEmpty_ConfirmsTheTrayIsEmpty()
    {
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty,
            TrayMirrorService.ClassifyBlipRead(trayShown: true, readWalkFailed: false, readCount: 0));
    }

    [Fact]
    public void BlipOverAShownTray_WithButtons_IsARefresh()
    {
        Assert.Equal(
            TrayMirrorService.TrayBlipOutcome.Refreshed,
            TrayMirrorService.ClassifyBlipRead(trayShown: true, readWalkFailed: false, readCount: 3));
    }

    /// <summary>
    /// Engel (maximize pencere / throttle) bir blip'i <b>deneceği</b> anlamına gelmez; bu
    /// yüzden grace yalnız bu sonucu sayar. Değer farkı yanlışlıkla düşerse (throttle
    /// edilmiş bir deneme kanıt sayılırsa) ayna yine de 60 sn'de bir boşalır.
    /// </summary>
    [Fact]
    public void SkippedBlip_IsDistinguishableFromEvidence()
    {
        Assert.NotEqual(TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty, TrayMirrorService.TrayBlipOutcome.Skipped);
        Assert.NotEqual(TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty, TrayMirrorService.TrayBlipOutcome.Inconclusive);
    }

    [Fact]
    public void ShowOnly_IsTriedUntilAProbeFails()
    {
        Assert.True(TrayMirrorService.ShouldTryShowOnly(null));
        Assert.True(TrayMirrorService.ShouldTryShowOnly(true));
        Assert.False(TrayMirrorService.ShouldTryShowOnly(false));
    }

    [Fact]
    public void ShowOnly_SuccessSticks_AndAMissBeforeSuccessLatchesTheStateBlip()
    {
        Assert.True(TrayMirrorService.LatchShowOnly(null, foundIcons: true));
        Assert.True(TrayMirrorService.LatchShowOnly(true, foundIcons: false));
        Assert.False(TrayMirrorService.LatchShowOnly(null, foundIcons: false));
        Assert.False(TrayMirrorService.LatchShowOnly(false, foundIcons: true));
    }
}