# WharfBar — Island: üst bar ortasında clipboard geçmişi + bırakılan dosya alanı

**Tarih:** 2026-10-01
**Durum:** Taslak — kullanıcı onayı bekliyor
**Kapsam:** macOS notch uygulamaları (Coucou/NotchBuddy) konseptine dayanan, WharfBar'ın üst
barının **ortasında** duran bir "Dynamic Island". Kapalıyken son kopyalananın önizlemesini
gösterir; açıldığında **clipboard geçmişi** ve **bırakılan dosyaların** tek birleşik zaman
çizelgesini sunar. Bırakılan dosyalar kopyalanıp WharfBar'ın kendi klasöründe saklanır.
Canlı uygulamalarla etkileşim, hook, onay akışı, entegrasyon, karakter/ses **yok**.

## Özet

Windows'ta notch yoktur; Coucou'nun kendi çözümü de aynısını yapar: ada "ekranın üst
kenarından kayarak çıkan bir ada" dönüşür. WharfBar'da üst kenar **zaten** shell
değişiminin kendisidir, dolayısıyla ikinci bir `Topmost` pencere yerine island doğrudan mevcut
`TopBarWindow` XAML'inin **orta sütununa** yerleşir ve açılışta mevcut flyout altyapısıyla
(`Popup` + `AnimateFlyoutOpen/Close`) aşağı doğru açılır. Böylece yeni pencere, yeni
z-order düzeni, yeni odak yönetimi ve yeni çoklu-monitör yolu gerekmez.

İçerik iki kaynaktan gelir: **pano** (`WM_CLIPBOARDUPDATE` dinleyicisi) ve **sürükle-bırak**
dosyalar. İkisi de aynı `IslandEntry` listesine, yeniden eskiye yazılır. Kayıt tıklandığında
metin panoya geri konur (yapıştırma yapılmaz), dosya varsayılan uygulamada açılır.

## Kararlar

| Konu | Seçim |
|------|-------|
| Form faktörü | Üst barın **içinde**, ortada Dynamic Island (onaylandı: A). Bağımsız `Topmost` pencere (B) ve bar'a sahip ayrı pencere (C) **reddedildi** — z-order/focus/Mica riski kazancı aşmıyor |
| Kapalı görünüm | Küçük pill: tür ikonu + son kaydın kırpılmış önizlemesi (~40 karakter) + kaynak uygulama adı, üst sınır ~280px. Yeni kayıtta kısa nabız (~1.2 sn) |
| Veri modeli | **Tek birleşik zaman çizelgesi** — pano ve dosyalar ayrı listeler değil, tek `IslandEntry` listesi (`Kind = Text \| File`), yeniden eskiye |
| Pano okuma thread'i | Ayrı **STA dispatcher thread** (`TrayMirrorService`'in mevcut kalıbı). `OpenClipboard` bloklayabildiği için UI thread'i asla kilitlenmez |
| Disk | Ayrı dosya: `%AppData%\WharfBar\island.json` (metadata) + `%AppData%\WharfBar\Drop\` (dosyalar). `config.json` **kullanılmaz** — `ConfigService.Save` her seferinde dosyanın tamamını UI thread'inde yeniden yazar, pano değişiklikleri yüksek frekanslı |
| Yazma zamanlaması | 750 ms debounce, arka plan thread'i, atomik (`File.Replace`) |
| Parola yöneticileri | `CanIncludeInClipboardHistory` formatı 0 ise kayıt **alınmaz** — Windows'un kendi `Win+V` geçmişinin kullandığı opt-out sinyali |
| Dedupe | Ardışık aynı metin **yukarı taşınır**, kopya açılmaz. "Panoya geri koy" eyleminin tetiklediği `WM_CLIPBOARDUPDATE`'i de bu kural kendiliğinden eler |
| `Enabled` | Ayar, varsayılan **`true`** — ada kullanıcının istediği ana özellik. `false` iken: pill hiç çizilmez, `WM_CLIPBOARDUPDATE` dinleyicisi **hiç kurulmaz** (RAM/CPU ayak izi sıfır), `island.json` okunmaz, mevcut kayıtlar diske dokunulmadan bırakılır. Ayar sayfasından tekrar `true` yapıldığında geçmiş yeniden yüklenir |
| `Persistence` | Ayar: `Persistence = Session \| Persistent` (varsayılan `Session`) + `RetentionDays` (varsayılan 30) + `MaxItems` (varsayılan 200) |
| "Panoya geri koy" | Panoya **yazar**, uygulamaya yapıştırmaz (Win+V / Maccy davranışı). `boundaries.md` enjeksiyonu yasaklıyor |
| Global hotkey | **v1'de yok.** Pill bar'da görünür, tek tık yeter. Eklenecek olursa `VeilPage`'e gömülü yakalama UI'ının `Services/`'e taşınması gerekir — ilgisiz refactor'den kaçınmak için ertelendi |
| Monitör kapsamı | v1: **yalnızca birincil monitörün bar'ı.** `trayMirror` bölümü de aynı sınırı koyuyor. Panel `Popup` olduğu için zaten tıklandığı bar'ın monitöründe açılır |
| Bırakma hedefi | Dosya **kopyalanır** (`%AppData%\WharfBar\Drop\<GG.AA.YY ss.dd>\<ad>`), orada durur. Orijinale dokunulmaz. Klasör bırakmaları yok sayılır |
| Zaman damgası | `CapturedAtUtc`; listede göreli zaman ("3 dk önce") |
| Kaynak uygulama | `GetClipboardOwner` → pid → process adı; dosya kayıtlarında boş |

## Mimari

```
TopBarWindow.xaml
  Grid (Margin=10,0)
    ├─ StackPanel (Left)  : logo + sistem istatistikleri
    ├─ Border "IslandPill"  (HorizontalAlignment=Center)   ← YENİ
    └─ StackPanel "StatusStrip" (Right) : tray aynası, saat, bildirim, QS, ses, parlaklık…

  Popup "IslandPopup" (Placement=Bottom, PlacementTarget={Binding IslandPill})  ← YENİ
    └─ local:IslandFlyout  (arama kutusu + liste)
```

Ust bar 30px yüksekliğinde; pill buna sığacak şekilde tasarlanır. Panel aşağı doğru açılır
ve pencerelerin üstüne iner — mevcut `QuickSettingsPopup`/`MediaPopup` ile aynı davranış.

### Yeni dosyalar

| Dosya | Sorumluluk |
|-------|-----------|
| `Native/ClipboardNative.cs` | `AddClipboardFormatListener` / `RemoveClipboardFormatListener`, `OpenClipboard` / `CloseClipboard`, `GetClipboardData` (`CF_UNICODETEXT`), `RegisterClipboardFormatW` (`CanIncludeInClipboardHistory`), `GetClipboardOwner`, metin yazma |
| `Models/IslandEntry.cs` | `internal enum IslandEntryKind { Text, File }` + `internal sealed record IslandEntry(long Id, IslandEntryKind Kind, string Text, string? FilePath, DateTime CapturedAtUtc, string? SourceApp)` |
| `Services/IslandService.cs` | Yaşam döngüsü: STA okuma thread'i, `ClipboardMessageWindow` kurulumu, yakalama/ekleme/silme **orkestrasyonu**, `Changed` olayı, `Snapshot`. Saf mantık `IslandPruner`'a devredilir |
| `Services/IslandStore.cs` | `island.json` atomik **değiştirilir** yazma (tmp + `File.Replace`) ve kilitli okuma/yazma (`Load`/`Save` tek kilit paylaşır); bozuk dosyada `.bak`, ikinci bozulmada `.bak.1`e döndürme; kayıt başına toleranslı okuma (bilinmeyen `kind` → `Text`, çözümlenemeyen alan → o kayıt atılır) + 64 MB boyut tavanı. **Sıralamaz** — sıra tüketicinin işidir |
| `Services/IslandPruner.cs` | **Saf:** gün/`MaxItems`/`Session` budama, dedupe, arama eşleştirme, önizleme biçimlendirme, bırakma yolu üretimi (test edilebilir saf çekirdek) |
| `Services/ClipboardMessageWindow.cs` | Mesaj-only `HwndSource` (`ParentWindow = -3`), `WM_CLIPBOARDUPDATE` |
| `Views/IslandFlyout.xaml(.cs)` | Panel: arama kutusu + `ScrollViewer`/`ItemsControl` liste |
| `Views/Pages/IslandPage.xaml(.cs)` | Ayar sayfası |

### Değişen dosyalar

| Dosya | Değişiklik |
|-------|-----------|
| `Models/AppConfig.cs` | `IslandConfig` sınıfı + `AppConfig.Island` özelliği (yumuşak ekleme; `schemaVersion` **değişmez** — ADR-007 deseni) |
| `Services/ConfigService.cs` | `Normalize()` içinde `config.Island ??= new IslandConfig()` + `IslandNormalizer` sınırları; `CreateDefault()` tohumu |
| `Views/TopBarWindow.xaml(.cs)` | `IslandPill` + `IslandPopup`, durum makinesi, `ToggleIsland`, `CloseFlyouts` zincirine ekleme, `_statusElements` benzeri kayıt |
| `Views/SettingsWindow.xaml` | `NavigationViewItem` "Ada" (WPF-UI `SymbolIcon` enum adı **pakette doğrulanmadan** kullanılmayacak) |
| `App.xaml.cs` | Servisin kurulumu (config sonrası) + `OnExit` sırasında `Dispose` (bekleyen debounce flush) |
| `Services/TopBarEngine.cs` | `IslandService` alanı + `Dispose` (mevcut motor kalıbı) |

`Models/TopBarStatusItemIds.cs` **değişmez** — island yapılandırılabilir status listesine
girmeyen, merkezde sabit duran bir öğedir.

## Veri akışı

### Pano yakalama

1. `ClipboardMessageWindow` `WM_CLIPBOARDUPDATE` alır (UI thread'i).
2. İş, `IslandService`'in STA dispatcher'ına kuyruğa girer — **okuma asla UI thread'inde olmaz.**
3. `OpenClipboard` denenir; başarısızsa kısa bekleyerek birkaç kez yeniden denenir, hâlâ
   başarısızlıksa olay **sessizce düşürülür**.
4. `CanIncludeInClipboardHistory` 0 ise → atla.
5. `CF_UNICODETEXT` okunur. Boş/yalnız boşluk veya 64 KB üzeri → atla.
6. `GetClipboardOwner` → pid → process adı.
7. Dedupe + `Prepend` → `Changed` → 750 ms debounce → arka planda diske yaz.

### Geri koyma / açma

- **Metin kaydı:** STA worker'da panoya yazılır. Bu, `WM_CLIPBOARDUPDATE`'i yeniden tetikler;
  aynı metin zaten en üstte olduğu için dedupe yeni kayıt açmaz.
- **Dosya kaydı:** `Process.Start(path)` — `UseShellExecute` ile varsayılan uygulamada açılır.
- **Sil:** `Delete` tuşu, kalıcıysa diske de yazılır.

### Bırakma

Pill ve panel `AllowDrop`. Düşen dosya `IslandPruner.BuildDropPath` ile
`%AppData%\WharfBar\Drop\<GG.AA.YY ss.dd>\<ad>` altına kopyalanır (ad çakışmasında
` (2)`, ` (3)` …), sonra `IslandEntry` eklenir.

## Arayüz

### Durum makinesi

`collapsed` ⇄ `expanded`. Yeni kayıt geldiğinde `collapsed` pill'i ~1.2 sn boyunca
genişleyip geri çekilir.

### Panel

~420×440px, ortalanmış. Arama kutusu üstte, altında liste. Satır: tür ikonu, önizleme, kaynak
uygulama, göreli zaman. **İç içe `Popup` kullanılmaz** (`docs/04-gotchas.md` tuzağı: odak
kaçar, liste görünmez).

### Klavye

`↑`/`↓` seçim · `Enter` panoya koy / dosyayı aç · `Delete` kaydı sil · `Esc` kapat.

### Kapanma yolları

Mevcut dörtlü kalıba birebir uyar: `WM_ACTIVATEAPP` dışına tıklama · bar'da başka yere
tıklama · `Esc` · pill'e tekrar tıklama.

### Animasyon

`AnimateFlyoutOpen` / `AnimateFlyoutClose` yeniden kullanılır (170/140 ms açılış,
110/90 ms kapanış). `PopupAnimation=None` bırakılır — kod kendi animasyonunu oynatır.
Yeni animasyon altyapısı eklenmez.

## Ayar sayfası (`IslandPage`)

`Enabled` · `Persistence` (`Session` / `Persistent`) · `RetentionDays` · `MaxItems` ·
`Drop` klasörünü Explorer'da aç · **Tüm geçmişi temizle** (onaylı) · bırakılan dosyaları sil.

## Hata yönetimi

| Durum | Davranış |
|-------|----------|
| Pano başka uygulamada kilitli | STA worker'da birkaç kısa bekleyerek yeniden dene, olmazsa **sessizce düşür**. UI thread'inde asla yeniden deneme |
| Bozuk `island.json` | `.bak` + boş liste (ikinci bozulmada eski yedek `.bak.1`e döner). **Açılışı asla kilitlemez** (`ConfigService` davranışı) — aynı sebeple 64 MB'ı aşan dosya ve tek bozuk *alan* (bilinmeyen `kind`, bozuk `capturedAtUtc`/`id`) tüm geçmişi düşürmez |
| Disk yazma hatası | Bellekteki liste çalışmaya devam eder, sonraki değişiklikte tekrar denenir, kullanıcıya gösterilmez |
| Dosya kopyalama hatası | O dosya atlanır, kalanlar işlenir |
| Çıkışta bekleyen yazım | `Dispose` sırasında flush (mevcut `Restore` / `WaitForRestore` disiplini) |

## Test

Depoda mock framework yok (`Moq` / `NSubstitute` / arayüz sahteleri sıfır) ve **dış paket
eklemek `boundaries.md`'ye göre onay gerektiriyor**. Bu yüzden depodaki mevcut kalıbı
sürdürülür: **saf `internal static` yardımcı + canlı servis onu çağırır.**

`IslandPruner` saf çekirdeği için xUnit:
budama (gün / `MaxItems` / `Session` vs `Persistent`) · dedupe (ardışık aynı metin yukarı
taşınır, kopya yok) · arama eşleştirme (normalize, büyük/küçük harf duyarsız, çok satırlı
metin) · önizleme biçimlendirme (kırpma + üç nokta, satır sonları, boşluklar) · bırakma yolu
üretimi ve ad çakışması · `Normalize` sınırları (`VeilNormalizer` deseni).

Test edilemeyenler (canlı gözlemle doğrulanır): Win32 pano okuma, sürükle-bırak, `Popup`
animasyonu, panel görsel yerleşimi.

## Kapsam dışı (gerekçeli)

- **Canlı uygulama etkileşimi** — hook, izin onayı, pencere bağlama, entegrasyon yok.
- **Karakter / kişilik / ses** — Coucou "Mochi" ve sesleri **© Louis Raillé, tüm hakları
  saklı** (kod MIT). Karakter/ses kopyalanmaz; kendi görsel dilimiz kullanılır.
- **`CF_HDROP` pano yakalama** — Explorer'da dosya kopyalandığında `Drop` klasörüne
  kopyalansaydı 2 GB'lık bir dosya yüzünden çökerdi. Dosyalar yalnız sürüklenerek gelir.
- **Klasör bırakma** — 10 GB'lık klasörü sürükleyip kopyalamak kötü deneyim; ilerleme
  arayüzü gerektirir.
- **Global hotkey** — bölümde gerekçelendirildi.
- **Dış bağımlılık** — yalnız mevcut WPF-UI + `System.Management`.

## Tuzaklar (uygulama sırasında gözetilecek)

- `AllowsTransparency=true` **ile** Mica/DWM backdrop birlikte kullanılamaz
  (`boundaries.md`). Biz `AllowsTransparency`'yi yalnızca `Popup`'ta kullanıyoruz — ana
  bar Mica yüzeyinde şeffaflık yok.
- QS `Popup` içinde iç içe `Popup` **yasak** — liste `ScrollViewer`/`ItemsControl` olmalı.
- WPF-UI `SymbolIcon` enum adı pakette **doğrulanmadan** kullanılmayacak; yanlış enum
  parse hatasıyla **prosesi öldürüyor** (`docs/04-gotchas.md`).
- `ConfigService.Save` senkron ve UI thread'inde; pano geçmişi **asla** oraya yazılmaz.
- Metadata JSON'una dosya içeriği girmez; dosyalar `Drop` klasöründe durur.
