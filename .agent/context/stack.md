# Stack

Öncelik: düşük kaynak > Fluent native görünüm > basitlik. WPF seçimi Flutter/Electron’a göre
RAM/başlangıç maliyeti içindir (gerekçe: [`../../docs/01-architecture.md`](../../docs/01-architecture.md)).

## Runtime / UI

| Parça | Seçim | Not |
|-------|--------|-----|
| Framework | .NET 8 | `net8.0-windows10.0.19041.0` |
| UI | WPF | `UseWPF` — gömülü Chromium/Skia yok |
| Tasarım sistemi | WPF-UI | Fluent; backdrop önce burada |
| DPI | Per-Monitor V2 | `app.manifest` |
| Unsafe | Kapalı (varsayılan) | Temel P/Invoke için gerekmez |
| Lisans | MIT | WPF-UI de MIT |

## Native (P/Invoke)

| API | Kullanım |
|-----|----------|
| `SHAppBarMessage` | Top bar AppBar |
| `DwmSetWindowAttribute` / `DwmExtendFrameIntoClientArea` | Yedek görsellik |
| `SHGetFileInfo` | Dock ikonları |
| `Shell_NotifyIcon` | Kendi NotifyIcon (`TrayIconService` / Quit menüsü) |

## .NET API’ler (simülasyon / sistem)

- Saat / kültür
- `NetworkInformation` (bağlantı)
- Güç / pil API’leri
- `Process.Start` / shell execute (launcher)

## Kalıcılık / OS entegrasyonu

- Config: JSON under `%AppData%/WharfBar/`
- Autostart: **Startup klasörü** kısayolu
- Single-instance: named **Mutex**

## Bilinçli dışarıda (v1)

- UWP ApplicationModel / AppxManifest ikon yolu
- Çoklu monitör AppBar
- Görev çubuğu manipülasyonu
- macOS global menü / Apple marka öğeleri
- Flutter / Electron (kaynak maliyeti)
