# 03 — Geliştirme Sırası

Önerilen fazlar. Her faz sonunda manuel doğrulama; AppBar kullanan fazlarda çökme sonrası `ABM_REMOVE` / explorer düzeltmesi bilinciyle çalış.

> Faz 1–7 tamamlandı. **Faz 8 kodu çalışma ağacında** (manuel doğrulama bekliyor).
> **Faz 9 kodu çalışma ağacında** (commit + 9a/9b/9c manuel doğrulama bekliyor).
> Faz 8 spec: [`superpowers/specs/2026-08-10-dock-auto-hide-design.md`](superpowers/specs/2026-08-10-dock-auto-hide-design.md).
> Faz 8 plan: [`superpowers/plans/2026-08-10-dock-auto-hide.md`](superpowers/plans/2026-08-10-dock-auto-hide.md).
> Faz 9 spec: [`superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md`](superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md).
> Faz 9 plan: [`superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md`](superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md).

## Faz 0 — Repo / docs ✓

- [x] `AGENTS.md`, `.agent/`, `docs/`, ince Cursor kuralları
- [x] WPF proje scaffold’u (`src/WharfBar/` + kök `WharfBar.sln`)

## Faz 1 — İskelet pencere ✓

- [x] `dotnet new wpf` + WPF-UI + manifest (Per-Monitor V2)
- [x] `TopBarWindow`: `WindowStyle=None`, `Topmost=true`, `ShowInTaskbar=false`
- [x] Sabit yükseklik (30 DIP), sade renk; **Mica yok** — yalnızca pozisyon ve boyut
- [x] Yerleşim fiziksel piksel + `GetDpiForWindow` ile; WPF DIP `Left/Top` kullanılmaz
- [x] Single-instance Mutex
- [x] `DockWindow` — Faz 4 ile geldi (overlay + demo pinler)

## Faz 2 — Top bar AppBar ✓

- [x] `SHAppBarMessage` akışı: NEW → QUERYPOS → SETPOS
- [x] `ABM_REMOVE`: `OnClosing`, `Application.Exit`, `SessionEnding`, `ProcessExit`,
      yakalanmamış hata yolları — idempotent (`Interlocked`)
- [x] Callback message (`RegisterWindowMessage`) + `ABN_POSCHANGED` / `ABN_STATECHANGE` /
      `ABN_FULLSCREENAPP` işleme
- [x] `TaskbarCreated` mesajı ile explorer restart sonrası yeniden kayıt
- [x] `WM_DPICHANGED` / `WM_DISPLAYCHANGE` ile yeniden konumlandırma
- [x] **Doğrulandı (betikle, 1920×1080 @ %100):** work area `T=0` → `T=30`, çıkışta baseline’a
      birebir dönüyor, maximize pencere bar altında kalıyor, ikinci örnek kendiliğinden çıkıyor.
      Ayrıntı ve kalan insan-gözü maddeleri: [`../.agent/context/roadmap.md`](../.agent/context/roadmap.md)

## Faz 3 — Görsellik ✓

- [x] WPF-UI tema sözlükleri + `ApplicationThemeManager` (sistem light/dark)
- [x] WPF-UI `WindowBackdropType.Mica` önce; ham DWM yalnızca yedek / doğrulama
- [x] `WindowStyle=None` + `DwmExtendFrameIntoClientArea` (30 DIP; caption min-height yok)
- [x] Köşe: `DWMWCP_DONOTROUND` — [ADR-004](decisions/004-topbar-mica-corners-and-theme.md)
- [x] Runtime tema: `WM_SETTINGCHANGE` / `ImmersiveColorSet` (+ tema/renk mesajları)
- [x] `AllowsTransparency=true` yok; backdrop yoksa opak fallback
- [x] Derleme temiz; work area betikle doğrulandı (kapanışta baseline)

## Faz 4 — Dock ✓

- [x] Overlay: Topmost, AppBar yok (ADR-001); work-area alt kenarı, ortalanmış
- [x] Demo pinned app listesi UI (`DemoPinnedApps.All` — 4 uygulama; kalıcı config Faz 5)
- [x] Hover büyüme: `ScaleTransform` + `Storyboard` (1.2×, 180 ms)
- [x] Tıklanınca `ProcessLauncherService` / `ShellIcon` + `AppIconService`
- [x] `FullscreenState` köprüsü (`TopBarWindow` → dock gizleme)
- [x] `WorkAreaChanged.Notify` ile AppBar yerleşim sonrası dock yeniden konumlanma
- [x] `DockAutoHideController` stub (her zaman görünür; gerçek auto-hide sonraki faz)
- [x] `DockHitTest` stub (tam etkileşimli; passthrough ertelendi)
- [x] Mica + `DWMWCP_ROUND` köşeler; DPI / display / tema yenileme

## Faz 5 — Config ve kalıcılık ✓

- [x] `AppConfig` şeması + `ConfigService` (`System.Text.Json`, camelCase)
- [x] `%AppData%/WharfBar/config.json` — ilk çalıştırmada demo pinlerle oluşturulur
- [x] Pinned apps dock’a config’ten yüklenir; `theme` (`system`/`light`/`dark`); dock `iconSize` / `autoHide` (auto-hide davranışı hâlâ stub)
- [x] Bozuk / desteklenmeyen `schemaVersion` → `.bak` yedek + varsayılanlara dön

## Faz 6 — Autostart ve dayanıklılık ✓

- [x] Startup klasörü kısayolu (`AutostartService` → `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\WharfBar.lnk`)
- [x] `AppConfig.autostart` ile senkron: yükleme (`App.OnStartup`) ve her `ConfigService.Save`
- [x] Explorer / shell restart sonrası AppBar yeniden kayıt — Faz 2’de (`TaskbarCreated`)
- [x] Fullscreen gizleme köprüsü — Faz 2/4 (`ABN_FULLSCREENAPP` + `FullscreenState`); hit-test polish sonraki faz

## Faz 7 — Durum göstergeleri / saat ✓

- [x] Canlı saat (`StatusService` + `TopBarWindow` sağ şerit)
- [x] Ağ + pil: .NET `NetworkInformation` + `GetSystemPowerStatus`; WPF-UI Fluent ikonları
- [x] Kendi tepsi ikonu (`Shell_NotifyIcon` / `TrayIconService`) — yalnızca Quit menüsü
- [x] Ses / volume göstergesi + hızlı ayarlar flyout (ADR-006; Core Audio + karıştırıcı + ağ)

> Tepsi ikonu sistem Wi-Fi/pil ikonlarını **okumaz**; yalnızca WharfBar’ın kendi çıkış menüsüdür.

## Faz 8 — Dock auto-hide / hit-test polish ✓ (kod)

> Tercihen **Faz 9a/9b sonrası** uygulandı: görev çubuğu gizlenince dock alt kenar geometrisi değişir.
> Spec: [`superpowers/specs/2026-08-10-dock-auto-hide-design.md`](superpowers/specs/2026-08-10-dock-auto-hide-design.md).
> Plan: [`superpowers/plans/2026-08-10-dock-auto-hide.md`](superpowers/plans/2026-08-10-dock-auto-hide.md).
> Kod çalışma ağacında; aşağıdaki manuel doğrulama bekliyor.

`dock.autoHide` davranışı `DockAutoHideController` + `DockHitTest` ile uygulandı:

- [x] Fare kenar bandına yaklaşınca dock gösterme; uzaklaşınca gizleme (animasyonlu)
- [x] Gizli / daraltılmış halde `WS_EX_TRANSPARENT` ile tıklamayı alttaki pencereye geçirme (`DockHitTest.ApplyPassthrough`)
- [x] Görünür halde hit-test’i tam etkileşimli tutma (`DockHitTest.ApplyInteractive`)
- [x] Fullscreen (`FullscreenState`) ile auto-hide çakışmasını netleştirme
- [x] `autoHide: false` iken dock her zaman görünür
- [ ] Manuel doğrulama: maximize pencere, oyun/video fullscreen, kenar hover

## Faz 9 — Shell yerine geçme (canlı dock + tepsi hibrit + görev çubuğu) ✓ (kod)

ADR-005 / [spec](superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md).
Uygulama planı: [`superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md`](superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md).
Kod çalışma ağacında; kullanıcı commit + aşağıdaki manuel maddeler bekliyor. Alt fazlar:

### 9a — Görev çubuğu yaşam döngüsü

- [x] Başlatmada görev çubuğu durumunu yakala (`ABM_GETSTATE` + `Shell_TrayWnd` görünürlük); gizle
- [x] Çıkış + crash yollarında önceki duruma geri yükle (restore → `ABM_REMOVE`; idempotent)
- [x] `TaskbarCreated` sonrası gizlemeyi yeniden uygula (`ReapplyHide`)
- [ ] Manuel: Quit → restore; zorla kill gotcha bilinci

### 9b — Canlı dock

- [x] `User Pinned\TaskBar` `.lnk` tarama; pin sırası (`TaskbarPinnedAppsSource`)
- [x] `EnumWindows` + AppUserModelID running gruplama (`RunningAppsSource`)
- [x] `config.pinnedApps` yalnızca seed/fallback (`DockAppsComposer`)
- [x] Pin + running birleşik UI; UWP için placeholder (kırık ikon yolu yok)
- [ ] Manuel: pin ekle/çıkar, çalışan gösterge, launch / focus

### 9c — Tepsi hibrit düğmesi

- [x] Üst barda “tray / gizli ikonlar” düğmesi → native overflow (`TrayOverflowInvoker` / `Win+B`)
- [x] Yabancı ikon çizimi / RPM / undocumented enumeration yok
- [x] İsteğe bağlı: ses / volume göstergesi + hızlı ayarlar (ADR-006; 9c sonrası genişletildi)
- [ ] Manuel: düğme overflow açar; WharfBar’da üçüncü taraf ikon yok

## Faz 10+ (v1 sonrası adaylar — sıra sabit değil)

- [ ] İkincil monitör / work-area stratejisi
- [ ] UWP/Store ikonları (tam yol)
- [ ] İsteğe bağlı “dock reserves space” (alt AppBar — ayrı ADR)
- [ ] Daha zengin durum menüleri / bildirim merkezi benzeri UI

