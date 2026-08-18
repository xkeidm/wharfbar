# ADR-008: Medya paneli — çalan medya gösterimi ve kontrolü (SMTC)

**Durum:** Kabul edildi (2026-08-18)
**Tarih:** 2026-08-18
**Kapsam:** Üst barda medya öğesi + medya flyout'u (kapak, başlık/sanatçı,
Oynat/Duraklat + İleri/Geri, çoklu oturum seçici)
(spec: [2026-08-18-media-panel-design](../superpowers/specs/2026-08-18-media-panel-design.md))

## Karar

| Konu | Seçim |
|------|--------|
| API | Windows SMTC — `GlobalSystemMediaTransportControlsSessionManager` (`Windows.Media.Control`); TFM `net8.0-windows10.0.19041.0` WinRT projection'ı zaten içerir → yeni NuGet paketi / `AllowUnsafeBlocks` yok |
| Üst bar öğesi | Katalog öğesi `media` (`DefaultOrder`'da `brightness` sonrası); **dinamik görünürlük**: oturum yokken `Collapsed` (Battery/Volume deseni; macOS menü bar davranışı); sağ tık menüsünden kalıcı göster/gizle + sıralama (katalog öğesi, "hide must stick") |
| Seçim politikası | **En son aktif oturum** başta (`MediaSessionSelector.MarkActive`); oturum kapanınca en son aktif kalan seçilir; hiçbiri yoksa öğe gizlenir. Saf liste mantığı — unit testli |
| Çoklu oturum | Flyout içinde chevron seçici (‹ oturum adı ›), yalnızca 1'den fazla oturum varken görünür; `MoveCurrent` seçimi halkada ilerletir |
| Komutlar | Oynat/Duraklat + İleri/Geri (`TryTogglePlayPauseAsync` / `TrySkipNextAsync` / `TrySkipPreviousAsync`); **seek/süre yok** (kullanıcı kararı — temel set) |
| Thread | WinRT olayları arka plan thread'den gelir → servisin kurulduğu Dispatcher'a `BeginInvoke` ile marshal; kapak UI thread'de `BitmapImage` (`OnLoad`) üretilir + `Freeze()` |
| Hata dayanıklılığı | Init hatası → 10 sn sonra `DispatcherTimer` ile yeniden dener (SMTC hizmeti geç gelirse); desteklenmeyen komut → yutulur (debug log); metadata alınamazsa kaynak uygulama adı fallback |
| Katalog | `CurrentStatusCatalogVersion = 4`; migration (`ConfigService.Normalize`): eksik `media` `brightness` sonrasına, o yoksa `volume` sonrasına, o da yoksa sona eklenir (`EnsureMedia`) — soft alan, `schemaVersion` değişmez |
| Sahiplik | `MediaSessionService` `TopBarWindow.OnSourceInitialized`'ta kurulur (Brightness/Volume deseni); `DisposeOwnedServices` → `Dispose` (`_disposed` guard; abonelik çözme) |

## Gerekçe

SMTC, tüm medya uygulamalarını (Spotify, tarayıcı, VLC, …) tek belgelenmiş API altında
sunar — uygulama başına özel entegrasyon (Spotify Web API, tarayıcı extension) gerekmez;
TFM zaten WinRT projection'ı taşıdığı için kaynak maliyeti sıfır ek bağımlılıkla gelir.
Öğe görünürlüğü dinamik tutulur (oturum varken göster) çünkü macOS menü barında olduğu gibi
boş ikonlar yer kaplamaz; bu, mevcut Battery/Volume dinamik görünürlük deseninin aynısıdır.
Seçim politikası saf bir sınıfa (`MediaSessionSelector`) ayrılır — en son aktif kuralı ve
halka ilerleme SMTC'den bağımsız test edilir. Flyout oturum bitince kapanmaz; "Çalan medya
yok" gösterir (kullanıcı denetiminde kalır, diğer flyout'larla aynı kapanış kuralları).

## Sonuçlar

- Yeni dosyalar: `Models/MediaSnapshot.cs` (saf record; `Idle` + `HasSession`),
  `Services/MediaSessionSelector.cs` (son-aktif politikası), `Services/MediaSessionService.cs`
  (SMTC wrapper: `Changed` / `SessionsChanged`, snapshot, komutlar, 10 sn retry),
  `Views/MediaFlyout.xaml(.cs)` (seçici + kapak + başlık/sanatçı + kontroller).
- `TopBarWindow`: `media` öğesi + `MediaPopup`/`MediaHost`; `_mediaActive` dinamik görünürlük;
  tooltip "Başlık – Sanatçı"; kapanış animasyonları QS/Brightness ile aynı.
- Katalog v4: mevcut config'ler açılışta `media`'yı `brightness` sonrasına eklenmiş bulur;
  gizleme tercihi korunur ("hide must stick").
- Seek/süre yok; medya uygulaması başlatma/durdurma yok (yalnızca çalışan oturumlar).
- Doküman: `docs/01-architecture.md`, `docs/03-development-order.md`, `.agent/*` güncellenir.

## Alternatifler (reddedilen)

1. **Uygulama başına özel entegrasyon** (Spotify Web API, tarayıcı extension'ları) —
   her uygulama için ayrı bakım; tek API'de kapsanamayan yelpaze; reddedildi.
2. **Seek / süre çubuğu eklemek** — SMTC desteklese de kapsam kullanıcı kararıyla temel
   kontrollerle sınırlandı (Oynat/Duraklat + İleri/Geri); v1 sonrası aday.
