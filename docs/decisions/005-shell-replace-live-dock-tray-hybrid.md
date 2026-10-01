# ADR-005: Shell yerine geçme — canlı dock, tepsi hibrit, görev çubuğu gizleme

**Durum:** Kabul edildi  
**Tarih:** 2026-08-09  
**Bağlam:** WharfBar’ın Windows görev çubuğu + tepsi deneyimini kısmen üstlenmesi;
kullanıcı onayı (görev çubuğu gizleme seçenek B; tepsi hibrit; canlı dock pin/running).
Tasarım: [`../superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md`](../superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md).

## Karar

Üç karar tek ADR’de tutulur — birlikte “shell yerine geçen” deneyimi oluştururlar; ayrı
uygulansalar çift çubuk / boş tepsi / sahte pin listesi üretirler.

### 1. Canlı dock uygulamaları

| Kaynak | Kullanım |
|--------|----------|
| `Quick Launch\User Pinned\TaskBar\*.lnk` | Pin listesinin birincil kaynağı |
| `EnumWindows` + AppUserModelID | Çalışan uygulama gruplama / göstergesi |
| `config.pinnedApps` | Seed / fallback (birincil değil; şemadan silinmez) |

UWP/Store ikonları v1’de sınırlı kalır (placeholder veya atlama; yarım AppxManifest yolu yok).

### 2. Üst bar — tepsi hibrit (tam ayna değil)

- Saat / ağ / ses / pil: .NET / Win32 API ile simülasyon (mevcut `StatusService` çizgisi).
- Üçüncü taraf tepsi ikonları WharfBar içinde **çizilmez**; click-forward yok.
- Tek “tray / gizli ikonlar” düğmesi gerçek Windows tray overflow’u açar (`Win+B` veya
  chevron tıklaması).
- `ReadProcessMemory` / belgelenmemiş yabancı `NotifyIcon` enumeration **yasak**.
- `Shell_NotifyIcon` yalnızca WharfBar’ın kendi ikonu (Quit) için kalır.

### 3. Windows görev çubuğu — çalışırken gizle, çıkışta geri yükle

WharfBar çalışırken birincil monitör görev çubuğu gizlenir veya auto-hide’a alınır
(teknik yol uygulama planında). Başlatmada durum **yakalanır**; çıkış ve crash yollarında
**önceki duruma** geri yüklenir — `ABM_REMOVE` ile aynı çok yollu + idempotent disiplin
([ADR-003](003-topbar-placement-and-appbar-lifecycle.md)).

Kalıcı (WharfBar kapalıyken de) görev çubuğu bozma yok. Zorla süreç öldürme restore’u
atlayabilir → gotcha olarak dokümante.

### 4. Ek not (2026-08-29): 25H2'de work-area genişletmesi yapılamaz

Windows 11 25H2 (build 26200) canlı ölümleri: gizlenen görev çubuğunun ~48px AppBar
rezervasyonu **hiçbir dış mekanizmayla kaldırılamaz** — `SPI_SETWORKAREA` her flag
kombinasyonunda `ERROR_INVALID_PARAMETER (87)` döndürür; `Shell_TrayWnd` geometrisi shell
tarafından geri alınır; `ABM_SETSTATE` yok sayılır; yabancı `ABM_SETPOS` onaylansa bile
work area değişmez; auto-hide canlıyken bile legacy AppBar katmanı şeridi tutar
(`ABM_GETAUTOHIDEBAR=0`). Bu nedenle WharfBar çalışırken maximize pencereler alt şeride
saygı duyar ve "tam ekran tam dolmaz".

O günkü karar: sınırlılık kabul edilir; işlevsiz SPI çağrıları `WorkAreaExpandPolicy`
devre kesicisiyle atlanır (2×87 → 10 dk skip; `TaskbarCreated` reset; tek prob re-probe).
Auto-hide ortak yaşamı (SW_HIDE'ı bırakıp Windows auto-hide'a dayanma) değerlendirildi:
25H2'de auto-hide legacy katmana bildirilmiyor + maximize ile bilinen z-order
regresyonları var — reddedildi (bu red geçerli kalmaya devam eder).

### 5. Ek not (2026-08-30, final): ABS_AUTOHIDE kalıcı + adaptif tray blip'i

> **2026-09-30 tekrar ölçümü — karar doğrulandı, değiştirilmedi.** WharfBar **çalışırken**
> ölçüldü: `ABM_GETSTATE=1`, `Shell_TrayWnd` görünmez ve ~2px kıskaca parklı (top=1078),
> `SPI_GETWORKAREA.Bottom`=**1080** → **alt boşluk 0**, yani maximize pencereler tam ekran
> kaplıyor. Uygulama kapalıyken 1032/48'e dönüyor. 9 child dahil `DesktopWindowContentBridge`
> gizli → XAML hayaleti yok. `SPI_SETWORKAREA` ise gerçekten sessiz bir no-op; devre kesicisi
> bu yüzden dönüş değerine değil **etkiye** bakacak şekilde güncellendi.
>
> **Ölçüm tuzağı:** `ABM_SETSTATE`/`ABM_SETPOS` yalnızca **kayıtlı bir AppBar** çağırdığında
> etkilidir. Kayıt dışı bir süreçten çağrılırsa `TRUE` döner ama hiçbir şey olmaz — 2026-09-30'da
> bu yüzden "25H2'de work-area genişletme imkânsız" sonucuna varıldı ve **yanlış** bulundu.
> Bu API'leri yalnızca WharfBar çalışırken ölç.

**Pencere bazlı telafi denendi, kaldırıldı.** `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)`
ile maximize bandındaki pencereleri `SetWindowPos` ile uzatan ilk uygulama, uygulama
pencere yöneticileriyle savaştı: Firefox ve WinForms, maximize edilmiş pencerelerinin
önbelleğe alınmış sınırlarını yeniden dayatıyor (canlı ölçüm: `SetWindowPos ok=True` ama
rect 7 ms'de bir geri alınıyor, pencere titriyor ve maximize state'i bozuluyor). Dünkü
"pencere yöneticisiyle savaş" gerekçesi bu yüzden haklı çıktı; yaklaşım tamamen söküldü.

**Gerçek çözüm — native auto-hide state:** gizleme akışı `ABM_SETSTATE(ABS_AUTOHIDE)`
verir ve çalışma süresince o state'te kalır (HWND gizli). Canlı ölçüm: auto-hide
state'te explorer görev çubuğunu ~2px'lik kenar kıskacına park eder ve alt AppBar
rezervasyonunu work-area'dan çıkarır (`rcWork` bottomGap 48 → 0) — maximize pencereler
alt şeridi hiç savaşmadan kapatır; hiçbir pencereye dokunulmaz.

**Tray aynası bu state'te iki kısıtla çalışır** (park edilmiş pencerenin XAML içeriği
UIA'ya tamamen virtualize olur; `SWP_NOSENDCHANGING` ile taşıma, `WM_MOUSEMOVE` hover,
reveal tetikleme — hepsi canlı ölü):
1. Okumalar **desktop-root'tan** (`RootElement.FindAll(SystemTray.NormalButton)`), özel
   **STA dispatcher thread'inde** (MTA'da masaüstü-altı UIA yürüyüşü boş döner; pump'suz
   STA'da kilitlenme). Park edilmişken okuma yine boş olur — butonlar ancak tray
   unpark edildiğinde var olur.
2. Bu yüzden **adaptif blip**: en fazla 60 sn'de bir ve birincil monitörde maximize
   pencere YOKKEN state 0'a düşürülüp ~0.5 sn'lik okuma penceresi açılır. Maximize
   pencere varken blip atlanır (work-area flap'i maximize pencereyi görünür şekilde
   kısaltır) ve **son iyi snapshot ekranda donuk kalır** — boş okumalar aynayı
   temizlemez. Sağ tık / sol tık da aynı blip'i kullanır (tıklama koordinatları için
   tray unpark şart); flap bu yüzden tıklama anında ~0.5 sn sürer.

**Çıkış restorasyonu:** `SetState(baseline)` + `SetTrayVisible(true)` — state 0 + tray
görünür olunca explorer kaydı yeniden oluşturur (doğrulandı), şerit döner. **Zorla kill
zehirlenmesi:** ABS_AUTOHIDE state süreç ölümünden sağ çıkar; bir sonraki açılış stale
watchdog dosyasından kayıtlı baseline state'i geri yükleyip öyle yakalar (yoksa
zehirlenmiş state baseline sanılır ve tüm oturumlar bozuk restore eder — canlı yaşandı).
`SPI_SETWORKAREA` devre kesicisi yedek olarak durur.

## Gerekçe

- Config’teki demo pinler gerçek görev çubuğu pinlerini yansıtmaz; kullanıcı “yerine geçen”
  dock bekler.
- Resmi API ile yabancı tepsi ikonlarını okumak desteklenmez; tam ayna kırılgan ve sınır
  ihlali riski taşır. Native overflow düğmesi yeterli erişim verir.
- Görev çubuğu görünürken altta iki şerit kalır; gizleme onayıyla (B) ürün hedefi netleşti.
  Restore kaçırmak AppBar asılı alanı kadar kötü bir shell durumudur.

## Sonuçlar

- `.agent/boundaries.md`: görev çubuğu gizleme artık “önce sor” değil; **restore zorunlu**
  “yap” + “asla restore’suz bırakma”.
- `docs/00-overview.md` non-goals: yabancı tepsi okuma non-goal kalır; görev çubuğu gizleme
  blanket yasak olmaktan çıkar, kontrollü yaşam döngüsüne bağlanır.
- `pinnedApps` semantiği seed/fallback olur; `schemaVersion` 1 kırılmaz.
- Geliştirme sırası: Faz 9 (9a taskbar lifecycle → 9b canlı dock → 9c tray düğmesi);
  Faz 8 auto-hide 9a/9b sonrası daha anlamlı.
- Cursor kurallarına Win32 rehberi yapıştırılmaz; işaretçi + bu ADR / spec yeter.

## Alternatifler (reddedilen)

1. **Tam tepsi aynası** (yabancı ikon çiz + forward) — belgelenmemiş API / RPM; reddedildi.
2. **Görev çubuğuna dokunmama** (önceki non-goal) — çift çubuk; kullanıcı B ile reddetti.
3. **Yalnızca config pinleri** — canlı shell pinleriyle uyumsuz; yetersiz.
4. **İki ayrı ADR** (taskbar vs tray) — sıkı bağlı ürün kararı; tek ADR tercih edildi.
