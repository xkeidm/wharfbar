# 01 — Mimari

> Faz 1–4 uygulandı. Aşağıda **mevcut** yapı ile **hedef** yapı ayrı ayrı işaretlidir.

## Stack

- .NET 8 WPF, `UseWPF`, hedef TFM: `net8.0-windows10.0.19041.0`
- WPF-UI paketi (Fluent kontroller, backdrop tercih sırası: WPF-UI önce)
- `app.manifest` → Per-Monitor V2 DPI
- `AllowUnsafeBlocks`: **gerekli değil** temel P/Invoke için; yalnızca gerçekten `unsafe` kod yazılırsa aç

## Mevcut klasör yapısı (Faz 1–4)

`/Windows` klasör adı Win32 ile karışmasın diye **Views** kullanılır.

```
WharfBar.sln                    # kökte, tek proje
.gitignore
src/WharfBar/
  Views/
    TopBarWindow.xaml(.cs)      # üst bar; AppBar sahibi; WndProc hook; tema mesajları
    DockWindow.xaml(.cs)        # alt overlay; demo pin strip; fullscreen / work-area dinleyicisi
  Native/
    NativeMethods.cs            # DllImport’lar, RECT / APPBARDATA / MONITORINFO / MARGINS
    AppBar.cs                   # SHAppBarMessage wrapper; idempotent ABM_REMOVE
    Display.cs                  # birincil monitör sınırları + DPI → fiziksel piksel
    Dwm.cs                      # köşe, immersive dark, backdrop yedek, frame extend
    ShellIcon.cs                # SHGetFileInfo ikon çıkarma / Destroy
    DockHitTest.cs              # hit-test stub (Faz 4: tam etkileşimli; passthrough ertelendi)
  Models/
    PinnedApp.cs                # pin kaydı (path, args, label)
    DemoPinnedApps.cs           # sabit 4 demo uygulama (Explorer, Notepad, Calc, cmd)
  Services/
    SingleInstanceGuard.cs      # named Mutex
    WindowAppearance.cs         # WPF-UI tema + backdrop; DWM ince ayar
    AppIconService.cs           # ShellIcon → WPF ImageSource
    ProcessLauncherService.cs   # Process.Start launcher
    FullscreenState.cs          # süreç içi fullscreen sinyali (TopBar → Dock)
    WorkAreaChanged.cs          # AppBar yerleşim sonrası dock yeniden konumlanma olayı
    DockAutoHideController.cs   # auto-hide stub (her zaman görünür)
  App.xaml(.cs)                 # StartupUri yok; TopBar + Dock kod ile açılır
  app.manifest                  # PerMonitorV2 + longPathAware + asInvoker
  WharfBar.csproj
```

`dotnet new wpf` şablonundan gelen `MainWindow.xaml(.cs)` silindi; `StartupUri` kaldırılıp
başlatma `App.OnStartup` içine alındı.

## Sonraki fazlarda eklenecek

```
src/WharfBar/
  Models/
    AppConfig.cs                # Faz 5 (JSON şeması)
  Services/
    ConfigService.cs            # Faz 5
    AutostartService.cs         # Faz 6 (Startup klasörü)
    StatusService.cs            # Faz 7
```

## Sorumluluklar

| Bileşen | Görev |
|---------|--------|
| `TopBarWindow` | Üst kenar UI; AppBar sahibi; `WndProc` hook; DPI / display / tema |
| `AppBar` | `ABM_NEW` / `QUERYPOS` / `SETPOS` / `REMOVE`; `ABN_*` çözümleme; yeniden kayıt |
| `NativeMethods` | Ham `DllImport` + struct + sabitler; `internal`, UI’dan doğrudan çağrılmaz |
| `Display` | Birincil monitör sınırları, DPI, DIP → piksel dönüşümü |
| `Dwm` | Köşe tercihi, immersive dark, backdrop yedek, frame extend |
| `WindowAppearance` | WPF-UI tema + Mica; DWM ince ayar; tema yenileme |
| `SingleInstanceGuard` | Named Mutex; ikinci örneği engeller |
| `DockWindow` | Alt overlay; demo pin strip; hover ölçek; Mica + yuvarlak köşe; AppBar yok |
| `ShellIcon` | `SHGetFileInfo` ile dosya ikonu çıkarma / `DestroyIcon` |
| `AppIconService` | `ShellIcon` → WPF `ImageSource` dönüşümü |
| `ProcessLauncherService` | Pin tıklamasında `Process.Start` |
| `FullscreenState` | `TopBarWindow` `ABN_FULLSCREENAPP` → dock gizleme/gösterme |
| `WorkAreaChanged` | Top bar AppBar yerleşiminden sonra dock’u yeniden konumlandırma |
| `DockAutoHideController` | Auto-hide stub (`Enabled=false`; gerçek davranış sonraki faz) |
| `DockHitTest` | Hit-test stub; Faz 4 tam etkileşimli, passthrough ertelendi |
| `DemoPinnedApps` | Sabit 4 demo uygulama listesi (kalıcı config Faz 5) |
| `ConfigService` | *(Faz 5)* JSON oku/yaz; şema versiyonu |
| `StatusService` | *(Faz 7)* Saat + ağ/pil vb. .NET API’lerinden |

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

Uygulanan (Faz 1–4):

1. `App.OnStartup` → `SingleInstanceGuard.TryAcquire()`; mutex alınamazsa `Shutdown()`
2. `TopBarWindow.Show()`
3. `DockWindow.Show()` — overlay; AppBar kaydı yok
4. `OnSourceInitialized` (TopBar) → `HwndSource.AddHook`; `RegisterWindowMessage("TaskbarCreated")`;
   `AppBar` oluştur (`ABE_TOP`, callback mesajı `WharfBarAppBarMessage`)
5. `OnSourceInitialized` (Dock) → hook; `WindowAppearance`; `DockHitTest.ApplyInteractive`;
   `FullscreenState` / `WorkAreaChanged` abonelikleri; pin görselleri; yerleşim
6. Çıkış yolları bağlanır → `ABM_NEW` → yerleşim uygulanır → `WorkAreaChanged.Notify`
7. Çıkış: dock kapat → `ABM_REMOVE` (idempotent, aşağıdaki tüm yollardan)

Sonraki fazlar: config yükleme (Faz 5).

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

## Config şeması (taslak)

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

Konum: `%AppData%/WharfBar/config.json`
