# Roadmap

## Şimdi

- Doküman + agent iskeleti (Approach B) ✓
- **Faz 1 — İskelet pencere ✓** — `src/WharfBar/` scaffold, `WharfBar.sln`, WPF-UI paketi,
  PerMonitorV2 manifest, `TopBarWindow` (DPI-farkında fiziksel piksel yerleşimi), Mutex
- **Faz 2 — AppBar ✓** — `Native/AppBar.cs`, NEW → QUERYPOS → SETPOS, idempotent `ABM_REMOVE`,
  `ABN_*` callback’leri, `TaskbarCreated` sonrası yeniden kayıt, fullscreen z-order
- **Faz 3 — Görsellik ✓** — WPF-UI Mica + sistem teması, `DWMWCP_DONOTROUND`,
  `WindowAppearance` / `Native/Dwm.cs`, ADR-004
- **Faz 4 — Dock ✓** — `DockWindow` overlay (Topmost, AppBar yok), 4 demo pin,
  `ShellIcon` / `AppIconService`, tıklayınca `ProcessLauncherService`, `FullscreenState`
  köprüsü, `DockAutoHideController` / `DockHitTest` stub, Mica + yuvarlak köşeler
- Derleme temiz (0 uyarı / 0 hata); **çalışma zamanı betikle doğrulandı** (aşağı bkz.)

## Sonraki

5. Config şeması  
6. Startup autostart  
7. Saat / durum göstergeleri  

## Faz 1–3 doğrulama durumu

Ölçüm ortamı: 1920×1080, **%100 ölçek (96 DPI)**, görev çubuğu altta (48 px).

### Makineyle doğrulandı ✓

`SystemParametersInfo(SPI_GETWORKAREA)` ile ölçülerek, betikle:

| Kontrol | Sonuç |
|---------|-------|
| Work area rezervasyonu | Baseline `T=0` → çalışırken `T=30`; tam olarak beklenen 30 px |
| Bar pencere rect’i | `L=0 T=0 R=1920 B=30` — rezerve şeritle birebir |
| Tek örnek (Mutex) | İkinci süreç kendiliğinden çıktı (kod 0); work area değişmedi |
| Zarif kapanış | `WM_CLOSE` → süreç kod 0 |
| **Work area geri yükleme** | Baseline ile **birebir aynı** |
| Maximize edilmiş pencere | Bar altında kalıyor (Faz 2’de doğrulandı) |

### Faz 3 görsel notları

- Mica / translucency ekran görüntüsünde güvenilir doğrulanamaz (DWM composition + wallpaper);
  attribute okuması + opak-olmayan client ile desteklenir, insan gözü gerekir.
- Light/dark runtime geçişi manuel tema değiştirerek doğrulanmalı.

### Yalnızca insan gözüyle / manuel

- Barın görsel görünümü, hizası, okunabilirliği, Mica dokusu
- DPI / ölçek değişiminde yeniden konumlanma — test makinesi %100 ölçekte
- Monitör tak-çıkar / `WM_DISPLAYCHANGE`
- Tam ekran oyun / video ile `ABN_FULLSCREENAPP` z-order davranışı
- `explorer.exe` yeniden başlatma sonrası `TaskbarCreated` yeniden kaydı (bilinçli atlandı)
- Süreç zorla öldürüldüğünde asılı kalan work area (bilinçli test edilmedi)

Ayrıntılı checklist: [`../../docs/03-development-order.md`](../../docs/03-development-order.md)

## Sonra (v1 sonrası adaylar)

- İkincil monitör / work-area stratejisi  
- UWP/Store ikonları  
- İsteğe bağlı “dock reserves space” (alt AppBar)  
- Daha zengin durum menüleri / bildirim merkezi benzeri UI  

## Bilinçli ertelenenler

- Görev çubuğu gizleme  
- Sistem tepsi ikon okuma (mümkün değil / desteklenmiyor)  
