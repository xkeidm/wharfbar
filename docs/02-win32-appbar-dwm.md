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

AppBar kaydında `uCallbackMessage` ile özel bir window message tanımla. Gelen `ABN_*` bildirimlerini işle (ör. `ABN_POSCHANGED`, `ABN_FULLSCREENAPP`, `ABN_WINDOWARRANGE`). Fullscreen gizleme ve yeniden konumlandırma buradan bağlanır.

### Çoklu monitör (v1 sınırı)

`SHAppBarMessage` tarihsel olarak **birincil monitör** ile daha uyumludur. İkincil monitör rezervasyonu güvenilir olmayabilir.

**v1 kararı:** Yalnızca birincil monitör. İleride yedek: `SystemParametersInfo(SPI_SETWORKAREA)` ile manuel work area (dikkatli kullan; diğer shell bileşenleriyle çakışabilir).

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
- Kendi ikon setini kullan (WPF-UI Fluent `SymbolIcon`)
- İsteğe bağlı: WharfBar’ın kendi `NotifyIcon`’u — uygulama: `TrayIconService` + `NotifyIconHost` (Quit menüsü); sistem ikon okuma değildir
- **Tepsi hibrit (ADR-005):** üçüncü taraf ikonlar çizilmez; tek düğme native Windows tray overflow’u açar (`Win+B` / chevron). `ReadProcessMemory` / belgelenmemiş enumeration yok.

**Uygulanan (Faz 7 + 9c):** saat, ağ, pil; overflow düğmesi (`TrayOverflowInvoker` → `SendInputKeys.TrySendWinB()`). Ses göstergesi yok.

---

## d) Uygulama ikonu (dock)

```csharp
SHGetFileInfo(..., SHGFI_ICON | SHGFI_LARGEICON)
// HICON → Imaging.CreateBitmapSourceFromHIcon → ImageSource
```

Klasik `.exe` / `.lnk` için uygundur. **UWP/Store** paketleri farklı yol ister (AppxManifest) —
**v1 tam destek yok**; çalışan UWP için placeholder kabul (ADR-005); kırık yarım yol sunulmaz.

---

## e) Görev çubuğu gizleme / geri yükleme (ADR-005)

WharfBar çalışırken birincil monitör görev çubuğu gizlenir; başlatmada durum yakalanır,
çıkış/crash yollarında geri yüklenir (`ABM_REMOVE` disiplini). Uygulama: `TaskbarVisibilityService`
+ `Native/TaskbarNative.cs`.

**Seçilen hide yolu (plan):**

1. **Baseline:** `ABM_GETSTATE` (auto-hide bayrağı) + `FindWindow("Shell_TrayWnd")` +
   `IsWindowVisible` — restore için saklanır.
2. **Gizleme:** `ShowWindow(Shell_TrayWnd, SW_HIDE)` — birincil görev çubuğu penceresini gizler.
3. **Geri yükleme:** önce `ShowWindow` ile kayıtlı görünürlük, ardından `ABM_SETSTATE` ile kayıtlı
   auto-hide durumu.
4. **Çıkış sırası:** görev çubuğu restore → `ABM_REMOVE` (`TopBarWindow.ReleaseShellState`).
5. **`TaskbarCreated`:** AppBar yeniden kayıt + `ReapplyHide()`.

Zorla kill restore’u atlayabilir → [`04-gotchas.md`](04-gotchas.md).

---

## f) Çalışan uygulama listesi (dock)

`EnumWindows` ile üst düzey görünür pencereler taranır; `Shell_TrayWnd`, `Progman`, `WorkerW` ve
kendi sürecimiz hariç tutulur (`Native/WindowEnumeration.cs`). Gruplama için
`GetCurrentProcessExplicitAppUserModelID` (`AppUserModelId.cs`). WharfBar kendi pencereleri
(`ShowInTaskbar=false`) için `Process.MainWindowHandle` güvenilmez — odaklama
`WindowActivationService` + `EnumWindows` + PID filtresi ile yapılır.

---

## g) DPI ve ekran değişimi

- Manifest: Per-Monitor V2
- `WM_DPICHANGED` ve display change bildirimlerini dinle; AppBar `QUERYPOS`/`SETPOS` ve pencere konumunu güncelle
- Handler’lar `TopBarWindow` / `AppBar` katmanında toplanmalı
- **Konumu WPF’in DIP tabanlı `Left/Top/Width/Height`’ı ile hesaplama** — bunlar birincil monitörün
  ölçeğini varsayar. `GetDpiForWindow` + `GetMonitorInfo` ile fiziksel piksel hesapla, `SetWindowPos`
  ile uygula ([ADR-003](decisions/003-topbar-placement-and-appbar-lifecycle.md)).

---

## h) Hit-testing (dock / overlay)

Overlay dock’ta mouse’un bar dışına geçmesi, auto-hide ve “tıklama altındaki pencereye gitsin mi?” davranışı için hit-test / `WS_EX_TRANSPARENT` veya benzeri stratejiler planlanmalı. AppBar kullanan top bar’da work area zaten ayrıldığı için hit-test ihtiyacı farklıdır.

---

## i) AllowUnsafeBlocks

Temel `[DllImport]` P/Invoke için `AllowUnsafeBlocks` **gerekmez**. Yalnızca pointer arithmetic veya `unsafe` blok gerçekten yazılırsa aç.
