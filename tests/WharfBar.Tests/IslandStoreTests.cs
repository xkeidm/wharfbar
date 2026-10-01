using System;
using System.IO;
using System.Text.Json;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class IslandStoreTests : IDisposable
{
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
        };

        store.Save(entries);
        var loaded = NewStore().Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("merhaba", loaded[0].Text);
        Assert.Equal("Notepad", loaded[0].SourceApp);
        Assert.Equal(IslandEntryKind.File, loaded[1].Kind);
        Assert.Equal(@"C:\Drop\a.pdf", loaded[1].FilePath);
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
    /// Depo yalnız okur: budama servisin işidir, çünkü <c>RetentionDays</c> /
    /// <c>MaxItems</c> yalnız <c>IslandService</c> tarafından bilinir. Depoda sabit bir
    /// gün limiti uygulanırsa kullanıcının 3650 gün seçimi sessizce ezilirdi.
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
    /// İkinci yazma mevcut dosyanın üzerine atomik olarak yapılır (replace dalı) ve
    /// yarım kalmış <c>.tmp</c> bırakmaz.
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

    private static IslandEntry Entry(long id, string text) =>
        new(id, IslandEntryKind.Text, text, null, new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc), null);
}