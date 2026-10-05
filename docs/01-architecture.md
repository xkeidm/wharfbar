# 01 — Mimari

> Faz 1–9 + Faz 8 + **dock yeniden tasarımı (2026-10-04)** kodu çalışma ağacında. Aşağıda
> **mevcut** yapı ile **hedef** yapı ayrı ayrı işaretlidir.

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
    TopBarWindow.xaml(.cs)      # üst bar; AppBar; sol Windows menü; ORTA ada (IslandPill); sağ durum/overflow; WndProc
    QuickSettingsFlyout.xaml(.cs) # Win11 tarzı hızlı ayarlar (ses/ağ; ADR-006)
    BrightnessFlyout.xaml(.cs)  # Monitör parlaklık uçan penceresi (DDC/CI + WMI Monitorian)
    MediaFlyout.xaml(.cs)       # Medya flyout: kapak/başlık/sanatçı/kontroller + oturum seçici (ADR-008)
    IslandFlyout.xaml(.cs)      # Ada paneli: arama + tek birleşik zaman çizelgesi (pano + bırakılan dosya; ADR-010)
    AboutWindow.xaml(.cs)       # Hakkında diyaloğu (AppBar yok)
    DockWindow.xaml(.cs)        # alt overlay; kendi listesinden 3 bölümlü dock şeridi; hover büyütme + reflow; tam ekran / work-area
    VeilWindow.xaml(.cs)        # tam ekran kaplama overlay (click-through, fade; ADR-007)
    SettingsWindow.xaml(.cs)     # Ayarlar ana penceresi (NavigationView)
    DockItemViewModel.cs        # slotun çizime hazır anlık görüntüsü (ikon + tooltip + bölüm bayrakları)
  Views/Controls/
    DockStrip.cs                # şerit `Panel`: slot ölçeği + yatay reflow (matematik `DockStripLayout`'da)
    DockItemCell.xaml(.cs)      # tek slot: ikon (alt kenarından büyür) + çalışan çizgisi | ayraç
  Views/Pages/
    GeneralPage.xaml(.cs)       # Ayarlar: genel (autostart)
    AppearancePage.xaml(.cs)    # Ayarlar: görünüm (tema, arka plan, accent)
    DockPage.xaml(.cs)          # Ayarlar: dock (içerik listesi, auto-hide, ikon boyutu, yüzey, büyütme, konum, çalışanlar)
    TopBarPage.xaml(.cs)        # Ayarlar: üst çubuk (durum öğeleri)
    VeilPage.xaml(.cs)          # Ayarlar: Kaplama (renk/opaklık/kısayol; ADR-007)
    IslandPage.xaml(.cs)        # Ayarlar: Ada (görünürlük/kalıcılık/limitler/bakım; ADR-010)
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
    TrayToolbar.cs              # gerçek tray toolbar TB_* okuyucu + click forward (ADR-009)
    WindowEnumeration.cs        # EnumWindows adayları (Shell_TrayWnd vb. hariç)
    AppUserModelId.cs           # GetCurrentProcessExplicitAppUserModelID
    DockHitTest.cs              # WS_EX_TRANSPARENT toggle (ApplyInteractive / ApplyPassthrough)
    PowerStatus.cs              # GetSystemPowerStatus sarmalayıcı
    NotifyIconHost.cs           # Shell_NotifyIcon ADD/DELETE (kendi tepsi ikonu)
    AudioEndpointVolume.cs      # master volume / mute (Core Audio)
    CoreAudioDevices.cs         # render aygıt listesi + varsayılan seçimi
    CoreAudioSessions.cs        # uygulama oturum sesleri
    Hotkey.cs                   # RegisterHotKey P/Invoke + kısayol parser (ADR-007)
    ClipboardNative.cs          # pano P/Invoke'ları (OpenClipboard/GlobalAlloc/dinleyici) + opt-out okuma/yazma (ADR-010)
  Models/
    PinnedApp.cs                # pin kaydı (path, args, label)
    DockAppItem.cs              # birleşik dock satırı + `DockSection` (Pinned | Running | Minimized)
    DemoPinnedApps.cs           # varsayılan 4 pin (config seed)
    TopBarStatusItemIds.cs      # durum şeridi id kataloğu + Normalize
    MediaSnapshot.cs            # çalan medya anlık görüntüsü (SMTC; Idle + HasSession) — ADR-008
    IslandEntry.cs              # ada kaydı (Text/File, önizleme, dosya yolu, zaman, kaynak uygulama) — ADR-010
    AppConfig.cs                # JSON kök şema + DockConfig + TopBarConfig + IslandConfig
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
    SystemStatsService.cs       # CPU/GPU/RAM örnekleme (GetSystemTimes / GlobalMemoryStatusEx / PDH)
    MediaSessionSelector.cs     # oturum seçim politikası: en son aktif; MoveCurrent halka (ADR-008)
    MediaSessionService.cs      # SMTC izleme: snapshot + oynat/duraklat/ileri/geri + seçici (ADR-008)
    MonitorBrightnessService.cs # Monitör tarama, DDC/CI + WMI parlaklık oku/yaz (debounced)
    VolumeService.cs            # master volume poll
    AudioMixerService.cs        # aygıt + uygulama karıştırıcı
    QuickSettingsService.cs     # hızlı ayar karoları / ağ eylemleri (veil karosu dahil)
    WifiService.cs              # Wlan API ile ağ listesi + bağlanma (QS Wifi sayfası)
    NetworkDetailsService.cs    # Ağ sayfası "Özellikler": IP/gateway/DNS/MAC/link hızı + canlı ↓/↑ (IPv4 byte delta)
    BluetoothService.cs         # eşleşmiş BT cihazları + bağlı durumu (QS Bluetooth sayfası)
    NightLightService.cs        # CloudStore best-effort gece ışığı okuma/yazma + oturum override
    ProjectionService.cs        # SetDisplayConfig ile yansıtma modları (QS Project sayfası)
    VeilService.cs              # kaplama durumu, fade, hotkey host, config kalıcılığı (ADR-007)
    ClipboardMessageWindow.cs   # pano değişim dinleyicisi (mesaj-only HwndSource; WM_CLIPBOARDUPDATE) — ADR-010
    IslandPruner.cs             # adanın SAF çekirdeği: kabul/dedupe/budama/arama/önizleme/drop adı — ADR-010
    IslandStore.cs              # %AppData%/WharfBar/island.json atomik yazma + %AppData%/WharfBar/Drop — ADR-010
    IslandService.cs            # ada yaşam döngüsü: STA okuma thread'i, tek zaman çizelgesi, debounce yazma — ADR-010
    TrayIconService.cs          # Kendi Shell_NotifyIcon + Quit menüsü
    TaskbarVisibilityService.cs # görev çubuğu capture / hide / restore + watchdog spawn/self-heal
    TaskbarWatchdogState.cs     # watchdog baseline JSON store (%LOCALAPPDATA%/WharfBar)
    TaskbarWatchdog.cs          # --taskbar-watchdog modu: restore'suz ölümde görev çubuğunu dışarıdan geri getirir
    DockItemStore.cs            # dock'un sıralı, tekilleştirilmiş, kalıcı sabit listesi (App tek örneği)
    RunningAppsSource.cs        # EnumWindows arka plan + AUMID gruplama
    MinimizedWindowsSource.cs   # tüm üst düzey pencere tablosu + küçültülmüş alt küme (arka iş parçacığı, 800 ms)
    DockAppsComposer.cs         # Pinned | Running | Minimized bölümlerini birleştiren zarif bileşik (saf `Compose`)
    DockStripBuilder.cs         # bölüm değişimlerine 1 px ayraç slotu ekler
    DockMagnification.cs        # SAF büyütme matematiği: düşüş eğrisi, etki yarıçapı, kare hızından bağımsız yumuşatma
    DockStripLayout.cs          # SAF yatay yerleşim: slot ölçekleri, reflow ofsetleri, pencere bütçesi (max genişlik/yükseklik)
    DockDropFormats.cs          # bırakılan dosya süzgeci (.lnk/.exe/.bat/.cmd) + yola çözümleme
    ShortcutResolver.cs         # .lnk → hedef yol / args / etiket (okuma yönü; `.lnk` **yazma** yolu 2026-10-04'te kaldırıldı)
    TrayOverflowInvoker.cs      # peek-show + native overflow + yeniden gizle
    TrayMirrorService.cs        # Tray toolbar'ı poll'lar, sistem ikonlarını filtreler, `TrayMirrorItem` snapshot yayınlar (ADR-009)
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
Dock yeniden tasarımı (bağımsız liste + büyütme motoru; ADR-005 revizyonu):
[spec](superpowers/specs/2026-10-04-dock-macos-redesign-design.md),
[plan](superpowers/plans/2026-10-04-dock-macos-redesign.md).
Renk kaplaması (veil): [ADR-007](decisions/007-veil-overlay.md),
[spec](superpowers/specs/2026-08-18-veil-overlay-design.md),
[plan](superpowers/plans/2026-08-18-veil-overlay.md).
Medya paneli (SMTC): [ADR-008](decisions/008-media-panel.md),
[spec](superpowers/specs/2026-08-18-media-panel-design.md),
[plan](superpowers/plans/2026-08-18-media-panel.md).
Ada (clipboard geçmişi + bırakılan dosyalar): [ADR-010](decisions/010-island-clipboard-drop.md),
[spec](superpowers/specs/2026-10-01-island-clipboard-drop-design.md),
[plan](superpowers/plans/2026-10-01-island-clipboard-drop.md).

## Sorumluluklar

| Bileşen | Görev |
|---------|--------|
| `TopBarWindow` | AppBar; sol logo menü; **ortada ada** (`IslandPill` + `IslandPopup`); sağ küme; hızlı ayarlar; `dateTime` + `notifications` (Win+N); `controlCenter` → QS Main; `volume` → QS Mixer; `brightness` → `BrightnessFlyout`; `media` → `MediaFlyout` (oturum yokken gizli); `network` → QS Network |
| `QuickSettingsFlyout` | Win11 tarzı hızlı ayarlar (karolar / ses karıştırıcı / ağ) — ADR-006 |
| `BrightnessFlyout` | Monitorian tarzı monitör parlaklık popover'ı (master slider + bağımsız monitör kartları) |
| `MediaSessionService` | SMTC (`GlobalSystemMediaTransportControlsSessionManager`) izleme: snapshot (kapak/başlık/sanatçı/çalma durumu), komutlar (Oynat/Duraklat, İleri/Geri), oturum seçimi; olaylar kurulduğu Dispatcher'a marshal; init hatası 10 sn retry; `Changed` / `SessionsChanged` (ADR-008) |
| `MediaSessionSelector` | Saf seçim politikası: en son aktif oturum başta; `MoveCurrent` ile seçici halkasında ilerleme; yalnızca playing/paused oturumlar — unit testli (ADR-008) |
| `MediaAppNameResolver` | AUMID → Windows uygulama adı (katmanlı): Start Menü kısayolu AUMID eşleşmesi (örn. "Zen", "YouTube Music"), sonra çalışan sürecin `FileDescription`'ı (örn. `chrome.exe` → "Google Chrome"), sonra basitleştirilmiş exe adı; AUMID başına önbellek — unit testli (ADR-008) |
| `MediaFlyout` | Flyout içeriği: oturum seçici (1'den fazlaysa), kapak + başlık/sanatçı, Oynat/Duraklat + İleri/Geri; sabit 340px genişlik; oturum yokken "Çalan medya yok" (popup kapanmaz) |
| `IslandService` | Adanın yaşam döngüsü: mesaj-only pano dinleyicisi, **ayrı STA okuma thread'i**, tek birleşik zaman çizelgesi, 750 ms debounce'lu diske yazma; `Changed` / `EntryAdded` UI dispatcher'ında yükselir; dosya kopyalama/silme STA'da; `Dispose` → bekleyen yazma flush (ADR-010) |
| `IslandPruner` | Adanın **saf** çekirdeği: kabul kuralı (opt-out, 64 KB, boş/boşluk), ardışık aynı içeriği yukarı taşıma, gün/`MaxItems` budaması, arama eşleştirme, önizleme/göreli zaman biçimlendirme, bırakma dosya adı üretimi — unit testli (ADR-010) |
| `IslandStore` | `%AppData%/WharfBar/island.json` (metadata) atomik **değiştirme** (tmp + `File.Replace`) + `%AppData%/WharfBar/Drop` (bırakılan dosyalar); tek `_ioGate` kilidi; bozuk dosya `.bak` (ikinci bozulmada `.bak.1`), 64 MB boyut tavanı, kayıt bazlı toleranslı okuma; **sıralamaz** (ADR-010) |
| `ClipboardNative` | Belgelenmiş pano P/Invoke'ları; `CanIncludeInClipboardHistory` opt-out okuma **ve** yazma; pano sahibi süreç adı (`GetClipboardOwner` → pid → `QueryFullProcessImageName`) (ADR-010) |
| `ClipboardMessageWindow` | Mesaj-only `HwndSource` (`ParentWindow = -3`) + `AddClipboardFormatListener`; `WM_CLIPBOARDUPDATE` → `ClipboardChanged`; `Dispose` dinleyiciyi **bir kez** kaldırır (API idempotent değil) (ADR-010) |
| `IslandFlyout` | Panel içeriği: arama kutusu + `ScrollViewer`/`StackPanel` liste (iç içe `Popup` yok); `↑`/`↓`/`Enter`/`Delete` klavye yönlendirmesi; satır kuralları (tür glifi, önizleme, kaynak uygulama, göreli zaman); `MaxRenderedRows = 200` (ADR-010) |
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
| `DockWindow` | Alt overlay; **kendi bağımsız listesi** (pin + running + küçültülmüş, bölüm ayraçlı); hover **büyütme + reflow** (özel `DockStrip` paneli, imlecin üstü + 3 komşu, `Phase`/`CursorPosition`); sürükle-sırala, dosya bırakma, konum (alt-sol/orta/sağ), "Pencereler" alt menüsü; katmanlı pencere + tema renkli yüzey (Mica/Acrylic WPF ile çizilir); AppBar yok |
| `TaskbarVisibilityService` | Başlatmada görev çubuğu baseline yakala; `Shell_TrayWnd` / secondary gizle; keep-hidden izleme (`ApplyPlacement` içinde `ReapplyHide` yok); çıkışta restore. Baseline watchdog state dosyasına yazılır, `--taskbar-watchdog` alt süreci spawn edilir; stale dosya varsa açılışta tray önce gösterilir (self-heal) |
| `TaskbarWatchdog` | Headless mod (`Program.Main` dallanır, WPF başlamaz): PID + start-time izler, restore'suz ölümde baseline'ı uygular. PID reuse ve yeni-instance dosya yenilemesi güvenli |
| `RunningAppsSource` | `EnumWindows` arka plan thread + `IsHungAppWindow`; AUMID gruplama |
| `MinimizedWindowsSource` | Tüm üst düzey pencere tablosunu **arka iş parçacığında** 800 ms'de bir tarar (`GetTopLevelCandidates` + pencere başına `IsIconic`); `Windows` önbelleğinden `ForProcess(pid)` alt kümesi — "Pencereler" menüsü UI thread'inde `EnumWindows` **çağırmaz** (donmuş HWND'de kilitler) |
| `DockItemStore` | Dock'un tek kaynağı: sıralı, normalize yola göre tekilleştirilmiş, kalıcı sabit liste (`dock.items`); `TryAdd`/`Remove`/`Move`/`Reload` + `Changed`. Liste çekirdeği `*Core` statiklerinde → diske dokunmadan testli. Anahtar **her zaman** normalize yoldur, `Id` değil (pinli-ve-çalışan kutucukta `Id` çalışan uygulamadan gelir) |
| `DockAppsComposer` | Pinned \| Running \| Minimized bölümlerini birleştirir; çalışan uygulamayı yolla eşleşen ilk sabitlenen öğeye bağlar; saf `Compose` çekirdeği test edilir; `ItemsEqual` ile yalnız gerçek değişimde yeniden üretir |
| `DockStripBuilder` / `DockStripLayout` / `DockMagnification` | Satır başına bir öğe (ayraç slotu **yok**, tek sürekli şerit) ve **saf** büyütme/yerleşim matematiği: düşüş eğrisi (0.82), etki yarıçapı, slot ölçekleri, reflow ofsetleri, pencere için max genişlik/yükseklik bütçesi — hepsi testli, WPF tipi yok |
| `ShortcutResolver` | `.lnk` → hedef yol / args / etiket çözümleme (bırakılan kısayolun dock listesine girmesi için). Dinamik `.lnk` **oluşturma** yolu 2026-10-04'te, görev çubuğuna `.lnk` yazan `TogglePin` ile birlikte kaldırıldı |
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
| `AppConfig` / `DockConfig` | JSON şema (`schemaVersion` 2) |
| `ConfigService` | JSON oku/yaz; bozuk/uyumsuz şemada `.bak` + varsayılan |
| `AppThemePreference` | `theme` alanı → `ApplicationTheme` |
| `AutostartService` | Startup klasörü `WharfBar.lnk`; `autostart` true/false ile oluştur/sil |
| `StatusService` | Saat + ağ/pil; `.NET NetworkInformation` + `GetSystemPowerStatus` |
| `NetworkDetailsService` | Ağ sayfası "Özellikler" (2 sn poll, sayfa açıkken): aktif adaptör (Wi-Fi > Ethernet önceliği) → IP/gateway/DNS/MAC/link hızı; canlı ↓/↑ Mbps `GetIPv4Statistics` byte-delta'sından, sayaç sıfırlaması/süre 0 → null ("—"). Saf hesap metotları unit testli |
| `SystemStatsService` | Üst bar sol küme CPU/GPU/RAM okuması (2 sn poll); `GetSystemTimes` + `GlobalMemoryStatusEx` + PDH `\GPU Engine(*)\Utilization Percentage` (`PdhAddEnglishCounterW`, lokalizasyondan bağımsız). GPU motor örnekleri örnek adındaki LUID'e göre fiziksel adaptöre gruplanır (`ParseAdapterKey`/`GroupByAdapters`); yazılımsal adaptörler (`DXGI_ADAPTER_FLAG_SOFTWARE` — Microsoft Basic Render Driver/WARP, `GetSoftwareAdapterLuidKeys`) dışlanır: Windows onları D3D adaptörü olarak sayar ama fiziksel GPU değildir ve Görev Yöneticisi de listelemez. `topBar.showGpuPerAdapter` açıkken `GPU0 x% · GPU1 y%`, kapalıyken tek toplam. Saf hesap metotları unit testli |
| `TrayIconService` / `NotifyIconHost` | Kendi tepsi ikonu + Quit menüsü (`Shell_NotifyIcon`) |
| `TrayMirrorService` | Tray toolbar'ı 1.5 sn poll'lar, sistem ikonlarını filtreler, `TrayMirrorItem` snapshot yayınlar; tıklama forward (ADR-009) |
| `PowerStatus` | Pil / AC satırı (`GetSystemPowerStatus`) |

## Island (ADR-010)

Ayrı pencere **değildir**: ada üst barın **içinde**, Grid'in orta hücresinde ortalanmış bir
`Border`; panel aynı pencere içindeki `Popup`. Kararın tamamı:
[`decisions/010-island-clipboard-drop.md`](decisions/010-island-clipboard-drop.md).

```
TopBarWindow.xaml — kök Grid (sütun yok; üç küme aynı hücre)
  ├─ StackPanel (sol)  : logo + sistem istatistikleri
  ├─ Border "IslandPill" (HorizontalAlignment=Center, AllowDrop)   ← ada
  └─ StackPanel "StatusStrip" (sağ) : tray aynası, saat, bildirim, QS, ses, pil …

Popup "IslandPopup" (Placement=Bottom, PlacementTarget=IslandPill, StaysOpen=True,
                     AllowsTransparency=True, PopupAnimation=None)
  └─ local:IslandFlyout (arama kutusu + ScrollViewer/StackPanel liste)
```

### Yeni dosyalar ve görevleri

| Dosya | Görev |
|-------|-------|
| `Native/ClipboardNative.cs` | Belgelenmiş pano P/Invoke'ları: `OpenClipboard`/`CloseClipboard`, `GetClipboardData`, `SetClipboardData`, `EmptyClipboard`, `IsClipboardFormatAvailable`, `RegisterClipboardFormatW`, `GlobalAlloc`/`GlobalLock`/`GlobalUnlock`/`GlobalFree`/`GlobalSize`, `Add`/`RemoveClipboardFormatListener`, `GetClipboardOwner` + `GetWindowThreadProcessId`. `TryReadText` opt-out işaretini **fail-closed** okur; `TryWriteText` metni UTF-16 olarak yazar **ve** opt-out'u birlikte yazar; `GetOwnerProcessName` kaynak uygulama adını verir |
| `Models/IslandEntry.cs` | `internal enum IslandEntryKind { Text, File }` + `internal sealed record IslandEntry(long Id, IslandEntryKind Kind, string Text, string? FilePath, DateTime CapturedAtUtc, string? SourceApp)` — tek birleşik zaman çizelgesinin kaydı |
| `Services/IslandPruner.cs` | **Saf** çekirdek (Win32 ve dosya sistemi yok → tamamı testli): `ShouldCapture`, `Prepend` (ardışık aynı → yukarı), `Prune` (gün + `MaxItems`, oturumluk modda gün limiti yok), `Matches`, `FormatPreview`, `FormatRelativeTime`, `BuildDropFileName` |
| `Services/IslandStore.cs` | Ayrı kalıcılık dosyası: `island.json` (tmp + `File.Replace`), `Drop` klasörü; tek `_ioGate` kilidi; `.bak` / `.bak.1`; 64 MB boyut tavanı; tek bozuk alan tüm geçmişi düşürmez; **sıralamaz** (sıra tüketicinin işi) |
| `Services/ClipboardMessageWindow.cs` | Mesaj-only `HwndSource` (`ParentWindow = -3`, mevcut `MonitorMessageWindow` kalıbı); `WM_CLIPBOARDUPDATE` UI thread'inde `ClipboardChanged` yükseltir |
| `Services/IslandService.cs` | Yaşam döngüsü + orkestrasyon: STA okuma thread'i, `Entries`/`Latest`, `ApplyConfig`, `AddDroppedFiles`, `PutBack`, `Remove`, `Clear`, `DeleteDroppedFiles`, `Changed`/`EntryAdded`, `Dispose` |
| `Views/IslandFlyout.xaml(.cs)` | Panel: arama kutusu + liste; `Attach(service)` yalnız panel açıkken; satır kurulumu code-behind'de (`MaxRenderedRows = 200`) |
| `Views/Pages/IslandPage.xaml(.cs)` | Ayar sayfası: görünürlük, kalıcılık, gün/limit, bakım eylemleri; her değişiklik `Config.Save` **+** `IslandService.ApplyConfig` (unutulursa ada güncellenmez) |

### Pano okuma STA thread'inde

`OpenClipboard` bloklayabilir; UI thread'inde yapılırsa tüm bar donar. Depoda
`System.Windows.Clipboard` **kullanılmaz** (UI thread'ine bağlıdır). Akış:

1. `WM_CLIPBOARDUPDATE` UI thread'inde gelir (`ClipboardMessageWindow`).
2. İş `IslandService`'in **ayrı STA dispatcher thread'ine** (`Dispatcher.Run`,
   `ApartmentState.STA`, `WharfBar.Island`) kuyruğa girer. Sinyal `volatile Dispatcher?` ile
   paylaşılır ve hazır olma 1 sn beklenir; geç kalınırsa yakalama **sessizce atlanır ama
   loglanır** (görünmez kalmaması için).
3. Kilitliyse 3 × 40 ms kısa bekleyip yeniden denenir; hâlâ açılmıyorsa olay **sessizce
   düşürülür**. UI thread'inde asla yeniden deneme yapılmaz.
4. Sonuç UI thread'ine `BeginInvoke` ile döner: kayıt ekleme, budama ve `Changed` **UI'da**
   olur (tek zaman çizelgesi UI-only). Dosya kopyalama/silme de STA'dadır.

### Ayrı kalıcılık dosyası

Geçmiş `config.json`'a **yazılmaz**: `ConfigService.Save` her çağrıda dosyanın tamamını UI
thread'inde yeniden yazar, pano değişiklikleri ise yüksek frekanslıdır. Bunun yerine:

| Yol | İçerik |
|-----|--------|
| `%AppData%\WharfBar\island.json` | Kayıt metadata'sı (`version` + `entries`); 750 ms debounce, atomik değiştirme |
| `%AppData%\WharfBar\Drop\` | Bırakılan dosyaların kopyaları (orijinale dokunulmaz) |

`island.persistence = "session"` iken **metadata** hiç yazılmaz (`Commit` yalnız persistent
modda `ScheduleSave` çağırır) ve `island.json` okunmaz; bırakılan dosyaların `Drop` kopyaları
ise moddan bağımsız olarak diske yazılır. Dosya yazma hatası kullanıcıya gösterilmez; bellekteki
liste çalışmaya devam eder ve sonraki değişiklikte yeniden denenir. Diskteki metadata'ya dosya
**içeriği** girmez.

### Kapsam

v1: yalnızca birincil monitörün barı (`trayMirror` ile aynı sınır — servis `TopBarEngine`'de
tek örnek, `AttachIsland` yalnız primary'de çağrılır). `Enabled=false` iken pill çizilmez,
dinleyici kurulmaz, `island.json` okunmaz; mevcut kayıtlar diske dokunulmadan bırakılır.
`Clear()` dosya kopyalarını **silmez** (bilinçli); onları yalnız `DeleteDroppedFiles()` siler ve o
eylem `Drop` klasörünü tarayarak yetimleri de kapsar.

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
4. `TaskbarVisibilityService.CaptureAndHide()` — stale watchdog dosyası varsa tray önce gösterilir
   (self-heal); `ABM_GETSTATE` + `Shell_TrayWnd` görünürlük baseline; baseline watchdog dosyasına
   yazılır; ardından `ShowWindow(SW_HIDE)` ile gizle ve `--taskbar-watchdog` alt süreci spawn edilir
5. `TopBarWindow.Show()` — AppBar + sol Windows menü + `StatusService` / overflow
6. `DockWindow.Show(config)` — overlay; `DockItemStore` + `RunningAppsSource` + `MinimizedWindowsSource`
   → `DockAppsComposer` ile kendi listesinin üç bölümü; AppBar kaydı yok
7. `TrayIconService` — kendi tepsi ikonu + Quit menüsü
8. `OnSourceInitialized` (TopBar) → `HwndSource.AddHook`; `RegisterWindowMessage("TaskbarCreated")`;
   `AppBar` oluştur (`ABE_TOP`, callback mesajı `WharfBarAppBarMessage`); durum şeridi bağlanır;
   medya servisi kurulur: `new MediaSessionService()` → `Changed`/`SessionsChanged` abonelikleri →
   `MediaHost.Attach(_media)` → `_ = _media.InitializeAsync()` (ADR-008)
9. `OnSourceInitialized` (Dock) → hook; `WindowAppearance`; `DockHitTest.ApplyInteractive`;
   `FullscreenState` / `WorkAreaChanged` abonelikleri; üç kaynak + composer kurulur; ilk
   `RebuildStrip`; yerleşim
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

## Config şeması (`schemaVersion` 2)

Konum: `%AppData%/WharfBar/config.json`  
Uygulama: `Models/AppConfig.cs` + `Services/ConfigService.cs` (camelCase JSON, girintili).

```json
{
  "schemaVersion": 2,
  "theme": "system",
  "autostart": true,
  "dock": {
    "autoHide": true,
    "iconSize": 28,
    "surface": "mica",
    "position": "bottom-center",
    "magnification": 1.15,
    "showRunningApps": true,
    "items": [
      { "path": "C:\\\\...\\\\app.exe", "args": "", "label": "App" }
    ]
  },
  "topBar": {
    "backgroundMode": "tint",
    "tintOpacity": 40,
    "showSystemStats": true,
    "showGpuPerAdapter": true,
    "statusCatalogVersion": 5,
    "statusItems": [
      "trayMirror",
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
  "island": {
    "enabled": true,
    "persistence": "session",
    "retentionDays": 30,
    "maxItems": 200
  },
  "pinnedApps": [
    { "path": "C:\\\\...\\\\app.exe", "args": "", "label": "App" }
  ]
}
```

| Alan | Anlam |
|------|--------|
| `schemaVersion` | Zorunlu; yalnızca `1`/`2` kabul (`> 2` → `.bak` + varsayılan). **2**, `monitors` bloğunu getirdi; dock yeniden tasarımı şemayı **yine de 2'de bıraktı** — alan bazlı varsayılanlarla kırılmadan okuma tercihi (`conventions.md:24`) |
| `theme` | `system` \| `light` \| `dark` (`AppThemePreference`) |
| `autostart` | `true` → Startup’ta `WharfBar.lnk`; `false` → kısayol silinir (`AutostartService`) |
| `dock.autoHide` | `true` → kenar bandı proximity + animasyonlu gizleme; `false` → her zaman görünür |
| `dock.iconSize` | DIP; 16–36 aralığına sıkıştırılır (varsayılan **28**, görev çubuğu benzeri). Eski 48/72 ve kısa süreli 24 varsayılanı yüklemede 28’e migrate edilir |
| `dock.surface` | `mica` \| `acrylic`; **WPF'in çizdiği** iki yarı saydam dolgu (dock katmanlı pencere olduğu için gerçek DWM backdrop kullanılamaz — `docs/04-gotchas.md:7`). Varsayılan **`mica`**; bilinmeyen değer → `mica`. Dolgu rengi **nötr gri**: koyu `#202020` @ `0x99`/`0xCC`, açık `#FAFAFA` @ `0xD9`/`0xE6` (`mica`/`acrylic`) — `ApplicationThemeManager.GetAppTheme()` ile seçilir, yani **tema rengi değil ve DWM'den gelmez**; üst barın Mica tonuyla aynı değeri kullanmaz |
| `dock.items` | macOS tarzı sıralı sabit liste (Path/Args/Label) — **dock'un tek kaynağı**; sıra config sırasıdır. Anahtar normalize tam yoldur, `DockItemStore` yolla tekilleştirir |
| `dock.position` | `bottom-left` \| `bottom-center` \| `bottom-right`; geçersiz/boş → `bottom-center`. Yan dock kapsam dışı |
| `dock.magnification` | Hover tavanı 1.0–2.0 (varsayılan **1.15**; ayarlar kaydırıcısı 1.0–1.5). Pencere bütçesi **ve** ikon raster boyutu bu değerden türetilir, bu yüzden tek başına kaydetmek şeridi yeniden kurar |
| `dock.showRunningApps` | Sabitlenmemiş çalışanlar + küçültülmüş pencereler |
| `topBar.backgroundMode` | `mica` \| `transparent` \| `tint`; bilinmeyen → `mica` (varsayılan). Mica ↔ translucent geçişi TopBar recreate gerektirir |
| `topBar.tintOpacity` | 0–100; tint modunda alpha; varsayılan **40**; yüklemede clamp |
| `topBar.statusCatalogVersion` | Soft katalog sürümü (`schemaVersion` 2'den bağımsız). Güncel **5**: eksik `controlCenter` `volume` önüne, eksik `brightness` `volume` sonrasına, eksik `media` `brightness` sonrasına eklenir (`Ensure*`; eski sürüm açılışta 5'e taşınır — ADR-008/009) |
| `topBar.statusItems` | Görünür durum öğesi id’leri (sol→sağ sıra). Bilinmeyen id düşülür; boş/yok → varsayılan tam set (`trayOverflow`, … `trayMirror` dahil, … `battery`). Soft alan (`schemaVersion` 2) |
| `topBar.showSystemStats` | Sol kümede Windows logosunun yanındaki `CPU x% · GPU y% · RAM z%` okuması (varsayılan **`true`**; soft bool — eksik alan varsayılana düşer). GPU sayacı yoksa GPU bölümü gösterilmez |
| `topBar.showGpuPerAdapter` | Birden çok fiziksel GPU varsa GPU bölümlerini numaralandır (`GPU0 x% · GPU1 y%`); kapalıyken tek toplam segment (varsayılan **`true`**; soft bool). Tek GPU'da iki mod da aynı görünür |
| `veil.enabled` | Kaplama açık/kapalı (kalıcı; QS karosu + kısayol + sayfa toggle'ı yazar). Varsayılan **`false`** |
| `veil.color` | Kaplama rengi `#RRGGBB` (soft; geçersiz/eksik → `VeilNormalizer` varsayılan **`#4A90D9`**, küçük harfe normalleştirilir; `#RGB` kabul edilir) |
| `veil.opacity` | 0–100 kaplama gücü; yüklemede `ClampOpacity` (varsayılan **40**). Siyah + yüksek opaklık = parlaklık azaltma (ayrı kontrol yok) |
| `veil.hotKey` | Global kısayol metni (`Ctrl+F11`); boş = yok. Soft alan: geçersizse kayıt yapılmaz, string korunur (sayfada uyarı) |
| `island.enabled` | Ada + pano dinleyicisi açık/kapalı (varsayılan **`true`**). `false` iken pill çizilmez, `WM_CLIPBOARDUPDATE` dinleyicisi kurulmaz, `island.json` okunmaz; kayıtlar diske dokunulmadan bırakılır. Ayar sayfasından kapatmak `IslandService.ApplyConfig` ile servis üzerinde de etkisini gösterir (ADR-010) |
| `island.persistence` | `"session"` \| `"persistent"` (varsayılan **`"session"`**). **String, enum değil** — enum-typed alan `"Persistent"` değerinde `JsonException` fırlatıp tüm config'i sıfırlıyordu. `IslandNormalizer.NormalizePersistence` büyük/küçük harf duyarsız; çözülemeyen değer `"session"`'a düşer (ADR-010) |
| `island.retentionDays` | Persistent modda kaç gün geriye bakılır; yüklemede `ClampRetentionDays` ile 1–3650 (varsayılan **30**). Oturumluk modda yok sayılır (ADR-010) |
| `island.maxItems` | Kayıt sayısı sert tavanı (gün limitinden bağımsız); yüklemede `ClampMaxItems` ile 10–5000 (varsayılan **200**). Panel ayrıca en fazla 200 satır kurar (`IslandFlyout.MaxRenderedRows`); fazlası "N kayıt daha var (arama ile daralt)" notuyla bildirilir (ADR-010) |
| `pinnedApps` | **Legacy.** Yalnızca `ConfigService.MigrateDockItems` tarafından, `dock.items` boşken bir kez okunur ve sonra **boşaltılır**. Canlı görev çubuğu `.lnk` taraması 2026-10-04'te kaldırıldı (ADR-005 revizyonu) |

İlk çalıştırmada dosya yoksa `DemoPinnedApps` ile varsayılan yazılır. Kayıt atomik
(temp + replace). Bozuk JSON → `config.json.bak` + varsayılan.
