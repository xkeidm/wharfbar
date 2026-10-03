using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Island'ın yaşam döngüsü: pano dinleyicisi, STA okuma thread'i, tek zaman çizelgesi ve
/// debounce'lu diske yazma. Saf mantık <see cref="IslandPruner"/>'da; burada yalnızca
/// Win32/disk/thread yönetimi ve orkestrasyon vardır.
/// </summary>
/// <remarks>
/// <para><b>Tek zaman çizelgesi:</b> <see cref="Entries"/>, <c>Config</c> ve diske yazılan
/// snapshot <b>yalnızca UI dispatcher'ında</b> okunur/başlanır. Pano okuması STA worker'da
/// yapılır ama yakalanan kaydı ekleme işi UI thread'ine devredilir; aynı şekilde
/// debounce sayacının callback'i de UI dispatcher'ına atlanır. <see cref="List{T}"/> eşzamanlı
/// okuma/yazmaya dayanıksızdır ve bir timer callback'i içinde atılan istisna süreci
/// düşürürdü.</para>
/// <para><b>Thread kuralı:</b> kurucu ve <see cref="Dispose"/> UI thread'inde çağrılmalıdır
/// (mesaj-only <c>HwndSource</c> ve pano dinleyicisi bunu gerektirir).</para>
/// </remarks>
internal sealed class IslandService : IDisposable
{
    private const int SaveDebounceMs = 750;
    private const int OpenClipboardRetries = 3;
    private const int OpenClipboardRetryDelayMs = 40;

    private readonly Dispatcher _dispatcher;
    private readonly IslandStore _store;
    private readonly List<IslandEntry> _entries = [];
    private Dispatcher? _staDispatcher;
    private ClipboardMessageWindow? _listener;
    private System.Threading.Timer? _saveTimer;
    private long _nextId;
    private bool _disposed;
    private volatile bool _savePending;

    /// <summary>UI thread'inde kurulur.</summary>
    internal IslandService(Dispatcher dispatcher, IslandStore store, IslandConfig config)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Config = config;

        // Pano okuması STA worker'da: OpenClipboard bloklayabilir, UI thread'i kilitlememek
        // zorunlu. TrayMirrorService ile aynı kalıp.
        var staThread = new Thread(() =>
        {
            _staDispatcher = Dispatcher.CurrentDispatcher;
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WharfBar.Island",
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        _saveTimer = new System.Threading.Timer(
            _ => OnSaveTimerTick(), null, Timeout.Infinite, Timeout.Infinite);

        if (Config.Enabled)
        {
            StartListening();
        }

        LoadPersisted();
    }

    internal IslandConfig Config { get; private set; }

    internal IReadOnlyList<IslandEntry> Entries => _entries;

    internal IslandEntry? Latest => _entries.Count > 0 ? _entries[0] : null;

    internal event EventHandler? Changed;

    /// <summary>Yeni kayıt eklendiğinde pill'in nabzını tetiklemek için dışarıdan okunur.</summary>
    internal event EventHandler? EntryAdded;

    /// <summary>Persistent mod mu? Karşılaştırma her zaman normalize edilmiş değerle yapılır.</summary>
    private bool IsPersistent =>
        IslandNormalizer.NormalizePersistence(Config.Persistence) == IslandConfig.PersistencePersistent;

    /// <summary>
    /// Persistent modda kayıtlı geçmişi geri yükler. Depo ham kayıt döndürür ve sıra garantisi
    /// vermez (serileştiricidir; elle düzenlenmiş dosyayı gizlice yeniden sıralamak
    /// kullanıcının dosyasını değiştirmek olurdu) — bu yüzden sıralama ve budama burada,
    /// kullanicinin ayarlariyla yapilir.
    /// </summary>
    private void LoadPersisted()
    {
        if (_disposed || !IsPersistent)
        {
            return;
        }

        IReadOnlyList<IslandEntry> loaded = _store.Load();
        if (loaded.Count == 0)
        {
            return;
        }

        var ordered = loaded
            .OrderByDescending(e => e.CapturedAtUtc)
            .ToList();

        // Id monotonik olmali: yeniden yuklenen en yuksek Id'den devam et, aksi halde
        // yeni kayitlar eski kayitlarin Id'leriyle cakisir.
        _nextId = ordered.Count > 0 ? ordered.Max(e => e.Id) : 0;

        // Budanmış hâli diske geri yaz: kullanıcı tavanı düşürdüyse (ya da gün limiti
        // dolduysa) dosya bir sonraki açılışta aynı budanmış listeyi verir.
        Commit(
            IslandPruner.Prune(ordered, Config.MaxItems, DateTimeOffset.UtcNow, Config.RetentionDays, persistent: true),
            added: null,
            raiseEntryAdded: false);
    }

    internal void ApplyConfig(IslandConfig config)
    {
        Config = config;
        if (Config.Enabled)
        {
            StartListening();
        }
        else
        {
            StopListening();
        }

        RaiseChanged();
    }

    private void StartListening()
    {
        if (_listener is not null || _disposed)
        {
            return;
        }

        _listener = new ClipboardMessageWindow();
        _listener.ClipboardChanged += OnClipboardChanged;
    }

    private void StopListening()
    {
        if (_listener is null)
        {
            return;
        }

        _listener.ClipboardChanged -= OnClipboardChanged;
        _listener.Dispose();
        _listener = null;
    }

    /// <summary>UI thread'inde tetiklenir; okuma işini STA worker'a devreder.</summary>
    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            return;
        }

        _ = sta.BeginInvoke(
            () =>
            {
                try
                {
                    CaptureFromClipboard();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WharfBar] island capture failed: {ex.Message}");
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>STA worker'da çalışır.</summary>
    private void CaptureFromClipboard()
    {
        if (!TryReadWithRetry(out string? text, out bool optOut, out int byteCount))
        {
            return;
        }

        if (!IslandPruner.ShouldCapture(optOut, text, byteCount))
        {
            return;
        }

        AddEntry(new IslandEntry(
            Interlocked.Increment(ref _nextId),
            IslandEntryKind.Text,
            text!,
            null,
            DateTime.UtcNow,
            ClipboardNative.GetOwnerProcessName()));
    }

    private static bool TryReadWithRetry(out string? text, out bool optOut, out int byteCount)
    {
        for (int attempt = 0; attempt < OpenClipboardRetries; attempt++)
        {
            if (ClipboardNative.TryReadText(out text, out optOut, out byteCount))
            {
                return true;
            }

            // Opt-out işareti içerik boşluğu değildir: yeniden denemek kaydı engellemez.
            if (optOut)
            {
                return false;
            }

            if (attempt < OpenClipboardRetries - 1)
            {
                Thread.Sleep(OpenClipboardRetryDelayMs);
            }
        }

        // Pano saniyede bir kez başka bir uygulama tarafından kilitli olabiliyor; bu bir
        // kullanıcı hatası değil, bu yüzden üç denemeden sonra sessizce düşürülür.
        text = null;
        optOut = false;
        byteCount = 0;
        return false;
    }

    /// <summary>
    /// Yeni kaydı başa ekler. STA worker'dan da çağrılabilir; zaman çizelgesi hesabı UI
    /// thread'inde yapılır (<see cref="_entries"/> yalnızca orada okunur).
    /// </summary>
    private void AddEntry(IslandEntry entry)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => AddEntry(entry), DispatcherPriority.Background);
            return;
        }

        IReadOnlyList<IslandEntry> merged = IslandPruner.Prepend(
            _entries,
            entry,
            Config.MaxItems,
            DateTimeOffset.UtcNow,
            Config.RetentionDays,
            IsPersistent);

        ApplySnapshot(merged, entry, raiseEntryAdded: true);
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    internal void AddDroppedFiles(IReadOnlyList<string> sourcePaths)
    {
        if (_disposed || sourcePaths.Count == 0)
        {
            return;
        }

        List<IslandEntry> added = [];
        foreach (string source in sourcePaths)
        {
            if (!File.Exists(source))
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(_store.DropRoot);
                string target = UniqueDropPath(Path.GetFileName(source));
                File.Copy(source, target, overwrite: false);
                added.Add(new IslandEntry(
                    Interlocked.Increment(ref _nextId),
                    IslandEntryKind.File,
                    Path.GetFileName(target),
                    target,
                    DateTime.UtcNow,
                    null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[WharfBar] island drop copy failed for {source}: {ex.Message}");
            }
        }

        if (added.Count == 0)
        {
            return;
        }

        IReadOnlyList<IslandEntry> merged = _entries;
        IslandEntry? last = null;
        foreach (IslandEntry e in added)
        {
            merged = IslandPruner.Prepend(
                merged, e, Config.MaxItems, DateTimeOffset.UtcNow,
                Config.RetentionDays, IsPersistent);
            last = e;
        }

        // Yalnızca sonuncusu "eklendi" sayılır: tek bildirim, tek nabız.
        ApplySnapshot(merged, last, raiseEntryAdded: true);
    }

    private string UniqueDropPath(string fileName)
    {
        string candidate = Path.Combine(_store.DropRoot, fileName);
        int suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(_store.DropRoot, IslandPruner.BuildDropFileName(fileName, suffix));
            suffix++;
        }

        return candidate;
    }

    /// <summary>UI thread'inde çağrılır. Metni panoya yazar (yapıştırma yapılmaz).</summary>
    internal void PutBack(IslandEntry entry)
    {
        if (entry.Kind == IslandEntryKind.File)
        {
            return;
        }

        Dispatcher? sta = _staDispatcher;
        if (sta is null)
        {
            return;
        }

        _ = sta.BeginInvoke(
            () =>
            {
                try
                {
                    if (!ClipboardNative.TryWriteText(entry.Text))
                    {
                        Debug.WriteLine("[WharfBar] island put-back write failed");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WharfBar] island put-back failed: {ex.Message}");
                }
            },
            DispatcherPriority.Background);
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    internal void Remove(IslandEntry entry)
    {
        IReadOnlyList<IslandEntry> next = _entries.Where(e => e.Id != entry.Id).ToList();
        ApplySnapshot(next, entry, raiseEntryAdded: false);
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    internal void Clear() => ApplySnapshot([], null, raiseEntryAdded: false);

    /// <summary>UI thread'inde çağrılır. Yalnız bırakılan dosyaları siler, panoya dokunmaz.</summary>
    internal void DeleteDroppedFiles()
    {
        foreach (IslandEntry e in _entries.Where(x => x.Kind == IslandEntryKind.File))
        {
            if (e.FilePath is null || !File.Exists(e.FilePath))
            {
                continue;
            }

            try
            {
                File.Delete(e.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[WharfBar] island drop delete failed: {ex.Message}");
            }
        }
    }

    private void ApplySnapshot(IReadOnlyList<IslandEntry> next, IslandEntry? added, bool raiseEntryAdded)
    {
        if (_dispatcher.CheckAccess())
        {
            Commit(next, added, raiseEntryAdded);
        }
        else
        {
            _dispatcher.BeginInvoke(() => Commit(next, added, raiseEntryAdded), DispatcherPriority.Background);
        }
    }

    /// <summary>UI thread'inde çalışır: tek noktada liste değiştir, kaydet, bildir.</summary>
    private void Commit(IReadOnlyList<IslandEntry> next, IslandEntry? added, bool raiseEntryAdded)
    {
        _entries.Clear();
        _entries.AddRange(next);

        if (IsPersistent)
        {
            ScheduleSave();
        }

        RaiseChanged();

        if (raiseEntryAdded && added is not null)
        {
            EntryAdded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RaiseChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty), DispatcherPriority.Background);
        }
    }

    private void ScheduleSave()
    {
        // Dispose sonrası gelen bir Commit'in sayacı yeniden canlandırmasına gerek yok:
        // Dispose zaten bekleyen yazmayı senkron olarak boşalttı, üstüne Timer.Change
        // yapmak ObjectDisposedException fırlatırdı.
        if (_disposed)
        {
            return;
        }

        _savePending = true;
        _saveTimer?.Change(SaveDebounceMs, Timeout.Infinite);
    }

    /// <summary>
    /// Timer callback'i threadpool'da çalışır; snapshot'ı UI thread'inde okumak için işi oraya
    /// atlar. <see cref="_dispatcher"/> kapanmışsa yazma zaten Dispose'ta yapılmıştır.
    /// </summary>
    private void OnSaveTimerTick()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = _dispatcher.BeginInvoke(FlushSave, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            // Kapanma yarışı: dispatcher BeginInvoke'yi reddedebilir. Yazı kaybı panonun
            // kendisinden daha önemli değil, ama görünmez de olmamalı.
            Debug.WriteLine($"[WharfBar] island save dispatch failed: {ex.Message}");
        }
    }

    /// <summary>UI thread'inde çalışır. Bekleyen debounce yazmasını diske yazar.</summary>
    private void FlushSave()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        List<IslandEntry> snapshot = [.. _entries];

        // Kucuk liste: senkron yazmak UI thread'ini anlamli bicimde bloklamaz, ama kapanista
        // kuyruga atmak sureci cikisyla yaristirir ve son kopyalamayi sessizce dusururdu.
        _store.Save(snapshot);
    }

    /// <summary>UI thread'inde çağrılmalıdır (HwndSource.Dispose UI thread'i ister).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopListening();
        _saveTimer?.Dispose();

        // Pending debounced write must not be lost on shutdown.
        FlushSave();

        _staDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
        _staDispatcher = null;
    }
}