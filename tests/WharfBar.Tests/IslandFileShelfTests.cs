using System.IO;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Bırakılan dosya <c>Drop</c> klasörüne kopyalanmaz; kayıt orijinal yolu tutar. Klasör de
/// aynı sözleşmeyle rafa düşer (B1). Silme hâlâ fail-closed: <c>Drop</c> dışındaki dosya
/// listeden düşer ama diskte kalır, <c>Drop</c> altında duran eski kopya silinmeye devam eder.
/// </summary>
public sealed class IslandFileShelfTests
{
    [Fact]
    public void AddDroppedFiles_RecordsOriginalPath_AndLeavesSourceInPlace()
    {
        Run((service, store, root) =>
        {
            string first = Write(root, "a.txt", "one");
            string second = Write(root, "b.txt", "two");
            string missing = Path.Combine(root, "missing.txt");
            string directory = Path.Combine(root, "folder");
            Directory.CreateDirectory(directory);

            int addedNotifications = 0;
            service.EntryAdded += (_, _) => addedNotifications++;

            service.AddDroppedFiles([first, directory, missing, second]);

            Assert.Equal(1, addedNotifications);

            // Klasör de kaydedilir (B1): yalnız "ne dosya ne klasör" olan yol elenir.
            Assert.Equal(3, service.Entries.Count);

            IslandEntry last = service.Entries[0];
            IslandEntry folder = service.Entries[1];
            IslandEntry earlier = service.Entries[2];
            Assert.Equal(IslandEntryKind.File, last.Kind);
            Assert.Equal("b.txt", last.Text);
            Assert.Equal(Path.GetFullPath(second), last.FilePath);
            Assert.Null(last.SourceApp);
            Assert.Equal(IslandEntryKind.File, folder.Kind);
            Assert.Equal("folder", folder.Text);
            Assert.Equal(Path.GetFullPath(directory), folder.FilePath);
            Assert.Equal("a.txt", earlier.Text);
            Assert.Equal(Path.GetFullPath(first), earlier.FilePath);

            Assert.Equal("one", File.ReadAllText(first));
            Assert.Equal("two", File.ReadAllText(second));
            Assert.False(Directory.Exists(store.DropRoot));
        });
    }

    [Fact]
    public void ClearAndDelete_DoNotDeleteShelfFileOutsideDrop()
    {
        Run((service, store, root) =>
        {
            string shelf = Write(root, "notes.txt", "keep");
            service.AddDroppedFiles([shelf]);

            Assert.Equal(Path.GetFullPath(shelf), Assert.Single(service.Entries).FilePath);
            Assert.False(Directory.Exists(store.DropRoot));
            Assert.True(File.Exists(shelf));

            service.Clear();
            Assert.Empty(service.Entries);
            Assert.Equal("keep", File.ReadAllText(shelf));

            service.AddDroppedFiles([shelf]);
            Directory.CreateDirectory(store.DropRoot);
            string sentinel = Path.Combine(store.DropRoot, "sentinel.txt");
            File.WriteAllText(sentinel, "scan");

            service.DeleteDroppedFiles();
            Assert.DoesNotContain(service.Entries, e => e.Kind == IslandEntryKind.File);
            Assert.True(WaitUntil(() => !File.Exists(sentinel)), "Drop taraması çalışmadı");
            Assert.Equal("keep", File.ReadAllText(shelf));
            Assert.False(File.Exists(Path.Combine(store.DropRoot, "notes.txt")));

            File.WriteAllText(sentinel, "remove");
            service.AddDroppedFiles([shelf, sentinel]);
            IslandEntry shelfEntry = service.Entries.Single(e =>
                string.Equals(e.FilePath, Path.GetFullPath(shelf), StringComparison.OrdinalIgnoreCase));
            IslandEntry sentinelEntry = service.Entries.Single(e =>
                string.Equals(e.FilePath, Path.GetFullPath(sentinel), StringComparison.OrdinalIgnoreCase));

            service.Remove(shelfEntry);
            service.Remove(sentinelEntry);
            Assert.True(WaitUntil(() => !File.Exists(sentinel)), "Drop altındaki kayıt silinmedi");
            Assert.Equal("keep", File.ReadAllText(shelf));
        });
    }

    [Fact]
    public void Delete_StillDeletesFileThatLivesUnderDropRoot()
    {
        Run((service, store, _) =>
        {
            Directory.CreateDirectory(store.DropRoot);
            string viaRemove = Path.Combine(store.DropRoot, "via-remove.txt");
            File.WriteAllText(viaRemove, "legacy");

            service.AddDroppedFiles([viaRemove]);
            IslandEntry entry = Assert.Single(service.Entries);
            Assert.Equal(Path.GetFullPath(viaRemove), entry.FilePath);
            Assert.Equal(
                Path.GetFullPath(viaRemove),
                Path.GetFullPath(Assert.Single(Directory.GetFiles(store.DropRoot))));

            service.Remove(entry);
            Assert.True(WaitUntil(() => !File.Exists(viaRemove)), "Drop altındaki dosya silinmedi");

            string viaScan = Path.Combine(store.DropRoot, "via-scan.txt");
            File.WriteAllText(viaScan, "orphan");
            service.DeleteDroppedFiles();
            Assert.True(WaitUntil(() => !File.Exists(viaScan)), "Drop klasör taraması dosyayı silmedi");
        });
    }

    /// <summary>
    /// B1: klasör de rafa düşer. Kayıt sözleşmesi değişmez — <see cref="IslandEntryKind.File"/>
    /// ve orijinal tam yol — yalnız <see cref="File.Exists"/> kapısı klasörleri de kabul eder.
    /// Kopyalama yok: <c>Drop</c> klasörü hâlâ hiç açılmaz.
    /// </summary>
    [Fact]
    public void AddDroppedFiles_RecordsFolder_NoFileRequired()
    {
        Run((service, store, root) =>
        {
            string dir = Path.Combine(root, "DroppedFolder");
            Directory.CreateDirectory(dir);

            service.AddDroppedFiles([dir]);

            IslandEntry entry = Assert.Single(service.Entries);
            Assert.Equal(IslandEntryKind.File, entry.Kind);
            Assert.Equal(Path.GetFullPath(dir), entry.FilePath);
            Assert.Equal(Path.GetFileName(dir), entry.Text);
            Assert.Null(entry.SourceApp);

            // Kopya yok: Drop klasörü bu yüzden hiç oluşturulmaz.
            Assert.False(Directory.Exists(store.DropRoot));
        });
    }

    /// <summary>
    /// Açma kapısı gerçek klasörü kabul eder (<c>Process.Start { UseShellExecute = true }</c>
    /// klasörde Explorer'ı açar). Reparse-point klasör reddi burada tekrarlanmaz: kalıcı
    /// kapsam <c>IslandDropPathGuardTests.CanOpenShelfFile_RejectsJunctionEscape</c>'te, junction
    /// kurulumu gerektirdiği için orada tutulur.
    /// </summary>
    [Fact]
    public void CanOpenShelfFile_AllowsRealFolder()
    {
        Run((service, _, root) =>
        {
            string dir = Path.Combine(root, "ShelfFolder");
            Directory.CreateDirectory(dir);

            Assert.True(service.CanOpenShelfFile(dir));

            // Klasörün kendisi gerçek bir yol olmalı: silinmiş klasör açılmaz.
            string missing = Path.Combine(root, "NoSuchFolder");
            Assert.False(service.CanOpenShelfFile(missing));
        });
    }

    /// <summary>
    /// Çözümleme sırası: kaydın kendi yolu (dosya ya da klasör) kazanır; o yol yoksa
    /// <c>Drop</c> kökündeki <b>aynı adlı</b> kopya döner — kopyalama kaldırılmadan önce
    /// yazılmış eski kayıtların tek açılabilir hedefi budur. Hiçbiri yoksa fail-closed.
    /// </summary>
    [Fact]
    public void TryGetExistingShelfPath_PrefersOriginal_AndFallsBackToDropCopy()
    {
        Run((service, store, root) =>
        {
            string real = Write(root, "keep.txt", "original");
            service.AddDroppedFiles([real]);
            IslandEntry entry = Assert.Single(service.Entries);

            Assert.True(service.TryGetExistingShelfPath(entry.FilePath, out string resolved));
            Assert.Equal(Path.GetFullPath(real), Path.GetFullPath(resolved));

            // Drop altında aynı adlı eski bir kopya dursa bile orijinal yol kazanır.
            Directory.CreateDirectory(store.DropRoot);
            string staleCopy = Path.Combine(store.DropRoot, "keep.txt");
            File.WriteAllText(staleCopy, "stale");
            Assert.True(service.TryGetExistingShelfPath(real, out string stillOriginal));
            Assert.Equal(Path.GetFullPath(real), Path.GetFullPath(stillOriginal));
            File.Delete(staleCopy);

            // Fallback dalı: kayıt orijinal yolu tutar, o yol silinmiştir, geriye Drop kopyası kalır.
            string gone = Path.Combine(root, "gone.txt");
            File.WriteAllText(gone, "legacy");
            string dropCopy = Path.Combine(store.DropRoot, "gone.txt");
            File.Move(gone, dropCopy);

            Assert.True(service.TryGetExistingShelfPath(gone, out string fallback));
            Assert.Equal(Path.GetFullPath(dropCopy), Path.GetFullPath(fallback));

            // Ne orijinal ne kopya: çözümleme yok (fail-closed).
            Assert.False(service.TryGetExistingShelfPath(Path.Combine(root, "never.txt"), out string missing));
            Assert.Equal(string.Empty, missing);
            Assert.False(service.TryGetExistingShelfPath(null, out _));
            Assert.False(service.TryGetExistingShelfPath(@"\\.\NUL", out _));
        });
    }

    /// <summary>
    /// Sondaki ayırıcıyı taşıyan klasör yolu da kaydedilir; ad <see cref="Path.GetFileName(string)"/>
    /// için kırpılır ve çağıranın "gerçekten eklendi" geri bildirimi sayıyla döner (0 = hiç kayıt yok).
    /// </summary>
    [Fact]
    public void AddDroppedFiles_FolderWithTrailingSeparator_CountsOneAndTrimsName()
    {
        Run((service, _, root) =>
        {
            string dir = Path.Combine(root, "TrailingFolder");
            Directory.CreateDirectory(dir);

            int added = service.AddDroppedFiles([dir + Path.DirectorySeparatorChar]);

            Assert.Equal(1, added);
            IslandEntry entry = Assert.Single(service.Entries);
            Assert.Equal(Path.GetFileName(dir), entry.Text);
            Assert.Equal(IslandEntryKind.File, entry.Kind);
            Assert.True(Directory.Exists(entry.FilePath));

            // Hiçbir yol gerçek değilse sayı 0'dır: geri bildirim kapısı buna dayanır.
            Assert.Equal(0, service.AddDroppedFiles([Path.Combine(root, "missing.txt")]));
            Assert.Single(service.Entries);
        });
    }

    /// <summary>
    /// Klasör kaydı da çözümlenir (<see cref="Directory.Exists"/> dalı) ve çözülen yol diskteki
    /// klasördür; aygıt yolu yine fail-closed reddedilir.
    /// </summary>
    [Fact]
    public void TryGetExistingShelfPath_ResolvesDirectoryRecord()
    {
        Run((service, _, root) =>
        {
            string dir = Path.Combine(root, "ShelfDir");
            Directory.CreateDirectory(dir);
            service.AddDroppedFiles([dir]);

            IslandEntry entry = Assert.Single(service.Entries);
            Assert.True(service.TryGetExistingShelfPath(entry.FilePath, out string resolved));
            Assert.Equal(Path.GetFullPath(dir), Path.GetFullPath(resolved));
            Assert.True(Directory.Exists(resolved));

            Assert.False(service.TryGetExistingShelfPath(@"\\.\NUL", out string device));
            Assert.Equal(string.Empty, device);
        });
    }

    /// <summary>
    /// Explorer'a "konumu göster" argümanı tek öğedir ve tırnak içermez: tırnaklamayı
    /// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> yapar. Sondaki ayırıcı
    /// (kök yol hariç) kırpılır; kök <c>C:\</c> ayırıcısını korur.
    /// </summary>
    [Fact]
    public void BuildRevealArguments_TrimsTrailingSeparator_AndKeepsRoot()
    {
        string normal = Path.Combine(Path.GetTempPath(), "WharfBarTests", "reveal", "file.txt");
        Assert.Equal(["/select," + normal], IslandService.BuildRevealArguments(normal));

        string dir = Path.Combine(Path.GetTempPath(), "WharfBarTests", "reveal");
        string withSeparator = dir + Path.DirectorySeparatorChar;
        string[] folderArgs = IslandService.BuildRevealArguments(withSeparator);
        Assert.Equal(["/select," + dir], folderArgs);
        Assert.DoesNotContain('"', folderArgs[0]);
        Assert.Single(folderArgs);

        string root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Equal(["/select," + root], IslandService.BuildRevealArguments(root));
    }

    private static void Run(Action<IslandService, IslandStore, string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "WharfBarTests", "shelf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            IslandService? service = null;
            try
            {
                var store = new IslandStore(root);
                service = new IslandService(
                    Dispatcher.CurrentDispatcher,
                    store,
                    new IslandConfig
                    {
                        Enabled = false,
                        Persistence = IslandConfig.PersistenceSession,
                    });
                body(service, store, root);
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
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "STA test thread did not finish");

        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (error is not null)
        {
            throw new Xunit.Sdk.XunitException(error.ToString());
        }
    }

    private static string Write(string root, string name, string content)
    {
        string full = Path.Combine(root, name);
        File.WriteAllText(full, content);
        return full;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }
}
