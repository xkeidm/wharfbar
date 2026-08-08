# WharfBar — Faz 4 Dock Overlay Tasarımı

**Tarih:** 2026-08-09  
**Durum:** Onaylandı (yaklaşım 1 — ince servisler + tek `DockWindow`)  
**Kapsam:** Overlay dock penceresi, sabit demo pinned apps, hover scale, launcher,
fullscreen gizleme temeli, auto-hide iskeleti (kapalı). Config kalıcılığı **yok** (Faz 5).

## Kararlar (özet)

| Konu | Seçim |
|------|--------|
| Pinned liste | Kod içi sabit demo (`DemoPinnedApps`); JSON yok |
| Görünürlük | Her zaman görünür; auto-hide **iskelet** (`Enabled=false`) |
| Fullscreen / hit-test | Çalışır gizleme + hit-test hazırlığı (auto-hide’a bağlı değil) |
| Görsel | WPF-UI Mica + `DWMWCP_ROUND`; `AllowsTransparency=false` |
| Yerleşim | Birincil monitör **work area** alt kenarı, yatay ortalı; görev çubuğuna binmez |
| Etkileşim | İkon + hover scale + tıkla → launch; running indicator / sağ tık yok |
| Mimari | İnce `Native/` + `Services/` + `Models/` + `Views/DockWindow` |

Dayanak: [ADR-001](../../decisions/001-dock-appbar-vs-overlay.md) (overlay, AppBar yok),
[ADR-004](../../decisions/004-topbar-mica-corners-and-theme.md) (Mica yolu; dock yuvarlak köşe serbest).

## Mimari

```
App.OnStartup
  ├─ TopBarWindow (mevcut; MainWindow; AppBar sahibi)
  └─ DockWindow.Show()

DockWindow
  ├─ WindowAppearance(Mica, Round)
  ├─ Work-area altına fiziksel piksel yerleşim (SetWindowPos)
  ├─ ItemsControl → PinnedApp ikonları
  ├─ Hover ScaleTransform + Storyboard
  └─ Click → ProcessLauncherService

Destek
  Native/ShellIcon
  Services/AppIconService
  Services/ProcessLauncherService
  Services/FullscreenState          ← TopBar yazar; Dock dinler
  Models/PinnedApp
  DemoPinnedApps (sabit liste)
  DockAutoHideController (stub; Enabled=false)
```

- Dock **`SHAppBarMessage` kullanmaz**; çıkışta ek `ABM_REMOVE` yok.
- Yerleşim WPF DIP `Left`/`Top` ile yapılmaz — top bar ile aynı fiziksel piksel kuralı.
- Tema yenileme: `DockWindow` kendi `HwndSource` hook’unda
  `WM_SETTINGCHANGE` / tema mesajları → `WindowAppearance.Refresh()` (top bar’dan bağımsız).

### Fullscreen sinyali

`ABN_FULLSCREENAPP` yalnızca AppBar kaydı olan hwnd’ye gelir (`TopBarWindow`).
Dock kendi AppBar’ı olmadığı için:

1. `Services/FullscreenState` — süreç içi tek örnek; `bool IsFullscreen` + `Changed` event.
2. `TopBarWindow` `ABN_FULLSCREENAPP` içinde durumu günceller.
3. `DockWindow` abone olur → fullscreen iken `Visibility=Collapsed`; bitince `Visible` + Topmost.

Ham alternatif (dock’ta ayrı shell gözlemi) v1’de yok — tek kaynak AppBar callback’i.

## Bileşenler

| Bileşen | Görev |
|---------|--------|
| `Models/PinnedApp` | `Path`, `Args`, `Label` — immutable |
| `DemoPinnedApps` | Sabit dört uygulama: Explorer, Notepad, Calculator, Command Prompt — yollar `%WINDIR%` / System32 |
| `Native/ShellIcon` | `SHGetFileInfo` → `HICON`; destroy çağıranın sorumluluğu |
| `Services/AppIconService` | Path → `ImageSource`; basit cache; eksik dosyada fallback |
| `Services/ProcessLauncherService` | `Process.Start` + `UseShellExecute=true`; UI toast yok |
| `Services/FullscreenState` | Süreç içi `IsFullscreen` + `Changed`; TopBar yazar, Dock dinler |
| `Views/DockWindow` | Overlay UI, yerleşim, hover, tık, fullscreen gizleme, appearance |
| `DockAutoHideController` | `Enabled=false`; `Show` / `Hide` / `OnPointerNear` stub — davranış yok |

**Faz 4 dışı:** `config.json`, pin ekle/çıkar UI, çalışan nokta, görev çubuğu gizleme,
UWP/Store ikonları, gerçek auto-hide animasyonu.

### Görsel sabitler

| Sabit | Değer |
|-------|--------|
| İkon boyutu | 48 DIP |
| Hover scale | ~1.2 |
| Animasyon | ~150–200 ms `Storyboard`; `RenderTransformOrigin=0.5,0.5` |
| Dock yükseklik | 72 DIP (ikon + padding) |
| Köşe | `DwmCornerPreference.Round` |
| Backdrop | `WindowBackdropType.Mica`; başarısızsa opak fallback |

## Veri akışı

1. `App.OnStartup` → mutex → sistem teması → `TopBarWindow.Show()` → `DockWindow.Show()`.
2. `DockWindow.OnSourceInitialized` → hook + appearance + work-area altına yerleştir.
3. `DemoPinnedApps` → her path için `AppIconService.GetIcon` → `ItemsControl`.
4. Tık → `ProcessLauncherService.Launch(path, args)`.
5. `WM_DPICHANGED` / `WM_DISPLAYCHANGE` / work area değişimi → yeniden yerleştir.
6. Fullscreen event → `Visibility=Collapsed`; bitince `Visible` + Topmost.
7. Top bar AppBar yerleşimi work area’yı değiştirebileceği için `FullscreenState` ile aynı
   ince köprü veya `TopBarWindow` sonrası bir `WorkAreaChanged` sinyali dock’u yeniden
   hizalar (en azından ilk Show ve DPI/display mesajlarında zorunlu).

### Hit-test hazırlığı (temel)

- Dock penceresinde ileride auto-hide için kullanılacak kenar-band / hit-test yardımcı
  iskeleti (`Native` veya `DockWindow` private) bulunur.
- Faz 4’te `WS_EX_TRANSPARENT` ile sürekli tıklama geçirme **açılmaz** (dock her zaman
  etkileşimli). Yalnızca API / metod stub’ları ve yorumla sınır çizilir.

## Hata davranışı

| Durum | Davranış |
|-------|----------|
| Path yok / ikon yok | Fallback ikon; label korunur; dock açılır |
| Launch exception | Yutulur / debug log; MessageBox yok; dock açık kalır |
| Backdrop başarısız | Opak `ApplicationBackgroundBrush` fallback |
| DPI / display change | Work area altına yeniden `SetWindowPos` |
| İkinci örnek | Mevcut Mutex; dock açılmaz |

## Yaşam döngüsü / çıkış

- `MainWindow` = `TopBarWindow`. Dock, `App` alanında referans tutulur; `OnExit` veya
  top bar kapanışında dock da kapanır.
- Dock AppBar kaydetmediği için `ABM_REMOVE` yalnızca mevcut top bar yollarından gelir —
  bu fazda bozulmamalı.

## Doğrulama

**Derleme:** `dotnet build WharfBar.sln` → 0 uyarı / 0 hata.

**Manuel:**

- [ ] Dock work area altında, yatay ortalı; Windows görev çubuğunun üstünde (binmeden)
- [ ] Mica + yuvarlak köşe (veya opak fallback); sistem light/dark ile uyumlu
- [ ] Demo ikonlar; hover büyüme; tıklanınca uygulama açılır
- [ ] Overlay: work area dock için **rezerve edilmez** (maximize alt kenarı dock’un altına girebilir — beklenen)
- [ ] Fullscreen → dock gizlenir; çıkınca geri gelir
- [ ] DPI / çözünürlük değişiminde yeniden hizalanma
- [ ] Uygulama kapanınca AppBar work area baseline’a döner
- [ ] Auto-hide kapalı (iskelet var, davranış yok)

## Doküman güncellemesi (uygulama ile birlikte)

Implementation plan çalışırken aynı değişiklik setinde:

- `.agent/context/roadmap.md` — Faz 4 işaretle
- `.agent/project.md` — “Henüz yok: dock” güncelle
- `docs/01-architecture.md` — mevcut klasör ağacına dock dosyaları
- `docs/03-development-order.md` — Faz 4 checklist
- Gerekirse kısa ADR-005 yalnızca köşe/fullscreen köprüsü için anlamlı sapma olursa

## Bilinçli ertelenenler

- Config / `pinnedApps` kalıcılığı (Faz 5)
- Auto-hide animasyonu ve proximity show (iskelet sonrası)
- Running indicator, sağ tık menü
- Çoklu monitör dock
- UWP ikonları
