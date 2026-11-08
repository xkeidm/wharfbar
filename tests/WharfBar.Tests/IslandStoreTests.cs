using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandStoreTests : IDisposable
{
    /// <summary>
    /// Text that can genuinely pass through the clipboard: Turkish characters, line breaks, quotes,
    /// emoji and a right-to-left override mark. If the serializer's escaping behavior is silently
    /// broken later by a change such as <c>UnsafeRelaxedJsonEscaping</c> (mojibake), this test
    /// catches it.
    /// </summary>
    private const string AwkwardText = "  çğıöşü\r\n\"yaz\"  😀 \u202Eters";

    /// <summary>
    /// Must match <c>IslandStore.MaxStoreBytes</c>. If it drifts (the cap grows), this test
    /// fails: the file then stays under the cap, <c>Load</c> reads the record and <c>Assert.Empty</c>
    /// blows up — so the drift cannot go unnoticed.
    /// </summary>
    private const long StoreSizeCap = 64L * 1024 * 1024;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-island-tests", Guid.NewGuid().ToString("N"));

    private IslandStore NewStore() => new(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void StorePath_And_DropRoot_LiveUnderAppDataRoot()
    {
        IslandStore store = NewStore();
        Assert.Equal(Path.Combine(_root, "island.json"), store.StorePath);
        Assert.Equal(Path.Combine(_root, "Drop"), store.DropRoot);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(NewStore().Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntries()
    {
        IslandStore store = NewStore();
        var entries = new[]
        {
            new IslandEntry(1, IslandEntryKind.Text, "merhaba", null, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), "Notepad"),
            new IslandEntry(2, IslandEntryKind.File, "a.pdf", @"C:\Drop\a.pdf", new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc), null),
            new IslandEntry(3, IslandEntryKind.Text, AwkwardText, null, new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), null),
        };

        store.Save(entries);
        var loaded = NewStore().Load();

        Assert.Equal(3, loaded.Count);
        Assert.Equal("merhaba", loaded[0].Text);
        Assert.Equal("Notepad", loaded[0].SourceApp);
        Assert.Equal(IslandEntryKind.File, loaded[1].Kind);
        Assert.Equal(@"C:\Drop\a.pdf", loaded[1].FilePath);
        Assert.Equal(AwkwardText, loaded[2].Text);
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndReturnsEmpty()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);
        File.WriteAllText(store.StorePath, "{ this is not json");

        Assert.Empty(store.Load());
        Assert.True(File.Exists(store.StorePath + ".bak"), "bozuk dosya .bak olarak korunmalı");
    }

    /// <summary>
    /// <c>.bak</c> is a recovery point, not just an archive of corrupt files: on a second corruption
    /// the content of the first corruption must not be lost permanently, it must rotate to <c>.bak.1</c>.
    /// </summary>
    [Fact]
    public void Load_SecondCorruption_KeepsFirstBackupAsDot1()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);

        File.WriteAllText(store.StorePath, "{ ilk bozulma");
        Assert.Empty(store.Load());
        Assert.True(File.Exists(store.StorePath + ".bak"));

        File.WriteAllText(store.StorePath, "{ ikinci bozulma");
        Assert.Empty(store.Load());

        Assert.Equal("{ ikinci bozulma", File.ReadAllText(store.StorePath + ".bak"));
        Assert.Equal("{ ilk bozulma", File.ReadAllText(store.StorePath + ".bak.1"));
    }

    /// <summary>
    /// The store only reads: pruning is the service's job, because <c>RetentionDays</c> /
    /// <c>MaxItems</c> are known only to <c>IslandService</c>. If a fixed day limit were
    /// applied in the store, the user's 3650-day choice would be silently overridden.
    /// </summary>
    [Fact]
    public void Load_KeepsAgedEntries_PruningBelongsToService()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);
        var payload = new
        {
            entries = new object[]
            {
                new { id = 1, kind = "Text", text = "40+ gun once", filePath = (string?)null, capturedAtUtc = DateTime.UtcNow.AddDays(-40), sourceApp = (string?)null },
                new { id = 2, kind = "Text", text = "yeni", filePath = (string?)null, capturedAtUtc = DateTime.UtcNow, sourceApp = (string?)null },
            },
        };
        File.WriteAllText(store.StorePath, JsonSerializer.Serialize(payload));

        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("40+ gun once", loaded[0].Text);
        Assert.Equal("yeni", loaded[1].Text);
    }

    [Fact]
    public void Load_EntryArrayWithNulls_SkipsThemAndKeepsTheRest()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            store.StorePath,
            "{\"version\":1,\"entries\":[null,{\"id\":7,\"kind\":\"Text\",\"text\":\"ayakta\",\"filePath\":null,\"capturedAtUtc\":\"2026-10-01T10:00:00Z\",\"sourceApp\":null}]}");

        var loaded = store.Load();

        Assert.Equal("ayakta", Assert.Single(loaded).Text);
        Assert.False(File.Exists(store.StorePath + ".bak"), "okunabilir dosya yedeklenmemeli");
    }

    /// <summary>
    /// An unknown <c>kind</c> (left over from a newer version or a hand-written file) must not
    /// push the whole history into <c>.bak</c>: the record is interpreted as <c>Text</c>.
    /// </summary>
    [Fact]
    public void Load_UnknownKindValue_KeepsRecordAsText()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            store.StorePath,
            "{\"version\":1,\"entries\":[{\"id\":7,\"kind\":\"Gif\",\"text\":\"animasyon\",\"filePath\":null,\"capturedAtUtc\":\"2026-10-01T10:00:00Z\",\"sourceApp\":null}]}");

        var loaded = store.Load();

        Assert.Equal(IslandEntryKind.Text, Assert.Single(loaded).Kind);
        Assert.False(File.Exists(store.StorePath + ".bak"), "bilinmeyen kind bozuk dosya sayılmamalı");
    }

    /// <summary>
    /// An unparseable <c>capturedAtUtc</c> drops only that record; its siblings are preserved.
    /// </summary>
    [Fact]
    public void Load_UnparseableCapturedAtUtc_DropsOnlyThatRecord()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            store.StorePath,
            "{\"version\":1,\"entries\":["
            + "{\"id\":1,\"kind\":\"Text\",\"text\":\"bozuk tarih\",\"capturedAtUtc\":\"degil-tarih\"},"
            + "{\"id\":2,\"kind\":\"Text\",\"text\":\"ayakta\",\"capturedAtUtc\":\"2026-10-01T10:00:00Z\"}]}");

        var loaded = store.Load();

        Assert.Equal("ayakta", Assert.Single(loaded).Text);
        Assert.False(File.Exists(store.StorePath + ".bak"), "tek bozuk alan bozuk dosya sayılmamalı");
    }

    /// <summary>
    /// A non-numeric <c>id</c> drops only that record; its siblings are preserved.
    /// </summary>
    [Fact]
    public void Load_NonNumericId_DropsOnlyThatRecord()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            store.StorePath,
            "{\"version\":1,\"entries\":["
            + "{\"id\":\"yedi\",\"kind\":\"Text\",\"text\":\"bozuk id\",\"capturedAtUtc\":\"2026-10-01T10:00:00Z\"},"
            + "{\"id\":2,\"kind\":\"Text\",\"text\":\"ayakta\",\"capturedAtUtc\":\"2026-10-01T10:00:00Z\"}]}");

        var loaded = store.Load();

        Assert.Equal(2, Assert.Single(loaded).Id);
        Assert.False(File.Exists(store.StorePath + ".bak"), "tek bozuk alan bozuk dosya sayılmamalı");
    }

/// <summary>
    /// Size cap: a file exceeding <c>IslandStore.MaxStoreBytes</c> is not read
    /// (<c>File.ReadAllText</c> could throw OOM and would fall outside this filter — startup would hang).
    /// The file is deliberately <b>valid</b> JSON (a single record plus padding): otherwise the test
    /// would stay green for the wrong reason (a parse error) and would not exercise the cap.
    /// </summary>
    [Fact]
    public void Load_OversizedFile_BacksUpAndReturnsEmpty()
    {
        IslandStore store = NewStore();
        Directory.CreateDirectory(_root);

        string head = "{\"version\":1,\"entries\":[{\"id\":7,\"kind\":\"Text\",\"text\":\"kocaman dosya\","
            + "\"capturedAtUtc\":\"2026-10-01T10:00:00Z\"}]}";
        long padding = StoreSizeCap + 1 - head.Length;
        using (var oversized = new FileStream(store.StorePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            oversized.Write(Encoding.UTF8.GetBytes(head));
            byte[] spaces = new byte[8192];
            Array.Fill(spaces, (byte)' ');
            for (long written = 0; written < padding; written += spaces.Length)
            {
                oversized.Write(spaces, 0, (int)Math.Min(spaces.Length, padding - written));
            }
        }

        Assert.True(new FileInfo(store.StorePath).Length > StoreSizeCap);
        Assert.Empty(store.Load());
        Assert.True(File.Exists(store.StorePath + ".bak"), "tavanı aşan dosya okunmadan .bak'a alınmalı");
    }

    /// <summary>
    /// A second write atomically replaces the existing file (the replace branch) and
    /// leaves no half-written <c>.tmp</c> behind.
    /// </summary>
    [Fact]
    public void Save_OverExistingFile_ReplacesItAndLeavesNoTempFile()
    {
        IslandStore store = NewStore();
        store.Save(new[] { Entry(1, "ilk") });
        store.Save(new[] { Entry(2, "son") });

        var loaded = store.Load();

        Assert.Equal(2, Assert.Single(loaded).Id);
        Assert.Equal("son", loaded[0].Text);
        Assert.False(File.Exists(store.StorePath + ".tmp"), "atomik yazma .tmp bırakmamalı");
    }

    /// <summary>
    /// <c>Save</c> calls cannot enter concurrently. With the fixed <c>StorePath + ".tmp"</c> path
    /// unlocked, a competing second writer got an <see cref="IOException"/> in <see cref="File.WriteAllText"/>
    /// and <c>Save</c> swallowed it: that writer's snapshot silently disappeared.
    /// </summary>
    [Fact]
    public async Task Save_WhileAnotherSaveIsInProgress_WaitsForIt()
    {
        IslandStore store = NewStore();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        Task first = Task.Run(() => store.Save(
            new BlockingEntryList(new[] { Entry(1, "birinci") }, reached, release)));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(10)), "ilk Save kayıtları okumaya başlamalı");

        Task second = Task.Run(() => store.Save(new[] { Entry(2, "ikinci") }));
        Task winner;
        try
        {
            winner = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            release.Set();
        }

        Assert.NotSame(second, winner);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(NewStore().Load(), e => e.Id == 2);
    }

    /// <summary>
    /// <c>Load</c> shares the same lock: a competing <c>Load</c> opened the file without delete/move
    /// sharing and could thus break <c>File.Replace</c> with an <see cref="IOException"/>.
    /// </summary>
    [Fact]
    public async Task Load_WhileSaveIsInProgress_WaitsForIt()
    {
        IslandStore store = NewStore();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        Task save = Task.Run(() => store.Save(
            new BlockingEntryList(new[] { Entry(1, "birinci") }, reached, release)));
        Assert.True(reached.Wait(TimeSpan.FromSeconds(10)), "Save kayıtları okumaya başlamalı");

        Task<IReadOnlyList<IslandEntry>> load = Task.Run(() => store.Load());
        Task winner;
        try
        {
            winner = await Task.WhenAny(load, Task.Delay(TimeSpan.FromMilliseconds(500)));
        }
        finally
        {
            release.Set();
        }

        Assert.NotSame(load, winner);
        await Task.WhenAll(save, load).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, Assert.Single(await load).Id);
    }

    /// <summary>
    /// The converter that is lenient when reading <c>kind</c> must also emit the <b>type name</b> when
    /// writing: the file format is a contract between versions (ordering, number, name — all of them
    /// affect the reader later). This test catches a numeric representation settling in silently.
    /// </summary>
    [Fact]
    public void Save_WritesKindAsEnumName_NotNumber()
    {
        IslandStore store = NewStore();
        store.Save(new[] { Entry(1, "bir") });

        string raw = File.ReadAllText(store.StorePath);

        Assert.Contains("\"Kind\": \"Text\"", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Kind\": 0", raw, StringComparison.Ordinal);
    }

    private static IslandEntry Entry(long id, string text) =>
        new(id, IslandEntryKind.Text, text, null, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), null);

    /// <summary>
    /// Blocks <c>Save</c> at the entry-reading stage so that the concurrency tests exercise the
    /// lock in a measurable way (it does not rely on timing guesses).
    /// </summary>
    private sealed class BlockingEntryList : IReadOnlyList<IslandEntry>
    {
        private readonly IReadOnlyList<IslandEntry> _inner;
        private readonly ManualResetEventSlim _reached;
        private readonly ManualResetEventSlim _release;
        private int _blocked;

        internal BlockingEntryList(
            IReadOnlyList<IslandEntry> inner,
            ManualResetEventSlim reached,
            ManualResetEventSlim release)
        {
            _inner = inner;
            _reached = reached;
            _release = release;
        }

        public int Count => _inner.Count;

        public IslandEntry this[int index] => _inner[index];

        public IEnumerator<IslandEntry> GetEnumerator()
        {
            if (Interlocked.CompareExchange(ref _blocked, 1, 0) == 0)
            {
                _reached.Set();
                _release.Wait(TimeSpan.FromSeconds(10));
            }

            return _inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}