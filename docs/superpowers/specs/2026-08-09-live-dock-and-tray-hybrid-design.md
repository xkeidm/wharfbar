# WharfBar — Canlı dock, tepsi hibrit ve görev çubuğu değişimi

**Tarih:** 2026-08-09  
**Durum:** Onaylandı (kullanıcı tasarım onayı); uygulama planı: [`../plans/2026-08-09-live-dock-and-tray-hybrid.md`](../plans/2026-08-09-live-dock-and-tray-hybrid.md)  
**ADR:** [005-shell-replace-live-dock-tray-hybrid.md](../../decisions/005-shell-replace-live-dock-tray-hybrid.md)  
**Kapsam:** Canlı görev çubuğu pin + çalışan uygulamalar (dock), üst barda tepsi hibrit
(native overflow; tam ayna değil), WharfBar çalışırken Windows görev çubuğunu gizleme /
auto-hide ve çıkışta geri yükleme.

## Kararlar (özet)

| Konu | Seçim |
|------|--------|
| Dock pin kaynağı | `Quick Launch\User Pinned\TaskBar` altındaki `.lnk` tarama |
| Dock çalışan uygulamalar | `EnumWindows` + AppUserModelID gruplama |
| `config.pinnedApps` | **Seed / fallback** — birincil kaynak değil; klasör boş/okunamazsa kullanılır |
| Üst bar durum | Saat / ağ / ses / pil simülasyonu devam (+ ses gerektiğinde genişletilir) |
| Üçüncü taraf tepsi ikonları | WharfBar içinde **çizilmez**; click-forward yok |
| Tepsi erişimi | Tek “tray / gizli ikonlar” düğmesi → gerçek Windows overflow (`Win+B` veya chevron) |
| Yabancı NotifyIcon | `ReadProcessMemory` / belgelenmemiş enumeration **yok** |
| Windows görev çubuğu | WharfBar çalışırken gizle / auto-hide (**onay B**); çıkış ve crash yollarında geri yükle |
| Global menü | Kapsam dışı (değişmez) |

## Hedefler

1. Dock, Windows görev çubuğundaki pinleri ve çalışan uygulamaları yansıtsın (klasik `.exe`/`.lnk`).
2. Üst bar, sistem durumunu .NET/Win32 API ile göstermeye devam etsin; üçüncü taraf tepsi
   ikonlarına ihtiyaç olmadan kullanıcı native overflow’a tek tıkla ulaşabilsin.
3. WharfBar açıkken Windows görev çubuğu görünmez / auto-hide olsun; kapanınca önceki duruma
   dönülsün — `ABM_REMOVE` ile aynı disiplin.

## Non-goals

- Üçüncü taraf / yabancı tepsi ikonlarını WharfBar içinde çizmek veya tıklamayı iletmek
- `Shell_NotifyIcon` veya belgelenmemiş yollarla sistem tepsi listesini “okumak”
- `ReadProcessMemory` ile explorer/notify ikon heap’ine erişim
- macOS tarzı global uygulama menüsü (File/Edit/View)
- UWP/Store paket ikonlarının tam desteği (v1 sınırı devam; kırık deneyim sunulmaz)
- İkincil monitör AppBar / work-area rezervasyonu
- Görev çubuğunu kalıcı olarak (WharfBar kapalıyken de) bozmak

## Mimari

```
App.OnStartup
  ├─ ConfigService / Autostart / Mutex (mevcut)
  ├─ TaskbarVisibilityService.CaptureAndHide()   ← yeni; çıkışta Restore
  ├─ TopBarWindow
  │    ├─ StatusService (saat / ağ / ses / pil)
  │    └─ TrayOverflowButton → native overflow
  ├─ DockWindow
  │    ├─ TaskbarPinnedAppsSource (.lnk tarama)
  │    ├─ RunningAppsSource (EnumWindows + AUMID)
  │    └─ birleşik pin + running strip
  └─ TrayIconService (kendi Quit ikonu; sistem okuma değil)

Çıkış / crash yolları (AppBar ile aynı set)
  ├─ TaskbarVisibilityService.Restore()
  └─ AppBar.Remove()
```

Yeni servisler `Services/` altında; ham P/Invoke `Native/` içinde `internal` kalır.
UI, `NativeMethods`’a doğrudan bağlanmaz.

### Dock — kaynak gerçekliği

| Katman | Kaynak | Rol |
|--------|--------|-----|
| Pinler | `%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\*.lnk` | Birincil pin listesi |
| Çalışan | Görünür top-level pencereler (`EnumWindows`) + AppUserModelID | Gruplama / “çalışıyor” göstergesi |
| Fallback | `config.pinnedApps` (+ `DemoPinnedApps` seed) | Pin klasörü boş, yok veya okunamazsa |

**Birleşik UI kuralı (v1):**

1. Pin sırası: TaskBar `.lnk` dosya sırası (veya shell’in verdiği kararlı sıra).
2. Pinlenmiş ve çalışan: tek ikon; çalışan göstergesi (nokta / glow — uygulama planında netleşir).
3. Pinlenmemiş çalışan: strip’in sağında (veya pinlerden sonra) eklenir; tıklayınca odak /
   restore; UWP ikonu çözülemezse jenerik placeholder (kırık özel ikon yolu yok).
4. Pin tıklaması: zaten çalışıyorsa öne getir; değilse `ProcessLauncherService` / `.lnk` aç.

**`config.pinnedApps` rolü (net):**

- **Kaldırılmaz** (şema kırılmaz; `schemaVersion` 1 kalır).
- Runtime birincil kaynak **değildir**.
- İlk kurulumda `DemoPinnedApps` ile seed edilmeye devam eder.
- Canlı tarama başarılı ve ≥1 pin bulursa dock **yalnızca** canlı pinleri gösterir.
- Tarama başarısız / 0 pin → config listesi fallback olarak kullanılır (kullanıcı boş dock görmez).
- Config’e yazma: bu fazda pin ekle/çıkar UI yok; Windows görev çubuğundan pin değişince
  WharfBar yeniden tarar (poll veya FS watcher — uygulama planında seçilir).

### Üst bar — tepsi hibrit

| Öğe | Davranış |
|-----|----------|
| Saat / ağ / pil | Mevcut `StatusService` yolu; simülasyon |
| Ses / volume | Gerekirse aynı modele eklenir (Core Audio veya basit proxy); tepsi ikonu kopyası değil |
| Tray / gizli ikonlar düğmesi | Tek kontrol; native Windows tray overflow’u açar |
| Overflow açma | Tercihen `Win+B` (odak + kullanıcı etkileşimi) veya görev çubuğu chevron’una programatik tıklama; hangisi güvenilir bulunursa uygulama planında sabitlenir |
| WharfBar kendi tepsi ikonu | `TrayIconService` Quit menüsü kalır; sistem ayna değildir |

**Yasak:** yabancı `NOTIFYICONDATA` okuma, explorer bellek tarama, WharfBar canvas’ında
üçüncü taraf ikon çizimi / click-forward.

### Windows görev çubuğu — gizle / geri yükle

WharfBar **çalışırken** birincil monitör görev çubuğu gizlenir veya auto-hide’a alınır
(uygulama planı teknik yolu seçer: `ABM_SETSTATE`, `SHAppBarMessage` state, veya belgelenmiş
eşdeğer). Amaç: çift çubuk hissini kaldırmak; WharfBar “yerine geçen” deneyim.

**Yaşam döngüsü (zorunlu):**

1. Başlatmada: mevcut görev çubuğu durumunu **yakala** (görünür / auto-hide bayrakları).
2. Gizle / auto-hide uygula.
3. Çıkışta **önceki duruma** geri yükle — `ABM_REMOVE` ile aynı yollar:
   `OnClosing`, `Application.Exit`, `SessionEnding`, `ProcessExit`,
   `DispatcherUnhandledException`, `UnhandledException`.
4. Restore idempotent olmalı (`Interlocked` veya eşdeğer); çift çağrı zararsız.
5. Restore, AppBar `Remove` ile birlikte bağlanır; sıra: mümkünse önce taskbar restore,
   sonra AppBar remove (veya tersi — hangisi flicker üretmezse plan aşamasında ölçülür;
   ikisi de kaçırılmamalı).
6. Zorla kill (`Stop-Process -Force`) restore’u atlar → gotcha: kullanıcı `explorer.exe`
   restart ile düzeltir; dokümante edilir.

Explorer / `TaskbarCreated`: shell yeniden doğunca WharfBar hâlâ açıksa gizleme yeniden
uygulanır; kapanışta yine yakalanan baseline’a dönülür.

## Config etkisi

| Alan | Değişiklik |
|------|------------|
| `schemaVersion` | 1 kalır (breaking yok) |
| `pinnedApps` | Anlam güncellenir: seed/fallback; canlı pinler baskın |
| `dock.*` | Aynı; auto-hide Faz 8 ile tamamlanır |
| Yeni alan (opsiyonel, plan) | Örn. `replaceTaskbar: true` — v1’de varsayılan açık olabilir; yoksa davranış her zaman gizle |

Bu spec uygulama kodu yazmaz; alan ekleme ihtiyacı implementation plan’da netleşir.

## Riskler

| Risk | Azaltma |
|------|---------|
| Görev çubuğu restore kaçması | AppBar ile aynı çok yollu + idempotent restore; gotcha dokümanı |
| Pin klasörü yolu / dil / Windows sürümü farkı | Bilinen `User Pinned\TaskBar` yolu; başarısızsa config fallback |
| AUMID’siz klasik pencereler | Executable yolu / pencere sınıfı ile gruplama yedekleri |
| UWP çalışan ama ikon yok | Placeholder; v1’de Store ikon yolu yok |
| Overflow açma API kırılganlığı | `Win+B` birincil; chevron yedek; başarısızsa kullanıcıya sessizce no-op + debug log |
| Çift dock + taskbar flicker (geçiş anı) | Önce capture+hide, sonra dock’u monitör altına yerleştir |
| Auto-hide taskbar ile WharfBar dock çakışması | Taskbar gizlenince dock work-area / monitör alt kenarına oturur |

## Fazlı teslimat

Bu iş **Faz 9** olarak sıralanır; Faz 8 (dock auto-hide polish) geometriyi etkilediği için
Faz 9a sonrası veya 9b ile birlikte yapılabilir.

| Alt faz | Ne gelir | Bağımlılık |
|---------|----------|------------|
| **9a** | `TaskbarVisibilityService`: capture → hide/auto-hide → restore (tüm çıkış yolları) | Yok (önce güvenlik) |
| **9b** | Canlı pin tarama + `EnumWindows`/AUMID running; `pinnedApps` fallback; dock UI birleşimi | 9a sonrası tercih (alt kenar boş) |
| **9c** | Üst bar “tray / gizli ikonlar” düğmesi; isteğe bağlı ses göstergesi | 9a şart değil |
| **8** | Dock auto-hide / hit-test polish | 9a/9b sonrası daha anlamlı (görev çubuğu yokken) |

**İlk gemi (minimum değer):** 9a + 9b. Tray düğmesi (9c) kısa süre sonra; tam ayna asla.

## Test / doğrulama notları

Manuel (v1):

1. Başlat → Windows görev çubuğu kaybolur / auto-hide; WharfBar dock altta.
2. Normal Quit / Alt+F4 / tepsi Quit → görev çubuğu önceki haline döner; work area AppBar ile tutarlı.
3. Bilinçli crash yolu (unhandled) → restore + `ABM_REMOVE` tetiklenir mi.
4. Zorla kill → görev çubuğu asılı kalabilir; `explorer.exe` restart düzeltir (beklenen gotcha).
5. TaskBar’a pin ekle/çıkar → dock listesi güncellenir (poll/watcher gecikmesi kabul).
6. Pinli uygulama çalışırken tek ikon + çalışan göstergesi; pinsiz çalışan ayrı görünür.
7. Tray düğmesi → Windows overflow / bildirim alanı odağı açılır; WharfBar’da yabancı ikon yok.
8. Explorer restart (`TaskbarCreated`) → AppBar yeniden kayıt + taskbar gizleme yeniden uygulanır.
9. UWP penceresi: çökmeden placeholder veya atlama (kırık ikon yok).

## Doküman güncellemeleri (bu değişiklikle)

- ADR-005
- `.agent/boundaries.md`, `.agent/project.md`, `.agent/context/roadmap.md`
- `docs/00-overview.md`, `docs/03-development-order.md`, `docs/04-gotchas.md`

## Self-review (2026-08-09)

- Bloklayıcı placeholder yok. Overflow açma (`Win+B` vs chevron) iki adayla sınırlı; uygulama
  planında tek yol seçilecek — tasarım kapsamını değiştirmez.
- ADR-005, boundaries ve non-goals ile çelişki yok: yabancı tepsi okuma hâlâ yasak;
  görev çubuğu gizleme kontrollü onay + restore zorunlu.
- Kapsam onaya uyuyor: tam tepsi aynası yok; global menü yok.
- Faz sırası net: 9a → 9b → 9c; Faz 8 auto-hide 9a/9b sonrası.
- `pinnedApps` = seed/fallback (silinmez); canlı TaskBar `.lnk` baskın.
