using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Persistent store for island history. <c>config.json</c> is not used: <c>ConfigService.Save</c>
/// rewrites the whole file on every call, while clipboard changes are high-frequency.
/// Writes are <b>replaced atomically</b> (tmp + <c>File.Replace</c>: a reader sees either the whole
/// old file or the whole new one), a corrupt file is kept as <c>.bak</c>, and startup never hangs.
/// </summary>
/// <remarks>
/// <para><b>Order contract:</b> the store does <b>not</b> sort — it is a serializer, and sorting
/// would silently change a hand-edited file behind the user's back. <see cref="Load"/> preserves the
/// file order but does <b>not guarantee</b> it; the consumer (today <c>IslandService</c>) must sort
/// by <see cref="IslandEntry.CapturedAtUtc"/>, because <c>IslandPruner.Prune</c>/<c>Prepend</c>
/// take "newest first" as a precondition and use <c>Take</c>, so in the wrong order they silently
/// prune the wrong entries.</para>
/// <para><b>Concurrency:</b> <see cref="Load"/> and <see cref="Save"/> share the same lock.
/// Without the lock, a racing <see cref="Save"/> — because it uses a fixed <c>.tmp</c> path — would
/// either get an <see cref="IOException"/> from <see cref="File.WriteAllText(string, string)"/> and
/// have the error swallowed (that writer's snapshot is silently lost) or wait on the startup lock;
/// a racing <see cref="Load"/> would open the file without delete/move sharing and could break
/// <see cref="File.Replace"/> with an <see cref="IOException"/>. The lock keeps the store safe
/// regardless of how the calling service behaves.</para>
/// </remarks>
internal sealed class IslandStore
{
    /// <summary>
    /// Size ceiling applied before reading. The worst legitimate history is on the order of
    /// <c>IslandConfig.ClampMaxItems</c> (≤5000 entries) × <c>IslandPruner.MaxTextBytes</c>
    /// (64 KB); this ceiling is far above that yet stays well below the ~1 billion character
    /// string limit (OOM) for <see cref="File.ReadAllText"/>. A file that exceeds it is treated as
    /// corrupt: it is moved to <c>.bak</c> and an empty list is returned, so <c>Load</c> never overflows.
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

    /// <summary>Full path of the JSON file that holds the history.</summary>
    internal string StorePath { get; }

    /// <summary>Folder that files dropped onto the island are copied to.</summary>
    internal string DropRoot { get; }

    /// <summary>Entry wrapper that interprets the timestamp field as UTC.</summary>
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
    /// Read-side counterpart: array elements are parsed as <see cref="JsonElement"/> and the
    /// <see cref="EntryDocument"/> conversion is attempted entry by entry inside <see cref="Load"/>
    /// (so one bad field does not take down the whole file).
    /// </summary>
    private sealed class StoreDocumentRaw
    {
        public int Version { get; set; } = 1;

        public List<JsonElement> Entries { get; set; } = [];
    }

    /// <summary>
    /// Tolerant when reading the <c>kind</c> field: an unknown value (the file was hand-edited, or the
    /// kind was added in a newer version) is interpreted as <see cref="IslandEntryKind.Text"/> instead
    /// of treating the whole file as corrupt and throwing it to <c>.bak</c> — the
    /// <see cref="JsonException"/> behavior of <c>JsonStringEnumConverter</c> is not what this file
    /// wants. The JSON written is identical to earlier versions (the kind name).
    /// </summary>
    private sealed class TolerantKindConverter : JsonConverter<IslandEntryKind>
    {
        public override IslandEntryKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                return IslandEntryKind.Text;
            }

            // IsDefined is required because Enum.TryParse("999") also counts an undefined value as "success".
            return Enum.TryParse(reader.GetString(), ignoreCase: true, out IslandEntryKind parsed)
                && Enum.IsDefined(parsed)
                    ? parsed
                    : IslandEntryKind.Text;
        }

        public override void Write(Utf8JsonWriter writer, IslandEntryKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>
    /// Reads the history raw — no pruning is applied. Returns an empty list if the file is missing;
    /// if it is corrupt it is kept as <c>.bak</c> and an empty list is returned (startup does not hang).
    /// </summary>
    /// <remarks>
    /// The order inside the array is preserved but <b>not guaranteed</b>: this store does not sort.
    /// The consumer must sort by <see cref="IslandEntry.CapturedAtUtc"/> (see the
    /// <c>IslandPruner.Prune</c> "newest first" precondition).
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
                    // ReadAllText can throw OOM; that is not in the filter, so we cut it off beforehand.
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
                    // A single bad field must not make the whole history invisible: an entry that
                    // cannot be parsed is dropped, its siblings are read, and the file does not go to .bak.
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
    /// Reads a single entry; if one of its fields cannot be parsed (<see cref="JsonException"/> for
    /// any field other than <c>kind</c>) it returns <see langword="null"/> — the caller drops that entry.
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
    /// Writes the history to an atomically replaced file (tmp + replace). Errors do not surface to the
    /// caller: this runs in the background at high frequency, and a lost write matters less than the
    /// clipboard itself.
    /// </summary>
    /// <remarks>
    /// <c>File.Replace</c> instead of <c>File.Move(overwrite: true)</c> is deliberate: both are atomic
    /// on NTFS (<c>MoveFileEx(MOVEFILE_REPLACE_EXISTING)</c>), but <c>File.Replace</c> preserves the
    /// target's ACLs/attributes (a move inherits the new file's defaults) and is the same pattern as
    /// <c>ConfigService.Save</c>.
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
                        // If the file vanished after the File.Exists check, the write must not be
                        // silently lost; MoveFileEx is atomic too.
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
    /// Keeps a corrupt file under the name <c>.bak</c>. The <c>.bak</c> is a recovery point, not just a
    /// corrupt-file archive: so that a second corruption does not lose the first one's history, the
    /// existing <c>.bak</c> is rotated to <c>.bak.1</c>.
    /// </summary>
    /// <remarks>
    /// Rotation is two <c>Move</c>s instead of <c>File.Copy</c> + delete: each step is atomic on NTFS
    /// and there is no risk of a half-finished copy. If a crash happens between the two steps, the
    /// corrupt file stays in <c>island.json</c> and the next <see cref="Load"/> retries.
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
