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
/// frekanslıdır. Yazma <b>atomik değiştirilir</b> (tmp + <c>File.Replace</c>: okuyucu ya eski
/// dosyanın tamamını ya yenisinin tamamını görür), bozuk dosya <c>.bak</c> olarak korunur ve
/// açılış asla kilitlenmez.
/// </summary>
/// <remarks>
/// <para><b>Sıra sözleşmesi:</b> depo <b>sıralamaz</b> — serileştiricidir, sıralamak elle
/// düzenlenmiş dosyayı kullanıcıdan gizlice değiştirmek olurdu. <see cref="Load"/> dosyadaki
/// sırayı korur ama onu <b>garanti etmez</b>; tüketicinin (bugün <c>IslandService</c>)
/// <see cref="IslandEntry.CapturedAtUtc"/>'ye göre sıralaması gerekir, çünkü
/// <c>IslandPruner.Prune</c>/<c>Prepend</c> "en yenisi başta"yı ön koşul olarak alır ve
/// <c>Take</c> kullandığı için yanlış sırada sessizce yanlış kayıtları budar.</para>
/// <para><b>Eşzamanlılık:</b> <see cref="Load"/> ve <see cref="Save"/> aynı kilidi paylaşır.
/// Kilit olmadan yarışan bir <see cref="Save"/>, sabit <c>.tmp</c> yolunu kullandığı için
/// ya <see cref="File.WriteAllText(string, string)"/>'te <see cref="IOException"/> alıp
/// hatanın yutulmasına (o yazıcının snapshot'ı sessizce kaybolur) ya da açılış kilidini
/// beklemesine yol açardı; yarışan bir <see cref="Load"/> ise dosyayı silme/taşıma payı olmadan
/// açtığı için <see cref="File.Replace"/>'i <see cref="IOException"/> ile bozabilirdi. Kilit
/// depoyu çağıran servisin davranışından bağımsız olarak güvenli kılar.</para>
/// </remarks>
internal sealed class IslandStore
{
    /// <summary>
    /// Okunmadan önce uygulanan boyut tavanı. Geçmişin en kötü meşru hâli
    /// <c>IslandConfig.ClampMaxItems</c> (≤5000 kayıt) × <c>IslandPruner.MaxTextBytes</c>
    /// (64 KB) mertebesindedir; bu tavan onun çok üstünde ama <see cref="File.ReadAllText"/>
    /// için ~1 milyar karakterlik dize sınırına (OOM) uzakta kalır. Aşan dosya bozuk sayılır:
    /// <c>.bak</c>'a alınır ve boş döner, böylece <c>Load</c> hiçbir koşulda dışarı taşmaz.
    /// </summary>
    private const long MaxStoreBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new TolerantKindConverter() },
    };

    private readonly object _ioGate = new();

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

        public List<EntryDocument> Entries { get; set; } = [];
    }

    /// <summary>
    /// Okuma tarafındaki karşılığı: dizi elemanları <see cref="JsonElement"/> olarak çözülür,
    /// <see cref="EntryDocument"/> dönüşümü <see cref="Load"/> içinde kayıt kayıt denenir (tek bir
    /// bozuk alan bütün dosyayı düşürmesin diye).
    /// </summary>
    private sealed class StoreDocumentRaw
    {
        public int Version { get; set; } = 1;

        public List<JsonElement> Entries { get; set; } = [];
    }

    /// <summary>
    /// <c>kind</c> alanını okurken toleranslıdır: bilinmeyen bir değer (dosya elle düzenlenmiş
    /// ya da daha yeni sürümde eklenmiş bir tür) tüm dosyayı bozuk sayıp <c>.bak</c>'a atmak yerine
    /// <see cref="IslandEntryKind.Text"/> olarak yorumlanır — <c>JsonStringEnumConverter</c>'ın
    /// <see cref="JsonException"/> davranışı bu dosyada istenen değildir. Yazarken üretilen JSON
    /// önceki sürümlerle birebir aynıdır (tür adı).
    /// </summary>
    private sealed class TolerantKindConverter : JsonConverter<IslandEntryKind>
    {
        public override IslandEntryKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                return IslandEntryKind.Text;
            }

            // Enum.TryParse("999") tanımsız bir değeri de "başarılı" saydığı için IsDefined şart.
            return Enum.TryParse(reader.GetString(), ignoreCase: true, out IslandEntryKind parsed)
                && Enum.IsDefined(parsed)
                    ? parsed
                    : IslandEntryKind.Text;
        }

        public override void Write(Utf8JsonWriter writer, IslandEntryKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>
    /// Geçmişi ham olarak okur — budama uygulanmaz. Dosya yoksa boş liste döner; bozuksa
    /// <c>.bak</c> olarak korunur ve boş döner (açılış kilitlenmez).
    /// </summary>
    /// <remarks>
    /// Dizi içindeki sıra korunur ama <b>garanti değildir</b>: bu depo sıralamaz. Tüketici
    /// <see cref="IslandEntry.CapturedAtUtc"/>'ye göre sıralamak zorundadır (bkz.
    /// <c>IslandPruner.Prune</c> "en yenisi başta" ön koşulu).
    /// </remarks>
    internal IReadOnlyList<IslandEntry> Load()
    {
        lock (_ioGate)
        {
            if (!File.Exists(StorePath))
            {
                return [];
            }

            try
            {
                if (new FileInfo(StorePath).Length > MaxStoreBytes)
                {
                    // ReadAllText OOM atabilir; filtrede olmayan istisnadır, önceden kesiyoruz.
                    BackupCorrupt();
                    return [];
                }

                string json = File.ReadAllText(StorePath);
                StoreDocumentRaw? doc = JsonSerializer.Deserialize<StoreDocumentRaw>(json, JsonOptions);
                if (doc?.Entries is null)
                {
                    return [];
                }

                var entries = new List<IslandEntry>(doc.Entries.Count);
                foreach (JsonElement element in doc.Entries)
                {
                    // Tek bir bozuk alan tüm geçmişi görünmez yapmamalı: çözümlenemeyen kayıt
                    // atılır, kardeşleri okunur, dosya .bak'a gitmez.
                    if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    {
                        continue;
                    }

                    if (TryReadEntry(element) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }

                return entries;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                BackupCorrupt();
                return [];
            }
        }
    }

    /// <summary>
    /// Tek bir kaydı okur; alanlarından biri çözümlenemiyorsa (<c>kind</c> dışındaki bir alan için
    /// <see cref="JsonException"/>) <see langword="null"/> döner — çağıran bu kaydı atar.
    /// </summary>
    private static IslandEntry? TryReadEntry(JsonElement element)
    {
        try
        {
            EntryDocument? e = JsonSerializer.Deserialize<EntryDocument>(element, JsonOptions);
            if (e is null)
            {
                return null;
            }

            return new IslandEntry(
                e.Id,
                e.Kind,
                e.Text ?? string.Empty,
                e.FilePath,
                DateTime.SpecifyKind(e.CapturedAtUtc, DateTimeKind.Utc),
                e.SourceApp);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Debug.WriteLine($"[WharfBar] island entry skipped: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Geçmişi atomik değiştirilen bir dosyaya yazar (tmp + replace). Hata çağırana yansımaz:
    /// arka planda yüksek frekanslı çalışır, yazma kaybı panonun kendisinden daha önemli
    /// değildir.
    /// </summary>
    /// <remarks>
    /// <c>File.Move(overwrite: true)</c> yerine <c>File.Replace</c> bilinçlidir: ikisi de NTFS'de
    /// atomiktir (<c>MoveFileEx(MOVEFILE_REPLACE_EXISTING)</c>), ama <c>File.Replace</c> hedefin
    /// ACL/özniteliklerini korur (taşıma yeni dosyanın varsayılanlarını devralır) ve
    /// <c>ConfigService.Save</c> ile aynı kalıptır.
    /// </remarks>
    internal void Save(IReadOnlyList<IslandEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_ioGate)
        {
            try
            {
                string? dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var doc = new StoreDocument
                {
                    Entries = new List<EntryDocument>(entries.Count),
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
                    try
                    {
                        File.Replace(tmp, StorePath, destinationBackupFileName: null);
                    }
                    catch (FileNotFoundException)
                    {
                        // Dosya File.Exists ile kontrol edildikten sonra kaybolduysa yazma
                        // sessizce kaybolmasın; MoveFileEx da atomik.
                        File.Move(tmp, StorePath, overwrite: true);
                    }
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
    }

    /// <summary>
    /// Bozuk dosyayı <c>.bak</c> adıyla saklar. <c>.bak</c> bir kurtarma noktasıdır, yalnız bozuk
    /// dosya arşivi değil: ikinci bir bozulmada ilk bozulmanın geçmişi kaybolmasın diye mevcut
    /// <c>.bak</c> <c>.bak.1</c>'e döndürülür.
    /// </summary>
    /// <remarks>
    /// Döndürme <c>File.Copy</c> + silme yerine iki <c>Move</c>'tur: her adım NTFS'de atomiktir ve
    /// kopyanın yarım kalma riski yoktur. İki hamle arasında bir çökme olursa bozuk dosya
    /// <c>island.json</c>'de durur ve sonraki <see cref="Load"/> yeniden dener.
    /// </remarks>
    private void BackupCorrupt()
    {
        try
        {
            string backup = StorePath + ".bak";
            if (File.Exists(backup))
            {
                File.Move(backup, backup + ".1", overwrite: true);
            }

            File.Move(StorePath, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[WharfBar] island backup failed: {ex.Message}");
        }
    }
}