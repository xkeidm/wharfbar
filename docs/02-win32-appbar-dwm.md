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
 ile manuel work area genişletmesi planlanmıştı (dikkatli kullan; diğer shell bileşenleriyle
çakışabilir — bkz. boundaries "Yap"). **25H2 (2026-08-29):** `SPI_SETWORKAREA` çalışmıyor;
yedek yol ölü. Work area override'ı destekleyen bir shell gelene dek **alt şeridi yalnızca
`ABM_SETSTATE(ABS_AUTOHIDE)` ile bırakmak mümkündür** (aşağıdaki görev çubuğu gizleme
bölümü) — `SPI_SETWORKAREA`'ya gerek yok.

> **Tuzak (2026-09-30):** Bu API'ye dışarıdan (kayıtlı **olmayan** bir süreçten) bakıp
> "25H2'de her şey ölü" sonucuna varmak yanıltıcıdır. `SPI_SETWORKAREA` gerçekten
> `TRUE` dönüp **sessizce hiçbir şey yapmıyor** (ölçüldü) — ama alt şeridin bırakılması
> zaten bu yola değil, `ABM_SETSTATE(ABS_AUTOHIDE)`'ye bağlıdır ve o **çalışır**.
> Aynı şekilde `ABM_SETSTATE`'i de kayıt dışı bir süreçten prob etmek yanlış negatif verir:
> yalnızca **kayıtlı bir AppBar**'ın çağrısı etkilidir. Ölçümü WharfBar çalışırken yap.

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
    (cloak + ekran dışı park) ile okunur, flash yok. DWM cloak bazı makinelerde reddedilir
    (`DwmSetWindowAttribute` S_OK dönmez) → `TrySetTrayVisibleStealth` o durumda
    **WS_EX_LAYERED + alpha 0** yedeğiyle görünmezlik sağlar (UIA `IsWindowVisible=true` görmeye
    devam eder); o da başarısızsa peek açılmaz, poll atlanır.

   > **İkon yakalaması PEEK'in İÇİNDE olmak zorunda (2026-09-30, canlı ölçüm + düzeltme).**
   > `PrintWindow(PW_RENDERFULLCONTENT)` görev çubuğu **`IsWindowVisible=False` iken DIB'e hiç
   > piksel yazmaz ama `TRUE` döner** → `1920x48` tamamen siyah (`[0,0,0,255]`) bitmap
   > "başarılı" diye yayınlanır ve üst bar **siyah kareler** gösterir. Ölçülen fark:
   >
   > | Durum | `IsWindowVisible` | Kırpılan pikseller |
   > |---|---|---|
   > | peek kapalı (yarış) | `False` | `[0,0,0,255]` ×9 — düz siyah |
   > | peek açık | `True` | `[67,67,67] [68,68,68] [33,33,33]…` — gerçek görev çubuğu |
   >
   > **alpha 0 `PrintWindow`'i engellemiyor** — tek belirleyici `IsWindowVisible`, yani peek'in
   > açık olması. `TryRefreshViaBlip` ikonları **kendi `finally`'inden (`EndTrayPeek`) önce**
   > yakalar ve hazır `TrayMirrorItem` listesi döndürür; `PollUia` yalnız o listeyi kullanır.
   > Kalıcı savunma: `TrayCapture.IsBlankCapture` tek renkli/tam saydam kırpımayı reddeder →
   > çağıran taraf harf glifine düşer, **artık siyah kare mümkün değildir**
> (`TrayCaptureTests` ile testli).
   Ayna yalnızca birincil monitörün
   üst barına işlenir (ikincil barlarda `trayMirror` bölümü oluşmaz). Tıklama: tüm türler için
   `SendInputMouse` fiziksel tıklama (`InvokePattern` 25H2 XAML tray butonlarında
   doğrulanmadı; rect bulunamazsa son çare fallback). Uygulamanın flyout/menüsü park edilmiş
   taskbar konumuna (alt kenar) ya da geri getirilen imlece göre açılır → `TrayMenuReanchor`
   uygulamanın yeni beliren popup penceresini ayna ikonunun altına taşır (`SetWindowPos`,
   work-area kelepçeli; `#32768` menü her zaman, diğer sınıflar yalnızca tıklanan butonun
   çevresindeki tık bölgesinde aday; tam ekran overlay'ler hiçbir zaman). Eşleşme pid ile
   DEĞİL: 25H2 XAML butonlarının tümü explorer pid'i raporlar.

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
// Tercih: SHGetFileInfo(SHGFI_ICONLOCATION) + SHDefExtractIcon(istenen px)
//         — kabuk jumbo kaynağından küçültür; 16/32 bitmap büyütülmez
// Yedek:  SHGetFileInfo(SHGFI_SYSICONINDEX) + SHGetImageList(SHIL_JUMBO 256 / SHIL_EXTRALARGE 48)
// Son:    SHGFI_ICON | LARGEICON / SMALLICON
// HICON → CreateBitmapSourceFromHIcon; çizim UseLayoutRounding + SnapsToDevicePixels
```

Çalışan göstergesi yalnızca `IsRunning` iken, düğmenin içinde **8×3 DIP** kapsül, alttan 3 DIP.
Çalışmayan sabitlerde çizgi yok.

Varsayılan `dock.iconSize` **24 DIP**. Düğme glif + 16 DIP, aralık 4, levha payı 10×8;
24'te düğme 40 ve levha yüksekliği **56 DIP**. Pencere levhaya eşittir (ölçek 1).
Yerleşim: birincil monitör alt kenarı (`BottomGapDip = 0`; gizlenmiş görev çubuğu work-area boşluğu
yok). Klasik `.exe` / `.lnk` için uygundur.
**UWP/Store** paketleri farklı yol ister (AppxManifest) — **v1 tam destek yok**; çalışan UWP
için placeholder kabul (ADR-005); kırık yarım yol sunulmaz.

İkon bitmap'i glif boyutunda istenir (`ResolveIconPixelSize` = `iconSize`, 16–256 px'e kırpılır).

Yatay hizalama `dock.position` ile seçilir: `bottom-left` / `bottom-center` / `bottom-right`.
`DockPositions.Normalize` geçersiz/boş değerde `bottom-center` döndürür (yan dock kapsam dışı:
work-area rezervasyonu ve aşağı kayma gizleme değişmez — yalnızca X hizalanır). Kenar yaslama
12 DIP iç payla.

**Küçültülmüş pencereler:** `MinimizedWindowsSource.Scan()` **tek** `EnumWindows` geçişi yapar —
`WindowEnumeration.GetTopLevelCandidates()` tablosu alınır, küçültülmüş alt küme her pencere
için ayrı `IsIconic(hwnd)` çağrısıyla ayrıştırılır. Küçültülmüş pencereler `WS_VISIBLE` korur,
bu yüzden `IsCandidate` filtresi tek başına yetmez; ikinci bir `EnumWindows` geçişi de
gereksizdir. UI iş parçacığında `EnumWindows` çağrırma (donmuş HWND'de kilitler) — pencere
tablosu `MinimizedWindowsSource`'da arka iş parçacığında 800 ms'de bir kez taranır.

---

## f) Görev çubuğu gizleme / geri yükleme (ADR-005)

WharfBar çalışırken birincil monitör görev çubuğu gizlenir; başlatmada durum yakalanır,
çıkış/crash yollarında geri yüklenir (`ABM_REMOVE` disiplini). Uygulama: `TaskbarVisibilityService`
+ `Native/TaskbarNative.cs`.

**Seçilen hide yolu (plan):**

1. **Baseline:** `ABM_GETSTATE` (auto-hide bayrağı) + birincil `SPI_GETWORKAREA` +
   `FindWindow("Shell_TrayWnd")` — restore için saklanır. (25H2'de `ABM_GETSTATE`
   güvenilmez: auto-hide açıkken de 0 döner.)
2. **Gizleme:** `ShowWindow` + `SWP_HIDEWINDOW` (`FindWindow("Shell_TrayWnd")` ve
`FindWindowEx` ile `Shell_SecondaryTrayWnd`; UI thread’de tam `EnumWindows` yok —
    asılı bir pencere AppBar’da wait cursor bırakır). **(Tarihsel: `ABM_SETSTATE` eskiden
    edge-reveal kaygısıyla kullanılmıyordu — 2026-08-30'da şerit çözümünün kendisi oldu.)**

   **25H2 gerçeği (2026-08-29 ölçüm, 2026-09-30 tekrar doğrulandı — Win11 25H2 build 26200.9457):**
   `SW_HIDE` tek başına AppBar rezervasyonunu bırakır (~48px hayalet şerit) ve
   `SPI_SETWORKAREA` ile genişletme **çalışmaz** (ölçüldü: `TRUE` dönüyor, work area
   değişmiyor; bazı çağrı biçimleri ayrıca `ERROR_INVALID_PARAMETER (87)` veriyor).

   **Çözüm — native auto-hide state (ADR-005 notu 5, çalıştığı doğrulandı):** gizleme akışı
   `ABM_SETSTATE(ABS_AUTOHIDE)` verir ve çalışma süresince o state'te kalır. Explorer görev
   çubuğunu ~2px'lik kenar kıskacına park eder ve **alt rezervasyonu work-area'dan çıkarır**.
   WharfBar çalışırken ölçülen tablo:

   | | `ABM_GETSTATE` | `Shell_TrayWnd` görünür | tray top | `SPI_GETWORKAREA.Bottom` | alt boşluk |
   |---|---|---|---|---|---|
   | uygulama kapalı | 0 | True | 1032 | 1032 | 48 |
   | **uygulama çalışırken** | **1** | **False** | **1078** (park) | **1080** | **0** |

   → maximize pencereler alt şeridi **tam ekran** kaplar, hiçbir pencereye müdahale edilmez
   (maximize telafisi `SetWindowPos` ile denendi ve 7 ms'lik savaş ürettiği için söküldü).
   Aynı ölçümde 9 child'ın hepsi gizli (`DesktopWindowContentBridge` dahil) → XAML hayaleti yok.

   > **Ölçüm tuzağı (2026-09-30):** `ABM_SETSTATE`/`ABM_SETPOS` **yalnızca kayıtlı bir AppBar**
   > çağırdığında etkilidir. Kayıt dışı bir süreçten çağrılırsa `TRUE` döner ama hiçbir şey
   > olmaz — bu, "API ölü" yanlış sonucuna yol açar. Doğrulama WharfBar çalışırken yapılır.

   Tray aynası parklı tray'de
    okunamaz (XAML içeriği virtualize): desktop-root UIA okumaları özel STA dispatcher
    thread'inden yapılır; tazeleme/tıklama maximize yokken ~0.5 sn'lik state blip'i
    ister; boş okuma aynayı temizlemez (son iyi snapshot donuk kalır). Restore,
    `SetState(baseline)` + `SetTrayVisible(true)`; zorla kill sonrası stale watchdog
    dosyası baseline state'i geri yükler.
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
   `EnsureWorkAreaExpanded` — **25H2+’ta `WorkAreaExpandPolicy` devre kesicisi
   araya girer:** arka arkaya 2 kez `ERROR_INVALID_PARAMETER` alan çağrı 10 dk
   boyunca atlanır (ölü API’ye saniyede bir vurmak yerine tek prob), 10 dk sonunda
   tek prob ile yeniden denenir; `TaskbarCreated` (shell restart) kesiciyi sıfırlar.
   `SPI_SETWORKAREA` (`SPIF_SENDCHANGE`) her üst düzey pencereye broadcast yapar ve
   sağlıklı shell’lerde ~1 sn bloklar — UI thread’i donmaması için çağrı arka plan
   thread’e kaydırıldı (guard: aynı anda tek genişletme; restore ile yarışan
   genişletme baseline’a geri alınır).
   `Shell_NotifyIcon` sonrası ve `TaskbarCreated` için `ReapplyHide`.
5. **Geri yükleme:** `ShowWindow(SW_SHOW)` + `ABM_SETSTATE(baseline)` +
   `SPI_SETWORKAREA(baseline work area)` (25H2+’ta SPI adımı devre kesicisiyle
   atlanır; tray görünür olunca explorer şeridi kendi onaylatır); izleme durur.
6. **Çıkış sırası:** görev çubuğu restore → `ABM_REMOVE` (`TopBarWindow.ReleaseShellState`).
7. **`TaskbarCreated`:** AppBar yeniden kayıt + `ReapplyHide()`.

Çoklu monitör: work area genişletmesi her monitör için o monitöre göre hedeflenir
(25H2+’ta API ölü — yukarıda); dock rezervasyonu `SetDockReservation(Monitor, heightPx)`
ile monitör bazında (bkz. boundaries "Yap").

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

**Hover ve pencere boyutu:** canlı ölçek **1**. Hover düğmenin içini doldurur (köşe 4 DIP).
Çalışan işareti düğmenin içinde **8×3 DIP** hap, alttan 3 DIP. `DockMagnification` /
`DockStripLayout` saf ve testli kalır; `dock.magnification` yüklenir, uygulanmaz.
Bölüm ayracı **yoktur**.

Pencere `DockPlateMetrics.PlateWidth` / `PlateHeight` kadardır (boş dock bir düğme bütçeler).
Hover sırasında `SetWindowPos` **çağrılmaz**. ScrollViewer yoktur; çok sayıda ikon ekran
genişliğine kırpılır.

**Dock yüzeyi:** `dock.backgroundMode`. `mica` (varsayılan): pencere levhadır, gerçek DWM Mica,
`DWMWCP_ROUND`, `AllowsTransparency=false`. `transparent` ve `tint`: pencere yeniden kurulur,
katmanlıdır, köşe WPF'te 14 DIP; tint dolgusu koyu `#202020` / açık `#FAFAFA`, alpha =
`tintOpacity`/100. Eski `dock.surface` (`mica` ve `acrylic`) yüklemede `mica` moduna iner.
Mica ile `AllowsTransparency` birlikte değildir (`docs/04-gotchas.md`).

**Sürükleme ve bırakma:** Sıralama için dock sürüklemesi `DoDragDrop(StringFormat)` yolunu,
dosya ekleme ise `DataFormats.FileDrop` yolunu kullanır — biri `Move`, diğeri `Copy` efektidir,
bu yüzden `DragOver` dosya bırakmasını hücrede **işaretlemez** (`Handled = false`), yüzey
handler'ına bubble eder. Sürükleyerek sıralama **yalnızca sabit (`Pinned`) kutucuklarda**
çalışır; sürükleme yükü `DockItemStore.Normalize(item.Path)` ile **yol** tabanlıdır, `Id` ile
değil (pinli-ve-çalışan kutucuğun `Id`'si çalışan uygulamadan gelir). **OLE riski (R1) pencere
düzeyindedir ve sıralamayı da kapsar:** `RegisterDragDrop` HWND başınadır; katmanlı
(`WS_EX_LAYERED`) `Topmost` pencere hedef olarak kabul edilmezse bu pencerede **hiçbir**
`DragOver`/`Drop` gelmez — Explorer'dan dosya bırakma de, uygulama içi sıralama da düşer.
Yedek yalnızca **dosya bırakmada** vardır (ayar listesindeki “ekle”); sıralamanın yedeği yoktur,
bu yüzden kabul canlı **ölçülmeli** ve elle doğrulamada önce sıralama denenmelidir. Kök `Grid`
bırakma hedefidir ama **arka planı yoktur** — pencere en geniş büyütülmüş genişliğe göre
boyutlandığı için arka plan ~57 DIP hayalet hit alanı eklerdi.

---

## h) DPI ve ekran değişimi

- Manifest: Per-Monitor V2
- `WM_DPICHANGED` ve display change bildirimlerini dinle; AppBar `QUERYPOS`/`SETPOS` ve pencere konumunu güncelle
- Handler’lar `TopBarWindow` / `AppBar` katmanında toplanmalı
- **Konumu WPF’in DIP tabanlı `Left/Top/Width/Height`’ı ile hesaplama** — bunlar birincil monitörün
  ölçeğini varsayar. `GetDpiForWindow` + `GetMonitorInfo` ile fiziksel piksel hesapla, `SetWindowPos`
  ile uygula ([ADR-003](decisions/003-topbar-placement-and-appbar-lifecycle.md)).
- Dock (`DockWindow`) `MonitorInfo.Dpi` değerini yapılandırma anında alır ve `WM_DPICHANGED`
  `wParam` üst kelimesiyle tazeler (`_monitorDpi`); `wParam` sıfır dönerse `MonitorInfo.Dpi`’ye
  düşer. Yerleşim ve edge probe yalnızca bu alanı okur — `MonitorInfo` kopyasını doğrudan okumaz.
  Ölçek değişimi strip’i de yeniden kurar: ikon rengi `AppIconService` önbelleğinde boyut anahtarıyla
  tutulduğu için eski rasterdan büyütülmez.

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

### user32.dll / kernel32.dll — pano (clipboard) — Island

Belgelenmiş pano API'si; uygulama: `Native/ClipboardNative.cs` + `Services/ClipboardMessageWindow.cs`
(ADR-010).

```csharp
// user32.dll
OpenClipboard(IntPtr hWndNewOwner) / CloseClipboard()      // OpenClipboard bloklayabilir
EmptyClipboard()                                           // önceki kopyayı GERİ DÖNÜŞSÜZ siler
IsClipboardFormatAvailable(uint format)
GetClipboardData(uint format)                              // CF_UNICODETEXT = 13
SetClipboardData(uint format, IntPtr hMem)                 // başarıda belleği DEVRALIR
AddClipboardFormatListener(IntPtr hwnd)
RemoveClipboardFormatListener(IntPtr hwnd)
RegisterClipboardFormatW(string name)                      // "CanIncludeInClipboardHistory"
GetClipboardOwner()                                        // pano sahibi HWND (yoksa IntPtr.Zero)
GetWindowThreadProcessId(IntPtr hwnd, out uint pid)
// WM_CLIPBOARDUPDATE = 0x031D (mesaj-only HwndSource + AddHook)

// kernel32.dll — SetClipboardData'ya verilen blok
GlobalAlloc(uint flags, IntPtr bytes)   // GMEM_MOVEABLE = 0x0002
GlobalLock(IntPtr mem) / GlobalUnlock(IntPtr mem) / GlobalSize(IntPtr mem) / GlobalFree(IntPtr mem)
```

- **Okuma STA thread'inde olmalıdır.** `OpenClipboard` başka bir süreç kilitlediyse bloklar;
  UI thread'inde yapılırsa tüm bar donar. Depoda `System.Windows.Clipboard` **kullanılmaz**
  (UI thread'ine bağlıdır) — `IslandService` kendi STA dispatcher'ını kurar, `TrayMirrorService`
  kalıbı. Kilitliyse birkaç kez kısa bekleyip olayı sessizce düşürür.
- **Sahiplik:** `OpenClipboard(NULL)` + `EmptyClipboard()` çağrısı pano sahibini `NULL` yapar —
  önceki sahip (kaynak uygulama) artık sahip değildir. `EmptyClipboard` kullanıcının önceki
  kopyasını **dönüşsüz** siler; bu yüzden bloklar `EmptyClipboard`'den **önce** ayrılıp doldurulur
  ve sonraki her hata hem `GlobalFree` **hem de log** gerektirir. `SetClipboardData` başarıda
  belleği devralır → o noktadan sonra `GlobalFree` **yapılmaz** (başarısızlıkta çağıran sorumludur).
- **Opt-out sinyali:** Windows'un kendi `Win+V` geçmişi `CanIncludeInClipboardHistory` formatını
  kullanır; DWORD `0` = "bu içeriği geçmişe alma". Okuma yolu DWORD'u 0 ise kayıt almaz, format
  id'i `RegisterClipboardFormatW` ile **alınamıyorsa** da kayıt almaz (fail-closed). Yazma yolu
  (`TryWriteText`) aynı işareti **birlikte yazar** — yoksa kendi `WM_CLIPBOARDUPDATE`'imiz kendi
  kaydımızı geri yakalar. Blok 4 bayttan kısaysa okunmaz ve 0 sayılır (taşma okuma yok).
- `CF_UNICODETEXT` **null sonlu UTF-16**'dır; UTF-8 yazılırsa geri okuma bozuk karakter üretir.
- `QueryFullProcessImageName` (kernel32) kanonik bildirim olarak `NativeMethods`'ta zaten var;
  pano sahibi HWND'sinden süreç adı çözümünde yeniden kullanılır (`GetClipboardOwner` →
  `GetWindowThreadProcessId` → `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` →
  `QueryFullProcessImageName` → dosya adı). Elevated / başka oturum sahipleri için `OpenProcess`
  `ERROR_ACCESS_DENIED` verir — normal, kaynak uygulama alanı boş kalır.

> **Tuzak — `RemoveClipboardFormatListener` idempotent DEĞİLDİR (ölçüldü, 5/5 deneme).**
> Kayıt varken ilk `Remove` `TRUE` döner; ikinci ve sonrakiler `FALSE` + `0x57`
> (`ERROR_INVALID_WINDOW`). Hiç eklenmemiş bir pencere de `0x57` verir. Yani bir güvenli çağrı
> ikinci bir çağrıyla **hataya dönüşür**. Yalnızca kayıtlıyken ve bir kez çağrılmalıdır;
> `ClipboardMessageWindow._disposed` bayrağı bunu garanti eder.

WPF tarafında sürükle-bırak OLE bırakma hedefiyle çalışır (`AllowDrop=True` + `DragOver`/`Drop`);
bu, projede **ilk** `AllowDrop` kullanımı ve kayıtlı bir **AppBar** penceresinin bırakma hedefi
olarak kabulü **canlı ölçülmedi** (bkz. `docs/04-gotchas.md`).

### wlanapi.dll — Wi-Fi listesi / bağlanma (Quick Settings)

- `WlanOpenHandle(2, NULL, &ver, &handle)` → `ERROR_SUCCESS`
- `WlanEnumInterfaces(handle, NULL, &list)` — WLAN_INTERFACE_INFO_LIST (InterfaceGuid)
- `WlanScan(handle, &guid, NULL, NULL, NULL)` — taramayı tetikler (sonuç asenkron; sonraki
  GetAvailableNetworkList çağrılarının cache'inde görünür). Tetiklenmezse bayat cache listelenir.
- `WlanGetAvailableNetworkList(handle, &guid, 0, NULL, &list)` — WLAN_AVAILABLE_NETWORK_LIST
  (dwFlags & CONNECTED/HAS_PROFILE, wlanSignalQuality 0-100, bSecurityEnabled).
  **Tuzak:** Struct native layout'a birebir uymalı (628 bayt, `strProfileName` ilk alan,
  `dwFlags`+`dwReserved` sonda, RSSI alanı yoktur) — `WlanStructLayoutTests` bunu kilitler.
  Sapması SSID metnini kaydırıp sinyali bozuyordu. Boş SSID = gizli ağ.
- `WlanSetProfile(handle, &guid, 0, xml, NULL, TRUE, NULL, &reason)` — kalıcı profil
- `WlanConnect(handle, &guid, &params)` — WLAN_CONNECTION_PARAMETERS (mode=profile)
- `WlanQueryInterface(handle, &guid, wlan_intf_opcode_current_connection, ...)` — mevcut SSID
- `WlanFreeMemory` / `WlanCloseHandle`

### user32.dll — display topology (Quick Settings / Yansıt)

- `QueryDisplayConfig(QDC_DATABASE_CURRENT, 0, NULL, 0, NULL, &topologyId)` — mevcut topoloji.
  **Tuzak:** Hibrit-GPU makinelerde (NVIDIA + AMD iGPU) ERROR_INVALID_PARAMETER (87) döner;
  mod tahmini GetSystemMetrics geometrisinden yapılır (ProjectionService.GuessCurrentMode).
  DISPLAYCONFIG_TOPOLOGY_ID değerleri 1, 2, 4, 8'dir (ardışık değil — wingdi.h).
- `SetDisplayConfig(0, NULL, 0, NULL, SDC_TOPOLOGY_x | SDC_APPLY)` — topoloji değiştirmenin
  kanıtlanmış tek güvenilir kombinasyonu. Ekstra flag'ler (SDC_USE_SUPPLIED_DISPLAY_CONFIG,
  SDC_ALLOW_CHANGES, SDC_SAVE_TO_DATABASE) hibrit-GPU'da 87 döndürüyor; SDC_APPLY +
  SDC_VALIDATE birlikte de geçersizdir.

### user32.dll / powrprof.dll — güç eylemleri (Windows logo menüsü)

- `LockWorkStation()` (user32) — oturumu kilitle
- `SetSuspendState(FALSE, FALSE, FALSE)` (powrprof) — uyku
- `shutdown.exe /s|/r /t 0` — kapat / yeniden başlat (P/Invoke değil, `Process.Start`)
