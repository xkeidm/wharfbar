# 03 — Geliştirme Sırası

Önerilen fazlar. Her faz sonunda manuel doğrulama; AppBar kullanan fazlarda çökme sonrası `ABM_REMOVE` / explorer düzeltmesi bilinciyle çalış.

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

## Faz 5 — Config ve kalıcılık

- `AppConfig` şeması + `ConfigService`
- `%AppData%/WharfBar/config.json`
- Pinned apps + tema + dock ayarları

## Faz 6 — Autostart ve dayanıklılık

- Startup klasörü kısayolu (Registry Run değil)
- Explorer / shell restart sonrası AppBar yeniden kayıt
- Fullscreen hide polish

## Faz 7 — Durum göstergeleri / saat

- Saat
- Ağ / pil vb. .NET API simülasyonu + özel ikonlar
- Kendi NotifyIcon (opsiyonel, uygulama menüsü)

> Tepsi/durum görsel olarak en az kritik; sona bırakılabilir.
