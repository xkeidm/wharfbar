using System.IO;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// A dropped file is not copied into the <c>Drop</c> folder; the record keeps the original path. A folder
/// also lands on the shelf under the same contract (B1). Deletion is still fail-closed: a file outside
/// <c>Drop</c> drops out of the list but stays on disk, while an old copy living under <c>Drop</c> is still deleted.
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

            // Folders are recorded too (B1): only a path that is neither a file nor a folder is filtered out.
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
    /// B1: a folder also lands on the shelf. The record contract does not change — <see cref="IslandEntryKind.File"/>
    /// and the original full path — only the <see cref="File.Exists"/> gate now accepts folders too.
    /// No copying: the <c>Drop</c> folder is still never created.
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

            // No copy: that is why the Drop folder is never created here.
            Assert.False(Directory.Exists(store.DropRoot));
        });
    }

    /// <summary>
    /// The open gate accepts a real folder (<c>Process.Start { UseShellExecute = true }</c>
    /// opens Explorer on the folder). Rejection of a reparse-point folder is not repeated here: the permanent
    /// coverage is in <c>IslandDropPathGuardTests.CanOpenShelfFile_RejectsJunctionEscape</c>, kept there
    /// because it requires setting up a junction.
    /// </summary>
    [Fact]
    public void CanOpenShelfFile_AllowsRealFolder()
    {
        Run((service, _, root) =>
        {
            string dir = Path.Combine(root, "ShelfFolder");
            Directory.CreateDirectory(dir);

            Assert.True(service.CanOpenShelfFile(dir));

            // The folder itself must be a real path: a deleted folder is not opened.
            string missing = Path.Combine(root, "NoSuchFolder");
            Assert.False(service.CanOpenShelfFile(missing));
        });
    }

    /// <summary>
    /// Resolution order: the record's own path (file or folder) wins; if that path does not exist,
    /// the copy with the <b>same name</b> under the <c>Drop</c> root is returned — that is the only
    /// openable target for old records written before copying was removed. If neither exists, fail-closed.
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

            // Even if a stale copy with the same name sits under Drop, the original path wins.
            Directory.CreateDirectory(store.DropRoot);
            string staleCopy = Path.Combine(store.DropRoot, "keep.txt");
            File.WriteAllText(staleCopy, "stale");
            Assert.True(service.TryGetExistingShelfPath(real, out string stillOriginal));
            Assert.Equal(Path.GetFullPath(real), Path.GetFullPath(stillOriginal));
            File.Delete(staleCopy);

            // Fallback branch: the record keeps the original path, that path has been deleted, only the Drop copy remains.
            string gone = Path.Combine(root, "gone.txt");
            File.WriteAllText(gone, "legacy");
            string dropCopy = Path.Combine(store.DropRoot, "gone.txt");
            File.Move(gone, dropCopy);

            Assert.True(service.TryGetExistingShelfPath(gone, out string fallback));
            Assert.Equal(Path.GetFullPath(dropCopy), Path.GetFullPath(fallback));

            // Neither the original nor a copy: no resolution (fail-closed).
            Assert.False(service.TryGetExistingShelfPath(Path.Combine(root, "never.txt"), out string missing));
            Assert.Equal(string.Empty, missing);
            Assert.False(service.TryGetExistingShelfPath(null, out _));
            Assert.False(service.TryGetExistingShelfPath(@"\\.\NUL", out _));
        });
    }

    /// <summary>
    /// A folder path carrying a trailing separator is recorded too; the name is trimmed for
    /// <see cref="Path.GetFileName(string)"/> and the caller's "really added" feedback comes back as a count (0 = no record at all).
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

            // If no path is real the count is 0: the feedback gate relies on this.
            Assert.Equal(0, service.AddDroppedFiles([Path.Combine(root, "missing.txt")]));
            Assert.Single(service.Entries);
        });
    }

    /// <summary>
    /// A folder record is resolved too (the <see cref="Directory.Exists"/> branch) and the resolved path is the
    /// folder on disk; a device path is still rejected fail-closed.
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
    /// The "show location" argument for Explorer is a single element and contains no quotes: quoting is
    /// done by <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>. The trailing separator
    /// is trimmed (except for a root path); a root such as <c>C:\</c> keeps its separator.
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
