# Ekran: Dock

`src/WharfBar/Views/DockWindow.xaml` (+ `.xaml.cs`).

Alt kenara yaslı **overlay** (`DockWindow`), AppBar değil (ADR-001). Topmost; tam ekran
uygulamada gizlenir; auto-hide + hit-test (`DockAutoHideController` + `DockHitTest`).
Tek sıra, köşeler 8px yuvarlak.

> **Katmanlı pencere:** `AllowsTransparency=True` — strip yüzeyi WPF çizimli (`Surface`
> border fırçası); pencere kenarları (büyütme headroom'u) gerçekten **şeffaf + tıklama
> geçirir**, böylece dock çevresinde görünür bir kutu olmaz. DWM backdrop (Mica/Acrylic)
> kullanılmaz (tüm pencereyi doldurur, kenarlarda kutu yaratır) — yüzey rengi
> `ApplySurfaceFromConfig` ile **tema kaynaklı** fırçadan gelir.

## Yerleşim

- **Sol grup:** sabitlenmiş uygulamalar (canlı görev çubuğu pinlerinden)
- **Sağ grup:** sabitlenmemiş ama çalışan uygulamalar (ayırıcı çizgi ile ayrılır)
- **Çalışan göstergesi:** ikonun altında küçük macOS noktası (`RunningDot`, değişmedi)
- **Hover:** sadece hover'daki ikon hafifçe büyür (`HoverScale` 1.3×, 140ms yumuşak geçiş);
  macOS tipi büyütme (komşu büyümesi / reflow / easing) yok. Pencere **sabit boyutta**
  (taban + küçük hover payı `ComputeMaxWidthPx`/`MaxHeightDip`), hover sırasında
  `SetWindowPos` çalışmaz. Hover etiketi sistem tooltip (değişmedi)
- **Sol tık:** çalışıyorsa pencereyi odakla, değilse başlat
- Varsayılan ikon **28 DIP** (16–36 aralığı), aralık 12 DIP, bar yüksekliği ~46 DIP;
  `IShellItemImageFactory` ile DPI-keskin ikonlar
- **Yüzey:** `Dock.Surface` config'iyle seçilir — `Mica` (varsayılan) = opak tema rengi
  (koyu #202020 / açık #FAFAFA); `Acrylic` = aynı tema rengi %80 saydam (cam hissi).
  Renk Windows temasından (sistem aydınlık/karanlık tercihi) gelir. Yüzeyde ince üst ışık
  çizgisi: Surface border 1px üst kenar

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
Simge Boyutu slider'ı (16–36 DIP) + **Yüzey** açılır menüsü (`Dock.Surface`: `Mica` / `Acrylic`).

## Tuzaklar

- Dock kıskacı `Measure` etmez (phantom taskbar work-area itmesine karşı).
- UWP/Store uygulama ikonları v1'de sınırlı (placeholder).
- UWP uygulamaları iki üst düzey pencere üretir (CoreWindow + ApplicationFrameWindow);
  frame penceresi `WindowEnumeration`'da hariç tutulur, aksi halde her UWP app iki kez görünür.
- `C:\Windows\SystemApps\` altındaki sistem yardımcı host'ları (Windows Input Experience /
  TextInputHost vb.) çalışan uygulama listesinde gösterilmez (`RunningAppsSource`).
- Auto-hide: hit-test bölgesi + `FullscreenState` köprüsü ile yönetilir
  (spec: `docs/superpowers/specs/2026-08-10-dock-auto-hide-design.md`).