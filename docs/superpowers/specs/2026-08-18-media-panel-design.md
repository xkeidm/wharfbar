# WharfBar — Medya Paneli: çalan medyayı göster + kontrol et (SMTC)

**Tarih:** 2026-08-18
**Durum:** Taslak — kullanıcı onayı bekliyor
**Kapsam:** Üst barda medya öğesi (yalnızca oturum aktifken görünür); tıklayınca açılan
medya flyout'u: kapak + başlık/sanatçı + Önceki / Oynat-Duraklat / Sonraki; birden çok
oturum varsa panel içinde seçici. Tüm medya uygulamaları Windows SMTC
(`GlobalSystemMediaTransportControlsSessionManager`) üzerinden — yeni bağımlılık yok
(WinRT projection TFM ile zaten mevcut).

## Özet

Windows'un Sistem Medya Taşıma Kontrolleri (SMTC) her uygulamanın çalan medyasını
(Spotify, tarayıcı, VLC, …) ve kontrol imkânını tek API altında sunar. WharfBar:
- Üst bar durum şeridine yeni `media` öğesi (katalog id) — müzik/medya çalarken görünür,
  oturum yokken `Collapsed` (macOS menü bar davranışı; Battery/Volume dinamik görünürlük
  deseninin aynısı). Sağ tık menüsünden kalıcı göster/gizle + sıralama (katalog öğesi).
- Tıklayınca medya flyout'u: yuvarlatılmış kapak, başlık + sanatçı (tek satır, kısaltmalı),
  Önceki / Oynat-Duraklat / Sonraki; birden çok aktif oturum varsa üstte küçük seçici
  (‹ oturum adı › chevron'lar).
- Seçili oturum politikası: **en son aktif olan** (durumu değişen / kontrol edilen).
  Oturum kapanınca en son aktif kalan oturuma geçilir; hiçbiri yoksa öğe gizlenir.

## Kararlar

| Konu | Seçim |
|------|--------|
| API | `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` (WinRT; TFM `net8.0-windows10.0.19041.0` — ekstra paket / `AllowUnsafeBlocks` gerekmez) |
| Kontroller | Oynat/Duraklat + İleri/Geri (`TryTogglePlayPauseAsync` / `TrySkipNextAsync` / `TrySkipPreviousAsync`); seek yok, süre yok (kullanıcı kararı) |
| Gösterilecek bilgi | Kapak + başlık + sanatçı (`TryGetMediaPropertiesAsync`); kapak `IRandomAccessStreamReference` → UI thread'de `BitmapImage` |
| Öğe görünürlüğü | Katalog öğesi `media` — oturum yokken dinamik `Collapsed` (Battery/Volume deseni); sağ tık menüsünden kalıcı gizleme katalogda zaten var ("hide must stick") |
| Varsayılan sıra | `brightness`'tan sonra (ses kümesiyle birlikte); katalog v2 migration: eksik `media` `brightness` sonrasına eklenir (`EnsureMedia`) |
| Çoklu oturum | Panelde seçici (chevron'lar + oturum adı), yalnızca 1'den fazla oturum varken görünür; seçim `MediaSessionService.Select(id)` |
| Seçim politikası | En son aktif oturum; kapandıysa en son aktif kalan; yoksa null → öğe gizli. Mantık saf `MediaSessionSelector` (test edilebilir) |
| Thread | WinRT olayları arka plan thread'de gelir → UI thread'e `Dispatcher` ile marshal; `BitmapImage` UI thread'de üretilir |
| Hata | `Try*Async` desteklenmeyen komutta throw → sessizce yutulur; metadata alınamazsa son bilinen bilgi + kaynak uygulama adı fallback |
| Init hatası | SMTC başlatılamazsa servis pasif kalır (öğe görünmez); `SessionsChanged`'de yeniden deneme |
| Config | Yeni alan yok; yalnızca katalog v2 (statusCatalogVersion 1 → 2) |
| Sahiplik | `MediaSessionService` `TopBarWindow` tarafından kurulur/sahiplenilir (Brightness/Volume servis deseni; `DisposeOwnedServices`) |

## Mimari

```
TopBarWindow
  ├─ MediaSessionService                # SMTC yönetimi; Changed / SessionsChanged olayları
  │    ├─ MediaSessionSelector (saf)    # "en son aktif" seçim politikası
  │    └─ MediaSnapshot (saf model)
  ├─ MediaButton (durum şeridi öğesi)   # oturum varken Visible, yokken Collapsed
  └─ MediaPopup / MediaHost             # açılış/kapanış animasyonlu flyout
       └─ MediaFlyout (UserControl)     # seçici + kapak + başlık/sanatçı + kontroller
```

### `Models/MediaSnapshot.cs` — saf model

```csharp
public sealed record MediaSnapshot(
    string SessionId,          // SMTC oturum kimliği (SourceAppUserModelId + index fallback)
    string AppName,            // kaynak uygulama adı (AUMID'den sadeleştirilmiş)
    string Title,
    string Artist,
    bool IsPlaying,            // playback status playing / paused
    bool HasThumbnail);
```

`MediaSnapshot.IsIdle` → `Title` boş ve `Artist` boş ve `!HasThumbnail` (metadata alınamadı
fallback'inde app adı gösterilir, öğe yine görünür).

### `Services/MediaSessionSelector.cs` — saf mantık (unit testli)

Olayları `MediaSessionService`'ten alır, durumu korur:

- `AddSession(id)` / `RemoveSession(id)` / `MarkActive(id)`
- `MediaSessionChanged(id, isActive)` — oynatma/duraklatma veya metadata değişiminde
  `MarkActive(id)` (son aktif politika).
- `Current` — son aktif oturum; silindiyse son aktif kalan (sıra listesi); yoksa `null`.
- `Sessions` — aktif oturum listesi (seçici için; yalnızca playing/paused durumdakiler).

### `Services/MediaSessionService.cs`

- `InitializeAsync()` — `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()`;
  `GetSessions()` → selector'a yükle; `SessionsChanged` → fark hesapla (eklenen/kaldırılan).
- Oturum olay abonelikleri: `PlaybackInfoChanged`, `MediaPropertiesChanged` (arka plan
  thread → `Dispatcher` marshal).
- `GetSnapshotAsync(session)` — `TryGetPlaybackInfoAsync()` (playback status) +
  `TryGetMediaPropertiesAsync()` (Title/Artist/Thumbnail) → `MediaSnapshot`; başarısız
  adımda varsayılan değerler + app adı fallback.
- `event EventHandler? Changed` — snapshot güncellenince (UI güncellemesi);
  `event EventHandler? SessionsChanged` — seçici görünürlüğü / öğe görünürlüğü.
- Komutlar: `TogglePlayPause()`, `Next()`, `Previous()`, `Select(id)` — `try/catch`
  (WinRT `IAsyncOperation` hatası → yutulur, debug log).
- Thumbnail cache: oturum id anahtarlı `Dictionary<string, BitmapImage>` (kapak değişmedikçe
  yeniden akış açılmaz).
- `Dispose()` — oturum olay abonelikleri çözülür (TopBarWindow `DisposeOwnedServices`).

### `Views/MediaFlyout.xaml(.cs)` — flyout içeriği

BrightnessFlyout deseni (WPF-UI popup içeriği; `MediaHost.Refresh()` güncelleme):

- **Seçici satırı** (yalnızca `Sessions.Count > 1`): ‹ chevron / oturum adı / › chevron —
  tıklama `Select(prev|next)` → snapshot yeniden yüklenir.
- **Kapak + bilgi**: yuvarlatılmış kapak (`Image`, yaklaşık 56–64 px; kapsızsa yer tutucu
  müzik glifi `\uE90C`), altında başlık (kalın, tek satır `TextTrimming`) + sanatçı (tek
  satır, soluk).
- **Kontroller**: `\uE892` Önceki / `\uE768`–`\uE769` Oynat-Duraklat (duruma göre glif
  değişir, pause'da `\uE769`)/ `\uE893` Sonraki — WPF-UI düğme stilleri.
- Araç ipucu: başlık – sanatçı (bar öğesi ve panelde).

### Üst bar öğesi (`TopBarWindow`)

- `TopBarWindow.xaml` — `MediaButton` (glif `\uE90B` MusicInfo — müzik notası;
  WinUI `Symbol` enum'undan, mevcut MDL2 fontuyla sorunsuz), `MediaPopup` + `MediaHost`
  (Brightness deseni).
- `_statusElements` sözlüğüne `[TopBarStatusItemIds.Media] = MediaButton`.
- `ApplyStatusItemLayout()` — `TopBarStatusItemIds.Media => _mediaActive` (Battery/Volume
  deseni); `_mediaActive` = seçili oturum var ve snapshot alındı.
- Tıklama → `ToggleMediaFlyout()` (Brightness deseni; aynı açılış/kapanış animasyonları,
  `_mediaClosePending`, `CancelPendingClose`); `CloseFlyouts()` üç flyout'u da kapatır.
- `MediaSessionService.Changed` → snapshot'ı uygula: `_mediaActive`, `MediaButton` tooltip
  ("Başlık – Sanatçı"), flyout açıksa içerik tazelenir.
- `DisposeOwnedServices` → `_media.Dispose()`.

### Katalog + migration

- `TopBarStatusItemIds`: `Media = "media"`; `DefaultOrder` → `brightness`'tan sonra
  (`trayOverflow, dateTime, notifications, controlCenter, volume, brightness, media,
  network, battery`).
- `EnsureMedia(List<string>)` — eksikse `brightness` sonrasına ekler (`EnsureBrightness`
  deseni; `brightness` yoksa `volume` sonrası, o da yoksa sona).
- `TopBarConfig.CurrentStatusCatalogVersion` → **2**; `ConfigService.Normalize`:
  `EnsureControlCenter` + `EnsureBrightness` + `EnsureMedia` (v1→v2 geçişte `media` eklenir;
  mevcut v1 kullanıcıları için öğe varsayılan olarak görünür).

## Veri akışı

1. `TopBarWindow.OnSourceInitialized` → `_media = new MediaSessionService(); _media.Changed +=
   ApplyMedia; _media.SessionsChanged += ApplyMediaLayout;` sonra `_ = _media.InitializeAsync()`.
2. SMTC oturum olayı (arka plan) → `Dispatcher.BeginInvoke` → `GetSnapshotAsync` → `Changed`
   → `ApplyMedia(snapshot)`: `_mediaActive` + tooltip + açıksa flyout tazeleme +
   `ApplyStatusItemLayout()`.
3. Flyout açılışı: `MediaHost.Refresh()` — `Changed` son snapshot'ı gösterir
   (`MediaFlyout.Apply(snapshot, sessions)`).
4. Kontrol düğmesi → servis `Try*Async` → SMTC → uygulama; olay geri döner → snapshot
   güncellenir (düğme glifi canlı değişir).
5. Seçici chevron → `Select(id)` → yeni oturumun snapshot'ı yüklenir + flyout tazelenir.

## Non-goals

- Seek / süre çubuğu (kullanıcı kararı — temel set)
- Medya uygulaması başlatma / durdurma (yalnızca çalışan oturumlar)
- Ses kontrolü panelde (üst bar `volume` öğesi + QS Mixer mevcut)
- Medya geçmişi / "şimdi çalanlar" listesi
- Bildirimde medya gizleme / SMTC görünürlük kontrolü
- Oturum adından daha zengin çoklu oturum yönetimi (v1: chevron seçici)

## Hata / kenar durumları

| Durum | Davranış |
|-------|----------|
| SMTC `RequestAsync` başarısız | Servis pasif; öğe görünmez; `SessionsChanged` / tekrar initialize denemesi (retry, best-effort) |
| Metadata alınamadı (uygulama yanıt vermiyor) | Son bilinen snapshot korunur; ilk kezse app adı fallback; öğe yine görünür (oturum var) |
| Desteklenmeyen komut (ör. radyo akışında skip) | `Try*Async` hatası → yutulur (debug log); UI bozulmaz |
| Oturum kapanması | `SessionsChanged` → selector `RemoveSession` → kalan en son aktif seçilir; yoksa `_mediaActive=false` → öğe `Collapsed`; flyout açıksa içerik "çalan medya yok" metnine döner, popup kapanmaz (kapatma yalnızca kullanıcı) |
| Kapak okunamadı (kodlanmamış akış) | `BitmapImage` hata → yer tutucu glif `\uE90C` |
| Çıkış / recreate | `DisposeOwnedServices` → abonelikler çözülür; WinRT nesneleri GC'ye bırakılır; yeniden oluşturmada yeni servis |

## Test stratejisi

Saf mantık unit testleri (xUnit, mevcut static-helper tarzı):

1. `MediaSessionSelectorTests` —
   - `MarkActive` → current değişir (son aktif politika)
   - `RemoveSession(current)` → en son aktif kalan seçilir
   - Tüm oturumlar kalkınca → `Current == null`
   - `MarkActive` bilinmeyen id → no-op
2. `TopBarStatusItemIdsTests` (mevcut normalize testlerine ek) — `EnsureMedia`:
   `brightness` sonrasına ekleme; `brightness` yoksa `volume` sonrası; ikisi yoksa sona;
   mevcutsa dokunma; `Normalize` `media` id'sini tanır.
3. `MediaSnapshot` — kayıt eşitliği / `IsIdle` türetimi.

SMTC yolları (WinRT etkileşimi, komutlar) unit test edilmez — manuel doğrulama listesi.

## Doğrulama (manuel)

1. `dotnet build WharfBar.sln` temiz; `dotnet test` geçer
2. Spotify/browser müziği çalarken: `media` öğesi belirir (brightness sonrası), tooltip
   "Başlık – Sanatçı"
3. Tıklama → flyout: kapak, başlık, sanatçı, kontroller; Oynat/Duraklat çalışır (glif canlı
   değişir), İleri/Geri parça değiştirir
4. Müzik durduruldu / uygulama kapandı → öğe kaybolur; flyout açıksa "çalan medya yok"
5. Spotify + tarayıcı aynı anda çalıyor → seçici görünür; chevron ile oturum değişir,
   kontroller doğru uygulamaya gider
6. Flyout dış tıklama / ESC / tekrar tıklama → animasyonlu kapanır; yeniden açılışta içerik
   tazelenir
7. Sağ tık menüsü: `media` onay kutusu gizleme kalıcı; yeniden başlatınca korunur
8. Mevcut config (v1 kataloğu) ile açılış → `media` otomatik `brightness` sonrasına eklenir
9. Çıkış → hata yok, medya uygulamaları etkilenmez (sadece dinleme bırakılır)

## Doküman güncellemesi (aynı değişiklik)

- `docs/decisions/008-media-panel.md` — ADR: SMTC yaklaşımı, dinamik görünürlük, son-aktif
  politika, çoklu oturum seçici
- `docs/01-architecture.md` — `MediaSessionService`, `MediaSessionSelector`, `MediaSnapshot`,
  `MediaFlyout`, katalog v2
- `docs/03-development-order.md` (gerekirse) — medya paneli sırası
- `.agent/project.md` / `.agent/context/roadmap.md` — özellik ilerlemesi
- `.agent/screens/README.md` + `topbar.md` — medya öğesi + flyout UI turu
