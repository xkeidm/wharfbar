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
/// <para><b>Dosya I/O'su UI thread'inde değil:</b> <c>Drop</c> altında kalan eski kopyaların
/// silinmesi STA worker'da olur; <c>island.json</c>'un serileştirilip diske yazılması da
/// STA worker'a verilir (snapshot UI thread'inde alınır). Üst bar kayıtlı bir AppBar olduğu
/// için UI thread'inde yapılan büyük bir silme ya da ~13 MB'lık bir JSON yazma tüm
/// masaüstünü dondurur. Yeni bırakmalar kopyalanmaz (<see cref="AddDroppedFiles"/> orijinal
/// yolu kaydeder, <c>Drop</c> klasörü açılmaz); kopyalama I/O'su olmadığı için bu iş STA
/// worker'a atlanmaz. Tek istisna <see cref="Dispose"/>'daki son yazma: senkron kalır,
/// çünkü kuyruğa atmak son yazmayı süreç sonuyla yarıştırıp sessizce düşürüyordu.</para>
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

    /// <summary>
    /// Kayıtlı geçmiş bu servis ömründe zaten denendi mi? <see cref="LoadPersisted"/>
    /// <c>_entries</c>'in boş olduğunu varsayar, bu yüzden ikinci bir çağrı aynı kayıtları
    /// iki kez listelerdi.
    /// </summary>
    private bool _persistedLoaded;

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

        // Bekleme zaman aşımına uğrarsa hata fırlatmaz: STA thread'i geç başlıyorsa bu
        // pencere tolere edilir. Ama zaman aşımı gerçekleşirse _staDispatcher null kalır ve
        // OnClipboardChanged her seferinde erken çıkar — oturum boyunca HİÇBİR yakalama
        // kaydedilmez. Bu yüzden görünmez kalmaması için burada loglanır. staReady kasıtlı
        // olarak Dispose edilmiyor — Set() hâlâ çağrılabilir olmalı, yoksa STA thread'inde
        // ObjectDisposedException işlenmemiş olarak süreci düşürürdü.
        if (!staReady.Wait(StaReadyTimeoutMs))
        {
            Debug.WriteLine("[WharfBar] island STA worker not ready; clipboard capture is skipped until it is");
        }

        _saveTimer = new System.Threading.Timer(
            _ => OnSaveTimerTick(), null, Timeout.Infinite, Timeout.Infinite);

        // Yükleme, dinleyiciyi kurmadan ÖNCE (ApplyConfig ile aynı sıra): StartListening ile
        // pano dinlemesi açılır açılmaz bir kopyalama gelebilir ve STA worker
        // Interlocked.Increment(ref _nextId) çalıştırabilir; LoadPersisted'taki Math.Max
        // yazmayı yutar ve aynı Id iki kayda düşer. Yükleme önce yapılınca pencere tamamen
        // kapanır (id sayacı yalnız UI'da ilerler).
        LoadPersisted();

        if (Config.Enabled)
        {
            StartListening();
        }
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

        // Servis ömrü boyunca en çok bir kez. Yukleme geçmişi _entries'in üstüne ekler
        // (AddRange), _entries'le birleştirir ya da tamamen değiştirir (Commit), yani ikinci
        // bir çağrı aynı kayıtları iki kez listelerdi. Bekleyen tek istisna: yukarıdaki
        // kapıdan elenen çağrı bayrağı yakmaz, çünkü koşullar sonraki ApplyConfig'de
        // değişebilir.
        if (_persistedLoaded)
        {
            return;
        }

        _persistedLoaded = true;

        IReadOnlyList<IslandEntry> loaded = _store.Load();
        if (loaded.Count == 0)
        {
            return;
        }

        var ordered = loaded
            .OrderByDescending(e => e.CapturedAtUtc)
            .ToList();

        // Id monotonik olmali: ilk kurulumda (oturum kaydi yokken) yeni kayitlarin sayaci
        // 1'den baslar ve yuklenen kayitlarin Id'leriyle cakisir. Math.Max: sayici geri
        // alinamaz, daha once uretilmis bir Id'ye dusmemek icin yalnizca yuksege cikarilir.
        _nextId = Math.Max(_nextId, ordered.Max(e => e.Id));

        // Oturumluk -> kalici gecisi: oturumun kayitlari hala listede ama diskte YOK
        // (session modunda ScheduleSave calismaz). Yalnizca diskten okunanlari yazmak
        // ("degistirmek") kullaniciyi bu oturumda kopyaladiklarindan sessizce atardi,
        // bu yuzden iki liste tek liste olusturulup birlikte budanir.
        bool merging = _entries.Count > 0;
        if (merging)
        {
            ordered = MergeWithSession(ordered);
        }

        IReadOnlyList<IslandEntry> pruned = IslandPruner.Prune(
            ordered, Config.MaxItems, DateTimeOffset.UtcNow, Config.RetentionDays, IsPersistent);

        if (!merging && pruned.Count == ordered.Count)
        {
            // Budama hicbir seyi atmadi. Prune yalnizca eler (sirayi degistirmez), yani
            // sayim esitse listeler birebir ayni demektir: Commit'e gidemek her acilista
            // gereksiz bir disk yazmasi ve bir Changed bildirimi uretirdi.
            _entries.AddRange(ordered);

            // Ama bildirim atlanamaz: AddRange Commit'i (ve RaiseChanged'i) atlar. Kapali
            // baslatilan ada ayardan acildiginda liste burada sessizce dolarsa pencere
            // abone degilse pill bayat kalir — "gecmis var ama gosterilmiyor".
            RaiseChanged();
            return;
        }

        // Budanmış hâli diske geri yaz: kullanıcı tavanı düşürdüyse (ya da gün limiti
        // dolduysa) dosya bir sonraki açılışta aynı budanmış listeyi verir.
        Commit(pruned, added: null, raiseEntryAdded: false);
    }

    /// <summary>
    /// Oturum kayıtlarıyla diske yazılmış geçmişi tek listede birleştirir (en yenisi başta).
    /// UI thread'inde çağrılır.
    /// </summary>
    /// <remarks>
    /// <para><b>Id birleştirme anahtarı olamaz.</b> Her süreç 1'den başladığı için oturum ve
    /// kalıcı kayıtlar aynı Id'yi taşır; <c>DistinctBy(e =&gt; e.Id)</c> ilkini (oturum kaydını)
    /// saklayıp kalıcı kaydı sessizce düşürüyordu. Varsayılan kalıcılık modu <c>session</c>
    /// (<c>AppConfig.Persistence</c>) olduğu için bu yaygın yoldu: ölçüldü — 10 kalıcı + 3
    /// oturum kaydı → 10 kayıt; <c>kalici-1..3</c> hem listeden hem <c>island.json</c>'dan
    /// silindi (<c>Commit</c> → <c>ScheduleSave</c> dosyayı geri yazdı, diskte 7/10 kaldı).</para>
    /// <para><b>Kimlik yerine içerik:</b> <c>(Kind, Text, FilePath)</c> üçlüsü —
    /// <c>IslandPruner.IsSameContent</c> ile aynı kurallar (<c>Text</c> ordinal,
    /// <c>FilePath</c> ordinal-ignore-case). Liste yeniden eskiye sıralandığı için ilk görülen
    /// kayıt en yenisidir ve kazanır.</para>
    /// <para><b>Yüklenen kayıtların Id'si <c>_nextId</c>'nin üstüne kaydırılır.</b> Önceki süreç
    /// neslinin sayacı anlamsızdır (her açılışta 1'den başlar), ama aynı Id iki kayıtta durursa
    /// <see cref="Remove"/>'un Id karşılaştırması ve <see cref="QueueDeleteOfDropped"/>'in üyelik
    /// hesabı yanılır. Oturum kayıtlarının Id'si korunur: UI'da tutulan kayıt referansları
    /// (seçili öğe, <c>Remove</c>/<c>PutBack</c> argümanı) geçerli kalsın.</para>
    /// </remarks>
    private List<IslandEntry> MergeWithSession(IReadOnlyList<IslandEntry> loaded)
    {
        var renumbered = new List<IslandEntry>(loaded.Count);
        foreach (IslandEntry e in loaded)
        {
            renumbered.Add(e with { Id = Interlocked.Increment(ref _nextId) });
        }

        var seen = new HashSet<IslandEntry>(EntryContentComparer.Instance);
        var merged = new List<IslandEntry>(_entries.Count + renumbered.Count);
        foreach (IslandEntry e in _entries
            .Concat(renumbered)
            .OrderByDescending(x => x.CapturedAtUtc))
        {
            if (seen.Add(e))
            {
                merged.Add(e);
            }
        }

        return merged;
    }

    /// <summary>UI thread'inde çağrılır.</summary>
    internal void ApplyConfig(IslandConfig config)
    {
        bool wasEnabled = Config.Enabled;
        bool wasPersistent = IsPersistent;
        Config = config;
        if (Config.Enabled)
        {
            // Yükleme, dinleyiciyi kurmadan ÖNCE. StartListening ile pano dinlemesi
            // açılır açılmaz bir kopyalama gelebilir ve STA worker
            // Interlocked.Increment(ref _nextId) çalıştırabilir; LoadPersisted'taki
            // Math.Max yazmayı yutar ve aynı Id iki kayda düşer. Yükleme önce
            // yapılınca pencere tamamen kapanır (id sayacı yalnız UI'da ilerler).
            //
            // Tetikleyici iki geçişi de kapsar:
            //  - false -> true: kapalı başlatılan ada ayardan açıldı (round 2).
            //  - session -> persistent: varsayılan mod oturumluk olduğu için bu yol
            //    hiç yükleme yapmıyordu ve kayıtlı geçmiş ancak yeniden başlatmada
            //    görünüyordu.
            if ((!wasEnabled || (!wasPersistent && IsPersistent)) && !_persistedLoaded)
            {
                LoadPersisted();
            }

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
            // Yukleme yukarida, gecis tetiklemesiyle yapildi; burada tekrar yuklenmez
            // (_persistedLoaded) ve Commit zaten RaiseChanged cagirdigi icin ikinci bir
            // bildirim olurdu.
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

    /// <summary>
    /// UI thread'inde çağrılır. Bırakılan dosyayı <c>Drop</c> klasörüne kopyalamaz;
    /// <see cref="IslandEntryKind.File"/> kaydının <see cref="IslandEntry.FilePath"/>'i
    /// orijinal tam yoldur, <see cref="IslandEntry.Text"/> dosya adıdır. <c>Drop</c>
    /// klasörü bu yüzden oluşturulmaz. Dizinler ve olmayan yollar
    /// <see cref="File.Exists"/> ile elenir. Birden fazla dosya
    /// <see cref="MergeDroppedFiles"/> ile birleşir; "eklendi" bildirimi yalnız son
    /// kayıt içindir.
    /// </summary>
    /// <remarks>
    /// Kopyalama kalkınca STA worker'a atlamak için bir dosya I/O'su kalmadı
    /// (<c>File.Exists</c> metadata kontrolüdür). İş çağıranın thread'inde, yani UI
    /// dispatcher'ında biter. UI dışında çağrılırsa kayıt listesi orada yazılmasın diye
    /// iş UI'ya devredilir — zaman çizelgesi sözleşmesi değişmez.
    /// </remarks>
    internal void AddDroppedFiles(IReadOnlyList<string> sourcePaths)
    {
        if (_disposed || sourcePaths.Count == 0)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            if (!CanPostToUi)
            {
                return;
            }

            _ = _dispatcher.BeginInvoke(() => AddDroppedFiles(sourcePaths), DispatcherPriority.Background);
            return;
        }

        List<IslandEntry> added = [];
        foreach (string source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(source);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                Debug.WriteLine($"[WharfBar] island drop path invalid: {source} ({ex.Message})");
                continue;
            }

            string name = Path.GetFileName(full);
            if (name.Length == 0)
            {
                continue;
            }

            added.Add(new IslandEntry(
                Interlocked.Increment(ref _nextId),
                IslandEntryKind.File,
                name,
                full,
                DateTime.UtcNow,
                null));
        }

        if (added.Count == 0)
        {
            return;
        }

        MergeDroppedFiles(added);
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

    /// <summary>
    /// UI thread'inde çağrılır. Tüm kayıtları listeden düşürür ama dosyaları silmez:
    /// rafın orijinal yolu da, <c>Drop</c> altındaki eski kopya da diskte kalır.
    /// Ayarlarda "Bırakılan dosyaları sil" ile "Tüm geçmişi temizle" iki ayrı butondur
    /// ve <c>Drop</c> altındaki kopyaları yalnız ilki siler. Önceki davranışta
    /// "geçmişi temizle" diyen kullanıcı dosya kopyalarını haberi olmadan kaybediyordu
    /// ve iki buton gereksiz bir tekrar oluyordu. Kopyaların diskte kalması geri
    /// alınamaz veri kaybı değildir; <see cref="DeleteDroppedFiles"/> onları temizler.
    /// <c>Drop</c> dışındaki bir raf dosyası bu eylemle de silinmez.
    /// </summary>
    internal void Clear() =>
        ApplySnapshot([], null, raiseEntryAdded: false, deleteDroppedFiles: false);

    /// <summary>
    /// UI thread'inde çağrılır. <c>Drop</c> klasörünü <b>tümüyle</b> boşaltır (alt klasörler
    /// dâhil) ve dosya kayıtlarını listeden düşürür; panoya dokunmaz. Raf kaydı
    /// <c>Drop</c> dışındaysa yalnız listeden çıkar — orijinal dosya silinmez.
    /// </summary>
    /// <remarks>
    /// <para><b>Klasör taranır, <c>_entries</c> taranmaz.</b> <see cref="Clear"/> dosya
    /// kopyalarını silmez (R30); "Tüm geçmişi temizle"ye önce basıldığında kayıtlar düşer ve
    /// liste boşalır — yalnız listeye bakan bir temizlik o andan sonra hiçbir şey silmeden
    /// çıkar, yani ayar sayfasındaki buton sessizce işlevsiz kalır. Aynı sebeple tek listeden
    /// hesaplanamayan yetimler de (MaxItems/retention budamasıyla düşenler dâhil) kapsanır;
    /// kayıt–dosya eşleşmesi kurmaya gerek kalmaz. Tarama <c>Drop</c> kökünün dışına çıkmaz:
    /// rafın orijinal yolu bu klasörde değilse dosya durur.</para>
    /// <para>Silme STA worker'da olur (sınıf kuralı: dosya I/O'su UI thread'inde olmaz);
    /// kayıtların listeden düşürülmesi ise <c>_entries</c> UI-only olduğu için çağıranın
    /// thread'inde olur. Sıralama önemsiz: kaydın düşürülmesi kopyanın silinmesinden bağımsız,
    /// ikisi de aynı kullanıcı eyleminin parçası.</para>
    /// </remarks>
    internal void DeleteDroppedFiles()
    {
        // Kayıtlar burada düşürülür: kopya silindikten sonra listede kalan bir dosya kaydı
        // var olmayan bir dosyayı gösterirdi. Dosya silme aşağıda STA'da.
        IReadOnlyList<IslandEntry> next = _entries.Where(e => e.Kind != IslandEntryKind.File).ToList();
        if (next.Count != _entries.Count)
        {
            // deleteDroppedFiles: false — Commit'in "listeden düşen kayıtın kopyasını sil"
            // yolu burada yanlış ve gereksiz: aynı dosyaları ikinci kez kuyruğa atardı.
            ApplySnapshot(next, null, raiseEntryAdded: false, deleteDroppedFiles: false);
        }

        // Kayıt yoksa da klasör taranır: "Tüm geçmişi temizle" sonrası asıl durum budur.
        Dispatcher? sta = _staDispatcher;
        if (sta is null || _disposed)
        {
            return;
        }

        _ = sta.BeginInvoke(DeleteDropFolderContents, DispatcherPriority.Background);
    }

    /// <summary>
    /// <c>Drop</c> klasörünü tarayarak boşaltır (STA worker). Klasör yoksa sessizce çıkar —
    /// hiç dosya bırakılmamış olmak normal durumdur.
    /// </summary>
    private void DeleteDropFolderContents()
    {
        if (!TryGetDropRoot(out string root, out string prefix))
        {
            return;
        }

        if (!Directory.Exists(root))
        {
            return;
        }

        int deleted = 0;
        try
        {
            // ReparsePoint işaretli dizinlere (junction / sembolik bağ) GİRİLMEZ.
            // Path.GetFullPath reparse noktalarını çözmediği için Drop içine yerleştirilmiş
            // bir junction, taramayı Drop DIŞINA çıkarıyor (ölçüldü: …\Drop\baglanti\
            // tehlikeli.docx listeleniyordu) ve TryDeleteDropCopy'nin ön ek kontrolü o yolu
            // "Drop altında" sayıyor — yani Drop dışındaki dosya sessizce siliniyordu.
            // Uygulama Drop'a düz dosya yazdığı için burada yalnız kullanıcının koyduğu
            // bağ vardır; Hidden/System atlanması önceki SearchOption aşırı yüklemesiyle
            // aynı davranışı korur.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                MatchType = MatchType.Simple,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
            };

            // Bırakma kopyaları DÜZ yazılır (Drop\<ad>, ad çakışmasında " (2)"), tarih-saat
            // alt klasörü yok. Enumeratörün verdiği yollar zaten Drop altındadır ama aynı guard
            // yine de uygulanır: tek doğrulama noktası (TryDeleteDropCopy) iki yol için de
            // geçerli.
            foreach (string path in Directory.EnumerateFiles(root, "*", options))
            {
                if (TryDeleteDropCopy(path, prefix))
                {
                    deleted++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sayım yarıda kalabilir; o da görünmez olmamalı.
            Debug.WriteLine($"[WharfBar] island drop scan failed: {ex.Message}");
        }

        Debug.WriteLine($"[WharfBar] island drop folder cleared: {deleted} file(s) deleted");
    }

    /// <summary>
    /// Verilen kayıtların <c>Drop</c> altındaki eski kopyalarını siler. Raf kaydının yolu
    /// <c>Drop</c> dışındaysa dosyaya dokunulmaz; kayıt zaten listeden düşmüştür. Eski kopya
    /// listeden çıktığı anda silinmezse <c>%AppData%\WharfBar\Drop</c> kalıcı olarak büyür:
    /// <c>MaxItems</c> 200 iken 300 dosya bırakılan kullanıcıda 100 kopya geride kalır. Bu yol
    /// yalnızca kayıt yoluyla ilgili — <see cref="DeleteDroppedFiles"/> klasörün tamamını tarar.
    /// </summary>
    private void DeleteFilesOf(IEnumerable<IslandEntry> dropped)
    {
        if (!TryGetDropRoot(out _, out string prefix))
        {
            return;
        }

        foreach (IslandEntry entry in dropped)
        {
            if (entry.Kind != IslandEntryKind.File || entry.FilePath is null)
            {
                continue;
            }

            _ = TryDeleteDropCopy(entry.FilePath, prefix);
        }
    }

    /// <summary>
    /// <c>Drop</c> klasörünü verir: tam yol (arama tabanı) ve altındaki yollarla karşılaştırma
    /// için ayırıcı eklenmiş ön ek. Klasörün <b>kendisi</b> "Drop altında" sayılmaz.
    /// </summary>
    private bool TryGetDropRoot(out string root, out string prefix)
    {
        root = string.Empty;
        prefix = string.Empty;
        try
        {
            root = Path.GetFullPath(_store.DropRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Debug.WriteLine($"[WharfBar] island drop root invalid: {_store.DropRoot} ({ex.Message})");
            return false;
        }

        prefix = root + Path.DirectorySeparatorChar;
        return true;
    }

    /// <summary>
    /// Bir kayıt yolunun <c>Drop</c> kökü altında olduğunu doğrular ve tam yolünü verir.
    /// <b>Silme sınırı</b>: kayıt <c>FilePath</c>'i diskin kendisinden geldiği için güvenilmezdir
    /// (<c>island.json</c> elle düzenlenebilir, <c>IslandStore.TryReadEntry</c> DropRoot kontrolü
    /// yapmaz). Sınırın olmaması Drop dışındaki bir dosyayı geri dönüşsüz siler. Açma bu ön eki
    /// kullanmaz — raf orijinal yolu tutar; açma kapısı <see cref="CanOpenShelfFile"/>'dır.
    /// </summary>
    internal bool TryResolveInsideDrop(string? path, out string full)
    {
        if (!TryGetDropRoot(out _, out string prefix))
        {
            full = string.Empty;
            return false;
        }

        return IsInsideDropPrefix(path, prefix, out full);
    }

    /// <summary>
    /// Kayıt yolu <c>Drop</c> altında mı? Silme yolunun sınırı
    /// (<see cref="TryResolveInsideDrop"/>). Açma için <see cref="CanOpenShelfFile"/>.
    /// </summary>
    internal bool IsInsideDropRoot(string? path) => TryResolveInsideDrop(path, out _);

    /// <summary>
    /// Raf dosyası varsayılan uygulamayla açılabilir mi? <c>island.json</c> güvenilmezdir;
    /// açmaya yalnız bu kapı izin verir. <c>Drop</c> öneki aranmaz. Yol mevcut normal bir
    /// dosya olmalıdır: <see langword="null"/>, boş, dizin, aygıt yolu ve dizin
    /// reparse/junction kaçışı reddedilir. Hiçbir şey çalıştırılmaz.
    /// </summary>
    internal bool CanOpenShelfFile(string? path)
    {
        if (!TryNormalizeOpenablePath(path, out string full))
        {
            return false;
        }

        try
        {
            // Dizin (junction dahil) dosya değildir. File.Exists dizinlerde false döner;
            // ikisi de fail-closed.
            if (Directory.Exists(full) || !File.Exists(full))
            {
                return false;
            }

            if ((File.GetAttributes(full) & FileAttributes.Directory) != 0)
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"[WharfBar] island open skipped, unreadable: {full} ({ex.Message})");
            return false;
        }

        if (!TryGetVolumePrefix(full, out string volumePrefix))
        {
            return false;
        }

        // Silme guard'ıyla aynı sınıf: yol üzerindeki her dizin bileşeni gerçek olmalı.
        // Dosyanın kendisi bileşen sayılmaz (bulut yer tutucusu reparse noktası olabilir).
        // Drop öneki yok — kök, sürücü veya UNC paylaşımıdır.
        if (!AllDirectoryComponentsAreReal(volumePrefix, full, out string? reparseDir))
        {
            Debug.WriteLine(
                $"[WharfBar] island open skipped, unsafe path component " +
                $"({reparseDir ?? "unreadable"}): {full}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Raf dosyasını varsayılan uygulamasıyla açar. Kapı <see cref="CanOpenShelfFile"/>'dır:
    /// açmadan önce yine girdi doğrulaması yapılır. Dosya silinmez.
    /// </summary>
    /// <returns>Kapıyı geçtiyse ve başlatma denediyse <c>true</c>; kapı reddeyse ya da varsayılan
    /// uygulama bulunamadıysa <c>false</c> (kayıt listede kalır).</returns>
    internal bool OpenShelfFile(string path)
    {
        if (!CanOpenShelfFile(path))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Bozuk kısayol, erişim yok, varsayılan uygulama yok.
            Debug.WriteLine($"[WharfBar] island open failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Açılacak yolu tam yola çevirir. Aygıt ad alanı (<c>\\.\</c>, <c>\\?\</c> çözülmeden
    /// kalanlar) ve ayrılmış DOS aygıt adları (<c>NUL</c>, <c>CON</c>, <c>COM1</c> …) reddedilir.
    /// </summary>
    private static bool TryNormalizeOpenablePath(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Debug.WriteLine($"[WharfBar] island open path invalid: {path} ({ex.Message})");
            return false;
        }

        // \\.\ aygıt ad alanıdır (NUL, PhysicalDrive). \\?\ geniş yol öneki GetFullPath
        // ile normal bir dosyaya çözülebilir; çözülmeden kalırsa aşağıdaki kontrol reddeder.
        if (IsWin32DeviceNamespace(path) || IsWin32DevicePath(full) || ContainsReservedDeviceName(full))
        {
            Debug.WriteLine($"[WharfBar] island open skipped, device path: {path}");
            full = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// <see cref="AllDirectoryComponentsAreReal"/> için ayırıcı eklenmiş birim kökü.
    /// <c>C:\</c> veya <c>\\sunucu\paylaşım\</c>. Aygıt kökü kabul edilmez.
    /// </summary>
    private static bool TryGetVolumePrefix(string full, out string prefix)
    {
        prefix = string.Empty;
        string? root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root) || IsWin32DevicePath(root) || IsWin32DevicePath(full))
        {
            return false;
        }

        prefix = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return true;
    }

    /// <summary><c>\\.\</c> aygıt ad alanı. Geniş yol öneki <c>\\?\</c> buna girmez.</summary>
    private static bool IsWin32DeviceNamespace(string path) =>
        path.StartsWith(@"\\.\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("//./", StringComparison.Ordinal);

    private static bool IsWin32DevicePath(string path) =>
        IsWin32DeviceNamespace(path)
        || path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("//?/", StringComparison.Ordinal);

    /// <summary>
    /// Yol parçalarından biri ayrılmış DOS aygıt adı mı (<c>NUL</c>, <c>CON</c>, <c>COM1</c>–<c>COM9</c>,
    /// <c>LPT1</c>–<c>LPT9</c>, uzantılı biçimler dahil)?
    /// </summary>
    private static bool ContainsReservedDeviceName(string path)
    {
        foreach (string segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (IsReservedDeviceName(segment))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReservedDeviceName(string segment)
    {
        if (segment.Length == 0)
        {
            return false;
        }

        string name = segment;
        int colon = name.IndexOf(':');
        if (colon >= 0)
        {
            name = name[..colon];
        }

        int dot = name.IndexOf('.');
        if (dot >= 0)
        {
            name = name[..dot];
        }

        if (name.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || name.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Length != 4)
        {
            return false;
        }

        if (!name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name[3] is >= '1' and <= '9';
    }

    /// <summary>
    /// Yalnız sınır kontrolü: servis/mağaza olmadan test edilebilir saf kısım.
    /// </summary>
    /// <param name="rootPrefix">
    /// <see cref="TryGetDropRoot"/>'in ürettiği ayırıcı eklenmiş ön ek. Ayırıcının eklenmiş
    /// olması şart: "…\Drop" ile "…\DropYedek" kardeş klasörler, ayırıcısız ön ek
    /// kontrolünde ikisi de "Drop" altında sayılırdı.
    /// </param>
    internal static bool IsInsideDropPrefix(string? path, string rootPrefix, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Debug.WriteLine($"[WharfBar] island drop path invalid: {path} ({ex.Message})");
            full = string.Empty;
            return false;
        }

        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[WharfBar] island drop path rejected, outside Drop: {path}");
            full = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Tek bir bırakma kopyasını siler. Yalnızca <c>%AppData%\WharfBar\Drop</c> altındaki
    /// yollar silinir; dışarıdaki bir yol loglanıp atlanır. Kayıt <c>FilePath</c>'i diskin
    /// kendisinden geldiği için güvenilir değildir (<c>island.json</c> elle düzenlenebilir):
    /// <c>IslandStore.TryReadEntry</c> DropRoot kontrolü yapmaz, yani
    /// <c>{"kind":"File","filePath":"C:\\…\\Documents\\tehlikeli.docx"}</c> girdisi onaysız ve
    /// geri alınamaz biçimde silinirdi.
    /// </summary>
    /// <returns>Dosya gerçekten silindiyse <c>true</c>.</returns>
    private static bool TryDeleteDropCopy(string path, string rootPrefix)
    {
        if (!IsInsideDropPrefix(path, rootPrefix, out string full))
        {
            return false;
        }

        // Ön ek kontrolü yeterli DEĞİL: Drop içine kullanıcının koyduğu bir junction
        // (…\Drop\baglanti\ → C:\Users\…\Documents) ön ek testini geçer, ama File.Delete
        // junction'ı izler ve Drop DIŞINDAKİ dosyayı siler. Tarama yolu bunu zaten
        // ReparsePoint'i atlayarak engelliyor (DeleteDropFolderContents); kayıt yolu da aynı
        // güvenceyi almalı, yoksa iki yol aynı girdi için farklı güvenlik sınırı uygular.
        if (!AllDirectoryComponentsAreReal(rootPrefix, full, out string? reparseDir))
        {
            Debug.WriteLine(
                $"[WharfBar] island drop delete skipped, unsafe path component " +
                $"({reparseDir ?? "unreadable"}): {full}");
            return false;
        }

        if (!File.Exists(full))
        {
            return false;
        }

        try
        {
            File.Delete(full);
            Debug.WriteLine($"[WharfBar] island drop copy deleted: {full}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[WharfBar] island drop delete failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// <paramref name="rootPrefix"/> ile <paramref name="full"/> arasındaki <b>her dizin
    /// bileşeni</b> gerçek bir dizin mi, yani <c>ReparsePoint</c> ile işaretli bir junction /
    /// sembolik bağ mı?
    /// </summary>
    /// <param name="offender">
    /// Bağlantı işaretli bileşenin yolu, yoksa <see langword="null"/>.
    /// </param>
    /// <returns>
    /// Güvenliyse <c>true</c>. Bağlantılı ya da <b>okunamayan</b> bir bileşende <c>false</c> —
    /// belirsizlikte silme yapılmaz (opt-out'un fail-closed kuralıyla aynı seçim).
    /// </returns>
    internal static bool AllDirectoryComponentsAreReal(string rootPrefix, string full, out string? offender)
    {
        offender = null;

        // Kökün kendisi (…\Drop) kullanıcının/uygulamanın klasörü; bileşen sayımı onun
        // ALTINDAN başlar. Dosyanın kendisi kontrol edilmez: File.Delete bir dosya sembolik
        // bağında bağın KENDİSİNİ siler, hedefi değil — sadece dizin bağı izlenir.
        int start = rootPrefix.Length;
        for (int i = start; i < full.Length; i++)
        {
            if (full[i] != Path.DirectorySeparatorChar && full[i] != Path.AltDirectorySeparatorChar)
            {
                continue;
            }

            string dir = full[..i];
            if (dir.Length <= start)
            {
                continue;
            }

            try
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                {
                    offender = dir;
                    return false;
                }
            }
            catch (FileNotFoundException)
            {
                // Bileşen yok: File.Exists(full) zaten false dönecek, silme olmayacak. Temiz
                // sayıp devam etmek güvenli — takip eden adımda zaten eleniyor.
            }
            catch (DirectoryNotFoundException)
            {
                // Aynı gerekçe, ara dizin yok.
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Erişilemeyen bileşenin bağ olup olmadığı bilinemez -> fail-closed.
                offender = dir;
                Debug.WriteLine($"[WharfBar] island drop component unreadable: {dir} ({ex.Message})");
                return false;
            }
        }

        return true;
    }

    /// <param name="deleteDroppedFiles">
    /// Listeden düşen bırakma kayıtlarının kopyaları da silinsin mi? <c>true</c> budama ve
    /// <see cref="Remove"/> yollarıdır (kayıt gittiği için kopya yetim olurdu);
    /// <c>false</c> <see cref="Clear"/> ve <see cref="DeleteDroppedFiles"/> — dosya silmek
    /// açık bir eylemdir ve bu iki yol silgiyi zaten kendi yönetir.
    /// </param>
    private void ApplySnapshot(
        IReadOnlyList<IslandEntry> next,
        IslandEntry? added,
        bool raiseEntryAdded,
        bool deleteDroppedFiles = true)
    {
        if (_dispatcher.CheckAccess())
        {
            Commit(next, added, raiseEntryAdded, deleteDroppedFiles);
        }
        else if (CanPostToUi)
        {
            _ = _dispatcher.BeginInvoke(
                () => Commit(next, added, raiseEntryAdded, deleteDroppedFiles),
                DispatcherPriority.Background);
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
    private void Commit(
        IReadOnlyList<IslandEntry> next,
        IslandEntry? added,
        bool raiseEntryAdded,
        bool deleteDroppedFiles = true)
    {
        // Commit tek choke point: Remove, Clear, budama ve ApplyConfig hepsi buradan geçer,
        // yani "listeden düşen kayıt" hesabı da tek yerde yapılır. Clear bu hesabı
        // devre dışı bırakır: budama kopyaları silmeye devam eder (aksi halde yetim kalırdı),
        // açık "dosyaları sil" eylemi ise DeleteDroppedFiles.
        if (deleteDroppedFiles)
        {
            QueueDeleteOfDropped(next);
        }

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
        // Karşılaştırma sayı değil ÜYELİKTİR: tavan taşması tam olarak liste zaten
        // MaxItems'te doluyken olur, o durumda next.Count == _entries.Count daima eşit
        // çıkar ve önceki erken çıkış her budamayı atlayıp dosyaları geride bırakıyordu
        // (ölçüldü: MaxItems=3, 3 dosya + metin, 25 yakalama → 3 dosya sızdı). HashSet
        // zaten kuruluyordu, yalnız karar sayıya bakıyordu.
        //
        // Boş listede düşecek kayıt olamaz; bu, yeni adanın ilk yakalamalarındaki tek
        // maliyetsiz erken çıkıştır (liste büyüdükçe HashSet kurulumu ~MaxItems giriş).
        if (_entries.Count == 0)
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
            _ = _dispatcher.BeginInvoke(FlushSaveOffThread, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            // Kapanma yarışı: dispatcher BeginInvoke'yi reddedebilir. Yazı kaybı panonun
            // kendisinden daha önemli değil, ama görünmez de olmamalı.
            Debug.WriteLine($"[WharfBar] island save dispatch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// UI thread'inde çalışır: yalnız <b>snapshot'ı</b> burada alır, serileştirme + diske
    /// yazma STA worker'a verilir.
    /// </summary>
    /// <remarks>
    /// Varsayılan 200 kayıt × 64 KB ≈ 13 MB'lık bir <c>JsonSerializer.Serialize</c> +
    /// <c>File.Replace</c> 750 ms debounce ile <b>AppBar UI thread'inde</b> çalışıyordu; üst
    /// bar kayıtlı bir AppBar olduğu için bu, tüm masaüstünü birkaç saniye donduruyordu.
    /// Sınıfın kendi gerekçesi dosya kopyalama/silmeyi STA'ya taşımıştı ama diske yazmayı
    /// burada bırakmıştı.
    /// <para><b>Sıralama garantisi:</b> <see cref="IslandStore.Save"/> serileştirmeyi de kendi
    /// <c>_ioGate</c> kilidi içinde yapar, yani iki yazma asla iç içe geçemez. Kuyrukta
    /// bekleyen eski bir yazma <see cref="Dispose"/>'daki senkron flush'tan sonra
    /// <c>BeginInvokeShutdown</c> ile düşer — yani diskte en son snapshot kalır.</para>
    /// </remarks>
    private void FlushSaveOffThread()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        QueueSave([.. _entries]);
    }

    /// <summary>
    /// <b>Senkron.</b> Çıkış yolunda kullanılır: kuyruğa atmak son kopyalamayı sessizce
    /// düşürüyordu (kapanış süreç sonuyla yarışır), bu yüzden buradaki yazma UI thread'inde
    /// kalmalı. Kuyruktaki eski yazmalar bu flush'tan sonra düşer.
    /// </summary>
    private void FlushSave()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        _store.Save([.. _entries]);
    }

    private void QueueSave(List<IslandEntry> snapshot)
    {
        Dispatcher? sta = _staDispatcher;
        if (sta is null)
        {
            // STA worker yoksa (Dispose sonrası) yazmağı sessizce düşürmektense senkron yaz.
            _store.Save(snapshot);
            return;
        }

        _ = sta.BeginInvoke(() => _store.Save(snapshot), DispatcherPriority.Background);
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

    /// <summary>
    /// Kayıt eşitliği <b>içerik</b> üzerinden: aynı <c>(Kind, Text, FilePath)</c> üçlüsü aynı
    /// kayıttır. Id bilinçli olarak <b>karşılaştırılmaz</b> — her süreç 1'den başladığı için
    /// oturum ve kalıcı kayıtlar aynı Id'yi taşıyabilir (bkz. <see cref="MergeWithSession"/>).
    /// Kurallar <c>IslandPruner.IsSameContent</c> ile aynıdır; o metot private olduğu için
    /// burada tekrarlanıyor (saf karşılaştırma, tek satır).
    /// </summary>
    private sealed class EntryContentComparer : IEqualityComparer<IslandEntry>
    {
        internal static readonly EntryContentComparer Instance = new();

        public bool Equals(IslandEntry? x, IslandEntry? y) =>
            ReferenceEquals(x, y)
            || (x is not null
                && y is not null
                && x.Kind == y.Kind
                && string.Equals(x.Text, y.Text, StringComparison.Ordinal)
                && string.Equals(x.FilePath, y.FilePath, StringComparison.OrdinalIgnoreCase));

        public int GetHashCode(IslandEntry obj) => HashCode.Combine(
            (int)obj.Kind,
            obj.Text is null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Text),
            obj.FilePath is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FilePath));
    }
}
