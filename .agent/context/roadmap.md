# Roadmap

## Şimdi (tamamlanan)

- Doküman + agent iskeleti (Approach B) ✓
- **Faz 1 — İskelet pencere ✓** — `src/WharfBar/` scaffold, `WharfBar.sln`, WPF-UI,
  PerMonitorV2 manifest, `TopBarWindow` (DPI-farkında fiziksel piksel yerleşimi), Mutex
- **Faz 2 — AppBar ✓** — `Native/AppBar.cs`, NEW → QUERYPOS → SETPOS, idempotent `ABM_REMOVE`,
  `ABN_*` callback’leri, `TaskbarCreated` sonrası yeniden kayıt, fullscreen z-order
- **Faz 3 — Görsellik ✓** — WPF-UI Mica + sistem teması, `DWMWCP_DONOTROUND`,
  `WindowAppearance` / `Native/Dwm.cs`, ADR-004
- **Faz 4 — Dock ✓** — `DockWindow` overlay (Topmost, AppBar yok), pinler,
  `ShellIcon` / `AppIconService`, `ProcessLauncherService`, `FullscreenState`,
  `DockAutoHideController` / `DockHitTest` stub, Mica + yuvarlak köşeler
- **Faz 5 — Config ✓** — `AppConfig` / `ConfigService`, `%AppData%/WharfBar/config.json`
- **Faz 6 — Autostart ✓** — Startup klasörü `WharfBar.lnk` (`AppConfig.autostart`)
- **Faz 7 — Saat / durum ✓** — `StatusService` (saat, ağ, pil); `TrayIconService` + Quit
- Derleme temiz; work area betikle doğrulandı (aşağı bkz.)
- **Tasarım onay ✓** — canlı dock + tepsi hibrit + görev çubuğu gizle/restore (ADR-005);
  spec: [`../../docs/superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md`](../../docs/superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md)
- **Faz 9 — Shell yerine geçme (ADR-005) ✓ (kod)** — plan:
  [`../../docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md`](../../docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md)
  - **9a** `TaskbarVisibilityService` — capture (`ABM_GETSTATE` + `Shell_TrayWnd` görünürlük) → hide → restore
  - **9b** Canlı pin (`TaskbarPinnedAppsSource`) + running (`RunningAppsSource` / `EnumWindows` + AUMID); `DockAppsComposer`
  - **9c** Üst bar overflow düğmesi (`TrayOverflowInvoker` / `Win+B`)
  - Kod çalışma ağacında; kullanıcı commit + 9a/9b/9c manuel UI doğrulaması bekliyor
- **Faz 8 — Dock auto-hide / hit-test polish ✓ (kod)** — spec:
  [`../../docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md`](../../docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md);
  plan: [`../../docs/superpowers/plans/2026-08-10-dock-auto-hide.md`](../../docs/superpowers/plans/2026-08-10-dock-auto-hide.md)
  - `DockAutoHideController` — kenar bandı poll, show/hide animasyon, fullscreen bridge
  - `DockHitTest` — `ApplyInteractive` / `ApplyPassthrough` (`WS_EX_TRANSPARENT`)
  - Kod çalışma ağacında; manuel doğrulama bekliyor
- **Monitör Parlaklık Kontrolü (Monitorian Entegrasyonu) ✓** — `MonitorBrightnessService`, `MonitorNative` (DDC/CI `dxva2.dll` + WMI `WmiMonitorBrightness` hibrit), `BrightnessFlyout` (TopBar `brightness` simgesi, master "Tüm Ekranlar" slider'ı, bağımsız monitör kartları, 50ms debounced arka plan kuyruğu, katalog v2).
- **Dock Sağ Tık Bağlam Menüsü & Düzen Polish ✓** — `DockWindow` sağ tık bağlam menüsü (Uygulama İkonu/Adı başlık kartı, Dock'a Sabitle/Kaldır dinamik kısayol senkronizasyonu, Görevi Sonlandır `Process.Kill`, Pencereyi Kapat `WM_CLOSE`, Yönetici Çalıştır, Dosya Konumunu Aç, Yeni Pencere), %100 ekran genişliği desteği ve `ScrollViewer` + `PreviewMouseWheel` yatay kaydırma entegrasyonu.
- **Renk kaplaması (veil) ✓ (kod; manuel doğrulama bekliyor)** — tam ekran katmanlı kaplama (`VeilWindow`, click-through + 280 ms fade), `VeilService` (durum + fade + `RegisterHotKey` host + config kalıcılığı), QS `veil` karosu + global kısayol (varsayılan Ctrl+F11, config'te değiştirilebilir), Kaplama ayarlar sayfası (`VeilPage`; renk/opaklık/kısayol). Karar: [ADR-007](../../docs/decisions/007-veil-overlay.md); spec: [`../../docs/superpowers/specs/2026-08-18-veil-overlay-design.md`](../../docs/superpowers/specs/2026-08-18-veil-overlay-design.md); plan: [`../../docs/superpowers/plans/2026-08-18-veil-overlay.md`](../../docs/superpowers/plans/2026-08-18-veil-overlay.md). Manuel doğrulama listesi spec'in "Doğrulama (manuel)" bölümünde.
- **Medya paneli (SMTC) ✓ (kod; manuel doğrulama bekliyor)** — üst bar `media` öğesi (yalnızca oturum varken görünür; `brightness` sonrası, katalog **v4** migration) + medya flyout'u (kapak, başlık/sanatçı, Oynat/Duraklat + İleri/Geri, çoklu oturumda chevron seçici). `MediaSessionService` (Windows SMTC izleme: snapshot + komutlar + `Changed`/`SessionsChanged` + 10 sn retry), `MediaSessionSelector` (en son aktif politikası + `MoveCurrent` halkası), `MediaSnapshot` modeli. Karar: [ADR-008](../../docs/decisions/008-media-panel.md); spec: [`../../docs/superpowers/specs/2026-08-18-media-panel-design.md`](../../docs/superpowers/specs/2026-08-18-media-panel-design.md); plan: [`../../docs/superpowers/plans/2026-08-18-media-panel.md`](../../docs/superpowers/plans/2026-08-18-media-panel.md). Manuel doğrulama listesi planın "Manuel doğrulama" bölümünde.
- **Tray aynası (ADR-009) ✓** — üst bar `trayMirror` (belgelenmiş comctl32 `TB_*` mesajlarıyla
  salt-okunur ayna + `SendMessage` click-forward; sistem ikonu filtresi; 1.5 sn poll; gizli
  ikonlar `Win+B`'de kalır). **25H2+ UIA fallback ✓ (manuel doğrulandı):** klasik toolbar
  kaldırıldığı için `TrayUiaReader` (`SystemTray.NormalButton` filtre) + harf glifi + UIA
  `InvokePattern`/peek tıklama. Spec: [`../../docs/superpowers/specs/2026-08-19-tray-mirror-design.md`](../../docs/superpowers/specs/2026-08-19-tray-mirror-design.md); plan: [`../../docs/superpowers/plans/2026-08-19-tray-mirror.md`](../../docs/superpowers/plans/2026-08-19-tray-mirror.md); karar: [`../../docs/decisions/009-tray-mirror-toolbar-enumeration.md`](../../docs/decisions/009-tray-mirror-toolbar-enumeration.md)

## Yapılacaklar listesi (öncelikli)

### P0 — Sıradaki uygulama

1. **Faz 8 + Faz 9 manuel doğrulama** — commit sonrası UI checklist
   - Faz 8: [`../../docs/03-development-order.md`](../../docs/03-development-order.md) Faz 8 (kenar hover, click-through, fullscreen)
   - Faz 9: aynı dosya Faz 9; plan uçtan uca bölümü (9a/9b/9c)

### P1 — Manuel doğrulama (insan gözü; kod yazmadan)

3. Bar / dock görsel görünüm, Mica dokusu, hiza
4. Saat / ağ / pil şeridi + tepsi **Quit WharfBar**
5. Sistem light/dark runtime geçişi
6. DPI / ölçek değişimi ve monitör tak-çıkar
7. Tam ekran uygulama ile `ABN_FULLSCREENAPP` z-order
8. Bilinçli: `explorer.exe` restart → `TaskbarCreated`; zorla kill → asılı work area / görev çubuğu farkındalığı

### P2 — v1 sonrası adaylar

8b. **Üst bar durum öğesi özelleştirme** (göster/gizle + sıra; yerleşik katalog) —
    QS + mixer stabilize olduktan sonra. Tasarım onaylı:
    [`../../docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md`](../../docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md);
    plan:
    [`../../docs/superpowers/plans/2026-08-09-topbar-status-items-customization.md`](../../docs/superpowers/plans/2026-08-09-topbar-status-items-customization.md)
    (kod landed; manuel UI checklist bekliyor; ayrı Control Center yok; tepsi aynası ADR-009'da; sağ tık menü)
8c. **Üst bar durum şeridi sırası + `controlCenter` + tek satır saat** ✓ (kod) —
    [`../../docs/superpowers/specs/2026-08-09-topbar-status-strip-design.md`](../../docs/superpowers/specs/2026-08-09-topbar-status-strip-design.md);
    plan: [`../../docs/superpowers/plans/2026-08-09-topbar-status-strip.md`](../../docs/superpowers/plans/2026-08-09-topbar-status-strip.md)
    (volume→Mixer, CC→Main; `statusCatalogVersion` soft migrate; manuel UI checklist)
9. Hızlı ayarlar cila ✓ (kod) — canlı Wi-Fi listesi + bağlanma (`WifiService` / wlanapi.dll;
   QS Wifi sayfası + şifre paneli), BT cihaz listesi (`BluetoothService`), gerçek Gece ışığı
   durum + toggle (`NightLightService` / CloudStore best-effort + oturum override), native
   yansıtma paneli 4 mod (`ProjectionService` / `SetDisplayConfig`), tile animasyonları
   (in-place pop + renk geçişi). Kalan: Enerji tasarrufu toggle; oturum ikonları. Spec:
   [`../../docs/superpowers/specs/2026-08-16-quick-settings-live-network-nightlight-project-design.md`](../../docs/superpowers/specs/2026-08-16-quick-settings-live-network-nightlight-project-design.md);
   plan: [`../../docs/superpowers/plans/2026-08-16-quick-settings-live-network-nightlight-project.md`](../../docs/superpowers/plans/2026-08-16-quick-settings-live-network-nightlight-project.md)
   (kod çalışma ağacında; manuel UI doğrulaması bekliyor)
10. İkincil monitör / work-area stratejisi
11. UWP/Store ikonları (tam yol)
12. İsteğe bağlı “dock reserves space” (alt AppBar — ayrı ADR)
13. Bildirim merkezi benzeri içerik aynası (ADR-005 hâlâ reddediyor; ayrı onay gerekir)

### Bilinçli ertelenenler / yasaklar

- Yabancı / sistem tepsi ikonlarını `ReadProcessMemory` / enjeksiyon / undocumented API ile
  okuma, çizme, click-forward (ADR-009 ile yalnızca belgelenmiş `TB_*` mesaj aynası serbest;
  gizli/overflow ikonlar hâlâ yasak — `Win+B` kullanılır)
- `ReadProcessMemory` / belgelenmemiş NotifyIcon enumeration
- Görev çubuğunu **kalıcı** bozmak (restore’suz)
- Registry `Run` autostart (Startup klasörü tercih)
- Global uygulama menüsü

## Faz 1–3 doğrulama durumu

Ölçüm ortamı: 1920×1080, **%100 ölçek (96 DPI)**, görev çubuğu altta (48 px).

### Makineyle doğrulandı ✓

`SystemParametersInfo(SPI_GETWORKAREA)` ile ölçülerek, betikle:

| Kontrol | Sonuç |
|---------|-------|
| Work area rezervasyonu | Baseline `T=0` → çalışırken `T=30`; tam olarak beklenen 30 px |
| Bar pencere rect’i | `L=0 T=0 R=1920 B=30` — rezerve şeritle birebir |
| Tek örnek (Mutex) | İkinci süreç kendiliğinden çıktı (kod 0); work area değişmedi |
| Zarif kapanış | `WM_CLOSE` → süreç kod 0 |
| **Work area geri yükleme** | Baseline ile **birebir aynı** |
| Maximize edilmiş pencere | Bar altında kalıyor (Faz 2’de doğrulandı) |

### Faz 3 görsel notları

- Mica / translucency ekran görüntüsünde güvenilir doğrulanamaz (DWM composition + wallpaper);
  attribute okuması + opak-olmayan client ile desteklenir, insan gözü gerekir.
- Light/dark runtime geçişi manuel tema değiştirerek doğrulanmalı.

Ayrıntılı checklist: [`../../docs/03-development-order.md`](../../docs/03-development-order.md)
