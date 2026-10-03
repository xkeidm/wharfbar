# ADR-010: Island — üst barın içinde clipboard geçmişi + bırakılan dosya alanı

**Durum:** Kabul edildi (2026-10-03)
**Tarih:** 2026-10-03
**Kapsam:** Üst barın **ortasında** duran Dynamic Island benzeri alan: kapalıyken son kopyalananın
önizlemesi, açıkken pano geçmişi + bırakılan dosyaların tek birleşik zaman çizelgesi
(spec: [2026-10-01-island-clipboard-drop-design](../superpowers/specs/2026-10-01-island-clipboard-drop-design.md),
[plan](../superpowers/plans/2026-10-01-island-clipboard-drop.md))

## Karar

| Konu | Seçim |
|------|--------|
| Form faktörü | **A — Üst barın içinde**, ayrı pencere değil: `TopBarWindow.xaml` Grid'inin orta hücresinde ortalanmış bir `Border` (`IslandPill`), açılışı aynı pencere içindeki `Popup` (`IslandPopup`) ile. Grid'de sütun yoktur — sol küme, ada ve `StatusStrip` aynı hücreyi paylaşır; ada `HorizontalAlignment=Center` |
| Panel | `IslandFlyout` (`Width=420`, `MaxHeight=440`): arama kutusu + `ScrollViewer`/`StackPanel` liste. **İç içe `Popup` yok** (`docs/04-gotchas.md` tuzağı) |
| Kapanış / animasyon | Mevcut flyout kalıbı birebir: `StaysOpen=True`, `PopupAnimation=None`, `AnimateFlyoutOpen/Close`; dört kapanış yolu (`WM_ACTIVATEAPP`, bar'da başka yere tıklama, `Esc`, pill'e tekrar tıklama) + `CloseFlyouts` zinciri |
| Pano dinleme | Mesaj-only `HwndSource` (`ParentWindow = -3`, mevcut `MonitorMessageWindow` kalıbı) + `AddClipboardFormatListener`; `WM_CLIPBOARDUPDATE` UI thread'inde alınır, **okuma STA worker'a devredilir** (`System.Windows.Clipboard` kullanılmaz) |
| Kalıcılık | `config.json` **kullanılmaz** (UI thread'inde tüm dosya yeniden yazılır). Ayrı atomik store: `%AppData%\WharfBar\island.json` (metadata) + `%AppData%\WharfBar\Drop\` (bırakılan dosyaların kopyaları) |
| Yazma zamanlaması | 750 ms debounce (`System.Threading.Timer`), snapshot UI thread'inde okunur, `File.Replace` ile atomik; `Dispose`'da senkron `FlushSave` |
| Parola yöneticileri | `CanIncludeInClipboardHistory` formatındaki DWORD 0 ise kayıt alınmaz; format id'i **okunamazsa da** kayıt alınmaz (fail-closed) |
| "Panoya geri koy" | Panoya **yazar**, yapıştırma yapmaz (`boundaries.md` enjeksiyonu yasaklıyor). Yazma `CanIncludeInClipboardHistory = 0` ile birlikte yapılır |
| Bırakma | Pill `AllowDrop`; dosya `Drop` klasörüne **kopyalanır** (ad çakışmasında ` (2)`, ` (3)` …), orijinale dokunulmaz. Klasör bırakmaları yok sayılır |
| Monitör kapsamı | v1: **yalnızca birincil monitörün bar'ı** (`trayMirror` ile aynı sınır). Servis `TopBarEngine`'de tek örnek; `AttachIsland` yalnız primary'de çağrılır |
| Config | `island.enabled` · `island.persistence` (**string** `session`/`persistent`) · `island.retentionDays` · `island.maxItems`; `schemaVersion` **1 kalır** (soft alan, ADR-007 deseni) |

## Gerekçe

- macOS'taki notch uygulamalarının (Coucou/NotchBuddy) fikri "ekranın üst kenarına yerleşen
  ince bir ada, üstüne tıklanınca genişleyip içeriği göstermesidir". **Kod MIT lisanslıdır, ama
  karakter "Mochi", ikonlar ve sesler © Louis Raillé, tüm hakları saklıdır.** Bu yüzden yalnız
  konsept alınmıştır; karakter, ikon ve ses kopyalanmamıştır — görsel dil WharfBar'ın kendi
  gliflerinden (`≡` metin, `📄` dosya) ve mevcut WPF-UI yüzeyinden gelir.
- Windows'ta notch **yoktur**; Coucou'nun kendi çözümü de aynısını yapar: ada "ekranın üst
  kenarından kayarak çıkan bir ada" dönüşür, yani ikinci bir `Topmost` pencere. WharfBar'da
  üst kenar **zaten** shell değişiminin kendisi (`TopBarWindow`, `ABE_TOP`): work area rezervasyonu,
  z-order, `ABN_*` yaşam döngüsü ve çoklu-monitör yerleşimi çözülmüş durumdadır. Yeni bir ada
  penceresi bu üçünü **tekrar** çözmek zorunda kalırdı. Ada doğrudan bar'ın ortasına konunca
  yeni pencere, yeni z-order düzeni, yeni odak yönetimi ve yeni çoklu-monitör yolu gerekmez.
- Tasarım kararı ölçülmüştür: dar monitörde ortalanmış ada ile sağ küme çakışmaya başlayınca
  önizleme tavanı statik `MaxWidth=160`'a indirildi (hesap task-6 raporunda; canlı ölçüm
  yapılmadı — bkz. Sonuçlar).

## Sonuçlar

- Yeni dosyalar: `Native/ClipboardNative.cs` (belgelenmiş pano P/Invoke'ları),
  `Models/IslandEntry.cs` (`IslandEntryKind` + `IslandEntry`), `Services/IslandPruner.cs` (saf
  çekirdek: kabul, dedupe, budama, arama, önizleme, bırakma dosya adı),
  `Services/IslandStore.cs` (atomik store), `Services/IslandService.cs` (yaşam döngüsü),
  `Services/ClipboardMessageWindow.cs` (mesaj-only dinleyici), `Views/IslandFlyout.xaml(.cs)`,
  `Views/Pages/IslandPage.xaml(.cs)` (ayar sayfası).
- `TopBarEngine.Island` tek örnek; `Enabled=false` iken kurucu ne dinleyici kurar ne de
  `island.json`'a dokunur (servis ayar kapalıyken de doğar ki ayardan sonradan açmak çalışsın).
- `TopBarWindow`: `IslandPill` + `IslandPopup` + `ToggleIsland`/`CloseIsland`/`AttachIsland`;
  `IslandHost` hit-test ve klavye yönlendirmesine eklenir.
- `Models/TopBarStatusItemIds.cs` **değişmez** — ada yapılandırılabilir durum şeridine girmeyen,
  merkezde sabit duran bir öğedir.
- Yeni dış bağımlılık yok.
- **Canlı uçtan uca doğrulama bu ADR ile yapılmadı.** Brief'in ölçüm maddeleri (1024/1280 px
  monitörde ada–sağ küme çakışması, ilk `AllowDrop` kullanımı olarak OLE hedefi, nabız
  animasyonunun bitiş değeri) **aritmetik ile aralık hesabı** yapılmış, ölçülmemiştir; bkz.
  `.superpowers/sdd/2026-10-01-island-clipboard-drop/task-8-report.md`.
- Doküman: `docs/01-architecture.md`, `docs/02-win32-appbar-dwm.md`, `docs/04-gotchas.md`,
  `.agent/*` güncellenir.

## Alternatifler (reddedilen)

1. **B — Bağımsız `Topmost` pencere** (ada kendi penceresinde) — z-order savaşı (bar `HWND_TOPMOST`
   yönetimi, `ABN_FULLSCREENAPP` gizleme) her iki pencereye de dokunmak zorunda kalır; ada açıkken
   odak yönetimi yeni bir problemdir (bar AppBar'da `WS_EX_NOACTIVATE` yönüne gider);
   `AllowsTransparency=true` ile Mica yasağı (`boundaries.md`) bu pencerede de gündeme gelir;
   çoklu monitörde her barda ayrı ada + hangi panelin açık olduğunun belirsizliği. Kazancı
   (ekran üst kenarından kayma efekti) Windows'ta notch olmadığı için zaten alınamaz.
2. **C — Bar'a sahip ayrı pencere** (kendi AppBar'ı olan ada) — A'nın avantajı yok: yeni AppBar
   kaydı, yeni `ABM_QUERYPOS/SETPOS` disiplini, yeni `TaskbarCreated` yeniden kayıt yolu ve iki
   ayrı rezervasyon. Reddedildi.
3. **Global hotkey ile açma** — pill bar'da görünür, tek tık yeter. Eklemek `VeilPage`'e gömülü
   `RegisterHotKey` yakalama UI'ının `Services/`'e taşınmasını gerektirirdi; ilgisiz refactor.

## Kapsam dışı (gerekçeli)

- **Canlı uygulama etkileşimi** — hook, izin onayı, pencere bağlama, entegrasyon yok.
- **Karakter / kişilik / ses** — Coucou "Mochi" ve sesleri © Louis Raillé, tüm hakları saklı
  (kod MIT). Kopyalanmadı.
- **`CF_HDROP` pano yakalama** — Explorer'da bir dosya kopyalandığında `Drop` klasörüne de
  kopyalansaydı 2 GB'lık tek bir dosya uygulamayı çökerirdi. Dosyalar yalnız **sürüklenerek**
  gelir; panodan dosya yolu okunmaz.
- **Klasör bırakma** — 10 GB'lık klasörü sessizce kopyalamak kötü deneyim; ilerleme arayüzü
  gerektirir.
- **Global hotkey** — yukarıda.
- **Dış bağımlılık** — yalnız mevcut WPF-UI + `System.Management`.

## Ölçülen tasarım kararları ve tuzaklar

- **`CanIncludeInClipboardHistory` DWORD 0 → kayıt alınmaz.** Windows'un kendi `Win+V`
  geçmişinin kullandığı opt-out sinyali (parola yöneticileri kopyalarını bu işaretle kendi dışında
  tutar). Format id'i `RegisterClipboardFormatW` ile **alınamıyorsa** belirsizlikte içerik
  **kaydedilmez** (fail-closed) — okuma yolundaki her `false` dönüşü kayıt dışı bırakır.
- **`TryWriteText` de bu işareti yazar.** Yazma `WM_CLIPBOARDUPDATE`'i kendi kuyruğuna geri
  tetikler; opt-out işareti yazılmazsa island kendi kopyasını geri yakalar. Bu yüzden okuma
  yolundaki `DWORD == 0` kontrolü yazma yolundaki bloğu tam olarak "geçmişe alma" diye okur.
  `SetClipboardData` başarıda belleği devralır — ikinci blok (`opt-out`) yazılamazsa metin yine de
  yazılmış sayılır ama **görünmez olmamalıdır** (loglanır).
- **Pano okuması STA thread'indedir.** `OpenClipboard` bloklayabilir; UI thread'inde yapılırsa
  tüm bar donar. Depoda `System.Windows.Clipboard` **kullanılmaz** (UI thread'ine bağlıdır).
  Kilitliyse 3 × 40 ms kısa bekleyip sessizce düşülür; UI thread'inde asla yeniden deneme yapılmaz.
- **Geçmiş `config.json`'a yazılmaz.** `ConfigService.Save` her çağrıda dosyanın tamamını UI
  thread'inde yeniden yazar; pano değişiklikleri yüksek frekanslı. Ayrı atomik store + `Drop`
  klasörü kullanılır.
- **`Persistence` string'dir, enum değil.** Enum-typed config alanı repoda bir ilkti;
  `"persistence": "Persistent"` yazmak `JsonException` fırlatıp **tüm config'i** sıfırlıyordu.
  Çözüm: `"session"` / `"persistent"` string'i + `IslandNormalizer.NormalizePersistence`
  (büyük/küçük harf duyarsız, çözülemeyen → `session`). `IslandStore`'daki `kind` alanı ise
  enum **kalabiliyor** çünkü toleranlı bir `JsonConverter` ile okunuyor.
- **`Clear()` dosya kopyalarını silmez.** "Tüm geçmişi temizle" diyen kullanıcı dosyaları
  habersizce kaybetmemeli; ayarlarda iki ayrı buton var (`Tüm geçmişi temizle` /
  `Bırakılan dosyaları sil`). Dosyalar yalnız `DeleteDroppedFiles()` ile silinir; o eylem
  `Drop` klasörünü **tarayarak** çalışır, böylece budamayla düşmüş yetimler de kapsanır (tek
  listeden hesaplanan eşleşme yetmezdi).
- **`DeleteFilesOf` `Drop` altındaki yolları doğrular.** Kontrol `Path.GetFullPath(DropRoot) +
  ayraç` önekiyle yapılır; aksi halde elle düzenlenmiş bir `island.json` kullanıcının Documents
  klasöründeki bir dosyayı silme emri verebilirdi. Tarama sırasında reparse noktaları atlanır.
- **`RemoveClipboardFormatListener` idempotent DEĞİLDİR.** Kayıtlı değilse `FALSE` +
  `ERROR_INVALID_WINDOW` (0x57) döner (ölçüldü: 5/5 deneme). Yalnız kayıtlıyken ve bir kez
  çağrılmalıdır; `ClipboardMessageWindow._disposed` bayrağı bunu garanti eder.