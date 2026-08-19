# WharfBar — Renk Kaplaması (Veil): tam ekran renk overlay + QS karosu + kısayol + ayarlar sayfası

**Tarih:** 2026-08-18
**Durum:** Taslak — kullanıcı onayı bekliyor
**Kapsam:** ColorVeil tarzı tam ekran renkli kaplama. Disleksi / Scotopic Sensitivity Syndrome /
görsel stres için ekranın üzerinde yarı saydam renk katmanı; aynı mekanizmayla (siyah + yüksek
opaklık) parlaklık azaltma. Aç/kapa: Hızlı Ayarlar karosu + global kısayol (varsayılan
**Ctrl+F11**, config'te değiştirilebilir). Renk/opaklık/kısayol ayarları yeni **Kaplama**
sayfasında (`SettingsWindow`).

## Özet

ColorVeil (east-tec) ücretsiz bir erişilebilirlik aracıdır: tüm ekranın üzerine ayarlanabilir
renk + opaklıkta yarı saydam bir kaplama koyar, tepsi ikonu veya kısayolla anında açılıp kapanır,
siyah kaplamayla donanım minimumunun altına parlaklık düşürür. WharfBar'a aynı davranış,
mevcut DockWindow deseniyle (katmanlı pencere + click-through) eklenir: tam ekran, Topmost,
etkinleştirilmeyen, `WS_EX_TRANSPARENT` (passthrough) tek renk dolgulu pencere. Yeni bağımlılık
yok (P/Invoke + WPF yeterli). Kaplama her zaman en üstte kalır (tam ekran uygulamalar dahil —
kullanıcı kararı); exclusive fullscreen oyunlar (DWM dışı render) üzerinde görünmez — ColorVeil'de
de aynı sınır. Renk ve opaklık tek mekanizmadır: siyah + yüksek opaklık = parlaklık azaltma.

## Kararlar

| Konu | Seçim |
|------|--------|
| Overlay mekanizması | Katmanlı WPF penceresi (DockWindow deseni): `AllowsTransparency=True`, tek renk dolgu, `Topmost`, `ShowInTaskbar=False`, fiziksel piksel `SetWindowPos` (`HWND_TOPMOST`, `SWP_NOACTIVATE`), `DockHitTest.ApplyPassthrough` ile tıklama geçirgenliği. Magnification API ve gamma ramp reddedildi (belgelenmemiş / admin gerekli / renk uzayı dönüşümü) |
| Tam ekran davranışı | **Her zaman göster** (kullanıcı kararı) — `FullscreenState` aboneliği yok. Sınır: exclusive fullscreen uygulamalarda kaplama görünmez (DWM dışı render) |
| Parlaklık azaltma | Ayrı kontrol yok — tek mekanizma: siyah renk + opaklık kaydırıcısı (kullanıcı kararı) |
| Kontrol noktaları | Hızlı Ayarlar karosu (`Id="veil"`, MDL2 `\uE790` renk glifi, nightlight karosu deseni) + global kısayol `RegisterHotKey` (kullanıcı kararları: karosu + Ctrl+F11) |
| Kısayol varsayılanı | `"Ctrl+F11"` — config'te değiştirilebilir; boş string = kısayol yok; geçersiz string = kayıt yok + ayarlar sayfasında uyarı (config bozulmaz) |
| Kısayol parse | `Native/Hotkey.cs` — `HotkeyParser` string → `(uint modifiers, uint vk)`; yalnızca Ctrl/Alt/Shift/Win + F1–F12 / A–Z / 0–9; **en az bir modifier zorunlu**; büyük/küçük harf duyarsız; geçersiz → `null` |
| Hotkey host | Mesaj-only `HwndSource` (TrayIconService `ParentWindow=-3` deseni) + `AddHook` `WM_HOTKEY` → toggle. Çıkışta `UnregisterHotKey` garantisi |
| Config şeması | `AppConfig.Veil` → `VeilConfig { Enabled, Color, Opacity, HotKey }` — eklenebilir alanlar, `schemaVersion` değişmez |
| Varsayılanlar | `Enabled=false`, `Color="#4A90D9"` (yumuşak mavi), `Opacity=40` (üst bar tint ile tutarlı), `HotKey="Ctrl+F11"` |
| Renk ayarı | Serbest seçim (hex kutusu) + hazır SSS renk paleti (kullanıcı kararı) |
| Opaklık | 0–100 int, config normalize'de clamp (mevcut `ClampOpacity` deseni) |
| Geçiş animasyonu | 250–300 ms yumuşak fade (opaklık `DoubleAnimation`); animasyon sırasında tekrar toggle → yeniden başlar |
| QS tümleşimi | `QuickSettingsService` ctor'una `VeilService`; tile state `VeilService.Changed` olayıyla **anında** güncellenir (poll gerekmez) |
| Kalıcılık | `VeilService.SetEnabled` → config'e yazar (`Config.Save`) — yeniden başlatınca durum korunur |
| Sahiplik | `App.OnStartup` `VeilService`'i kurar (config sonrası); `TopBarWindow` ctor'una verilir (QS kurulumu için); çıkışta `Dispose` (hotkey unregister + pencere) |
| Monitör kapsamı | v1: yalnızca birincil monitör (proje kapsamı); `WM_DISPLAYCHANGE` / DPI değişiminde rect yeniden hesaplanır |

## Mimari

```
App (OnStartup)
  ├─ ConfigService.LoadOrCreate() → config
  ├─ VeilService (config, _config)      # durum, fade, hotkey, Changed; çıkışta Dispose
  │    └─ VeilWindow                    # tam ekran katmanlı pencere (DockWindow deseni)
  │         └─ DockHitTest.ApplyPassthrough
  │         └─ Native/Hotkey.cs (RegisterHotKey)  ← mesaj-only HwndSource
  └─ TopBarWindow(config, veilService)
       └─ QuickSettingsService(nightLight, veilService) → BuildTiles() + ToggleTileAsync("veil")
            └─ QuickSettingsFlyout (tile otomatik render edilir)
```

### `Views/VeilWindow.xaml(.cs)`

- XAML: `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=False`, `Topmost=True`,
  `WindowStartupLocation=Manual`, `Background=Transparent`, `AllowsTransparency=True`
  (ctor'da, `InitializeComponent` öncesi — üst bar kuralı). `AllowsTransparency` + Mica yasağı
  etkilenmez: düz renk dolgu, Mica yok.
- İçerik: `Border` tek dolgu (renk `Color` + opaklık animasyonu).
- Kod: `ApplyRect()` — birincil monitör fiziksel piksel sınırları (`Native/Display.cs`) →
  `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`; `WM_DISPLAYCHANGE` / `WM_DPICHANGED` → yeniden
  `ApplyRect()`.
- `SetVisible(bool, animate)` — 250–300 ms opaklık fade; animasyon biter/kesilir → `Show()`/`Hide()`.
- Tıklama geçirgenliği: başlatmada `DockHitTest.ApplyPassthrough(hwnd)` (kalıcı passthrough —
  etkileşim yok, yalnızca görsel katman).

### `Services/VeilService.cs`

- `bool IsEnabled`, `Color` (Color), `Opacity` (0–100) — yalnızca okunan durum.
- `event EventHandler? Changed` — toggle + config uygulaması sonrası.
- `ApplyConfig(VeilConfig)` — başlatmada ve Kaplama sayfası değişikliklerinde; renk/opaklık/enabled
  uygular (fade'li). **Pencere yalnızca enabled'da kurulur** — kapalıyken tam ekran pencere
  gerçekleşmez (startup'ta tema uygulamasının Mica bağlayıp opak kaplama flaşı yaratmaması için);
  ilk enable'da `EnsureWindow` + config'den tint uygulanır.
- `SetEnabled(bool)` — durum + `Config.Save` (kalıcılık) + `Changed` + fade.
- Hotkey: `HotkeyParser.Parse(cfg.HotKey)` → başarısızsa kayıt yok + `debug` log; başarılıysa
  mesaj-only `HwndSource` (`ParentWindow=new IntPtr(-3)`) + `AddHook`; `WM_HOTKEY` → `SetEnabled(!IsEnabled)`.
- `Dispose()` — `UnregisterHotKey` + `VeilWindow.Close()`; çıkış yollarına bağlanır (normal çıkış,
  `App.Exit`).

### `Native/Hotkey.cs`

- `RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk)` / `UnregisterHotKey` — user32 P/Invoke.
- `HotkeyParser.Parse(string)` **saf statik**: `"Ctrl+F11"` → `(MOD_CONTROL, VK_F11)`.
  - Modifier sözcükleri: `Ctrl`, `Alt`, `Shift`, `Win` (`+` ile ayrılır); sıra önemsiz.
  - Tuş: `F1`–`F12`, `A`–`Z`, `0`–`9` (VK eşlemesi).
  - En az bir modifier zorunlu; tekrarlanan modifier / bilinmeyen tuş / boş → `null`.
  - `MOD_NOREPEAT` kullanılmaz (basitlik).
- Semboller: `MOD_ALT=0x1`, `MOD_CONTROL=0x2`, `MOD_SHIFT=0x4`, `MOD_WIN=0x8`.

### `Models/AppConfig.cs` — `VeilConfig`

```csharp
public sealed class VeilConfig
{
    public bool Enabled { get; set; } = false;
    public string Color { get; set; } = "#4A90D9";   // "#RRGGBB"
    public int Opacity { get; set; } = 40;           // 0–100
    public string HotKey { get; set; } = "Ctrl+F11"; // boş = yok
}
```

`ConfigService.CreateDefault()` → `Veil = new VeilConfig()`; `Normalize()`:
- `Opacity` → `ClampOpacity` (0–100, varsayılan 40).
- `Color` → `VeilNormalizer.TryParseColor` (saf statik): geçerli `#RRGGBB` değilse varsayılan
  `#4A90D9`; `#RGB` kabul edilir, küçük harfe normalleştirilir.
- `HotKey` → normalleştirilmez (soft alan): geçersizse yalnızca kayıt yapılmaz, string korunur
  (kullanıcı düzeltebilir; sayfa uyarı gösterir).

### Hızlı Ayarlar karosu

- `QuickSettingsService.BuildTiles()` → `QuickTileState("veil", "Kaplama", "\uE790", IsEnabled: true, IsActive: veil.IsEnabled, HasChevron: false, tooltip: "Renk kaplaması aç/kapat")` (nightlight girişinin yanı).
- `ToggleTileAsync` → `case "veil": veil.SetEnabled(!veil.IsEnabled);`
- Durum güncelleme: `veil.Changed` → `Refresh()` → `Changed` → flyout `UpdateTile` (2 s poll gerekmez).
- `TopBarWindow.xaml.cs` kurulumu: `_veil` App'ten ctor ile gelir; `_quick = new QuickSettingsService(_nightLight, _veil)`.
- Flyout kod değişikliği gerekmez (karolar veri odaklı).

### `Views/Pages/VeilPage.xaml(.cs)` — "Kaplama"

AppearancePage deseni (`PowerToysCard`, `_isLoading` guard, her değişiklikte `Config.Save(Cfg)` + canlı uygula):

1. **Etkinleştir** kartı — toggle `Switch`; değişimde `Config.Save` + `WharfApp.Veil.ApplyConfig(Cfg.Veil)` (canlı önizleme = uygulama).
2. **Renk** kartı — hazır SSS paleti (6–8 yumuşak renk swatch: mavi `#4A90D9`, yeşil `#7CB342`, pembe `#E57373`, turuncu `#E6A23C`, sarı `#D4C24A`, mor `#9E7BB5`, gri `#8E8E8E`, koyu `#2B2B2B`) + hex `TextBox` + "Varsayılana dön" (`#4A90D9`). Geçersiz hex → TextBox kırmızı çerçeve, uygulanmaz.
3. **Opaklık** kartı — `Slider` 0–100 (iki satırlı kart, üst bar tint kartı deseni); canlı uygulanır.
4. **Kısayol** kartı — mevcut kısayolu gösteren metin + "Yeni kısayolu yakala" düğmesi (KeyDown yakalama: modifier + tuş; geçersizse uyarı) + "Kaldır" (boş) + "Sıfırla" (Ctrl+F11). Kayıt başarısızsa (çakışma/geçersiz) kart içi uyarı satırı ("kısayol kullanılamıyor"), config bozulmaz.

`SettingsWindow.xaml` → `MenuItems`'e `NavigationViewItem Content="Kaplama" TargetPageType="{x:Type pages:VeilPage}"` + `SymbolIcon` (örn. `Color24` — paketteki enum adı doğrulanır; `SymbolRegular` XAML'de eksikse alternatif).

## Veri akışı

1. Başlatma: `App.OnStartup` → config → tema uygulaması (**önce** — kaplama penceresi kurulmadan;
   aksi halde Mica bağlanır ve opak flaş olur) → `new VeilService(config, _config)` → `ApplyConfig`
   (kaplama varsa açılır; kapalıysa pencere hiç kurulmaz) → hotkey kaydı.
2. QS karosu tık: `ToggleTileAsync("veil")` → `SetEnabled(!)` → fade + `Config.Save` + `Changed` → `Refresh()` → tile aktif/arka plan rengi.
3. Kısayol: `WM_HOTKEY` → aynı `SetEnabled` yolu (karodan bağımsız, aynı durum).
4. Kaplama sayfası: değer değişimi → `Config.Save` → `Veil.ApplyConfig` → canlı uygulanır; `Changed` → karosu da güncellenir.

## Non-goals

- Çoklu monitör kaplaması (v1 birincil monitör; project kapsamı)
- Yükseltilmiş (elevated) pencerelerin üzerine kaplama (süreç sınırı, ColorVeil'de de var)
- Ayrı "parlaklık azaltma" kontrolü / ön ayarı (tek mekanizma kararı)
- Zamanlanmış / otomatik açma-kapama (program desteği)
- Birden çok kaplama profili kaydetme (v1 tek profil)
- Exclusive fullscreen üzerinde görünürlük (DWM dışı render — mümkün değil)
- Kaplama için etkileşim (yalnızca görsel katman; tıklama passthrough)

## Hata / kenar durumları

| Durum | Davranış |
|-------|----------|
| `RegisterHotKey` başarısız (çakışma) | Kayıt yok, `debug` log; Kaplama sayfasında "kısayol kullanılamıyor" uyarısı; config bozulmaz |
| Geçersiz kısayol string'i | Kayıt yok; string korunur; sayfada uyarı |
| Geçersiz renk config | `VeilNormalizer.TryParseColor` → varsayılan `#4A90D9` (normalize yeniden kaydeder) |
| Opaklık sınır dışı | `ClampOpacity` 0–100 |
| Fade sırasında hızlı toggle | Animasyon durdurulur/yeniden başlar; son durum her zaman doğru |
| Çıkış / crash yolları | `Dispose` → `UnregisterHotKey` (yeni `App.Exit` bağlantısı); kaplama penceresi süreçle birlikte kapanır (kalıcı etki yok) |
| `WM_DISPLAYCHANGE` / DPI değişimi | `ApplyRect()` yeniden — birincil monitör sınırları |
| Exclusive fullscreen uygulama | Kaplama görünmez (sınır — belgelenir) |
| Kaplama açıkken uygulama yeniden başlatma | `Config.Save` kalıcılığı → açık durum + renk + kısayol korunur |

## Test stratejisi

Saf mantık unit testleri (xUnit, mevcut static-helper tarzı):

1. `HotkeyParserTests` — `"Ctrl+F11"` → `(MOD_CONTROL, VK_F11)`; `"Alt+Shift+A"`; `"win+1"` (küçük harf); `""`/`null` → `null`; `"F11"` (modifier yok) → `null`; `"Ctrl+Ctrl+A"` → `null`; `"Ctrl+F13"` → `null`.
2. `VeilConfigTests` — `ClampOpacity` sınırlar; `TryParseColor` geçerli/`#RGB`/geçersiz → varsayılan.

Win32 yolları (`RegisterHotKey`, pencere/fade) unit test edilmez — manuel doğrulama listesi.

## Doğrulama (manuel)

1. `dotnet build WharfBar.sln` temiz; `dotnet test` geçer
2. QS karosu aç → 250–300 ms fade ile kaplama gelir, karo vurgulu; kapat → fade ile gider
3. Kaplama açıkken masaüstü/pencere tıklamaları normal çalışır (passthrough)
4. `Ctrl+F11` → aç/kapa (karoyla aynı durum senkron)
5. Kaplama sayfası: renk swatch'ı tıkla → canlı renk değişir; hex ile özel renk; geçersiz hex reddedilir
6. Opaklık kaydırıcısı → canlı değişir; 100 + siyah → ekran pratikte kararır (parlaklık azaltma)
7. Kısayol değiştir (örn. `Ctrl+Alt+V`) → yeni kısayol çalışır, eski kısayol artık çalışmaz
8. Çakışan kısayol senaryosu (başka uygulama kısayolu alınca / ikinci WharfBar yok ama el ile çakışma) → uyarı, çökme yok
9. Yeniden başlat → kaplama durumu, renk, opaklık, kısayol korunur
10. Tam ekran video → kaplama üstte kalır (her zaman göster)
11. Çıkış (Quit) → hotkey serbest bırakılır; ikinci başlatmada kısayol yeniden çalışır

## Doküman güncellemesi (aynı değişiklik)

- `docs/decisions/007-veil-overlay.md` — ADR: katmanlı pencere yaklaşımı, kontrol noktaları (karosu + kısayol), her zaman göster, tek mekanizma
- `docs/01-architecture.md` — `VeilWindow`, `VeilService`, `Native/Hotkey.cs`, `VeilPage`, config şeması (`VeilConfig`)
- `docs/02-win32-appbar-dwm.md` — `RegisterHotKey`/`UnregisterHotKey` imzaları (kısa)
- `.agent/project.md` / `.agent/context/roadmap.md` — özellik ilerlemesi
- `.agent/screens/README.md` — Kaplama sayfası + QS karosu UI turu
