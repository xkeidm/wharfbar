using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Boş UIA okumasında aynanın donup donmayacağı. Donma "son iyi snapshot'ı tut" demekse
/// de <b>sınırsız</b> olamaz: blip tazelemesi 60 sn'de bir kısıtlı ve pencerelerden biri
/// maximize olduğu hiç çalışmıyor, yani sınırsız tolerans barı kalıcı olarak bayat bırakıyor.
/// </summary>
public class TrayMirrorEmptyReadTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptyReads_WithinGrace_KeepTheHeldSnapshot(int consecutiveEmptyReads)
    {
        Assert.True(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(consecutiveEmptyReads, heldItemCount: 3));
    }

    /// <summary>
    /// O2: sınırsız donmanın kaybolması. Üçüncü boş okumada ayna temizlenir — tıklama artık
    /// kapatılmış bir tray uygulamasının bayat UIA öğesine gitmez.
    /// </summary>
    [Fact]
    public void EmptyRead_PastGrace_ClearsTheMirror()
    {
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(3, heldItemCount: 3));
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(9, heldItemCount: 3));
    }

    [Fact]
    public void EmptyRead_NothingHeld_ReportsEmpty()
    {
        // Hiç iyi snapshot yok: tutulacak bir şey de yok, boş liste zaten doğru.
        Assert.False(TrayMirrorService.ShouldHoldSnapshotOnEmptyRead(1, heldItemCount: 0));
    }
}
