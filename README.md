# WharfBar

Windows için macOS tarzı **üst bar** (saat, durum göstergeleri) ve **dock** (sabitlenmiş uygulamalar).

Öncelik: düşük kaynak tüketimi → Windows Fluent native görünüm → basitlik. macOS marka/ikon dili yok.

> Durum: Geliştirme aşaması. **Faz 1–9 + Parlaklık Kontrolü + Dock Bağlam Menüsü tamamlandı** — üst bar AppBar + Mica,
> dock overlay (sağ tık bağlam menüsü: Başlık kartı, Dock'a sabitle/kaldır, Görevi sonlandır, Pencereyi kapat; yatay kaydırma desteği), canlı görev çubuğu / tepsi entegrasyonu (ADR-005), Hızlı Ayarlar flyout (ADR-006),
> **Monitör Parlaklık Kontrolü** (Monitorian tarzı DDC/CI + WMI hibrit), kalıcı JSON config (`%AppData%/WharfBar/config.json`),
> Startup klasörü autostart, saat / ağ / pil / ses / parlaklık göstergeleri, tepsi Quit menüsü.

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
> `dotnet run` konsolunu kapatmak) **görev çubuğu watchdog alt süreci tarafından ~0.5 sn içinde
> geri getirilir**; yalnızca rezerve AppBar şeridi asılı kalabilir — onun düzeltmesi Görev
> Yöneticisi'nden **Windows Gezgini'ni yeniden başlatmak**tır. Normal kapatmada (tepsi Quit /
> Alt+F4) her ikisi de geri gelir. `run.bat` kullan; ham `dotnet run` konsolunu kapatma.

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
  Views/     TopBarWindow, DockWindow, QuickSettingsFlyout, BrightnessFlyout, AboutWindow, SettingsWindow
  Native/    AppBar, Dwm, NativeMethods, Display, ShellIcon, PowerStatus, NotifyIconHost, MonitorNative, …
  Models/    AppConfig, TopBarStatusItemIds, PinnedApp, DockAppItem, TopBarBackgroundModes
  Services/  ConfigService, StatusService, MonitorBrightnessService, VolumeService, AudioMixerService, …
  app.manifest                — Per-Monitor V2 DPI
```

Ayrıntı: [`docs/01-architecture.md`](docs/01-architecture.md)

## Stack

| Katman | Teknoloji |
|--------|-----------|
| Runtime | .NET 8 (`net8.0-windows10.0.19041.0`) |
| UI | WPF + [WPF-UI](https://github.com/lepoco/wpfui) |
| Native | Win32 P/Invoke (AppBar, DWM, shell ikonları, DDC/CI Monitor Configuration, WMI) |

## Hedefler (v1)

- Ekranın üstünde rezerve edilmiş top bar (AppBar)
- Altta overlay dock (Topmost + auto-hide; canlı pin ve çalışan uygulamalar; sağ tık bağlam menüsü; tam ekran genişliği & yatay kaydırma)
- Monitör parlaklık kontrolü (Monitorian tarzı DDC/CI + laptop WMI hibrit uçan pencere)
- Win11 tarzı Hızlı Ayarlar flyout (ses karıştırıcı, ağ durumu, sistem ayarları)
- Sabitlenmiş ve çalışan masaüstü uygulamaları
- JSON config (`%AppData%/WharfBar/`) ve görsel ayarlar arabirimi
- Startup klasörü ile otomatik başlatma
- Per-Monitor V2 DPI awareness
- Tek örnek (Mutex)

## Non-goals (v1)

- Çoklu monitör AppBar rezervasyonu
- UWP / Microsoft Store uygulama ikonları
- Gerçek sistem tepsisi ikonlarını okuma (Wi-Fi/pil vb. — .NET API ile simülasyon)

## Önemli uyarılar

1. **Tray:** `Shell_NotifyIcon` kendi tepsi ikonun içindir; sistem Wi-Fi/pil ikonlarını okumaz.
2. **Dock:** macOS dock gibi overlay’dir; üst bar için AppBar, dock için Topmost + auto-hide tercih edilir.
3. **Mica:** Önce WPF-UI backdrop; ham DWM Mica ile çakışmadan kaçın. `AllowsTransparency=true` Mica’yı bozar.
4. **AppBar:** Çıkışta mutlaka `ABM_REMOVE`; explorer çöküşünden sonra yeniden kayıt planlanmalı.
5. **Autostart:** Registry `Run` yerine **Startup klasörü** tercih edilir.
6. **Parlaklık:** DDC/CI (masaüstü) ve WMI (laptop) komutları 50ms debounced arka plan kuyruğuyla yürütülür; WPF UI thread kilitlenmez.

Ayrıntılar: [`docs/`](docs/) · Agent bağlamı: [`AGENTS.md`](AGENTS.md) · [`.agent/`](.agent/)

## Geliştirme sırası (özet)

1. ✅ İskelet pencere (pozisyon/boyut, DPI, Mutex)
2. ✅ Top bar AppBar (`ABM_REMOVE` garantisi, callback’ler, yeniden kayıt)
3. ✅ Görsellik (WPF-UI Mica, sistem teması, kare köşeler — ADR-004)
4. ✅ Dock overlay + pinned apps
5. ✅ Config
6. ✅ Autostart (Startup klasörü)
7. ✅ Durum göstergeleri / saat (+ tepsi Quit)
8. ✅ Dock auto-hide / hit-test polish
9. ✅ Shell yerine geçme / Canlı Dock (ADR-005)
10. ✅ Hızlı ayarlar flyout (ADR-006)
11. ✅ Monitör parlaklık kontrolü (Monitorian entegrasyonu)

Tam sıra ve yapılacaklar: [`docs/03-development-order.md`](docs/03-development-order.md) ·
[`.agent/context/roadmap.md`](.agent/context/roadmap.md)

## Lisans

[MIT](LICENSE) (WPF-UI de MIT).
