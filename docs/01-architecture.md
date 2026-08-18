# 01 — Mimari

> Faz 1–9 + Faz 8 kodu çalışma ağacında (commit + manuel doğrulama bekliyor). Aşağıda **mevcut**
> yapı ile **hedef** yapı ayrı ayrı işaretlidir.

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

## Mevcut klasör yapısı (Faz 1–9)

`/Windows` klasör adı Win32 ile karışmasın diye **Views** kullanılır.

```
WharfBar.sln                    # kökte, tek proje
.gitignore
src/WharfBar/
  Views/
    TopBarWindow.xaml(.cs)      # üst bar; AppBar; sol Windows menü; sağ durum/overflow; WndProc
    QuickSettingsFlyout.xaml(.cs) # Win11 tarzı hızlı ayarlar (ses/ağ; ADR-006)
    BrightnessFlyout.xaml(.cs)  # Monitör parlaklık uçan penceresi (DDC/CI + WMI Monitorian)
    MediaFlyout.xaml(.cs)       # Medya flyout: kapak/başlık/sanatçı/kontroller + oturum seçici (ADR-008)
    AboutWindow.xaml(.cs)       # Hakkında diyaloğu (AppBar yok)
    DockWindow.xaml(.cs)        # alt overlay; canlı pin/running strip; fullscreen / work-area
    VeilWindow.xaml(.cs)        # tam ekran kaplama overlay (click-through, fade; ADR-007)
    SettingsWindow.xaml(.cs)     # Ayarlar ana penceresi (NavigationView)
  Views/Pages/
    GeneralPage.xaml(.cs)       # Ayarlar: genel (autostart)
    AppearancePage.xaml(.cs)    # Ayarlar: görünüm (tema, arka plan, accent)
    DockPage.xaml(.cs)          # Ayarlar: dock (auto-hide, ikon boyutu)
    TopBarPage.xaml(.cs)        # Ayarlar: üst çubuk (durum öğeleri)
    VeilPage.xaml(.cs)          # Ayarlar: Kaplama (renk/opaklık/kısayol; ADR-007)
    AboutPage.xaml(.cs)         # Ayarlar: hakkında
  Native/
    NativeMethods.cs            # DllImport’lar, RECT / APPBARDATA / MONITORINFO / MARGINS
    MonitorNative.cs            # dxva2.dll (DDC/CI) + user32.dll monitör P/Invoke
    AppBar.cs                   # SHAppBarMessage wrapper; QUERYPOS/SETPOS; idempotent ABM_REMOVE
    AppBarPlacement.cs          # SETPOS no-op, WM_WINDOWPOSCHANGING kıskaç, dock slide aralığı
    Display.cs                  # birincil monitör sınırları + DPI → fiziksel piksel
    Dwm.cs                      # köşe, immersive dark, backdrop yedek, frame extend
    ShellIcon.cs                # IShellItemImageFactory + SHGetFileInfo ikon çıkarma / Destroy
    TaskbarNative.cs            # Shell_TrayWnd + ABM_GETSTATE/SETSTATE sarmalayıcı
    SendInputKeys.cs            # Win+B / Win+N / Win+P / Space / Enter
    TrayOverflowNative.cs       # overflow penceresi / TrayNotifyWnd chevron / Win+B
    WindowEnumeration.cs        # EnumWindows adayları (Shell_TrayWnd vb. hariç)
    AppUserModelId.cs           # GetCurrentProcessExplicitAppUserModelID
    DockHitTest.cs              # WS_EX_TRANSPARENT toggle (ApplyInteractive / ApplyPassthrough)
    PowerStatus.cs              # GetSystemPowerStatus sarmalayıcı
    NotifyIconHost.cs           # Shell_NotifyIcon ADD/DELETE (kendi tepsi ikonu)
    AudioEndpointVolume.cs      # master volume / mute (Core Audio)
    CoreAudioDevices.cs         # render aygıt listesi + varsayılan seçimi
    CoreAudioSessions.cs        # uygulama oturum sesleri
    Hotkey.cs                   # RegisterHotKey P/Invoke + kısayol parser (ADR-007)
  Models/
    PinnedApp.cs                # pin kaydı (path, args, label)
    DockAppItem.cs              # birleşik dock satırı (pin / running)
    DemoPinnedApps.cs           # varsayılan 4 pin (config seed)
    TopBarStatusItemIds.cs      # durum şeridi id kataloğu + Normalize
    MediaSnapshot.cs            # çalan medya anlık görüntüsü (SMTC; Idle + HasSession) — ADR-008
    AppConfig.cs                # JSON kök şema + DockConfig + TopBarConfig
  Services/
    SingleInstanceGuard.cs      # named Mutex
    WindowAppearance.cs         # WPF-UI tema + backdrop; DWM ince ayar
    AppThemePreference.cs       # config theme → ApplicationTheme
    ConfigService.cs            # %AppData%/WharfBar/config.json oku/yaz
    AppIconService.cs           # ShellIcon → WPF ImageSource (DPI boyutlu)
    ProcessLauncherService.cs   # Process.Start launcher
    WindowActivationService.cs  # odaklama / tekrar tıklamada minimize toggle
    FullscreenState.cs          # süreç içi fullscreen sinyali (TopBar → Dock)
    WorkAreaChanged.cs          # AppBar yerleşim sonrası dock yeniden konumlanma olayı
    DockAutoHideController.cs   # kenar bandı poll, show/hide animasyon, state makinesi
    AutostartService.cs         # Startup klasörü .lnk; AppConfig.autostart ile senkron
    StatusService.cs            # Saat + ağ/pil (.NET / GetSystemPowerStatus)
    MediaSessionSelector.cs     # oturum seçim politikası: en son aktif; MoveCurrent halka (ADR-008)
    MediaSessionService.cs      # SMTC izleme: snapshot + oynat/duraklat/ileri/geri + seçici (ADR-008)
    MonitorBrightnessService.cs # Monitör tarama, DDC/CI + WMI parlaklık oku/yaz (debounced)
    VolumeService.cs            # master volume poll
    AudioMixerService.cs        # aygıt + uygulama karıştırıcı
    QuickSettingsService.cs     # hızlı ayar karoları / ağ eylemleri (veil karosu dahil)
    WifiService.cs              # Wlan API ile ağ listesi + bağlanma (QS Wifi sayfası)
    BluetoothService.cs         # eşleşmiş BT cihazları + bağlı durumu (QS Bluetooth sayfası)
    NightLightService.cs        # CloudStore best-effort gece ışığı okuma/yazma + oturum override
    ProjectionService.cs        # SetDisplayConfig ile yansıtma modları (QS Project sayfası)
    VeilService.cs              # kaplama durumu, fade, hotkey host, config kalıcılığı (ADR-007)
    TrayIconService.cs          # Kendi Shell_NotifyIcon + Quit menüsü
    TaskbarVisibilityService.cs # görev çubuğu capture / hide / restore
    TaskbarPinnedAppsSource.cs  # User Pinned\TaskBar .lnk tarama + watcher
    RunningAppsSource.cs        # EnumWindows arka plan + AUMID gruplama
    DockAppsComposer.cs         # pin + running birleşik liste
    ShortcutResolver.cs         # .lnk → hedef yol / args
    TrayOverflowInvoker.cs      # peek-show + native overflow + yeniden gizle
    NotificationCenterInvoker.cs # Win+N
  App.xaml(.cs)                 # Startup: taskbar hide → TopBar + Dock + tray
  app.manifest                  # PerMonitorV2 + longPathAware + asInvoker
  WharfBar.csproj
```

`dotnet new wpf` şablonundan gelen `MainWindow.xaml(.cs)` silindi; `StartupUri` kaldırılıp
başlatma `App.OnStartup` içine alındı.

Dock auto-hide / hit-test (Faz 8) uygulandı — ayrıntı:
[spec](superpowers/specs/2026-08-10-dock-auto-hide-design.md),
[plan](superpowers/plans/2026-08-10-dock-auto-hide.md).
Shell yerine geçme (Faz 9): [ADR-005](decisions/005-shell-replace-live-dock-tray-hybrid.md),
[spec](superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md).
Renk kaplaması (veil): [ADR-007](decisions/007-veil-overlay.md),
[spec](superpowers/specs/2026-08-18-veil-overlay-design.md),
[plan](superpowers/plans/2026-08-18-veil-overlay.md).
Medya paneli (SMTC): [ADR-008](decisions/008-media-panel.md),
[spec](superpowers/specs/2026-08-18-media-panel-design.md),
[plan](superpowers/plans/2026-08-18-media-panel.md).

## Sorumluluklar

| Bileşen | Görev |
|---------|--------|
| `TopBarWindow` | AppBar; sol logo menü; sağ küme; hızlı ayarlar; `dateTime` + `notifications` (Win+N); `controlCenter` → QS Main; `volume` → QS Mixer; `brightness` → `BrightnessFlyout`; `media` → `MediaFlyout` (oturum yokken gizli); `network` → QS Network |
| `QuickSettingsFlyout` | Win11 tarzı hızlı ayarlar (karolar / ses karıştırıcı / ağ) — ADR-006 |
| `BrightnessFlyout` | Monitorian tarzı monitör parlaklık popover'ı (master slider + bağımsız monitör kartları) |
| `MediaSessionService` | SMTC (`GlobalSystemMediaTransportControlsSessionManager`) izleme: snapshot (kapak/başlık/sanatçı/çalma durumu), komutlar (Oynat/Duraklat, İleri/Geri), oturum seçimi; olaylar kurulduğu Dispatcher'a marshal; init hatası 10 sn retry; `Changed` / `SessionsChanged` (ADR-008) |
| `MediaSessionSelector` | Saf seçim politikası: en son aktif oturum başta; `MoveCurrent` ile seçici halkasında ilerleme; yalnızca playing/paused oturumlar — unit testli (ADR-008) |
| `MediaAppNameResolver` | AUMID → Windows uygulama adı: çalışan sürecin `FileDescription`'ı (örn. `chrome.exe` → "Google Chrome"), bulunamazsa basitleştirilmiş exe adı; AUMID başına önbellek — unit testli (ADR-008) |
| `MediaFlyout` | Flyout içeriği: oturum seçici (1'den fazlaysa), kapak + başlık/sanatçı, Oynat/Duraklat + İleri/Geri; sabit 340px genişlik; oturum yokken "Çalan medya yok" (popup kapanmaz) |
| `VeilService` | Kaplama durumu + fade + hotkey host (mesaj-only `HwndSource`); `SetEnabled` → `Config.Save`; çıkışta `UnregisterHotKey` (ADR-007) |
| `VeilWindow` | Tam ekran katmanlı kaplama (düz renk dolgu, `Topmost`, kalıcı `WS_EX_TRANSPARENT` passthrough, 280 ms fade); birincil monitör rect + `WM_DISPLAYCHANGE` / `WM_DPICHANGED` yeniden yerleşim |
| `HotkeyNative` / `HotkeyParser` | `RegisterHotKey` / `UnregisterHotKey` P/Invoke; `Ctrl+F11` string ↔ (modifiers, vk) parse/format (ADR-007) |
| `AboutWindow` | Hakkında diyaloğu; shell kaydı yok |
| `MonitorBrightnessService` | DDC/CI (masaüstü `dxva2.dll`) + WMI (`WmiMonitorBrightness`) taraması; 50ms debounced arka plan yazma |
| `MonitorNative` | `dxva2.dll` P/Invoke fonksiyonları + `user32.dll` monitör sorguları |
| `VolumeService` / `AudioEndpointVolume` | Varsayılan render endpoint ses seviyesi / mute (Core Audio) |
| `AudioMixerService` / `CoreAudioDevices` / `CoreAudioSessions` | Tek aygıt + dropdown + uygulama oturum sesleri (ADR-006) |
| `QuickSettingsService` | Ağ detayı, WinRT radyo, Settings URI / Win+P |
| `NotificationCenterInvoker` | Native bildirim merkezi (`Win+N`) |
| `AppBar` | `ABM_NEW` / `QUERYPOS` / `SETPOS` / `REMOVE`; aynı rect’te SETPOS yok; `ABN_*` çözümleme; yeniden kayıt |
| `AppBarPlacement` | QUERYPOS no-op, onaylı rect kıskaç, dock auto-hide Y aralığı |
| `NativeMethods` | Ham `DllImport` + struct + sabitler; `internal`, UI’dan doğrudan çağrılmaz |
| `Display` | Birincil monitör sınırları, DPI, DIP → piksel dönüşümü |
| `Dwm` | Köşe tercihi, immersive dark, backdrop yedek, frame extend |
| `WindowAppearance` | WPF-UI tema + Mica; DWM ince ayar; tema yenileme |
| `SingleInstanceGuard` | Named Mutex; ikinci örneği engeller |
| `DockWindow` | Alt overlay; canlı pin/running strip; hover ölçek (yalnız hover'daki ikon, 1.3×); sağ tık bağlam menüsü (Başlık kartı, Dock'a sabitle/kaldır, Görevi sonlandır, Pencereyi kapat, Yönetici çalıştır, Dosya konumunu aç, Yeni pencere); katmanlı pencere + tema renkli yüzey (Mica/Acrylic); AppBar yok |
| `TaskbarVisibilityService` | Başlatmada görev çubuğu baseline yakala; `Shell_TrayWnd` / secondary gizle; keep-hidden izleme (`ApplyPlacement` içinde `ReapplyHide` yok); çıkışta restore |
| `TaskbarPinnedAppsSource` | `User Pinned\TaskBar` `.lnk` tarama; değişiklik watcher |
| `RunningAppsSource` | `EnumWindows` arka plan thread + `IsHungAppWindow`; AUMID gruplama |
| `DockAppsComposer` | Pin + running birleşik `DockAppItem` listesi (Sabitler solda, sabit olmayan çalışanlar ayırıcı ile sağda); `pinnedApps` fallback |
| `ShortcutResolver` | `.lnk` hedef yol / args çözümleme ve dinamik `.lnk` kısayolu oluşturma |
| `WindowActivationService` | Çalışan pencereye odaklama; öndeyse tekrar tıklamada `SW_MINIMIZE` |
| `TrayOverflowInvoker` | Peek-show gizli tepsi → `TrayOverflowNative` → overflow; sonra yeniden gizle |
| `ShellIcon` | `IShellItemImageFactory` (DPI px) + `SHGetFileInfo` yedek; ikon / bitmap destroy |
| `AppIconService` | `ShellIcon` → WPF `ImageSource` (boyut önbellek anahtarlı) |
| `ProcessLauncherService` | Pin tıklamasında `Process.Start` |
| `FullscreenState` | `TopBarWindow` `ABN_FULLSCREENAPP` → dock gizleme/gösterme |
| `WorkAreaChanged` | Top bar AppBar yerleşiminden sonra dock’u yeniden konumlandırma |
| `DockAutoHideController` | `dock.autoHide` state + kenar bandı poll; show/hide animasyon; fullscreen bridge |
| `DockHitTest` | `ApplyInteractive` / `ApplyPassthrough` — `WS_EX_TRANSPARENT` toggle |
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

Uygulanan (Faz 1–9):

1. `App.OnStartup` → `SingleInstanceGuard.TryAcquire()`; mutex alınamazsa `Shutdown()`
2. `ConfigService.LoadOrCreate()` → `%AppData%/WharfBar/config.json`; `AppThemePreference` uygulanır
3. `AutostartService.Apply(config.Autostart)` — Startup klasörü kısayolunu senkronlar
4. `TaskbarVisibilityService.CaptureAndHide()` — `ABM_GETSTATE` + `Shell_TrayWnd` görünürlük baseline;
   ardından `ShowWindow(SW_HIDE)` ile gizle
5. `TopBarWindow.Show()` — AppBar + sol Windows menü + `StatusService` / overflow
6. `DockWindow.Show(config)` — overlay; `DockAppsComposer` ile canlı pin/running; AppBar kaydı yok
7. `TrayIconService` — kendi tepsi ikonu + Quit menüsü
8. `OnSourceInitialized` (TopBar) → `HwndSource.AddHook`; `RegisterWindowMessage("TaskbarCreated")`;
   `AppBar` oluştur (`ABE_TOP`, callback mesajı `WharfBarAppBarMessage`); durum şeridi bağlanır;
   medya servisi kurulur: `new MediaSessionService()` → `Changed`/`SessionsChanged` abonelikleri →
   `MediaHost.Attach(_media)` → `_ = _media.InitializeAsync()` (ADR-008)
9. `OnSourceInitialized` (Dock) → hook; `WindowAppearance`; `DockHitTest.ApplyInteractive`;
   `FullscreenState` / `WorkAreaChanged` abonelikleri; pin görselleri; yerleşim
10. Çıkış yolları bağlanır → `ABM_NEW` → yerleşim uygulanır → `WorkAreaChanged.Notify`
11. Çıkış: `DisposeOwnedServices` (servis abonelikleri çözülür; `_media.Dispose()`) →
    **görev çubuğu restore** (`TaskbarVisibilityService.Restore`) → tray dispose → dock kapat
    → `ABM_REMOVE` (idempotent; `ReleaseShellState` sırası: restore önce, sonra remove)

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
| `ABN_POSCHANGED`, `ABN_STATECHANGE` | `Dispatcher.BeginInvoke` ile yerleşimi kuyruğa al; `ABM_SETPOS` yalnızca QUERYPOS sonucu değişince (explorer `SendMessage` içinde `SHAppBarMessage` deadlock / wait cursor) |
| `ABN_FULLSCREENAPP` | Tam ekran uygulama varken `HWND_BOTTOM`, bitince `HWND_TOPMOST`; `FullscreenState.Instance.Set` |
| `ABN_WINDOWARRANGE` | v1’de işlem yok |
| `TaskbarCreated` | `ABM_NEW` tekrar (explorer yeniden başladı) + yerleşim + `TaskbarVisibilityService.ReapplyHide()` |
| `WM_DPICHANGED`, `WM_DISPLAYCHANGE` | `Dispatcher.BeginInvoke` ile yerleşimi yeniden uygula |
| `WM_WINDOWPOSCHANGED` | `ABM_WINDOWPOSCHANGED` (kendi taşımamız sırasında atlanır) |
| `WM_WINDOWPOSCHANGING` | Onaylı AppBar rect’ine kıskaç (phantom work-area itmesi; bar altında bar) |
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
    "iconSize": 28,
    "surface": "mica"
  },
  "topBar": {
    "backgroundMode": "tint",
    "tintOpacity": 40,
    "statusCatalogVersion": 4,
    "statusItems": [
      "trayOverflow",
      "dateTime",
      "notifications",
      "controlCenter",
      "volume",
      "brightness",
      "media",
      "network",
      "battery"
    ]
  },
  "veil": {
    "enabled": false,
    "color": "#4a90d9",
    "opacity": 40,
    "hotKey": "Ctrl+F11"
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
| `dock.autoHide` | `true` → kenar bandı proximity + animasyonlu gizleme; `false` → her zaman görünür |
| `dock.iconSize` | DIP; 16–36 aralığına sıkıştırılır (varsayılan **28**, görev çubuğu benzeri). Eski 48/72 ve kısa süreli 24 varsayılanı yüklemede 28’e migrate edilir |
| `dock.surface` | `mica` \| `acrylic`; dock yüzeyi (`WindowAppearance` → `WindowBackdropType`). Varsayılan **`mica`**; bilinmeyen değer → `mica`. Zemin rengi üst barla aynı DWM sistem kaynağından (Windows temasından otomatik) |
| `topBar.backgroundMode` | `mica` \| `transparent` \| `tint`; bilinmeyen → `mica` (varsayılan). Mica ↔ translucent geçişi TopBar recreate gerektirir |
| `topBar.tintOpacity` | 0–100; tint modunda alpha; varsayılan **40**; yüklemede clamp |
| `topBar.statusCatalogVersion` | Soft katalog sürümü (`schemaVersion` 1). Güncel **4**: eksik `controlCenter` `volume` önüne, eksik `brightness` `volume` sonrasına, eksik `media` `brightness` sonrasına eklenir (`Ensure*`; eski sürüm açılışta 4'e taşınır — ADR-008) |
| `topBar.statusItems` | Görünür durum öğesi id’leri (sol→sağ sıra). Bilinmeyen id düşülür; boş/yok → varsayılan tam set (`trayOverflow`, … `media` dahil, … `battery`). Soft alan (`schemaVersion` 1) |
| `veil.enabled` | Kaplama açık/kapalı (kalıcı; QS karosu + kısayol + sayfa toggle'ı yazar). Varsayılan **`false`** |
| `veil.color` | Kaplama rengi `#RRGGBB` (soft; geçersiz/eksik → `VeilNormalizer` varsayılan **`#4A90D9`**, küçük harfe normalleştirilir; `#RGB` kabul edilir) |
| `veil.opacity` | 0–100 kaplama gücü; yüklemede `ClampOpacity` (varsayılan **40**). Siyah + yüksek opaklık = parlaklık azaltma (ayrı kontrol yok) |
| `veil.hotKey` | Global kısayol metni (`Ctrl+F11`); boş = yok. Soft alan: geçersizse kayıt yapılmaz, string korunur (sayfada uyarı) |
| `pinnedApps` | Seed / fallback pin listesi; canlı TaskBar `.lnk` tarama başarılıysa baskın değildir (ADR-005) |

İlk çalıştırmada dosya yoksa `DemoPinnedApps` ile varsayılan yazılır. Kayıt atomik
(temp + replace). Bozuk JSON → `config.json.bak` + varsayılan.
