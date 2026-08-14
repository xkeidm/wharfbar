# Ekran: Dock

`src/WharfBar/Views/DockWindow.xaml` (+ `.xaml.cs`).

Alt kenara yaslı **overlay** (`DockWindow`), AppBar değil (ADR-001). Topmost; tam ekran
uygulamada gizlenir; auto-hide + hit-test (`DockAutoHideController` + `DockHitTest`).
Tek sıra, `ScrollViewer` (yatay kaydırma), köşeler 8px yuvarlak.

## Yerleşim

- **Sol grup:** sabitlenmiş uygulamalar (canlı görev çubuğu pinlerinden)
- **Sağ grup:** sabitlenmemiş ama çalışan uygulamalar (ayırıcı çizgi ile ayrılır)
- **Çalışan göstergesi:** ikonun altında küçük nokta (`RunningDot`)
- **Hover:** ikon ölçeklenir; **sol tık:** çalışıyorsa pencereyi odakla, değilse başlat
- Varsayılan ikon **28 DIP** (16–36 aralığı), aralık 12 DIP, bar yüksekliği ~46 DIP;
  `IShellItemImageFactory` ile DPI-keskin ikonlar

## Sağ tık menüsü (`BuildContextMenuForApp`)

1. **Başlık kartı** (ikon + uygulama adı, tıklanamaz)
2. **Dock'a sabitle / Dock'tan kaldır** (pin durumuna göre)
3. **Görevi sonlandır** (yalnızca çalışıyorsa; `proc.Kill` ağaç)
4. **Pencereyi kapat** (yalnızca çalışıyorsa)
5. Yol varsa (ayırıcıdan sonra):
   - **Yönetici olarak çalıştır**
   - **Dosya konumunu aç**
   - **Yeni pencere aç**

## Ayarlar

[Dock sayfası](../screens/settings.md#dock) (Ayarlar penceresi): Otomatik Gizle toggle +
Simge Boyutu slider'ı (16–36 DIP).

## Tuzaklar

- Dock kıskacı `Measure` etmez (phantom taskbar work-area itmesine karşı).
- UWP/Store uygulama ikonları v1'de sınırlı (placeholder).
- Auto-hide: hit-test bölgesi + `FullscreenState` köprüsü ile yönetilir
  (spec: `docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md`).