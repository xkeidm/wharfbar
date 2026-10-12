using System.IO;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Bırakılan dosya <c>Drop</c> klasörüne kopyalanmaz; kayıt orijinal yolu tutar.
/// Silme hâlâ fail-closed: <c>Drop</c> dışındaki dosya listeden düşer ama diskte kalır,
/// <c>Drop</c> altında duran eski kopya silinmeye devam eder.
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
            Assert.Equal(2, service.Entries.Count);

            IslandEntry last = service.Entries[0];
            IslandEntry earlier = service.Entries[1];
            Assert.Equal(IslandEntryKind.File, last.Kind);
            Assert.Equal("b.txt", last.Text);
            Assert.Equal(Path.GetFullPath(second), last.FilePath);
            Assert.Null(last.SourceApp);
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
