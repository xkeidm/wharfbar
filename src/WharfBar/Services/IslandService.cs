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
/// <para><b>Dosya I/O'su UI thread'inde değil:</b> bırakılan dosyaların kopyalanması
/// (<see cref="AddDroppedFiles"/>) ve düşen kayıtların kopyalarının silinmesi STA worker'da
/// olur. Üst bar kayıtlı bir AppBar olduğu için UI thread'inde yapılan büyük bir kopyalama
/// ya da silme tüm masaüstünü dondurur.</para>
/// </remarks>
internal sealed class IslandService : IDisposable
{
    private const int SaveDebounceMs = 750;
    private const int OpenClipboardRetries = 3;
    private const int OpenClipboardRetryDelayMs = 40;

    /// <summary>STA worker hazır olmadan bu kadar beklenir; sonrası yine de sessiz devam eder.</summary>
    private const int StaReadyTimeoutMs = 1000;

    private readonly Dispatcher _dispatcher;
    private readonly IslandStore _store;
    private readonly List<IslandEntry> _entries = [];

    // STA thread'i yazıyor, UI thread'i okuyor: volatile olmadan atama görünmeyebilir ve
    // pencere ctor'da açıldığı için gelen ilk pano değişimi sta is null görüp sessizce
    // düşerdi ("ilk kopyalama kayboldu", tekrarlanması zor hata).
    private volatile Dispatcher? _staDispatcher;
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
        // Sinyal durumu başlangıçta KAPALI olmalı (STA thread'i atamayı yapıp Set() ile açar);
        // "ManualResetEventSlim(1)" değil: tek int parametresi spinCount'tir ve ctor zaten
        // kapalı başlar.
        var staReady = new ManualResetEventSlim(initialState: false);
        var staThread = new Thread(() =>
        {
            _staDispatcher = Dispatcher.CurrentDispatcher;
            staReady.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WharfBar.Island",
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        // Bekleme zaman aşımına uğrarsa hata fırlatmaz: dinleyici zaten kurulmamış olabilir,
        // bu durumda yalnızca ilk pano okuması düşer. Bu yüzden staReady kasıtlı olarak
        // Dispose edilmiyor — Set() hâlâ çağrılabilir olmalı, yoksa STA thread'inde
        // ObjectDisposedException işlenmemiş olarak süreci düşürürdü.
        staReady.Wait(StaReadyTimeoutMs);

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
        // !Config.Enabled şart: servis artık ayar kapalıyken de kurulur (ayardan sonradan
        // açılabilsin diye), ama kapalıyken island.json'a dokunmak ayak izi bırakır.
        if (_disposed || !Config.Enabled || !IsPersistent)
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
        // yeni kayitlar eski kayitlarin Id'leriyle cakisir. Math.Max: sayici geri
        // alinamaz, daha once uretilmis bir Id'ye dusmemek icin yalnizca yuksege cikarilir.
        _nextId = Math.Max(_nextId, ordered.Max(e => e.Id));

        IReadOnlyList<IslandEntry> pruned = IslandPruner.Prune(
            ordered, Config.MaxItems, DateTimeOffset.UtcNow, Config.RetentionDays, persistent: true);

        if (pruned.Count == ordered.Count)
        {
            // Budama hicbir seyi atmadi. Prune yalnizca eler (sirayi degistirmez), yani
            // sayim esitse listeler birebir ayni demektir: Commit'e gidemek her acilista
            // gereksiz bir disk yazmasi ve bir Changed bildirimi uretirdi.
            _entries.AddRange(ordered);
            return;
        }

        // Budanmış hâli diske geri yaz: kullanıcı tavanı düşürdüyse (ya da gün limiti
        // dolduysa) dosya bir sonraki açılışta aynı budanmış listeyi verir.
        Commit(pruned, added: null, raiseEntryAdded: false);
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

        // Sayisal ayarlar (MaxItems / RetentionDays) yalnizca bir sonraki yakalamada
        // uygulanirdi: kullanici tavani 200'den 20'ye indirdiginde liste 200 gostermeye
        // devam ederdi. Ayar degisimi budamayi da hemen uygulamali.
        IReadOnlyList<IslandEntry> pruned = IslandPruner.Prune(
            _entries, Config.MaxItems, DateTimeOffset.UtcNow, Config.RetentionDays, IsPersistent);
        if (pruned.Count != _entries.Count)
        {
            // LoadPersisted buradan CAGRILMAZ: o cagri _nextId'yi dusurur (K5) ve burada
            // zaten bellekte bir geri yukleme yapilacak bir sey yok.
            Commit(pruned, added: null, raiseEntryAdded: false);
            return;
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
            if (!CanPostToUi)
            {
                return;
            }

            _ = _dispatcher.BeginInvoke(() => AddEntry(entry), DispatcherPriority.Background);
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

        // Dosya kopyalama isi STA worker'a devredilir: ust bar kayitli bir AppBar oldugu icin
        // buyuk bir surukleme tum masaustunu dondururdu. Yalnizca Commit UI'da calisir.
        Dispatcher? sta = _staDispatcher;
        if (sta is null || !CanPostToUi)
        {
            return;
        }

        _ = sta.BeginInvoke(() => CopyDroppedFiles(sourcePaths), DispatcherPriority.Background);
    }

    /// <summary>STA worker'da çalışır: kayıtları kopyalar, birleştirmeyi UI'ya devreder.</summary>
    private void CopyDroppedFiles(IReadOnlyList<string> sourcePaths)
    {
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

        if (added.Count == 0 || _disposed || !CanPostToUi)
        {
            return;
        }

        _ = _dispatcher.BeginInvoke(() => MergeDroppedFiles(added), DispatcherPriority.Background);
    }

    /// <summary>UI thread'inde çalışır.</summary>
    private void MergeDroppedFiles(List<IslandEntry> added)
    {
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
        // Kapalı bir island'da geri koymak kullanıcının istemediği bir pano değişikliğidir:
        // pano, görünmeyen bir listeden gelen istekle sessizce değişmiş olur.
        if (entry.Kind == IslandEntryKind.File || !Config.Enabled)
        {
            return;
        }

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

        // added: null — kaldırılan kayıt "eklendi" değildir, raiseEntryAdded da false.
        ApplySnapshot(next, null, raiseEntryAdded: false);
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    internal void Clear() => ApplySnapshot([], null, raiseEntryAdded: false);

    /// <summary>UI thread'inde çağrılır. Yalnız bırakılan dosyaları siler, panoya dokunmaz.</summary>
    internal void DeleteDroppedFiles() => DeleteFilesOf(_entries);

    /// <summary>
    /// Verilen kayıtların bırakma kopyalarını <c>Drop</c> klasöründen siler. Kayıt listeden
    /// çıktığı anda kopyası da silinmezse <c>%AppData%\WharfBar\Drop</c> kalıcı olarak büyür:
    /// <c>MaxItems</c> 200 iken 300 dosya bırakılan kullanıcıda 100 kopya geride kalır ve
    /// <see cref="Clear"/> bırakılan dosyaların <b>tümünü</b> geride bırakır.
    /// </summary>
    private void DeleteFilesOf(IEnumerable<IslandEntry> dropped)
    {
        foreach (IslandEntry entry in dropped)
        {
            if (entry.Kind != IslandEntryKind.File
                || entry.FilePath is null
                || !File.Exists(entry.FilePath))
            {
                continue;
            }

            try
            {
                File.Delete(entry.FilePath);
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
        else if (CanPostToUi)
        {
            _ = _dispatcher.BeginInvoke(() => Commit(next, added, raiseEntryAdded), DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// UI dispatcher'ına iş atılabilir mi? Dispose/kapanma yarışında <c>BeginInvoke</c>
    /// <see cref="InvalidOperationException"/> fırlatırdı: Dispose sonrası UI dışından gelen bir
    /// <see cref="Remove"/>/<see cref="Clear"/> tam olarak bu yolu kullanır.
    /// </summary>
    private bool CanPostToUi =>
        !_disposed && !_dispatcher.HasShutdownStarted && !_dispatcher.HasShutdownFinished;

    /// <summary>UI thread'inde çalışır: tek noktada liste değiştir, kaydet, bildir.</summary>
    private void Commit(IReadOnlyList<IslandEntry> next, IslandEntry? added, bool raiseEntryAdded)
    {
        // Commit tek choke point: Remove, Clear, budama ve ApplyConfig hepsi buradan geçer,
        // yani "listeden düşen kayıt" hesabı da tek yerde yapılır.
        QueueDeleteOfDropped(next);

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

    /// <summary>
    /// <paramref name="next"/>'te olmayan kayıtların bırakma kopyalarını silmeyi STA worker'a
    /// devreder (dosya I/O'su UI thread'inde olmaz — üst bar kayıtlı bir AppBar'dır, bloklanan
    /// UI tüm masaüstünü dondurur).
    /// </summary>
    private void QueueDeleteOfDropped(IReadOnlyList<IslandEntry> next)
    {
        // Commit listeyi olduğu gibi değiştirir; aynı sayı => hiçbir şey düşmemiş demektir.
        // Bu, her pano yakalamasında geçilen sıcak yolun erken çıkışıdır.
        if (next.Count == _entries.Count)
        {
            return;
        }

        var kept = new HashSet<long>(next.Count);
        foreach (IslandEntry e in next)
        {
            _ = kept.Add(e.Id);
        }

        List<IslandEntry> dropped = [];
        foreach (IslandEntry e in _entries)
        {
            if (!kept.Contains(e.Id))
            {
                dropped.Add(e);
            }
        }

        if (dropped.Count == 0)
        {
            return;
        }

        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            return;
        }

        _ = sta.BeginInvoke(() => DeleteFilesOf(dropped), DispatcherPriority.Background);
    }

    private void RaiseChanged()
    {
        if (_dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else if (CanPostToUi)
        {
            _ = _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty), DispatcherPriority.Background);
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
        if (!CanPostToUi)
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