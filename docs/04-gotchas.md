# 04 — Tuzaklar ve Azaltmalar

| Tuzak | Azaltma |
|-------|---------|
| AppBar kaydı silinmeden crash → asılı work area | `OnClosing` + `Exit` + mümkünse unhandled path’te `ABM_REMOVE`; geliştirmede `explorer.exe` restart |
| Shell/explorer restart → AppBar kaybı | Yeniden kayıt stratejisi (shell mesajı / sağlık kontrolü) |
| `AllowsTransparency=true` + Mica | Asla birlikte kullanma; WPF-UI backdrop veya opak + DWM extend |
| WPF-UI backdrop + ham DWM aynı anda | Önce WPF-UI; ham DWM’yi yedek tut |
| `WindowChrome` / caption + 30 DIP şerit | Caption min-height ~39 px; `WindowStyle=None` + frame extend (ADR-004) |
| `SystemThemeWatcher` istenen backdrop’u ezer | Watcher varsayılan Mica basar; tema mesajlarını `WindowAppearance.Refresh` ile işle |
| Kenar şeritte yuvarlak DWM köşe | DWM dört köşeyi birden yuvarlar; üst bar `DONOTROUND` |
| DPI değişimi / monitör tak-çıkar | Per-Monitor V2 + `WM_DPICHANGED` + display handlers + AppBar yeniden SETPOS |
| Çoklu monitör AppBar | v1: yalnızca birincil; ikincil için ileride SPI_SETWORKAREA değerlendirmesi |
| Sistem / yabancı tepsi ikonlarını okumaya çalışma | Simüle et (.NET API + kendi ikonlar); overflow için native `Win+B`/chevron; `Shell_NotifyIcon` ≠ okuma; RPM yok (ADR-005) |
| UWP ikonları | v1 tam destek yok; placeholder veya atla; kırık Appx yolu sunma |
| Görev çubuğu gizleme sonrası restore kaçması | Başlatmada durumu yakala; `ABM_REMOVE` ile aynı çıkış/crash yollarında geri yükle; idempotent tut (ADR-005) |
| Zorla kill → görev çubuğu gizli kalır | `Stop-Process -Force` restore’u atlar; `explorer.exe` restart; normal Quit kullan |
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

## Geliştirme ipucu

AppBar ile uğraşırken sık crash olursa asılı alan normaldir. Görev Yöneticisi’nden Windows Explorer’ı yeniden başlatmak genelde düzeltir. Kalıcı çözüm yine doğru `ABM_REMOVE` ve yeniden kayıt yoludur.
