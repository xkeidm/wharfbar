# WharfBar — Hızlı ayarlar: canlı ağ listeleri, gece ışığı, yansıtma ve tile animasyonları

**Tarih:** 2026-08-16  
**Durum:** Onaylandı (kullanıcı 2026-08-16) — uygulama planı: `../plans/2026-08-16-quick-settings-live-network-nightlight-project.md`  
**Kapsam:** Quick Settings flyout’unun 4 ayrı ekseni:
1. Wi-Fi chevron’u → canlı ağ listesi + bağlanma
2. Bluetooth chevron’u → eşleşmiş cihaz listesi
3. Gece ışığı tile’ı → gerçek durum + gerçek aç/kapa
4. Yansıt tile’ı → Windows’un Win+P yerine WharfBar’ın kendi paneli + tile toggle animasyonları

## Özet

Flyout’a `Mixer`/`Network` desenine benzer üç yeni sayfa eklenir: `Wifi`, `Bluetooth`, `Project`.
Wi-Fi listesi ve bağlanma **native Wlan API** (`wlanapi.dll`) ile yapılır (WinRT `WiFiAdapter`
değil — neden: scan sonuçları Windows listesiyle örtüşmez, location izni ister, capability gerekebilir;
Wlan API Windows’un gösterdiği listeyi, sinyal kalitesini ve profil/bağlanma akışını belgeli şekilde verir).
Bluetooth, eşleşmiş cihazlar için `DeviceInformation` + `BluetoothDevice` kullanır. Gece ışığı durumu
CloudStore registry blob’undan **en iyi çaba** okunur (format Windows sürümleri arasında kırılabilir);
WharfBar kendi aç/kapa’sını registry’ye yazmayı dener ve her durumda bir oturum override’ı ile
tile’ı doğru tutar. Yansıtma paneli `SetDisplayConfig`/`QueryDisplayConfig` ile Win+P’nin 4 modunu
native olarak uygular. Tile’lar artık her poll’da yeniden kurulmaz (in-place güncelleme) ve toggle
anında ikon + arka plan animasyonu oynatır.

## Kararlar

| Konu | Seçim |
|------|--------|
| Wi-Fi enumerasyon/bağlanma | `Native/WlanApi.cs` P/Invoke + `Services/WifiService.cs` |
| Wi-Fi listesi modeli | `WifiNetworkInfo(Ssid, Signal, IsSecured, IsKnown, IsConnected, ProfileName)`; sinyal `wlanSignalQuality` 0–100 |
| Yeni güvenli ağa bağlanma | Profil XML’i üret, `WlanSetProfile` (kalıcı), `WlanConnect(profile)` |
| Bilinen ağa bağlanma | `WlanConnect` doğrudan (profil adı) |
| Açık ağa bağlanma | `WlanSetProfile` (open/none) + `WlanConnect` |
| Şifre UI | Flyout içi satır-içi panel (PasswordBox + Bağlan/İptal); ayrı pencere yok (Popup StaysOpen=False) |
| Bluetooth | `Services/BluetoothService.cs`; `FindAllAsync(BluetoothDevice.GetDeviceSelector())` + `IsPaired` filtresi; `ConnectionStatus` ile “bağlı” işareti |
| Gece ışığı okuma | `Services/NightLightService.cs`; CloudStore `bluelightreductionstate` `Data` blob’u tolerant parse; yapı tanınmazsa “bilinmiyor” → override |
| Gece ışığı yazma | Parse başarılıysa blob’u (etkin bayrak + zaman alanı) güncelleyip yaz; kuşkuda yazma, logla (plan uygulaması: zaman alanı güncellenmez — format bilinmiyor, "kuşkuda yazma" kuralı; manuel doğrulama 5. madde OS kalıcılığını test eder) |
| Gece ışığı override | `bool? _override`; harici registry değişimi okununca override temizlenir |
| Gece ışığı polling | QuickSettingsService’in mevcut 2 s timer’ı (tile her zaman doğru) |
| Yansıtma | `Native/DisplayTopology.cs` + `Services/ProjectionService.cs`; `QueryDisplayConfig` (sıfır buffer) ile mevcut topoloji, `SetDisplayConfig` ile uygula |
| Yansıtma modları | `ProjectionMode { InternalOnly, Clone, Extend, ExternalOnly }` ↔ `SDC_TOPOLOGY_INTERNAL/CLONE/EXTEND/EXTERNAL` |
| Tile yönlendirme | project tile/chevron → `Project` sayfası (Win+P yok); wifi/bt chevron → `Wifi`/`Bluetooth` sayfası; üst bar ağ ikonu → `Network` (değişmez) |
| Tile animasyonu | In-place güncelleme + toggle anında ikon “pop” (scale 1→1.15→1, ~180 ms) + arka plan `ColorAnimation` (base↔accent, ~250 ms) |
| Sayfa enum | `QuickSettingsPage { Main, Mixer, Network, Wifi, Bluetooth, Project }` |
| Polling | Wifi/BT sayfası açıkken poll; kapanınca dur (Mixer deseni) |

## Mimari

```
QuickSettingsFlyout
  ├─ QuickSettingsService     # tiles + radyolar + network + gece ışığı state (override)
  ├─ WifiService              # Wlan API: liste + bağlan + profil; 2 s poll (Wifi sayfası açıkken)
  ├─ BluetoothService         # eşleşmiş cihazlar + bağlı durumu; poll (BT sayfası açıkken)
  ├─ ProjectionService        # mevcut topoloji okuma + mod uygulama (eventsiz, çağrı anında)
  └─ NightLightService        # registry best-effort + oturum override (QS timer içinden)
Native/
  ├─ WlanApi.cs               # wlanapi.dll P/Invoke ince wrapper
  └─ DisplayTopology.cs       # SetDisplayConfig / QueryDisplayConfig P/Invoke
```

Servis kurulumu `TopBarWindow.xaml.cs` içinde (mevcut `_volume/_mixer/_quick` yanına)
`_nightLight`, `_wifi`, `_bluetooth`, `_projection`; `QuickSettingsService` ctor’u
`NightLightService` alır (tile state için). `QuickSettingsHost.Attach(volume, mixer, quick, wifi, bluetooth, projection)`.

### `Services/WifiService.cs`

Sorumluluklar:

- `IsAvailable` (adaptör var mı), `IReadOnlyList<WifiNetworkInfo> Networks`; `IsRadioOn` / `CurrentConnection` plan'da yok — radyo durumu QuickSettingsService'te
- `Changed` event; `StartPolling` / `StopPolling` (2 s, DispatcherTimer)
- `TryConnect(WifiNetworkInfo, string? password)` → `(bool ok, string? error)`
- `Disconnect()` (plan'da yok — uygulanmadı)

Akış (yeni güvenli ağ):

1. SSID’den profil XML üret (WPA2PSK/AES, `keyMaterial`=şifre, `connectionMode=manual`).
2. `WlanSetProfile` (user scope, `bOverwrite=true`). Hata → `(false, reason)`.
3. `WlanConnect(profile)`. Hata → `(false, reason)`.
4. Başarı → liste tazelenir; Windows bağlantıyı kendi sürdürür.

SSID byte dizisinden `string` dönüşümü UTF-8 best-effort (legacy ANSI için fallback); profil XML’inde
profil adı ve SSID elemanları için XML escape. Bağlı ağ, `wlanProfileFlags & WLAN_AVAILABLE_NETWORK_CONNECTED`
ile; bilinen ağ, `wlanProfileName` doluluğu ile belirlenir.

### `Services/BluetoothService.cs`

- `IReadOnlyList<BluetoothDeviceInfo> Devices`; `Changed`; poll (BT sayfası açıkken).
- `FindAllAsync(BluetoothDevice.GetDeviceSelector())` → `IsPaired` filtre; `Name` boşsa “(Adsız cihaz)”.
- Bağlı durumu: `BluetoothDevice.FromIdAsync(id).ConnectionStatus` (try/catch; başarısızsa `false`).
- Satıra tıklama flyout’ta `ms-settings:bluetooth` açar.

### `Services/NightLightService.cs`

- `bool IsEnabled` → `_override ?? TryReadRegistry() is (known, enabled) ? enabled : _lastKnown`.
- `SetEnabled(bool)` → önce `_override = value`, sonra registry yazımını dene.
- `TryParse(byte[] data, out bool enabled)` **saf statik** (test edilebilir): length-prefixed alan
  listesinde etkin bayrağı (`0x02 0x01` çifti) tanır; yapı kuşkuluysa `false` döner (bilinmiyor).
- Poll (`Refresh()` içinden): registry okunur; okuma “biliniyor” ise ve override’tan farklıysa
  override temizlenir (harici değişiklik kazanır). “Bilinmiyor” ise override korunur.
- Yazma: ayrıştırılmış alan listesini in-place güncelle (etkin bayrak ekle/kaldır, zaman alanını
  artır) ve `Registry.CurrentUser`’a yaz. Parse başarısızsa yazma; yalnızca log.
  (plan uygulaması: zaman alanı güncellenmez — format bilinmiyor, "kuşkuda yazma" kuralı;
  manuel doğrulama 5. madde OS kalıcılığını test eder)

### `Services/ProjectionService.cs`

- `bool TryGetCurrent(out ProjectionMode mode)` → `QueryDisplayConfig(QDC_DATABASE_CURRENT, 0, null, 0, null, out topology)`.
- `bool TryApply(ProjectionMode mode)` → `SetDisplayConfig(0, null, 0, null, flag | SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES)`.
- Topoloji ID → mod eşlemesi tablosu saf (test edilebilir); hata `debug` log, çağıran taraf UI’ı geri alır.

## Flyout sayfaları

Her sayfa mevcut `MixerPage`/`NetworkPage` gibi ayrı `Grid`, `ShowPage` ile görünürlük. Ortak başlık
deseni (geri düğmesi + başlık) korunur.

### `WifiPage`

- Satır 0: başlık (geri + “Wi-Fi”)
- Satır 1: bağlantı özeti (SSID + sinyal) veya “Wi-Fi kapalı” / “adaptör yok” durumu
- Satır 2: `ScrollViewer` ağ listesi — satır başına sinyal barı (4 bar), SSID, kilit/security ikonu,
  bilinen/“Bağlı” rozeti; tıklama: bilinen→bağlan, yeni güvenli→şifre paneli, yeni açık→bağlan
- Satır 3: şifre paneli (varsayılan gizli): SSID + `PasswordBox` + “Bağlan”/“İptal” + hata metni
- Satır 4: “Wi-Fi ayarları” düğmesi (mevcut `OnOpenWifiSettingsClick`)

### `BluetoothPage`

- Başlık (geri + “Bluetooth”), açık/kapalı durum özeti, eşleşmiş cihaz listesi, “Bluetooth ayarlarını aç”

### `ProjectPage`

- Başlık (geri + “Yansıt”), 4 mod satırı (mevcut mod vurgulu, radyo görünümü), tıklama anında uygular,
  “Ekran ayarları” bağlantısı (`ms-settings:display`)

### Tile davranışı değişiklikleri

| Tile | Tıklama | Chevron |
|------|---------|---------|
| wifi | radyo aç/kapa (değişmez) | `Wifi` sayfası |
| bluetooth | radyo aç/kapa (değişmez) | `Bluetooth` sayfası |
| project | `Project` sayfası (Win+P yok) | `Project` sayfası |
| nightlight | `NightLightService.SetEnabled(!IsEnabled)` (ms-settings değil) | — |
| batterysaver | `ms-settings:batterysaver` (değişmez) | — |

### In-place güncelleme + animasyon

`RefreshQuick()` artık `TilesGrid.Children.Clear()` + yeniden kurma yapmaz. `Dictionary<string, TileUi>`
(id → border, ikon, label, base/accent renk, önceki IsActive) tutar:

- id seti değiştiyse → yeniden kur.
- Değilse → her tile’ı güncelle: style (QsTile/QsTileActive/QsTileDisabled), glyph, foreground,
  tooltip, label. `IsActive` geçişi tespit edilirse kısa animasyon oynat.

Animasyon (`AnimateTileToggle`):

- İkon: `ScaleTransform` 1→1.15→1 (~180 ms, ease) + hafif `Opacity` 1→0.4→1; wifi/airplane için
  ekstra ~90° `RotateTransform` dönüşü.
- Arka plan: `ControlFillColorDefaultBrush` (base) ↔ `AccentFillColorDefaultBrush` (accent) renklerini
  resource’tan çöz, `SolidColorBrush` üzerinde `ColorAnimation` (~250 ms). Tile elemanları canlı
  tutulduğu için animasyon ortadan kalkmaz.

> Uygulama farkı (plan sadeleştirmesi): yalnızca scale-pop + arka plan renk geçişi uygulandı;
> icon `Opacity` dip ve wifi/airplane ~90° `RotateTransform` dönüşü çıkarıldı.

## Non-goals

- Wi-Fi ağını silme / unutma, profil düzenleme
- Ağ şifresi hatırlatma / otomatik bağlanma tercihi (Windows yönetir)
- BT cihazı eşleştirme / bağlama (yalnızca liste + ayarlar bağlantısı)
- Gece ışığı programı / sıcaklık ayarı (yalnızca aç/kapa)
- Yansıtma: önizleme / doğrulama diyaloğu yok; çoklu monitör ayrıntıları
- Windows Win+P UI’sının taklidi — yalnızca 4 mod eylemi

## Hata / kenar durumları

| Durum | Davranış |
|-------|----------|
| Wi-Fi adaptörü yok | `WifiPage` “adaptör bulunamadı” + ayarlar linki; liste boş |
| Wi-Fi radyosu kapalı | `WifiPage` “Wi-Fi kapalı” özeti; liste boş; tile’dan açılabilir |
| `WlanConnect`/`WlanSetProfile` hata | Şifre panelinde satır-içi hata metni; `debug` log |
| SSID non-UTF8 | UTF-8 best-effort + fallback; boşsa “(Bilinmeyen ağ)” |
| Gece ışığı blob malformed | `TryParse` → bilinmiyor → override; yazım denenmez |
| Gece ışığı yazım hatası | `debug` log; override korunur (tile doğru kalır) |
| Harici gece ışığı değişimi | Poll okur, override temizlenir, tile güncellenir |
| `QueryDisplayConfig` hata | `ProjectPage` “topoloji okunamadı”; hiçbir mod vurgulanmaz |
| `SetDisplayConfig` hata | Vurgu korunur; `debug` log |
| Tek monitör | Clone/Extend/External `SetDisplayConfig` hata verir → yutulur, UI geri alınır |
| Flyout kapanır | Wifi/BT poll durur; QS timer de mevcut davranış gibi durur (night light, flyout açılınca yeniden okunur) |
| Erişim reddi (registry/radyo) | İlgili service `IsAvailable=false` / unknown; UI boş durum gösterir |

## Test stratejisi

Saf mantık unit testleri (donanım gerektirmeyen):

1. `NightLightBlobParserTests` — üretilmiş byte dizileriyle (etkin/devre dışı/malformed) `TryParse`.
2. `WifiSignalTests` — `wlanSignalQuality` 0–100 → 1–4 bar eşlemesi.
3. `ProjectionMappingTests` — `ProjectionMode` ↔ SDC flag / topoloji ID eşlemesi.

Win32/hardware yolları (Wlan API, registry blob yazma, `SetDisplayConfig`, BT) unit test edilmez;
manuel doğrulama listesi ile.

## Doğrulama (manuel)

1. `dotnet build WharfBar.sln` temiz; `dotnet test` geçer
2. Wi-Fi chevron → ağ listesi + sinyal barı gelir; “Bağlı” ağ vurgulu
3. Bilinen ağa tık → bağlanır; yeni güvenli ağa tık → şifre paneli açılır, bağlanınca bağlanır
4. Bluetooth chevron → eşleşmiş cihazlar listesi; bağlı olan “Bağlı” gösterir
5. Gece ışığı açıkken flyout açılınca tile “açık” görünür; tıklayınca kapanır (animasyonla) ve Windows durumu değişir
6. Yansıt tile’ı/chevron → WharfBar paneli (Win+P açılmaz); mod seçimi ekran topolojisini değiştirir; mevcut mod vurgulu
7. Tile toggle anında ikon “pop” + arka plan dolgusu animasyonlu; poll sırasında tile kaybolmaz/titremez
8. Flyout kapalıyken Wifi/BT poll durur (Performans profili sabit)

## Doküman güncellemesi (aynı değişiklik)

- `docs/01-architecture.md` — yeni servisler (Wifi/Bluetooth/NightLight/Projection) + flyout sayfaları notu
- `docs/02-win32-appbar-dwm.md` — Wlan API ve `SetDisplayConfig`/`QueryDisplayConfig` imzaları (kısa)
- `.agent/project.md` / `.agent/context/roadmap.md` — quick settings ilerlemesi
- `.agent/screens/README.md` — quick settings UI turu güncellemesi