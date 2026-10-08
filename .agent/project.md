# WharfBar — Proje Brief (AI)

## Tek cümle

Windows’ta macOS tarzı **top bar + dock**: .NET 8 WPF + WPF-UI + Win32 P/Invoke.

## Öncelik (çakışmada)

1. Düşük kaynak tüketimi → 2. Windows Fluent native görünüm → 3. Basitlik.
   macOS marka/ikon dili yok; yalnızca düzen ilhamı. Ayrıntı: [`../docs/00-overview.md`](../docs/00-overview.md).

## Ürün

- **Top bar:** Sol Windows logo (çizim) + sistem istatistikleri (`CPU x% · GPU y% · RAM z%`;
  çoklu GPU'da `GPU0/GPU1` ayrımı: `topBar.showGpuPerAdapter`;
  `topBar.showSystemStats`, `SystemStatsService`) + menü (Hakkında / Kapat); sağda durum şeridi
  (`trayMirror` → `trayOverflow` → `dateTime` → `notifications` → `controlCenter` → `volume` → `brightness` → `media` →
  `network` → `battery`; özelleştirilebilir). Arka plan kişiselleştirme: `mica` / `transparent` / `tint`
  + opaklık kaydırıcısı (sağ tık menü). Saat tek satır `tr-TR`; saat+bildirim → native `Win+N`;
  `controlCenter` → QS Main; ses → QS Mixer; parlaklık → `BrightnessFlyout` (Monitorian DDC/CI + WMI hibrit);
  medya → `MediaFlyout` (SMTC; oturum yokken gizli, ADR-008); ağ → QS Network (ADR-006); **AppBar** + tepsi hibrit.
- **Dock:** macOS tarzı alt şerit (overlay; Topmost ± auto-hide) — **tek kaynağı kendi listesi**
  (`dock.items` / `DockItemStore`; Windows görev çubuğunun **pin tarafına dokunulmuyor** —
  ADR-005 revizyonu, `config.pinnedApps` yalnızca göç seed'i olarak bir kez okunur). Üç bölüm
  sırayla **Pinned | Running | Minimized**, tek sürekli şerit; ortalanmış levha
  (`dock.backgroundMode`: Mica / saydam / renkli). Sürükle-sırala, Explorer'dan dosya bırakma,
  alt-sol/orta/sağ konum; sağ tık bağlam menüsü (Uygulama Başlık Kartı, Dock'a Sabitle/Kaldır,
  Görevi Sonlandır, Pencereyi Kapat, Yönetici Çalıştır, Dosya Konumunu Aç, Yeni Pencere,
  **Pencereler**).
- **Görev çubuğu:** WharfBar çalışırken gizle/auto-hide; çıkışta geri yükle (ADR-005).
- **Monitör:** Monitör bazında (per-monitor) top bar / dock — `monitors` config ile her
  monitör ayrı açılır/kapanır; tüm bar'lar paylaşılan bir motor üzerinde çalışır; her bar
  kendi monitöründe AppBar (`SPI_SETWORKAREA` yedeği 25H2+'ta ölü — ADR-005 ek notu).
  Veil birincil monitörle sınırlı.
- **Global menü yok** — top bar File/Edit benzeri uygulama menüsü taşımaz.

## Stack (özet)

.NET 8 WPF, WPF-UI, `net8.0-windows10.0.19041.0`, Per-Monitor V2 DPI. Temel P/Invoke için `AllowUnsafeBlocks` gerekmez. Ayrıntı: [`context/stack.md`](context/stack.md).

## Mimari özeti

- UI klasörü: **`Views/`** (veya `UI/`) — `/Windows` kullanma (Win32 ile karışır).
- Native: `AppBar`, DWM (yedek), `ShellIcon`.
- Services: config, launcher, ikon, autostart (Startup klasörü), Mutex, status (.NET API).
- Config: `%AppData%/WharfBar/config.json` + `schemaVersion`.

Tam yapı: [`../docs/01-architecture.md`](../docs/01-architecture.md).

## Kritik teknik gerçekler (unutma)

1. **`Shell_NotifyIcon` ≠ sistem tepsi okuma.** Wi-Fi/pil/ses için .NET API + kendi ikonlar.
2. **Dock AppBar zorunlu değil** — overlay tercih (ADR-001). Top bar AppBar kullanır.
3. **WPF-UI backdrop önce**; ham DWM Mica ile çakıştırma. **`AllowsTransparency=true` + Mica yasak.**
4. **AppBar:** çıkışta `ABM_REMOVE`; `ABN_POSCHANGED` içinde `SHAppBarMessage` yok (`BeginInvoke`, aynı rect’te SETPOS atla); `WM_WINDOWPOSCHANGING` onaylı rect’e kıskaç (phantom taskbar work-area itmesi; dock kıskacı `Measure` etmez); onaylı rect’te `ABM_WINDOWPOSCHANGED` yok; explorer restart sonrası yeniden kayıt; DPI/display handler’lar.
5. **v1:** UWP ikonları sınırlı (placeholder); görev çubuğu gizleme + restore (ADR-005); tek örnek Mutex.
6. **Autostart:** Startup klasörü > Registry Run (`AutostartService` + `AppConfig.autostart`).
7. **Hit-testing + auto-hide** dock’ta uygulandı (`DockHitTest` + `DockAutoHideController`; `FullscreenState` köprüsü). Spec: [`../docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md`](../docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md).
8. **Durum:** saat / ağ / pil `.NET` + `GetSystemPowerStatus`; ses Core Audio; hızlı ayarlar
   (ADR-006; `controlCenter` → Main, volume → Mixer); tepsi yalnızca kendi Quit menüsü.
   Üst bar sol küme: Windows logo menüsü + sistem istatistikleri (`GetSystemTimes` /
   `GlobalMemoryStatusEx` / PDH `PdhAddEnglishCounterW`; GPU sayacı yoksa GPU bölümü gizlenir).
9. **Tray aynası (ADR-009):** yabancı tepsi ikonları belgelenmiş comctl32 `TB_*` mesajlarıyla
   salt-okunur okunur + `SendMessage` click-forward; RPM/enjeksiyon/undocumented API **yasak**;
   gizli ikonlar kapsam dışı (`Win+B` kalır); sistem ikonları tooltip filtreyle düşülür.
10. **Dock'un tek listesi kendisidir (ADR-005 revizyonu):** görev çubuğunun **pin tarafına**
    dokunulmuyor (görev çubuğunu gizleme/geri yükleme kapsamda, ADR-005 §3); büyütme motoru saf
    (`DockMagnification` / `DockStripLayout`) ve pencere hover'da yeniden boyutlanmaz.

## Doküman haritası

| Dosya | İçerik |
|-------|--------|
| `docs/00-overview.md` | Amaç / non-goals |
| `docs/01-architecture.md` | Klasörler, config şeması |
| `docs/02-win32-appbar-dwm.md` | AppBar/DWM/tray/ikon |
| `docs/03-development-order.md` | Fazlar |
| `docs/04-gotchas.md` | Tuzaklar |
| `docs/decisions/001-...` | Dock overlay kararı |
| `docs/decisions/002-...` | Proje yerleşimi, TFM, `global.json` yok |
| `docs/decisions/003-...` | Top bar yerleşimi, `ABM_REMOVE`, fullscreen |
| `docs/decisions/004-...` | Mica / WPF-UI, köşe `DONOTROUND`, tema yenileme |
| `docs/decisions/005-...` | Dock, tepsi hibrit, görev çubuğu gizle/geri yükle — **2026-10-04 revizyonu:** dock'un pin kaynağı `dock.items` (başlıktaki "canlı dock" ifadesi de bu revizyonla düştü) |
| `docs/decisions/006-...` | Hızlı ayarlar flyout (ses karıştırıcı + ağ) |
| `docs/decisions/007-...` | Renk kaplaması (veil) — katmanlı pencere |
| `docs/decisions/008-...` | Medya paneli (SMTC) — çalan medya + kontroller |

## Mevcut repo durumu

**Faz 1–9 + Faz 8 + dock yeniden tasarımı (2026-10-04) kodu çalışma ağacında** (manuel doğrulama
bekliyor). Kod kökü
`src/WharfBar/`, çözüm kökte `WharfBar.sln`. Çalışan: top bar penceresi (DPI-farkında yerleşim),
AppBar kaydı + garantili `ABM_REMOVE`, `ABN_*` callback’leri, explorer restart sonrası yeniden kayıt,
tek örnek Mutex, WPF-UI Mica + sistem light/dark (veya config `theme`), kare köşeler
(`DWMWCP_DONOTROUND`, ADR-004), dock overlay (`DockWindow`; kendi `dock.items` listesi + canlı
çalışan uygulamalar ve küçültülmüş pencereler → üç bölümlü şerit, ortalanmış levha (ölçek 1),
sürükle-sırala, dosya bırakma, konum seçimi, süreç başlatma / pencere odaklama, "Pencereler"
alt menüsü, canlı DPI, fullscreen gizleme, `WorkAreaChanged` yeniden konumlanma, auto-hide +
hit-test (`DockAutoHideController`, `DockHitTest`)), `ConfigService` → `%AppData%/WharfBar/config.json`,
`AutostartService` → Startup klasörü `WharfBar.lnk` (`AppConfig.autostart`), saat + ağ + pil
(`StatusService`, üst bar sağ şerit; tek satır `tr-TR` saat; **Segoe MDL2** glifleri + overflow +
`controlCenter`), kendi tepsi ikonu + Quit (`TrayIconService`),
görev çubuğu gizle/restore (`TaskbarVisibilityService`; keep-hidden izleme; çıkışta restore → `ABM_REMOVE`),
canlı çalışan uygulamalar (`RunningAppsSource`) + küçültülmüş pencereler (`MinimizedWindowsSource`)
→ `DockAppsComposer`, üst bar tray overflow düğmesi (`TrayOverflowInvoker` / `Win+B`).
Dock varsayılan glif **24 DIP**, düğme 40, levha yüksekliği 56, aralık 4 DIP; pencere levhaya
eşittir (`IShellItemImageFactory` glif boyutunda). Üst bar tooltip’leri Segoe UI (MDL2 mirası tofu yok). Overflow: gizli taskbar’da peek-show.
Sol menü: Hakkında + Kapat (`Application.Shutdown` → restore + `ABM_REMOVE`). **Hızlı ayarlar**
(ADR-006): `controlCenter`/ses/ağ ikonundan paylaşılan flyout (Main / Mixer / Network);
tek aygıt + dropdown + uygulama oturumları; ağ durumu / Settings. Canlı ağ özellikleri
tamamlandı: canlı Wi-Fi listesi + bağlanma (`WifiService`, wlanapi.dll; QS Wifi sayfası + şifre paneli),
BT cihaz listesi (`BluetoothService`), gece ışığı gerçek durum + toggle (`NightLightService`,
CloudStore best-effort + oturum override), native yansıtma paneli 4 mod (`ProjectionService`,
`QueryDisplayConfig`/`SetDisplayConfig`), tile animasyonları (in-place pop + renk geçişi).
Spec: [`../docs/superpowers/specs/2026-08-16-quick-settings-live-network-nightlight-project-design.md`](../docs/superpowers/specs/2026-08-16-quick-settings-live-network-nightlight-project-design.md);
plan: [`../docs/superpowers/plans/2026-08-16-quick-settings-live-network-nightlight-project.md`](../docs/superpowers/plans/2026-08-16-quick-settings-live-network-nightlight-project.md).
**Renk kaplaması (veil):** QS karosu + global kısayol (Ctrl+F11) ile tam ekran renk overlay;
Kaplama ayarlar sayfası (renk/opaklık/kısayol).
Spec/plan: [`../docs/superpowers/specs/2026-08-18-veil-overlay-design.md`](../docs/superpowers/specs/2026-08-18-veil-overlay-design.md),
[`../docs/superpowers/plans/2026-08-18-veil-overlay.md`](../docs/superpowers/plans/2026-08-18-veil-overlay.md);
karar: [`../docs/decisions/007-veil-overlay.md`](../docs/decisions/007-veil-overlay.md).
**Medya paneli (SMTC):** üst bar `media` öğesi (yalnızca oturum varken görünür) + medya
flyout'u (kapak, başlık/sanatçı, Oynat/Duraklat + İleri/Geri, çoklu oturumda chevron
seçici); `MediaSessionService` (Windows SMTC izleme + komutlar + 10 sn retry),
`MediaSessionSelector` (en son aktif politikası), `MediaSnapshot`; katalog **v4**
migration (`media` `brightness` sonrasına eklenir).
Spec: [`../docs/superpowers/specs/2026-08-18-media-panel-design.md`](../docs/superpowers/specs/2026-08-18-media-panel-design.md);
plan: [`../docs/superpowers/plans/2026-08-18-media-panel.md`](../docs/superpowers/plans/2026-08-18-media-panel.md);
karar: [`../docs/decisions/008-media-panel.md`](../docs/decisions/008-media-panel.md).
**Tray aynası (ADR-009):** üst bar `trayMirror` öğesi (katalog **v5**) — gerçek tray
toolbar'ından (TrayNotifyWnd/SysPager/ToolbarWindow32) üçüncü taraf ikonları okur,
sistem ikonlarını filtreler, 1.5 sn poll, tıklama forward. **Windows 11 25H2+ UIA fallback:**
klasik toolbar kaldırıldığı için `TrayUiaReader` (SystemTray.NormalButton) + `TrayCapture`
(PrintWindow ile park+cloak'lı taskbar'dan gerçek ikon yakalama; başarısızsa harf glifi) + UIA
`InvokePattern`/peek tıklama. Spec:
[`../docs/superpowers/specs/2026-08-19-tray-mirror-design.md`](../docs/superpowers/specs/2026-08-19-tray-mirror-design.md);
ADR: [`../docs/decisions/009-tray-mirror-toolbar-enumeration.md`](../docs/decisions/009-tray-mirror-toolbar-enumeration.md).
**Monitör Parlaklık Kontrolü:**
Monitorian tarzı DDC/CI + WMI hibrit servisi (`MonitorBrightnessService`, `MonitorNative`) ve
`BrightnessFlyout` (TopBar parlaklık simgesi `&#xE706;`, master "Tüm Ekranlar" slider'ı, bağımsız monitör kartları, 50ms debounced arka plan kuyruğu).
**Sistem istatistikleri:** üst bar sol kümede Windows logosunun yanında
`CPU x% · GPU y% · RAM z%` (`SystemStatsService`, 2 sn poll; `topBar.showSystemStats`
toggle Ayarlar → Üst Çubuk). GPU PDH `\GPU Engine(*)\Utilization Percentage`
(`PdhAddEnglishCounterW`) — motor örnekleri LUID'e göre fiziksel adaptöre gruplanır;
`topBar.showGpuPerAdapter` açıkken çoklu GPU'da `GPU0 x% · GPU1 y%` (kapalıyken tek
toplam; tek GPU'da fark yok). Sayaç yoksa GPU bölümü gizlenir; ilk örnekte `--`.
Faz 8 spec: [`../docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md`](../docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md).
Faz 8 plan: [`../docs/superpowers/plans/2026-08-10-dock-auto-hide.md`](../docs/superpowers/plans/2026-08-10-dock-auto-hide.md).
Faz 9 spec: [`../docs/superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md`](../docs/superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md).
Faz 9 plan: [`../docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md`](../docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md).
Üst bar özelleştirme (göster/gizle + sıra; sağ tık):
[`../docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md`](../docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md);
plan: [`../docs/superpowers/plans/2026-08-09-topbar-status-items-customization.md`](../docs/superpowers/plans/2026-08-09-topbar-status-items-customization.md).
Durum şeridi sırası + `controlCenter` + tek satır saat:
[`../docs/superpowers/specs/2026-08-09-topbar-status-strip-design.md`](../docs/superpowers/specs/2026-08-09-topbar-status-strip-design.md);
plan: [`../docs/superpowers/plans/2026-08-09-topbar-status-strip.md`](../docs/superpowers/plans/2026-08-09-topbar-status-strip.md).
Üst bar arka plan kişiselleştirme:
[`../docs/superpowers/specs/2026-08-09-topbar-background-personalization-design.md`](../docs/superpowers/specs/2026-08-09-topbar-background-personalization-design.md);
plan: [`../docs/superpowers/plans/2026-08-09-topbar-background-personalization.md`](../docs/superpowers/plans/2026-08-09-topbar-background-personalization.md).

Derleme: `dotnet build WharfBar.sln`. Ayrıntı: [`../docs/01-architecture.md`](../docs/01-architecture.md).

## Uygulama yapmadan önce

[`boundaries.md`](boundaries.md) + ilgili `docs/` dosyasını oku. Cursor kurallarını tek kaynak sanma.
