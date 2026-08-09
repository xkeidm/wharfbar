# 01 — Mimari

> Faz 1–7 uygulandı. Aşağıda **mevcut** yapı ile **hedef** yapı ayrı ayrı işaretlidir.

## Stack

- .NET 8 WPF, `UseWPF`, hedef TFM: `net8.0-windows10.0.19041.0`
- WPF-UI paketi (Fluent kontroller, backdrop tercih sırası: WPF-UI önce)
- `app.manifest` → Per-Monitor V2 DPI
- `AllowUnsafeBlocks`: **gerekli değil** temel P/Invoke için; yalnızca gerçekten `unsafe` kod yazılırsa aç

## Neden WPF (Flutter / Electron değil)

Kaynak tüketimi önceliği baskın: Flutter/Electron kendi render motorunu (Skia/Chromium) pakete
gömür; basit bir dock/bar için gereksiz RAM ve başlangıç maliyeti. WPF, DirectX üzerinden
Windows compositor’ını kullanır; tipik hafif dock/bar ~30–60 MB RAM bandında kalmayı hedefler.

Tam native (C++/Win32 + Direct2D) daha da hafif olurdu ama Fluent (Mica, tema) için hazır tasarım
sistemi olmazdı — kişisel proje için WPF + WPF-UI dengeyi kuruyor.

> Erken proje notları her iki pencereyi de AppBar sanıyordu. Güncel karar: top bar AppBar,
> dock overlay (ADR-001). Klasör adı `/Windows` değil **`Views/`**.

## Bilinen mimari sınır — global menü yok

Windows’ta macOS’taki gibi sistem genelinde uygulama menüsü yoktur. Top bar’da File/Edit/View
benzeri uygulamaya özel menüler **olmayacak** — yalnızca saat, durum göstergeleri ve kontrol
alanı. Bu bilinçli bir kapsam sınırı, eksik özellik değil.

## Mevcut klasör yapısı (Faz 1–7)

`/Windows` klasör adı Win32 ile karışmasın diye **Views** kullanılır.

```
WharfBar.sln                    # kökte, tek proje
.gitignore
src/WharfBar/
  Views/
    TopBarWindow.xaml(.cs)      # üst bar; AppBar sahibi; WndProc hook; tema mesajları
    DockWindow.xaml(.cs)        # alt overlay; config pin strip; fullscreen / work-area dinleyicisi
  Native/
    NativeMethods.cs            # DllImport’lar, RECT / APPBARDATA / MONITORINFO / MARGINS
    AppBar.cs                   # SHAppBarMessage wrapper; idempotent ABM_REMOVE
    Display.cs                  # birincil monitör sınırları + DPI → fiziksel piksel
    Dwm.cs                      # köşe, immersive dark, backdrop yedek, frame extend
    ShellIcon.cs                # SHGetFileInfo ikon çıkarma / Destroy
    DockHitTest.cs              # hit-test stub (Faz 4: tam etkileşimli; passthrough ertelendi)
    PowerStatus.cs              # GetSystemPowerStatus sarmalayıcı
    NotifyIconHost.cs           # Shell_NotifyIcon ADD/DELETE (kendi tepsi ikonu)
  Models/
    PinnedApp.cs                # pin kaydı (path, args, label)
    DemoPinnedApps.cs           # varsayılan 4 pin (config seed)
    AppConfig.cs                # JSON kök şema + DockConfig
  Services/
    SingleInstanceGuard.cs      # named Mutex
    WindowAppearance.cs         # WPF-UI tema + backdrop; DWM ince ayar
    AppThemePreference.cs       # config theme → ApplicationTheme
    ConfigService.cs            # %AppData%/WharfBar/config.json oku/yaz
    AppIconService.cs           # ShellIcon → WPF ImageSource
    ProcessLauncherService.cs   # Process.Start launcher
    FullscreenState.cs          # süreç içi fullscreen sinyali (TopBar → Dock)
    WorkAreaChanged.cs          # AppBar yerleşim sonrası dock yeniden konumlanma olayı
    DockAutoHideController.cs   # auto-hide stub (Enabled config’ten; davranış sonraki faz)
    AutostartService.cs         # Startup klasörü .lnk; AppConfig.autostart ile senkron
    StatusService.cs            # Saat + ağ/pil (.NET / GetSystemPowerStatus)
    TrayIconService.cs          # Kendi Shell_NotifyIcon + Quit menüsü
  App.xaml(.cs)                 # StartupUri yok; config + autostart + tray → TopBar + Dock
  app.manifest                  # PerMonitorV2 + longPathAware + asInvoker
  WharfBar.csproj
```

`dotnet new wpf` şablonundan gelen `MainWindow.xaml(.cs)` silindi; `StartupUri` kaldırılıp
başlatma `App.OnStartup` içine alındı.

## Sonraki fazlarda eklenecek

```
src/WharfBar/
  Services/
    TaskbarVisibilityService.cs   # Faz 9a — capture / hide / restore
    TaskbarPinnedAppsSource.cs    # Faz 9b — User Pinned\TaskBar .lnk
    RunningAppsSource.cs          # Faz 9b — EnumWindows + AUMID
    (dock auto-hide / hit-test polish — Faz 8, tercihen 9a/9b sonrası)
  Views/
    TopBarWindow — tray overflow düğmesi (Faz 9c)
```

Ayrıntı: [ADR-005](decisions/005-shell-replace-live-dock-tray-hybrid.md),
[spec](superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md).
## Sorumluluklar

| Bileşen | Görev |
|---------|--------|
| `TopBarWindow` | Üst kenar UI; AppBar sahibi; `WndProc` hook; DPI / display / tema; saat/ağ/pil şeridi |
| `AppBar` | `ABM_NEW` / `QUERYPOS` / `SETPOS` / `REMOVE`; `ABN_*` çözümleme; yeniden kayıt |
| `NativeMethods` | Ham `DllImport` + struct + sabitler; `internal`, UI’dan doğrudan çağrılmaz |
| `Display` | Birincil monitör sınırları, DPI, DIP → piksel dönüşümü |
| `Dwm` | Köşe tercihi, immersive dark, backdrop yedek, frame extend |
| `WindowAppearance` | WPF-UI tema + Mica; DWM ince ayar; tema yenileme |
| `SingleInstanceGuard` | Named Mutex; ikinci örneği engeller |
| `DockWindow` | Alt overlay; config pin strip; hover ölçek; Mica + yuvarlak köşe; AppBar yok |
| `ShellIcon` | `SHGetFileInfo` ile dosya ikonu çıkarma / `DestroyIcon` |
| `AppIconService` | `ShellIcon` → WPF `ImageSource` dönüşümü |
| `ProcessLauncherService` | Pin tıklamasında `Process.Start` |
| `FullscreenState` | `TopBarWindow` `ABN_FULLSCREENAPP` → dock gizleme/gösterme |
| `WorkAreaChanged` | Top bar AppBar yerleşiminden sonra dock’u yeniden konumlandırma |
| `DockAutoHideController` | Auto-hide stub (`Enabled` config’ten; gerçek davranış sonraki faz) |
| `DockHitTest` | Hit-test stub; Faz 4 tam etkileşimli, passthrough ertelendi |
| `DemoPinnedApps` | Varsayılan 4 pin — ilk `config.json` seed |
| `AppConfig` / `DockConfig` | JSON şema (`schemaVersion` 1) |
| `ConfigService` | JSON oku/yaz; bozuk/uyumsuz şemada `.bak` + varsayılan |
| `AppThemePreference` | `theme` alanı → `ApplicationTheme` |
| `AutostartService` | Startup klasörü `WharfBar.lnk`; `autostart` true/false ile oluştur/sil |
| `StatusService` | Saat + ağ/pil; `.NET NetworkInformation` + `GetSystemPowerStatus` |
| `TrayIconService` / `NotifyIconHost` | Kendi tepsi ikonu + Quit menüsü (`Shell_NotifyIcon`) |
| `PowerStatus` | Pil / AC satırı (`GetSystemPowerStatus`) |

## Pencere modeli

```
┌─────────────────────────────────────┐
│ TopBarWindow (AppBar, ABE_TOP)      │  ← work area rezerve
├─────────────────────────────────────┤
│                                     │
│           Masaüstü / apps           │
│                                     │
├─────────────────────────────────────┤
│ DockWindow (Topmost overlay)        │  ← genelde AppBar yok
└─────────────────────────────────────┘
```

Karar gerekçesi: [`decisions/001-dock-appbar-vs-overlay.md`](decisions/001-dock-appbar-vs-overlay.md)

## Yaşam döngüsü

Uygulanan (Faz 1–7):

1. `App.OnStartup` → `SingleInstanceGuard.TryAcquire()`; mutex alınamazsa `Shutdown()`
2. `ConfigService.LoadOrCreate()` → `%AppData%/WharfBar/config.json`; `AppThemePreference` uygulanır
3. `AutostartService.Apply(config.Autostart)` — Startup klasörü kısayolunu senkronlar
   (`Save` da aynı senkronu yapar)
4. `TopBarWindow.Show()` — AppBar + `StatusService` (saat / ağ / pil şeridi)
5. `DockWindow.Show(config)` — overlay; pinler config’ten; AppBar kaydı yok
6. `TrayIconService` — kendi tepsi ikonu + Quit menüsü
7. `OnSourceInitialized` (TopBar) → `HwndSource.AddHook`; `RegisterWindowMessage("TaskbarCreated")`;
   `AppBar` oluştur (`ABE_TOP`, callback mesajı `WharfBarAppBarMessage`); durum şeridi bağlanır
8. `OnSourceInitialized` (Dock) → hook; `WindowAppearance`; `DockHitTest.ApplyInteractive`;
   `FullscreenState` / `WorkAreaChanged` abonelikleri; pin görselleri; yerleşim
9. Çıkış yolları bağlanır → `ABM_NEW` → yerleşim uygulanır → `WorkAreaChanged.Notify`
10. Çıkış: tray dispose → dock kapat → `ABM_REMOVE` (idempotent, aşağıdaki tüm yollardan)

Sonraki faz: Faz 9 (görev çubuğu gizle/restore + canlı dock + tepsi hibrit; ADR-005);
Faz 8 dock auto-hide polish tercihen 9a/9b sonrası.

### Yerleşim hesabı (fiziksel piksel)

WPF’in DIP tabanlı `Left/Top/Width/Height` özellikleri **kullanılmaz** — bunlar birincil
monitörün ölçeğini varsayar. Bunun yerine:

1. `GetDpiForWindow(hwnd)` → o anki monitör DPI’ı (0 dönerse 96)
2. `dip * dpi / 96` → bar yüksekliği piksel
3. `MonitorFromWindow(MONITOR_DEFAULTTOPRIMARY)` + `GetMonitorInfo` → birincil monitör sınırları
4. `ABM_QUERYPOS` → shell’in onayladığı kenar; karşı kenar yüksekliği koruyacak şekilde geri yazılır
5. `ABM_SETPOS` → `SetWindowPos` ile onaylanan rect’e taşı

### `ABM_REMOVE` garantisi

`AppBar.Remove()` `Interlocked.Exchange` ile korunur: yalnızca ilk çağrı shell’e ulaşır, bu yüzden
birden çok yola bağlamak güvenlidir. Bağlı yollar:

| Yol | Kapsadığı senaryo |
|-----|-------------------|
| `Window.OnClosing` | Normal kapatma |
| `Application.Exit` | `Shutdown()` ile çıkış |
| `Application.SessionEnding` | Oturum kapatma / yeniden başlatma |
| `AppDomain.ProcessExit` | Pencere olmadan süreç sonu |
| `DispatcherUnhandledException` | UI thread’de yakalanmamış hata |
| `AppDomain.UnhandledException` | Diğer thread’lerde yakalanmamış hata |

`Remove()` sonrası `Register()` reddedilir; kapanış sırasında gelen geç bir `TaskbarCreated`
mesajının AppBar’ı yeniden kaydetmesi böyle engellenir.

### Mesaj işleme (`TopBarWindow.WndProc`)

| Mesaj | Davranış |
|-------|----------|
| `ABN_POSCHANGED`, `ABN_STATECHANGE` | Yerleşimi yeniden uygula |
| `ABN_FULLSCREENAPP` | Tam ekran uygulama varken `HWND_BOTTOM`, bitince `HWND_TOPMOST`; `FullscreenState.Instance.Set` |
| `ABN_WINDOWARRANGE` | v1’de işlem yok |
| `TaskbarCreated` | `ABM_NEW` tekrar (explorer yeniden başladı) + yerleşim |
| `WM_DPICHANGED`, `WM_DISPLAYCHANGE` | `Dispatcher.BeginInvoke` ile yerleşimi yeniden uygula |
| `WM_WINDOWPOSCHANGED` | `ABM_WINDOWPOSCHANGED` (kendi taşımamız sırasında atlanır) |
| `WM_ACTIVATE` | `ABM_ACTIVATE` |

## Config şeması (`schemaVersion` 1)

Konum: `%AppData%/WharfBar/config.json`  
Uygulama: `Models/AppConfig.cs` + `Services/ConfigService.cs` (camelCase JSON, girintili).

```json
{
  "schemaVersion": 1,
  "theme": "system",
  "autostart": true,
  "dock": {
    "autoHide": true,
    "iconSize": 48
  },
  "pinnedApps": [
    { "path": "C:\\\\...\\\\app.exe", "args": "", "label": "App" }
  ]
}
```

| Alan | Anlam |
|------|--------|
| `schemaVersion` | Zorunlu; yalnızca `1` kabul. Diğer değer → `.bak` + varsayılan |
| `theme` | `system` \| `light` \| `dark` (`AppThemePreference`) |
| `autostart` | `true` → Startup’ta `WharfBar.lnk`; `false` → kısayol silinir (`AutostartService`) |
| `dock.autoHide` | Tercih saklanır; gerçek gizleme sonraki faz |
| `dock.iconSize` | DIP; 24–72 aralığına sıkıştırılır (varsayılan 48) |
| `pinnedApps` | Seed / fallback pin listesi; canlı TaskBar `.lnk` tarama başarılıysa baskın değildir (ADR-005) |

İlk çalıştırmada dosya yoksa `DemoPinnedApps` ile varsayılan yazılır. Kayıt atomik
(temp + replace). Bozuk JSON → `config.json.bak` + varsayılan.
