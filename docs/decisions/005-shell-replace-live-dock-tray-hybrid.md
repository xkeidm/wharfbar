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

Karar: sınırlılık kabul edilir; işlevsiz SPI çağrıları `WorkAreaExpandPolicy` devre
kesicisiyle atlanır (2×87 → 10 dk skip; `TaskbarCreated` reset; tek prob re-probe).
Auto-hide ortak yaşamı (SW_HIDE'ı bırakıp Windows auto-hide'a dayanma) değerlendirildi:
25H2'de auto-hide legacy katmana bildirilmiyor + maximize ile bilinen z-order
regresyonları var — reddedildi. Pencere bazlı telafi (maximize pencereleri elle
büyütme) pencere yöneticisiyle sürekli savaş — reddedildi.

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
