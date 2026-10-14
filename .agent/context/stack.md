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

- Config: JSON under `%AppData%/WharfBar/` (`config.json`; yerel eklenti klasörü `plugins/`)
- Autostart: **Startup klasörü** kısayolu
- Single-instance: named **Mutex**

## Dock kodu (2026-10-04 yeniden tasarımı)

| Dosya | Rol |
|-------|-----|
| `Services/DockMagnification.cs` + `Services/DockStripLayout.cs` | Saf büyütme/yerleşim matematiği (WPF tipi yok, testli) |
| `Services/DockItemStore.cs` | Bağımsız sabit liste (`dock.items`; `App` tek örneği) |
| `Services/WindowCensus.cs` | Tüm dock'lar için tek pencere sayımı (3 sn, pid yol önbelleği) |
| `Services/MinimizedWindowsSource.cs` | Küçültülmüş pencere tablosu (`WindowCensus` anlık görüntüsü) |
| `Services/DockStripBuilder.cs` | Bölüm ayraç slotları (1 DIP) |
| `Services/DockPlateMetrics.cs` | Levha geometrisi (düğme, aralık, pay, yükseklik/genişlik) |
| `Views/Controls/DockStrip.cs` | Şerit `Panel` — canlı ölçek 1; matematik `DockStripLayout`'da |
| `Views/Controls/DockItemCell.xaml` | Tek hücre: ikon + çalışan çizgisi \| ayraç |

## Bilinçli dışarıda (v1)

- UWP ApplicationModel / AppxManifest ikon yolu
- Çoklu monitör AppBar
- Görev çubuğu **pinlerini** okuma/yazma (dock'un tek listesi kendi `dock.items`'i —
  ADR-005 revizyonu). Görev çubuğunu **gizleme/geri yükleme** ise kapsamdadır (ADR-005 §3).
- macOS global menü / Apple marka öğeleri
- Flutter / Electron (kaynak maliyeti)
