# ADR-005: Shell yerine geçme — dock, tepsi hibrit, görev çubuğu gizleme

**Durum:** Kabul edildi  
**Tarih:** 2026-08-09  
**Bağlam:** WharfBar’ın Windows görev çubuğu + tepsi deneyimini kısmen üstlenmesi;
kullanıcı onayı (görev çubuğu gizleme seçenek B; tepsi hibrit; canlı dock pin/running).
Tasarım: [`../superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md`](../superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md).

> **Revizyon (2026-10-04, dock yeniden tasarımı):** §1’in — ve dolayısıyla başlıktaki “canlı
> dock” ifadesinin — “birincil pin kaynağı = `Quick Launch\User Pinned\TaskBar\*.lnk`” kararı
> **tersine çevrildi**. Dock’un tek kaynağı artık `dock.items` — WharfBar’ın kendi kalıcı
> listesidir — ve Windows görev çubuğunun **pin tarafına hiç dokunulmaz**: `User Pinned`
> klasöründen `.lnk` **okunmaz**, görev çubuğuna `.lnk` **yazılmaz** (`TaskbarPinnedAppsSource`
> silindi; “Dock’tan kaldır” artık görev çubuğunu değiştirmez).
> *Kapsam notu:* bu, **yalnız pin listesidir.** Görev çubuğu WharfBar çalışırken yine de
> gizlenir/auto-hide'a alınır ve çıkışta geri yüklenir — o disiplin §3–5'in konusudur ve
> etkilenmemiştir.
> **Bu revizyon yalnız §1’in kaynak tablosunu kapsar.**
> §1’deki `EnumWindows` + AppUserModelID satırı (çalışan uygulama gruplama / göstergesi),
> §2 (tepsi hibrit) ve §3–5’in görev çubuğu gizleme/restore disiplini **değişmemiştir.**
> Yeniden tasarımın gerekçesi ve kapsamı:
> [`../superpowers/specs/2026-10-04-dock-macos-redesign-design.md`](../superpowers/specs/2026-10-04-dock-macos-redesign-design.md).

## Karar

Üç karar tek ADR’de tutulur — birlikte “shell yerine geçen” deneyimi oluştururlar; ayrı
uygulansalar çift çubuk / boş tepsi / sahte pin listesi üretirler.

### 1. Dock uygulamaları

| Kaynak | Kullanım |
|--------|----------|
| `dock.items` (config) | **Tek** sabit listesi; sıra = görüntü sırası |
| `EnumWindows` + AppUserModelID | Çalışan uygulama gruplama / göstergesi (değişmedi) |
| `config.pinnedApps` | Eski şema alanı; yalnızca `dock.items`’e bir kez taşınır, sonra boşaltılır |

Göç tek seferliktir ve `ConfigService.MigrateDockItems` yapar: `dock.items` boşken eski kök
`pinnedApps` doluysa içerik kopyalanır ve eski liste **boşaltılır** — aksi halde kullanıcının
bilerek boşalttığı dock her açılışta eski pinleri geri getirirdi. `schemaVersion` bu değişiklikte
**2'de kaldı**: alan bazlı varsayılanlarla kırılmadan okuma tercih edilir (`conventions.md:24`),
yeni alanların hepsi yazılmayan config'te kendi varsayılanına düşer.

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

- ~~Config’teki demo pinler gerçek görev çubuğu pinlerini yansıtmaz; kullanıcı “yerine geçen”
  dock bekler.~~ **2026-10-04’te tersine döndü:** yansıma zorunluluğu değil, listeden
  bağımsız bir liste isteniyor. Aynı gerekçeyi tersinden okuyan karşı-argüman bugün geçerli:
  bir “yerine geçen” dock için başka bir uygulamanın (explorer) pin listesini okumak zorunda
  kalmak, o listedeki kırılganlığı (`Quick Launch` yolu, dil, sürüm) WharfBar’a taşır.
- Resmi API ile yabancı tepsi ikonlarını okumak desteklenmez; tam ayna kırılgan ve sınır
  ihlali riski taşır. Native overflow düğmesi yeterli erişim verir.
- Görev çubuğu görünürken altta iki şerit kalır; gizleme onayıyla (B) ürün hedefi netleşti.
  Restore kaçırmak AppBar asılı alanı kadar kötü bir shell durumudur.

## Sonuçlar

- `.agent/boundaries.md`: görev çubuğu gizleme artık “önce sor” değil; **restore zorunlu**
  “yap” + “asla restore’suz bırakma”.
- `docs/00-overview.md` non-goals: yabancı tepsi okuma non-goal kalır; görev çubuğu gizleme
  blanket yasak olmaktan çıkar, kontrollü yaşam döngüsüne bağlanır.
- `pinnedApps` semantiği seed/fallback olur; `schemaVersion` kırılmaz.
  **2026-10-04'te ikisi de çözüldü:** alan `dock.items`'e bir kez taşınıp **boşaltılır**, `schemaVersion`
  ise monitör fazından gelen **2'de kalır** — göç alan bazlı varsayılanlarla kırılmadan okunduğu
  için ek sürüm gerekmedi (bkz. §1).
- Geliştirme sırası: Faz 9 (9a taskbar lifecycle → 9b dock → 9c tray düğmesi);
  Faz 8 auto-hide 9a/9b sonrası daha anlamlı.
- Cursor kurallarına Win32 rehberi yapıştırılmaz; işaretçi + bu ADR / spec yeter.

## Alternatifler (o gün reddedilenler)

1. **Tam tepsi aynası** (yabancı ikon çiz + forward) — belgelenmemiş API / RPM; reddedildi.
2. **Görev çubuğuna dokunmama** (önceki non-goal) — çift çubuk; kullanıcı B ile reddetti.
3. **Yalnızca config pinleri** — canlı shell pinleriyle uyumsuz; yetersiz.
   **2026-10-04’te bu alternatif kabul edildi** (§1 revizyonu): görev çubuğunun pin tarafına
   dokunulmadığı için “uyumsuzluk” artık bir maliyet değil; liste büyütüldü (sürükle-sırala,
   dosya bırakma, ayar editörü) ve dock yine de “yerine geçen” deneyimi sunuyor. Bu yüzden
   **bugün reddedilenler listesi yalnız 1, 2 ve 4’ü kapsar.**
4. **İki ayrı ADR** (taskbar vs tray) — sıkı bağlı ürün kararı; tek ADR tercih edildi.
