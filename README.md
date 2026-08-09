# WharfBar

Windows için macOS tarzı **üst bar** (saat, durum göstergeleri) ve **dock** (sabitlenmiş uygulamalar).

Öncelik: düşük kaynak tüketimi → Windows Fluent native görünüm → basitlik. macOS marka/ikon dili yok.

> Durum: Geliştirme aşaması. **Faz 1–7 tamamlandı** — üst bar AppBar + Mica, dock overlay,
> kalıcı JSON config (`%AppData%/WharfBar/config.json`), Startup klasörü autostart,
> saat / ağ / pil göstergeleri, tepsi Quit menüsü.
> Sırada: dock auto-hide / hit-test polish (Faz 8).

## Kurulum ve çalıştırma

Gereken: **.NET SDK 8 veya 9** (Windows). SDK 9 ile `net8.0-windows10.0.19041.0` hedefi sorunsuz
derlenir; depoda bilinçli olarak `global.json` yoktur.

```bash
git clone https://github.com/<kullanıcı>/wharfbar.git
cd wharfbar

dotnet build WharfBar.sln
dotnet run --project src/WharfBar
```

Çift tıklayarak: kökteki `run.bat` (derler, `WharfBar.exe`’yi **ayrık** başlatır — bat
penceresini kapatmak uygulamayı öldürmez).

> ⚠️ **Çalıştırmadan önce oku.** Uygulama açıkken ekranın üstünde bir şerit alan rezerve edilir
> ve Windows görev çubuğu gizlenir. Süreç zorla sonlandırılırsa (`Stop-Process -Force`,
> `dotnet run` konsolunu kapatmak) restore / `ABM_REMOVE` atlanabilir: görev çubuğu veya üst
> work area asılı kalır. Düzeltmesi: Görev Yöneticisi’nden **Windows Gezgini’ni yeniden başlat**.
> Normal kapatmada (tepsi Quit / Alt+F4) her ikisi de geri gelir. `run.bat` kullan; ham
> `dotnet run` konsolunu kapatma.

İkinci bir örnek başlatılamaz (named Mutex); çalışan örnek varken `dotnet run` sessizce çıkar.

### Nasıl kapatılır

Sistem tepsisindeki WharfBar ikonuna tıklayıp **Quit WharfBar** seçin. Alternatif: bara odaklanıp
**Alt+F4**.

`Process.MainWindowHandle` bu pencere için `0` döner (`ShowInTaskbar=false` WPF’te pencereye
gizli bir owner verir), bu yüzden betikten kapatırken `CloseMainWindow()` işe yaramaz — PID’e
göre pencereyi bulup `WM_CLOSE` göndermek gerekir.

### Doğrulanan davranış

1920×1080 @ %100 ölçekte betikle ölçüldü: work area üst kenarı `0` → `30` px iniyor, uygulama
kapanınca **birebir** geri geliyor, maximize edilmiş pencereler barın altında kalıyor, ikinci
örnek kendiliğinden çıkıyor. Ayrıntı: [`.agent/context/roadmap.md`](.agent/context/roadmap.md)

## Proje yapısı

```
WharfBar.sln
src/WharfBar/
  Views/     TopBarWindow (saat/durum), DockWindow
  Native/    AppBar, Dwm, NativeMethods, Display, ShellIcon, PowerStatus, NotifyIconHost
  Models/    AppConfig, PinnedApp, DemoPinnedApps
  Services/  ConfigService, StatusService, TrayIconService, WindowAppearance, …
  app.manifest                — Per-Monitor V2 DPI
```

Ayrıntı: [`docs/01-architecture.md`](docs/01-architecture.md)

## Stack

| Katman | Teknoloji |
|--------|-----------|
| Runtime | .NET 8 (`net8.0-windows10.0.19041.0`) |
| UI | WPF + [WPF-UI](https://github.com/lepoco/wpfui) |
| Native | Win32 P/Invoke (AppBar, DWM, shell ikonları) |

## Hedefler (v1)

- Ekranın üstünde rezerve edilmiş top bar (AppBar)
- Altta overlay dock (Topmost + gerekirse auto-hide; AppBar zorunlu değil)
- Sabitlenmiş klasik masaüstü uygulamaları (`.exe` / `.lnk`)
- JSON config (`%AppData%/WharfBar/`)
- Startup klasörü ile otomatik başlatma
- Per-Monitor V2 DPI awareness
- Tek örnek (Mutex)

## Non-goals (v1)

- Çoklu monitör AppBar rezervasyonu
- UWP / Microsoft Store uygulama ikonları
- Gerçek sistem tepsisi ikonlarını okuma (Wi-Fi/pil vb. — .NET API ile simülasyon)
- Windows görev çubuğunu gizleme veya değiştirme

## Önemli uyarılar

1. **Tray:** `Shell_NotifyIcon` kendi tepsi ikonun içindir; sistem Wi-Fi/pil ikonlarını okumaz.
2. **Dock:** macOS dock gibi overlay olabilir; üst bar için AppBar, dock için Topmost + auto-hide tercih edilir.
3. **Mica:** Önce WPF-UI backdrop; ham DWM Mica ile çakışmadan kaçın. `AllowsTransparency=true` Mica’yı bozar.
4. **AppBar:** Çıkışta mutlaka `ABM_REMOVE`; explorer çöküşünden sonra yeniden kayıt planlanmalı.
5. **Autostart:** Registry `Run` yerine **Startup klasörü** tercih edilir.

Ayrıntılar: [`docs/`](docs/) · Agent bağlamı: [`AGENTS.md`](AGENTS.md) · [`.agent/`](.agent/)

## Geliştirme sırası (özet)

1. ✅ İskelet pencere (pozisyon/boyut, DPI, Mutex)
2. ✅ Top bar AppBar (`ABM_REMOVE` garantisi, callback’ler, yeniden kayıt)
3. ✅ Görsellik (WPF-UI Mica, sistem teması, kare köşeler — ADR-004)
4. ✅ Dock overlay + pinned apps
5. ✅ Config
6. ✅ Autostart (Startup klasörü)
7. ✅ Durum göstergeleri / saat (+ tepsi Quit)
8. ⬜ Dock auto-hide / hit-test polish

Tam sıra ve yapılacaklar: [`docs/03-development-order.md`](docs/03-development-order.md) ·
[`.agent/context/roadmap.md`](.agent/context/roadmap.md)

## Lisans

[MIT](LICENSE) (WPF-UI de MIT).
