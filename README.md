# WharfBar

Windows için macOS tarzı **üst bar** (saat, durum göstergeleri) ve **dock** (sabitlenmiş uygulamalar).

> Durum: Geliştirme aşaması. **Faz 1–3 tamamlandı** — üst bar AppBar ile alan rezerve eder;
> WPF-UI Mica + sistem light/dark uygulanır. Dock, config ve durum göstergeleri henüz yok.

## Kurulum ve çalıştırma

Gereken: **.NET SDK 8 veya 9** (Windows). SDK 9 ile `net8.0-windows10.0.19041.0` hedefi sorunsuz
derlenir; depoda bilinçli olarak `global.json` yoktur.

```bash
git clone https://github.com/<kullanıcı>/wharfbar.git
cd wharfbar

dotnet build WharfBar.sln
dotnet run --project src/WharfBar
```

> ⚠️ **Çalıştırmadan önce oku.** Uygulama açıkken ekranın üstünde bir şerit alan rezerve edilir.
> Süreç zorla sonlandırılırsa (`Stop-Process -Force`, hata ayıklayıcıyı koparma) `ABM_REMOVE`
> çalışmaz ve bu alan asılı kalır. Düzeltmesi: Görev Yöneticisi’nden **Windows Gezgini’ni
> yeniden başlat**. Normal kapatmada alan kendiliğinden geri gelir.

İkinci bir örnek başlatılamaz (named Mutex); çalışan örnek varken `dotnet run` sessizce çıkar.

### Nasıl kapatılır

Barın başlık çubuğu ve görev çubuğu girişi yoktur; şu an kapatmanın yolu **bara tıklayıp
Alt+F4**’tür. Kalıcı bir “Çıkış” menüsü Faz 7’de tepsi ikonuyla gelecek.

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
  Views/     TopBarWindow    — üst bar UI + AppBar sahibi
  Native/    AppBar, Dwm, NativeMethods, Display — Win32 P/Invoke
  Services/  SingleInstanceGuard, WindowAppearance
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
4. ⬜ Dock overlay + pinned apps
5. ⬜ Config
6. ⬜ Autostart
7. ⬜ Durum göstergeleri / saat

Tam sıra: [`docs/03-development-order.md`](docs/03-development-order.md)

## Lisans

Bkz. [LICENSE](LICENSE).
