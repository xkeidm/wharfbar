# 02 — Win32: AppBar, DWM, Tray, İkonlar

## a) AppBar — ekran alanı rezervasyonu

Sadece `Topmost=true` yetmez; diğer pencerelerin altına girmemesi için Windows’a AppBar kaydı gerekir (görev çubuğunun yaptığı).

### API özeti

```csharp
[DllImport("shell32.dll")]
static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

// ABM_NEW = 0x00, ABM_REMOVE = 0x01, ABM_QUERYPOS = 0x02, ABM_SETPOS = 0x03
// ABE_TOP = 1, ABE_BOTTOM = 3
```

**Akış:** `ABM_NEW` → `ABM_QUERYPOS` → `ABM_SETPOS` → kapanırken **mutlaka** `ABM_REMOVE`.

`ABM_REMOVE` kaçırılırsa rezerve alan asılı kalır; kullanıcı genelde `explorer.exe` yeniden başlatmak zorunda kalır.

### Callback’ler

AppBar kaydında `uCallbackMessage` ile özel bir window message tanımla. Gelen `ABN_*`
bildirimlerini işle (ör. `ABN_POSCHANGED`, `ABN_FULLSCREENAPP`, `ABN_WINDOWARRANGE`).
**`ABN_POSCHANGED` içinde `SHAppBarMessage` çağırma** — explorer `SendMessage` ile bildirir;
içeriden `ABM_SETPOS` deadlock (üst barda wait cursor, tıklama yok). `BeginInvoke` +
aynı rect’te SETPOS atla. Fullscreen gizleme ve yeniden konumlandırma buradan bağlanır.

### Çoklu monitör

AppBar `ABM_NEW` sırasında pencerenin bulunduğu monitöre bağlanır; bar önce hedef monitöre
taşınıp sonra kaydedilirse her bar kendi monitöründe kenar rezerve eder. Yerleşim
`Display.GetMonitorBounds` (ADR-003) ile ilgili monitörün fiziksel piksel sınırından hesaplanır;
her monitör config'te ayrı açılır/kapanır (`monitors`).

Birincil monitörde bu yol görev çubuğuyla birebir çalışır. İkincil monitörlerde shell
uyumluluğu daha kırılgan olabildiğinden yedek olarak `SystemParametersInfo(SPI_SETWORKAREA)`
ile manuel work area genişletmesi uygulanır (dikkatli kullan; diğer shell bileşenleriyle
çakışabilir — bkz. boundaries "Yap").

### Explorer çöküşü / yeniden başlama

Shell yeniden başlayınca AppBar kaydı kaybolur.

**Uygulanan çözüm:** `RegisterWindowMessage("TaskbarCreated")` dinlenir; mesaj gelince `ABM_NEW`
tekrar çalıştırılıp yerleşim yeniden uygulanır. Eski kayıt ölen shell ile birlikte gittiği için
öncesinde `ABM_REMOVE` gerekmez. Bkz. [ADR-003](decisions/003-topbar-placement-and-appbar-lifecycle.md).

### Dock ve AppBar

Üst bar için AppBar uygundur. Dock için macOS benzeri overlay (Topmost + auto-hide) genelde yeterlidir; alt AppBar zorunlu değildir. Bkz. ADR-001.

---

## b) DWM / Mica / backdrop

```csharp
// DwmSetWindowAttribute / DwmExtendFrameIntoClientArea
// DWMWA_USE_IMMERSIVE_DARK_MODE = 20
// DWMWA_WINDOW_CORNER_PREFERENCE = 33  (1 = do not round, 2 = round)
// DWMWA_SYSTEMBACKDROP_TYPE = 38       (2 = Mica, 3 = Acrylic, 4 = MicaAlt)
```

**Tercih sırası:** Önce **WPF-UI** backdrop API’leri. Ham DWM Mica yalnızca WPF-UI yetmezse veya ince ayar için; çakışmadan kaçın. Uygulama: `Services/WindowAppearance.cs` + `Native/Dwm.cs`. Ayrıntı: [ADR-004](decisions/004-topbar-mica-corners-and-theme.md).

**Kritik tuzak:** WPF `AllowsTransparency=true` DWM composition’ı (Mica’yı) bozar.

**Üst bar uygulanan desen (Faz 3):**

- Opak pencere; `AllowsTransparency` kapalı; `WindowStyle=None` (caption min-height ~39 px’i aşmak için)
- Backdrop doğrulanınca arka plan `Transparent` brush; aksi halde opak fallback
- `DwmExtendFrameIntoClientArea` ile `MARGINS` = `-1` (glass bölgesi; caption gerekmez)
- Köşe: üst bar `DWMWCP_DONOTROUND`; dock (Faz 4) yuvarlak kullanabilir

> `WindowChrome` ile caption bırakmak Mica için gerekli değildir ve 30 DIP şeridi
> DefWindowProc min yüksekliğine (~39) şişirir — kullanılmaz (ADR-004).

---

## c) Sistem tepsisi / durum göstergeleri

```csharp
Shell_NotifyIcon  // NIM_ADD / MODIFY / DELETE
```

**Önemli:** `Shell_NotifyIcon` **kendi** tepsi ikonunu eklemek/güncellemek içindir. Wi-Fi, ses, pil gibi **sistem tepsi ikonlarını okumak resmi API ile desteklenmez**.

Top bar göstergeleri için:

- Durumu `NetworkInformation`, güç API’leri (`GetSystemPowerStatus`), saat vb. **.NET / Win32 API’lerinden** oku
- Kendi ikon setini kullan (üst barda **Segoe MDL2 Assets** glifleri — Mica AppBar şeridinde
  WPF-UI `SymbolIcon` boş kalabiliyordu; saat dışı göstergeler kayboluyordu)
- İsteğe bağlı: WharfBar’ın kendi `NotifyIcon`’u — uygulama: `TrayIconService` + `NotifyIconHost` (Quit menüsü); sistem ikon okuma değildir
- **Tepsi hibrit + ayna (ADR-005 + ADR-009):** üçüncü taraf ikonlar **belgelenmiş comctl32
  toolbar mesajlarıyla** (`TB_BUTTONCOUNT`, `TB_GETBUTTON` (index; `TBBUTTON` → `idCommand`/`iBitmap`),
  `TB_GETIMAGELIST` + `ImageList_GetIcon`, `TB_GETITEMRECT`, `TB_GETBUTTONTEXT` (command id))
  üst barda aynalanır; tıklama `SendMessageTimeout` mouse mesajlarıyla forward edilir.
  `ReadProcessMemory` / enjeksiyon / belgelenmemiş enumeration yok. Gizli/overflow ikonlar
  aynalanamaz (yalnızca açık popup'ta var); `TrayOverflowInvoker` (`Win+B` / chevron) kalır.
  Poll 1.5 sn; `SendMessageTimeout` (`SMTO_ABORTIFHUNG`, 500 ms) asılı explorer'da UI'ı
  kilitlemez.
- **Windows 11 25H2+ UIA fallback:** klasik `ToolbarWindow32` tepsi bu sürümde kaldırıldı
  (25H2'de `TrayNotifyWnd` altında `SysPager`/`ToolbarWindow32` yok). `FindToolbarHwnd()` boş
  dönünce ayna UI Automation'a düşer: `Shell_TrayWnd → … → TaskbarFrameAutomationPeer` altındaki
  `SystemTray.NormalButton` butonları (ad + `AutomationElement`); sistem ikonları farklı
  class'larda (`AccentButton`/`OmniButton*`/`ShowDesktopButton`) + giriş göstergesi tooltip
  filtreyle elenir. İkon bitmap'i UIA'da yok → `TrayCapture` `PrintWindow(PW_RENDERFULLCONTENT)`
  ile park+cloak'lı taskbar'ın kendi içeriğini çekip butonun `Image` çocuk rect'ini kırpar
  (gerçek ikon, flash yok; başarısızsa `TrayLetterGlyph` fallback). UIA
  çocukları yalnızca taskbar `SW_SHOW` iken açığa çıkar → her poll `BeginTrayPeek(stealth:true)`
  (cloak + ekran dışı park) ile okunur, flash yok. Tıklama: sol → `InvokePattern` (stealth-peek);
  sağ/çift → UIA ifade edemediği için görünür peek + `SendInputMouse` fiziksel tıklama (kısa
  flash; overflow deseniyle aynı).

**Uygulanan:** sol Windows logo (4 kare **Path/Rectangle**, font glifi değil — Mica’da görünürlük);
sağ küme varsayılan LTR `trayMirror · gizli · tarih/saat · bildirim · controlCenter · ses · ağ · pil`
(`topBar.statusItems`; özelleştirilebilir). Saat/tarih: tek satır `tr-TR` birleşik etiket
(örn. `9 Ağu Paz 21:57`, Segoe UI) + zil glifi; ikisi de native Notification Center
(`Win+N` — takvim + bildirimler; ayrı takvim `Popup` yok; ADR-005 aynası yok).
`controlCenter` → QS **Main**; ses → QS **Mixer**; ağ → QS **Network** — paylaşılan
**Hızlı ayarlar** flyout (ADR-006); `PopupAnimation=Fade` için katmanlı (layered) `Popup`
(`AllowsTransparency=true`). Kural değişmedi: **ana pencerede** Mica + `AllowsTransparency`
yasak; popup ayrı bir pencere olduğundan Mica'yı etkilemez.

**Overflow + gizli görev çubuğu:** `Shell_TrayWnd` `SW_HIDE` iken düz `Win+B` no-op olur.
`TrayOverflowInvoker` önce `BeginTrayPeek()` ile tepsiyi geçici gösterir, overflow açar,
sonra `EndTrayPeek()`. Çıkışta tam `Restore`.

---

## d) Ses (Core Audio) — hızlı ayarlar

```
IMMDeviceEnumerator / IMMDevice          // aygıt listesi + varsayılan
IAudioEndpointVolume                     // master seviye / mute
IAudioSessionManager2 / ISimpleAudioVolume // uygulama oturumları
IPolicyConfig.SetDefaultEndpoint         // varsayılan çıkış (ADR-006; dar istisna)
```

Ham COM `Native/AudioEndpointVolume.cs`, `CoreAudioDevices.cs`, `CoreAudioSessions.cs` içinde;
UI `QuickSettingsFlyout` üzerinden.

---

## e) Uygulama ikonu (dock)

```csharp
// Tercih: IShellItemImageFactory.GetImage(sizePx) — DPI’ya göre keskin
// Yedek: SHGetFileInfo(..., SHGFI_ICON | SHGFI_LARGEICON / SMALLICON)
// HBITMAP/HICON → Imaging.CreateBitmapSourceFromHBitmap/HIcon → ImageSource
```

Varsayılan `dock.iconSize` **28 DIP** (görev çubuğu benzeri, 24’ten hafif büyük); dock yüksekliği
ikon + padding + çalışıyor noktasına göre hesaplanır (~46 DIP @ 28). Yerleşim: birincil monitör
alt kenarı (`BottomGapDip = 0`; gizlenmiş görev çubuğu work-area boşluğu yok). İkon aralığı
**12 DIP**. Klasik `.exe` / `.lnk` için uygundur.
**UWP/Store** paketleri farklı yol ister (AppxManifest) — **v1 tam destek yok**; çalışan UWP
için placeholder kabul (ADR-005); kırık yarım yol sunulmaz.

---

## f) Görev çubuğu gizleme / geri yükleme (ADR-005)

WharfBar çalışırken birincil monitör görev çubuğu gizlenir; başlatmada durum yakalanır,
çıkış/crash yollarında geri yüklenir (`ABM_REMOVE` disiplini). Uygulama: `TaskbarVisibilityService`
+ `Native/TaskbarNative.cs`.

**Seçilen hide yolu (plan):**

1. **Baseline:** `ABM_GETSTATE` (auto-hide bayrağı) + birincil `SPI_GETWORKAREA` +
   `FindWindow("Shell_TrayWnd")` — restore için saklanır.
2. **Gizleme:** `ShowWindow` + `SWP_HIDEWINDOW` (`FindWindow("Shell_TrayWnd")` ve
   `FindWindowEx` ile `Shell_SecondaryTrayWnd`; UI thread’de tam `EnumWindows` yok —
   asılı bir pencere AppBar’da wait cursor bırakır) + `SPI_SETWORKAREA` (üst/sol/sağ
   korunur; alt = monitör kenarı). `ABM_SETSTATE(ABS_AUTOHIDE)` kullanılmaz — explorer
   kenar reveal ile çubuğu tekrar gösterir. Yalnızca `SW_HIDE` AppBar rezervasyonunu
   bırakır; bu yüzden work area ayrıca genişletilir.
3. **AppBar `SETPOS` sonrası:** shell alt şeridi geri yazabiliyorsa
   `EnsureWorkAreaExpanded()`. `ApplyPlacement` içinde `ReapplyHide` yok — gizleme +
   `SETPOS` `ABN_POSCHANGED` döngüsü üst barda wait cursor ve dock auto-hide iptali
   üretir. `ABN_POSCHANGED` / `ABN_STATECHANGE` `Dispatcher.BeginInvoke` ile işlenir
   (explorer `SendMessage` içinde `SHAppBarMessage` deadlock). `ABM_SETPOS` yalnızca
   QUERYPOS sonucu son commit’ten farklıysa. `WorkAreaChanged` yalnızca yerleşim
   gerçekten değişince.    `WM_WINDOWPOSCHANGING` onaylı AppBar rect’ine kıskanç:
   phantom alt şerit dönünce WPF barı work-area tepesine (`y=30`) iter; rezerv
   şeritte takılı kare kalır. Dock overlay aynı itmede `ClampSlideY` ile
   auto-hide aralığında kalır (önbellekli Y; `WM_WINDOWPOSCHANGING` içinde
   `Measure` yok). Onaylı rect’teyken `ABM_WINDOWPOSCHANGED` atlanır —
   aksi halde WPF’nin work-area itmesi her layout’ta explorer `SendMessage`
   ile UI’yi dondurur.
4. **Keep-hidden:** 250 ms `FindWindow` / `FindWindowEx` — peek dışında görünür
   tepsiye `SW_HIDE`. **Arka plan `System.Threading.Timer` üzerinde çalışır**
   (WPF dispatcher’a bağımlı değil; meşgul UI thread’i re-hide’ı asla
   açlıktan öldüremez). Peek yalnızca **taze** iken tepsiyi korur: kaçırılmış
   `EndTrayPeek` ile takılı kalan (orphaned) peek, `TrayPeekTracker`’ın 45 sn
   sınırını aşınca keep-hidden tarafından zorla sonlandırılır (`Reset` +
   re-hide) — görev çubuğu bu yüzden asla sonsuza dek görünür kalamaz.
   HWND gizli kalsa bile explorer görev çubuğu AppBar’ını
   yeniden `SETPOS` edebilir; work area altı daralmışsa en fazla 1 sn’de bir
   `EnsureWorkAreaExpanded`. `SPI_SETWORKAREA` (`SPIF_SENDCHANGE`) her üst düzey
   pencereye broadcast yapar ve bu makinede ~1 sn bloklar — UI thread’i
   donmaması için çağrı arka plan thread’e kaydırıldı (guard: aynı anda tek
   genişletme; restore ile yarışan genişletme baseline’a geri alınır).
   `Shell_NotifyIcon` sonrası ve `TaskbarCreated` için `ReapplyHide`.
5. **Geri yükleme:** `ShowWindow(SW_SHOW)` + `ABM_SETSTATE(baseline)` +
   `SPI_SETWORKAREA(baseline work area)`; izleme durur.
6. **Çıkış sırası:** görev çubuğu restore → `ABM_REMOVE` (`TopBarWindow.ReleaseShellState`).
7. **`TaskbarCreated`:** AppBar yeniden kayıt + `ReapplyHide()`.

Çoklu monitör: work area her monitör için o monitöre göre genişletilir; dock rezervasyonu
`SetDockReservation(Monitor, heightPx)` ile monitör bazında (bkz. boundaries "Yap").

Zorla kill restore’u atlayabilir → [`04-gotchas.md`](04-gotchas.md).

---

## g) Çalışan uygulama listesi (dock)

`EnumWindows` ile üst düzey görünür pencereler **arka plan thread**’de taranır (UI’da
asılı bir HWND wait cursor bırakır); `IsHungAppWindow` atlanır. `Shell_TrayWnd`, `Progman`,
`WorkerW`, `ApplicationFrameWindow` ve kendi sürecimiz hariç tutulur
(`Native/WindowEnumeration.cs`). `ApplicationFrameWindow`, UWP uygulamalarının
ApplicationFrameHost çerçevesidir (asıl uygulama penceresi `Windows.UI.Core.CoreWindow`
ayrıca enumerate edilir); hariç tutulmazsa her UWP uygulaması iki kez görünür.
Yol için `QueryFullProcessImageName` (`Process.MainModule` UI’yi kilitleyebilir). Gruplama
için `GetProp(ApplicationUserModelID)` (`AppUserModelId.cs`; Win11’de UWP pencerelerinde
boş döner, dedup’ı `ApplicationFrameWindow` hariç tutulması sağlar). `C:\Windows\SystemApps\`
altındaki süreçler (Windows Input Experience / TextInputHost vb. sistem yardımcı
host’ları) çalışan uygulama sayılmaz (`RunningAppsSource`). WharfBar kendi pencereleri
(`ShowInTaskbar=false`) için `Process.MainWindowHandle` güvenilmez — odaklama
`WindowActivationService` + `EnumWindows` + PID filtresi ile yapılır.

Dock ikon tıklaması (görev çubuğu benzeri toggle): minimized → `SW_RESTORE` + `SetForegroundWindow`;
arka planda → öne getir; zaten öndeyse → `SW_MINIMIZE`. Dock tıklamada odağı çalabileceği için
`PreviewMouseLeftButtonDown` anında `GetForegroundWindow` kaydedilir; aynı süreç PID’si de
“önde” sayılır (Electron vb. alt pencereler).

**Hover ve pencere boyutu:** macOS tipi büyütme (komşu ölçeği / reflow) yok — hover'daki
ikon tek başına `HoverScale` 1.3× büyür (140ms geçiş). Pencere **sabit boyuttadır** (taban
şerit + küçük hover payı; `ComputeMaxWidthPx`/`MaxHeightDip`), hover sırasında `SetWindowPos`
hiç çalışmaz — frame-başına resize/recenter (stutter) önlenir. ScrollViewer yoktur; çok
sayıda ikon ekran genişliğine kırpılır.

**Dock yüzeyi:** Katmanlı pencere (`AllowsTransparency=True`) — strip yüzeyi `Surface`
border’ın WPF fırçasıyla çizilir; pencere kenarları (headroom) şeffaf ve tıklama geçirir,
dock çevresinde görünür kutu oluşmaz. **DWM backdrop kullanılmaz** (tüm pencereyi doldurur,
kenarlarda kutu yaratır). Renk `ApplySurfaceFromConfig` ile temadan gelir (`dock.surface`:
`mica` = opak tema rengi koyu `#202020` / açık `#FAFAFA`; `acrylic` = aynı renk %80 saydam).

---

## h) DPI ve ekran değişimi

- Manifest: Per-Monitor V2
- `WM_DPICHANGED` ve display change bildirimlerini dinle; AppBar `QUERYPOS`/`SETPOS` ve pencere konumunu güncelle
- Handler’lar `TopBarWindow` / `AppBar` katmanında toplanmalı
- **Konumu WPF’in DIP tabanlı `Left/Top/Width/Height`’ı ile hesaplama** — bunlar birincil monitörün
  ölçeğini varsayar. `GetDpiForWindow` + `GetMonitorInfo` ile fiziksel piksel hesapla, `SetWindowPos`
  ile uygula ([ADR-003](decisions/003-topbar-placement-and-appbar-lifecycle.md)).

---

## i) Hit-testing (dock / overlay)

Overlay dock’ta mouse’un bar dışına geçmesi, auto-hide ve “tıklama altındaki pencereye gitsin mi?” davranışı için hit-test stratejileri gerekir. AppBar kullanan top bar’da work area zaten ayrıldığı için hit-test ihtiyacı farklıdır.

**Faz 8 (uygulandı):** `DockHitTest` görünür / gösteriliyor durumda `ApplyInteractive` (`WS_EX_TRANSPARENT` temiz); gizli veya off-screen animasyonda `ApplyPassthrough` (`WS_EX_TRANSPARENT` set) — tıklama alttaki pencereye geçer. `DockAutoHideController` kenar bandı poll + slide animasyonu yönetir. Ayrıntı: [dock auto-hide spec](superpowers/specs/2026-08-10-dock-auto-hide-design.md).

---

## j) AllowUnsafeBlocks

Temel `[DllImport]` P/Invoke için `AllowUnsafeBlocks` **gerekmez**. Yalnızca pointer arithmetic veya `unsafe` blok gerçekten yazılırsa aç.

---

## k) Global kısayol (RegisterHotKey)

Kaplama (ADR-007) ve benzeri toggle'lar için sistem geneli kısayol; uygulama: `Native/Hotkey.cs` + `VeilService`.

```csharp
RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk)      // user32.dll
UnregisterHotKey(IntPtr hWnd, int id)                               // user32.dll
// fsModifiers: MOD_ALT=0x1, MOD_CONTROL=0x2, MOD_SHIFT=0x4, MOD_WIN=0x8
// WM_HOTKEY = 0x0312; wParam = id; mesaj-only HwndSource (ParentWindow=-3)
```

- Mesaj alımı için **mesaj-only** pencere: `HwndSourceParameters` + `ParentWindow = new IntPtr(-3)` (TrayIconService deseni); `AddHook` ile `WM_HOTKEY` → toggle.
- `RegisterHotKey` başarısızlığı (başka uygulamanın çakışması) çökme değildir: kayıt yok + uyarı, config bozulmaz.
- **Çıkış garantisi:** `UnregisterHotKey` `VeilService.Dispose`'ta (normal çıkış + `App.Exit`); kaçırılırsa kısayol ikinci başlatmada yeniden kaydedilemez.

---

### wlanapi.dll — Wi-Fi listesi / bağlanma (Quick Settings)

- `WlanOpenHandle(2, NULL, &ver, &handle)` → `ERROR_SUCCESS`
- `WlanEnumInterfaces(handle, NULL, &list)` — WLAN_INTERFACE_INFO_LIST (InterfaceGuid)
- `WlanGetAvailableNetworkList(handle, &guid, 0, NULL, &list)` — WLAN_AVAILABLE_NETWORK_LIST
  (wlanProfileFlags & CONNECTED/HAS_PROFILE, wlanSignalQuality 0-100, bSecurityEnabled)
- `WlanSetProfile(handle, &guid, 0, xml, NULL, TRUE, NULL, &reason)` — kalıcı profil
- `WlanConnect(handle, &guid, &params)` — WLAN_CONNECTION_PARAMETERS (mode=profile)
- `WlanQueryInterface(handle, &guid, wlan_intf_opcode_current_connection, ...)` — mevcut SSID
- `WlanFreeMemory` / `WlanCloseHandle`

### user32.dll — display topology (Quick Settings / Yansıt)

- `QueryDisplayConfig(QDC_DATABASE_CURRENT, 0, NULL, 0, NULL, &topologyId)` — mevcut topoloji
- `SetDisplayConfig(0, NULL, 0, NULL, SDC_TOPOLOGY_x | SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES)`

### user32.dll / powrprof.dll — güç eylemleri (Windows logo menüsü)

- `LockWorkStation()` (user32) — oturumu kilitle
- `SetSuspendState(FALSE, FALSE, FALSE)` (powrprof) — uyku
- `shutdown.exe /s|/r /t 0` — kapat / yeniden başlat (P/Invoke değil, `Process.Start`)
