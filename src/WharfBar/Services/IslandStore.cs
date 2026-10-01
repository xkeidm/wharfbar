using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Island geçmişinin kalıcı deposu. <c>config.json</c> kullanılmaz: <c>ConfigService.Save</c>
/// her çağrıda dosyanın tamamını yeniden yazar, pano değişiklikleri ise yüksek
/// frekanslıdır. Yazma atomiktir (tmp + <c>File.Replace</c>), bozuk dosya <c>.bak</c> olarak
/// korunur ve açılış asla kilitlenmez.
/// </summary>
/// <remarks>
/// Depo <b>yalnız okur</b>: gün limiti (<c>RetentionDays</c>) ve kayıt tavanı
/// (<c>MaxItems</c>) burada uygulanmaz, çünkü bu değerleri yalnız <c>IslandService</c>
/// bilir — depoda sabit bir gün limiti kullanmak kullanıcının ayarını sessizce ezerdi.
/// Budama <c>IslandPruner.Prune</c> ile servis katmanında yapılır.
/// </remarks>
internal sealed class IslandStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    internal IslandStore(string appDataRoot)
    {
        StorePath = Path.Combine(appDataRoot, "island.json");
        DropRoot = Path.Combine(appDataRoot, "Drop");
    }

    /// <summary>Geçmişin tutulduğu JSON dosyasının tam yolu.</summary>
    internal string StorePath { get; }

    /// <summary>Island'a bırakılan dosyaların kopyalandığı klasör.</summary>
    internal string DropRoot { get; }

    /// <summary>Zaman damgası alanını UTC olarak yorumlayan kayıt sarmalayıcısı.</summary>
    private sealed class EntryDocument
    {
        public long Id { get; set; }

        public IslandEntryKind Kind { get; set; }

        public string Text { get; set; } = string.Empty;

        public string? FilePath { get; set; }

        public DateTime CapturedAtUtc { get; set; }

        public string? SourceApp { get; set; }
    }

    private sealed class StoreDocument
    {
        public int Version { get; set; } = 1;

        public List<EntryDocument?> Entries { get; set; } = [];
    }

    /// <summary>
    /// Geçmişi ham olarak okur — budama uygulanmaz. Dosya yoksa boş liste döner; bozuksa
    /// <c>.bak</c> olarak korunur ve boş döner (açılış kilitlenmez).
    /// </summary>
    internal IReadOnlyList<IslandEntry> Load()
    {
        if (!File.Exists(StorePath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(StorePath);
            StoreDocument? doc = JsonSerializer.Deserialize<StoreDocument>(json, JsonOptions);
            if (doc?.Entries is null)
            {
                return [];
            }

            var entries = new List<IslandEntry>(doc.Entries.Count);
            foreach (EntryDocument? e in doc.Entries)
            {
                if (e is null)
                {
                    // Elle düzenlenmiş bir dosyada dizide null eleman olabilir; açılışı kilitlememek
                    // için atlanır (tüm dosyayı .bak yapmaya gerek yok).
                    continue;
                }

                entries.Add(new IslandEntry(
                    e.Id,
                    e.Kind,
                    e.Text ?? string.Empty,
                    e.FilePath,
                    DateTime.SpecifyKind(e.CapturedAtUtc, DateTimeKind.Utc),
                    e.SourceApp));
            }

            return entries;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            BackupCorrupt();
            return [];
        }
    }

    /// <summary>
    /// Geçmişi atomik olarak yazar (tmp + replace). Hata çağırana yansımaz: arka planda
    /// yüksek frekanslı çalışır, yazma kaybı panonun kendisinden daha önemli değildir.
    /// </summary>
    internal void Save(IReadOnlyList<IslandEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        try
        {
            string? dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var doc = new StoreDocument
            {
                Entries = new List<EntryDocument?>(entries.Count),
            };

            foreach (IslandEntry e in entries)
            {
                doc.Entries.Add(new EntryDocument
                {
                    Id = e.Id,
                    Kind = e.Kind,
                    Text = e.Text,
                    FilePath = e.FilePath,
                    CapturedAtUtc = e.CapturedAtUtc,
                    SourceApp = e.SourceApp,
                });
            }

            string json = JsonSerializer.Serialize(doc, JsonOptions);
            string tmp = StorePath + ".tmp";
            File.WriteAllText(tmp, json);

            if (File.Exists(StorePath))
            {
                File.Replace(tmp, StorePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmp, StorePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[WharfBar] island save failed: {ex.Message}");
        }
    }

    private void BackupCorrupt()
    {
        try
        {
            File.Move(StorePath, StorePath + ".bak", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[WharfBar] island backup failed: {ex.Message}");
        }
    }
}