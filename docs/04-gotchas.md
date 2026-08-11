# 04 — Tuzaklar ve Azaltmalar

| Tuzak | Azaltma |
|-------|---------|
| AppBar kaydı silinmeden crash → asılı work area | `OnClosing` + `Exit` + mümkünse unhandled path’te `ABM_REMOVE`; geliştirmede `explorer.exe` restart |
| Shell/explorer restart → AppBar kaybı | Yeniden kayıt stratejisi (shell mesajı / sağlık kontrolü) |
| `AllowsTransparency=true` + Mica | Asla birlikte kullanma; WPF-UI backdrop veya opak + DWM extend; hızlı ayarlar Popup da opak |
| Hızlı ayarlar / PolicyConfig | Varsayılan ses aygıtı için `IPolicyConfig` (ADR-006); tepsi RPM değil |
| Ses karıştırıcı | Tek seçili çıkış aygıtı + satır içi expander ile varsayılan değiştirme (QS `Popup` içinde iç içe `Popup` kullanma — odak kaçar, liste görünmez); master `IAudioEndpointVolume` + oturum listesi; `IMMDeviceCollection` IID `…8397-CC5392387B5E`; `IAudioSessionEnumerator` ComImport’u IUnknown IID ile sar (resmi IID QI `E_NOINTERFACE` verir, vtable yine enumerator); oturum slider sürüklerken satırları yeniden kurma (poll/`Changed` mid-drag UI’yi yıkıyordu); oturum ikonu süreç yolu yoksa MDL2; UWP ikonları sınırlı (ADR-006) |
| WPF-UI backdrop + ham DWM aynı anda | Önce WPF-UI; ham DWM’yi yedek tut |
| `WindowChrome` / caption + 30 DIP şerit | Caption min-height ~39 px; `WindowStyle=None` + frame extend (ADR-004) |
| `SystemThemeWatcher` istenen backdrop’u ezer | Watcher varsayılan Mica basar; tema mesajlarını `WindowAppearance.Refresh` ile işle |
| `ContextMenu`/`MenuItem` stilini `BasedOn` olmadan yeniden tanımlama | WPF-UI Fluent şablonları düşer; Aero2 açık gri ikon sütunu + koyu menü gövdesi → `SymbolIcon` kaybolur. `App.xaml`’de `UiContextMenu` / `UiMenuItem` / `WpfUiMenuItem` üzerine BasedOn; yalnızca font ayarla |
| Kenar şeritte yuvarlak DWM köşe | DWM dört köşeyi birden yuvarlar; üst bar `DONOTROUND` |
| DPI değişimi / monitör tak-çıkar | Per-Monitor V2 + `WM_DPICHANGED` + display handlers + AppBar yeniden SETPOS |
| Çoklu monitör AppBar | v1: yalnızca birincil; ikincil için ileride SPI_SETWORKAREA değerlendirmesi |
| Sistem / yabancı tepsi ikonlarını okumaya çalışma | Overflow: native `Win+B`/chevron (ADR-005). Deneysel ayna: `TrayIconMirrorService` (ADR-007) — Win11’de UIA `NotifyItemIcon` + stealth peek (DWM cloak + off-screen; görünür flaş yok) / ikon için görünür peek’te `BitBlt` + düğme chrome’unu (koyu kutu / ayırıcı) alpha ile sil; `ToolbarWindow32`/RPM yok. Explorer `HICON` kullanma → AV |
| WPF-UI `SymbolRegular` XAML’de yok | Örn. `Meter24` yok → Ayarlar `GeneralPage` parse patlar, süreç ölür. Paketteki enum adını doğrula (`DataUsage24` vb.) |
| Üst barda yalnızca saat görünmesi | WPF-UI `SymbolIcon` Mica AppBar’da boş kalabiliyor; durum/overflow için Segoe MDL2 `TextBlock` kullan |
| Sol Windows logosu görünmüyor | MDL2 `E782` bazı yüzlerde boş; 4 kare `Rectangle`/`Viewbox` çiz (font bağımsız) |
| Durum tooltip’lerinde □□□□ (tofu) | MDL2 glif `TextBlock` üzerinde string `ToolTip` fontu miras alır; `ToolTip` stilini Segoe UI yap / `SetPlainToolTip` kullan |
| “Gizli simgeler” tıklanınca hiçbir şey olmaması | Görev çubuğu `SW_HIDE` iken Win+B işe yaramaz; `BeginTrayPeek` → overflow aç → `EndTrayPeek` |
| Dock tekrar tıklamada alta almıyor | `LaunchOrActivate` yalnızca activate ediyordu; öndeyse `SW_MINIMIZE`. Dock odağı çalmadan önce `CaptureForegroundHint` (`GetForegroundWindow`) |
| Dock ikonları bulanık / aşırı büyük | `dock.iconSize` varsayılan 28 DIP; `IShellItemImageFactory` ile DPI piksel boyutu iste; 32px `SHGFI_LARGEICON`’u 48+ DIP’e büyütme |
| Dock ekran kenarından yukarıda | Görev çubuğu gizliyken work area boşluğu kalabilir; dock Y için monitör alt kenarını kullan |
| Maximize altta boşluk (dock gizli) | Overlay dock work area rezerve etmez; boşluk gizlenmiş görev çubuğunun AppBar rezervasyonudur — hide sonrası `SPI_SETWORKAREA` (`SPIF_SENDCHANGE`, ini’ye yazma) + AppBar `SETPOS` sonrası `EnsureWorkAreaExpanded` |
| UWP ikonları | v1 tam destek yok; placeholder veya atla; kırık Appx yolu sunma |
| Görev çubuğu gizleme sonrası restore kaçması | Başlatmada `ABM_GETSTATE` + `SPI_GETWORKAREA` yakala; çıkışta **her zaman** `ShowWindow(SW_SHOW)` + `ABM_SETSTATE(baseline)` + baseline `SPI_SETWORKAREA` sonra `ABM_REMOVE`; `IsWindowVisible` auto-hide’ta yanıltır — restore’da kullanma |
| Görev çubuğu hide API seçimi | `ShowWindow(Shell_TrayWnd, SW_HIDE)` gizler; ayrıca `ABS_AUTOHIDE` + `SPI_SETWORKAREA` (alt kenar = monitör) — yalnızca `SW_HIDE` phantom alt şerit bırakır; restore `SW_SHOW` (SHOWNA yetmez) + `ABM_SETSTATE` baseline + work area baseline |
| Zorla kill / konsol kapatma → görev çubuğu gizli kalır | `Stop-Process -Force` restore’u atlar. `dotnet run` konsolunu kapatmak da WPF `OnExit`’i atlayabilir — `SetConsoleCtrlHandler` + `run.bat` artık EXE’yi **ayrık** başlatır. Düzeltme: `explorer.exe` restart veya `ShowWindow(Shell_TrayWnd, SW_SHOW)`. Normal Quit kullan |
| Dock için gereksiz AppBar | Overlay + Topmost (+ auto-hide); ADR-001 — erken notlar “her iki yüzey AppBar” diyordu; geçersiz |
| Erken notta `AllowUnsafeBlocks=true` / `/Windows` | ADR-002: unsafe kapalı; klasör `Views/` |
| İkinci örnek / çift AppBar | Named Mutex |
| Autostart için Registry Run | Startup klasörü tercih |
| `/Windows` klasör adı kafa karışıklığı | `Views/` veya `UI/` kullan |
| `AllowUnsafeBlocks` gereksiz açma | Temel P/Invoke için kapalı bırak; `LibraryImport` yerine `DllImport` |
| WPF DIP `Left/Top` ile konumlandırma | Birincil monitör ölçeğini varsayar; fiziksel piksel + `SetWindowPos` kullan |
| `global.json` ile SDK pinleme | Makinede yalnızca .NET 9 SDK var; 8.x’e pinleme derlemeyi kırar (ADR-002) |
| Kendi `SetWindowPos`’umuz → `ABM_WINDOWPOSCHANGED` → `ABN_POSCHANGED` döngüsü | Yerleşim uygularken bildirimi atla (`_isApplyingPlacement`) |
| `ShowInTaskbar=false` → `Process.MainWindowHandle` **0** | WPF pencereye gizli bir owner verir; `CloseMainWindow()` çalışmaz. Dışarıdan kapatmak için `EnumWindows` + PID filtresi ile `WM_CLOSE` gönder |
| Uygulamayı kapatmanın görünür yolu yoktu | Tepsi ikonu → **Quit WharfBar** (`TrayIconService`); Alt+F4 hâlâ çalışır |
| Fullscreen’de bar üstüne binme | `ABN_FULLSCREENAPP` / hit-test ile gizle |
| Config şemasız JSON | `schemaVersion` + net model (`ConfigService`; uyumsuz → `.bak` + varsayılan) |
| Translucent üst bar / Mica geçişi | `AllowsTransparency` ctor'da; Mica↔translucent = TopBar recreate; recreate'de **taskbar restore yok**, yalnız `ABM_REMOVE`+yeni `ABM_NEW`; Mica+`AllowsTransparency` yasak |
| Translucent bar bembeyaz / opak | (1) XAML `Background={DynamicResource ApplicationBackgroundBrush}` + `SetCurrentValue` → tema `Apply` opak fırçayı geri yazar; yerel `Background = …` ata. (2) WPF-UI `RemoveBackdrop` → `RestoreContentBackground` `CompositionTarget.BackgroundColor = SystemColors.WindowColor` yapar — layered pencerede `HwndSource.CompositionTarget.BackgroundColor = Transparent` de şart. (3) Dock `WindowAppearance.Apply` MainWindow chrome’unu ezer → `MainWindowChromeClobbered` ile üst bar yeniden uygular |

## Geliştirme ipucu

AppBar ile uğraşırken sık crash olursa asılı alan normaldir. Görev Yöneticisi’nden Windows Explorer’ı yeniden başlatmak genelde düzeltir. Kalıcı çözüm yine doğru `ABM_REMOVE` ve yeniden kayıt yoludur.
