using System;
using System.IO;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Bırakma kopyası yollarının güvenlik sınırı. <c>island.json</c> elle düzenlenebildiği için
/// <c>FilePath</c> güvenilmezdir; <c>Drop</c> dışındaki hiçbir yol silinmemelidir.
/// Açma ayrı kapıdadır (<c>CanOpenShelfFile</c>): <c>Drop</c> öneki aranmaz, eksik yol, aygıt
/// yolu, reparse noktalı klasör ve junction kaçışı reddedilir; gerçek dosya ve gerçek klasör
/// kabul edilir (raf klasör de barındırır). Testler gerçek dosya sistemi
/// üzerinde çalışır, çünkü asıl tehlike
/// (junction'ın Drop dışına taşıması) yalnız diskte görünür.
/// </summary>
public sealed class IslandDropPathGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "WharfBarTests", "dropguard-" + Guid.NewGuid().ToString("N"));

    private string DropRoot => Path.Combine(_root, "Drop");

    private string Prefix => DropRoot + Path.DirectorySeparatorChar;

    public IslandDropPathGuardTests()
    {
        Directory.CreateDirectory(DropRoot);
    }

    public void Dispose()
    {
        try
        {
            // Junctions silinirken hedefleri takip edebilir; yalnız kendi ağacımızı temizle.
            foreach (string dir in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(dir);
                }
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Geçici klasör; temizlik başarısız olursa testin sonucu etkilenmez.
        }
    }

    private string WriteFile(string relative, string content)
    {
        string full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void IsInsideDropPrefix_AcceptsAFileUnderDrop()
    {
        Assert.True(IslandService.IsInsideDropPrefix(
            Path.Combine(DropRoot, "a.txt"), Prefix, out string full));
        Assert.Equal(Path.Combine(DropRoot, "a.txt"), full);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsInsideDropPrefix_RejectsEmptyInput(string? path)
    {
        Assert.False(IslandService.IsInsideDropPrefix(path, Prefix, out string full));
        Assert.Equal(string.Empty, full);
    }

    /// <summary>Kardeş klasör: "…\DropYedek" "…\Drop" altında sayılmamalı (ayırıcı şartı).</summary>
    [Fact]
    public void IsInsideDropPrefix_RejectsSiblingFolderWithDropPrefix()
    {
        string sibling = Path.Combine(_root, "DropYedek", "a.txt");
        Assert.False(IslandService.IsInsideDropPrefix(sibling, Prefix, out _));
    }

    [Fact]
    public void IsInsideDropPrefix_RejectsAnythingOutsideDrop()
    {
        string outside = WriteFile(Path.Combine("Documents", "tehlikeli.docx"), "user data");
        Assert.False(IslandService.IsInsideDropPrefix(outside, Prefix, out _));
    }

    [Fact]
    public void IsInsideDropPrefix_RejectsTraversalOutOfDrop()
    {
        string escape = Path.Combine(DropRoot, "..", "Documents", "tehlikeli.docx");
        Assert.False(IslandService.IsInsideDropPrefix(escape, Prefix, out _));
    }

    [Fact]
    public void AllDirectoryComponentsAreReal_True_ForPlainSubfolders()
    {
        Directory.CreateDirectory(Path.Combine(DropRoot, "2026", "10"));
        string file = Path.Combine(DropRoot, "2026", "10", "rapor.docx");
        File.WriteAllText(file, "x");

        Assert.True(IslandService.AllDirectoryComponentsAreReal(Prefix, file, out string? offender));
        Assert.Null(offender);
    }

    /// <summary>Drop'ın kendisi bir bileşen sayılmaz — kullanıcı oraya bağ koymamış olabilir.</summary>
    [Fact]
    public void AllDirectoryComponentsAreReal_IgnoresTheRootItself()
    {
        string file = Path.Combine(DropRoot, "a.txt");
        File.WriteAllText(file, "x");

        Assert.True(IslandService.AllDirectoryComponentsAreReal(Prefix, file, out _));
    }

    /// <summary>
    /// O3'in çekirdeği: <c>Drop\baglanti\</c> bir junction ise yol ön ek testini geçer, ama
    /// <c>File.Delete</c> onu izler ve Drop DIŞINDAKİ dosyayı geri dönüşsüz siler. Tarama yolu
    /// (<c>DeleteDropFolderContents</c>) zaten <c>AttributesToSkip = … | ReparsePoint</c> ile
    /// bunu engelliyor; kayıt yolunun aynı korumaya sahip olması gerekiyor.
    /// </summary>
    [Fact]
    public void AllDirectoryComponentsAreReal_False_ForJunctionInsideDrop()
    {
        string outside = Path.Combine(_root, "Outside");
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "tehlikeli.docx");
        File.WriteAllText(victim, "important");

        string link = Path.Combine(DropRoot, "baglanti");
        Assert.True(CreateJunction(link, outside), "mklink /J başarısız — koruma test edilemiyor");

        string throughLink = Path.Combine(link, "tehlikeli.docx");
        Assert.True(File.Exists(throughLink), "junction hedefe ulaşmıyor");

        // Ön ek kontrolü bu yolu "Drop altında" sayar — yani tek başına yetersiz.
        Assert.True(IslandService.IsInsideDropPrefix(throughLink, Prefix, out _));

        Assert.False(IslandService.AllDirectoryComponentsAreReal(Prefix, throughLink, out string? offender));
        Assert.Equal(link, offender);
        Assert.True(File.Exists(victim));
    }

    [Fact]
    public void AllDirectoryComponentsAreReal_False_ForJunctionDeeperInDrop()
    {
        string outside = Path.Combine(_root, "Outside2");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "v.txt"), "important");

        string nested = Path.Combine(DropRoot, "2026");
        Directory.CreateDirectory(nested);
        string link = Path.Combine(nested, "baglanti");
        Assert.True(CreateJunction(link, outside), "mklink /J başarısız");

        string throughLink = Path.Combine(link, "v.txt");
        Assert.False(IslandService.AllDirectoryComponentsAreReal(Prefix, throughLink, out string? offender));
        Assert.Equal(link, offender);
    }

    /// <summary>Yok olan bileşen "tehlikeli" değildir; asıl dosya zaten yok sayılıyor.</summary>
    [Fact]
    public void AllDirectoryComponentsAreReal_True_WhenAComponentIsMissing()
    {
        string missing = Path.Combine(DropRoot, "yok", "a.txt");
        Assert.True(IslandService.AllDirectoryComponentsAreReal(Prefix, missing, out _));
    }

    /// <summary>
    /// Kapı gerçek dosyayı ve gerçek klasörü kabul eder (raf klasör de barındırır; klasör
    /// <c>UseShellExecute</c> ile Explorer'da açılır). Reparse noktalı klasörün reddi
    /// <see cref="CanOpenShelfFile_RejectsJunctionEscape"/>'te — junction kurulumu orada.
    /// </summary>
    [Fact]
    public void CanOpenShelfFile_AcceptsRegularFileAndRealFolder_RejectsMissingAndDevice()
    {
        string file = WriteFile("raf.txt", "shelf");
        string legacy = WriteFile(Path.Combine("Drop", "legacy.txt"), "old copy");
        string directory = Path.Combine(_root, "bir-klasor");
        Directory.CreateDirectory(directory);
        string missing = Path.Combine(_root, "yok.txt");

        WithIsland(service =>
        {
            Assert.True(service.CanOpenShelfFile(file));
            Assert.False(IslandService.IsInsideDropPrefix(file, Prefix, out _));

            // Eski Drop kopyası da normal dosyadır; açma Drop önekini şart koşmaz
            // ama önekin içinde olmak da engel değildir.
            Assert.True(service.CanOpenShelfFile(legacy));

            Assert.True(service.CanOpenShelfFile(directory));
            Assert.False(service.CanOpenShelfFile(missing));
            Assert.False(service.CanOpenShelfFile(null));
            Assert.False(service.CanOpenShelfFile(string.Empty));
            Assert.False(service.CanOpenShelfFile("   "));
            Assert.False(service.CanOpenShelfFile(@"\\.\NUL"));
            Assert.False(service.CanOpenShelfFile("NUL"));

            Assert.True(File.Exists(file));
            Assert.True(Directory.Exists(directory));
        });
    }

    /// <summary>
    /// Junction ön ek testini geçer ama açma hedefi izler. Kapı bunu reddeder ve
    /// hedef dosyaya dokunmaz.
    /// </summary>
    [Fact]
    public void CanOpenShelfFile_RejectsJunctionEscape()
    {
        string outside = Path.Combine(_root, "OutsideOpen");
        Directory.CreateDirectory(outside);
        string victim = Path.Combine(outside, "tehlikeli.docx");
        File.WriteAllText(victim, "important");

        string link = Path.Combine(DropRoot, "baglanti-open");
        Assert.True(CreateJunction(link, outside), "mklink /J başarısız — koruma test edilemiyor");

        string throughLink = Path.Combine(link, "tehlikeli.docx");
        Assert.True(File.Exists(throughLink), "junction hedefe ulaşmıyor");
        Assert.True(IslandService.IsInsideDropPrefix(throughLink, Prefix, out _));

        WithIsland(service =>
        {
            Assert.False(service.CanOpenShelfFile(throughLink));
            Assert.False(service.CanOpenShelfFile(link));
        });

        Assert.True(File.Exists(victim));
        Assert.Equal("important", File.ReadAllText(victim));
    }

    private void WithIsland(Action<IslandService> body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            IslandService? service = null;
            try
            {
                service = new IslandService(
                    Dispatcher.CurrentDispatcher,
                    new IslandStore(_root),
                    new IslandConfig
                    {
                        Enabled = false,
                        Persistence = IslandConfig.PersistenceSession,
                    });
                body(service);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                service?.Dispose();
                try
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
                catch (InvalidOperationException)
                {
                    // Dispatcher hiç kurulmadıysa kapatma da yoktur.
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test thread did not finish");
        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }

    /// <summary>Junction kurulumu yönetici yetkisi gerektirmez (mklink /J).</summary>
    private static bool CreateJunction(string linkPath, string targetPath)
    {
        try
        {
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (mklink is null)
            {
                return false;
            }

            mklink.WaitForExit(10_000);
            return mklink.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
