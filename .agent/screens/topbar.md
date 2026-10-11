# Ekran: Top bar

`src/WharfBar/Views/TopBarWindow.xaml` (+ `.xaml.cs`).

Yükseklik 30 DIP, **AppBar** (work area rezerve), her zaman üstte. Her etkin monitör için
ayrı bar kurulur (per-monitor; her bar kendi monitöründe AppBar, bar önce o monitöre taşınıp
kaydedilir). Görünürlük `monitors` config ile monitör bazında açılır/kapanır.
Arka plan modu config'den: `mica` (varsayılan, `AllowsTransparency=false`),
`transparent` / `tint` (`AllowsTransparency=true` — Mica ile asla birlikte değil).

## Sol küme

- **Windows logosu** (geometri çizimi, font glifi değil — Mica'da kaybolmaz) → sol tık menü
  (ikonun sol kenarına yapışık açılır; `OnAppMenuLoaded` WPF-UI'nin 30px'lik effect margin'inden
  sol marjı sıfırlar — popup penceresi ekran kenarına clamp edilip menüyü sağa kaydırmasın):
  - **Ayarlar (PC)** → `ms-settings:` · **Profil** → `ms-settings:yourinfo` · **Bilgisayar hakkında** → `ms-settings:about`
  - **Bilgisayarı kilitle** / **Uyku** → `PowerActions` (LockWorkStation / SetSuspendState)
  - **Kapat** / **Yeniden başlat** → `PowerActions` (shutdown.exe `/s|/r /t 0`)
  - **WharfBar Ayarları** → [`settings.md`](settings.md)
  - **WharfBar hakkında** → `AboutWindow`
  - **WharfBar'ı kapat** → düzgün çıkış (görev çubuğu restore + `ABM_REMOVE`)

## Orta — ada hapı (`IslandPill`, ADR-010 + ADR-011)

Yalnızca **birincil** monitör. Ada ayarı açıkken ortada bir hap durur
(`Visibility=Collapsed` yalnız kapalıyken veya birincil olmayan barda). AppBar **30 DIP**
kalır; çok satırlı haller aynı `IslandPopup`'ın altında, hapın **ortasına** hizalı açılır
(ikinci pencere yok, iç içe `Popup` yok). `Placement=Bottom` solu hizaladığı için
`IslandPopupAlign.HorizontalOffset` panel merkezi ile hap merkezini eşitler. Offset
yalnız **ölçülmüş** genişlikle yazılır (XAML `Width` yedek değildir); genişlik 0 iken
panel `Opacity=0` kalır, sonra ortalanıp belirir. Hap veya panel sonradan değişince
(DPI, gövde, hover) yeniden hesaplanır ve çalışma alanına clamp edilir. Panel üst kenarı hapın altına ~2 DIP biner. Açıkken hapın alt
köşeleri düzleşir, dolgu panelinkiyle aynıdır (`FlyoutSurface`); kapanınca yine kapsüldür.

Hap, barın içinde dikey ortalanmış bir kapsüldür (`Height=24`, `CornerRadius=12`,
boşta yaklaşık **108 DIP** — `IslandIdleDip`). Dolgu ve 1 px kontur tema fırçalarıdır
(`ControlFillColorDefaultBrush`, `ControlStrokeColorDefaultBrush`); hover
`ControlFillColorSecondaryBrush` ve hap ~4 DIP genişler. Açık ve koyu temada aynı
kaynaklar çözülür. Glifler Segoe MDL2 / Fluent (`AppIconFontFamily`). Genişlik değişimi
~200 ms `CubicEase`. Boşta genişlik tabandır; parça adı ve sayaç bunun üstüne büyür.

- **Boşta:** gösterge çubuğu — pano glifi (`E8C8`) + **Pano**. Üzerine gelince aynı
  `IslandPopup` pano gövdesiyle açılır (kayıt yoksa kısa boş durum cümlesi). İmleç
  ayrılınca ~300 ms. Tıklama sabitler. **Sürükle-bırak** aynı kontrolde (`AllowDrop`).
- **Şimdi çalıyor** (`island.musicPresence`, varsayılan `persistent`): hap nota (`E90C`)
  + tek satır parça adı olarak genişler (CubicEase). Ad `IslandPresence.TitleMaxWidth`
  ile sol istatistik ve sağ şeride değmeyecek şekilde kesilir. Üzerine gelince müzik
  paneli açılır; imleç ayrılınca ~300 ms. Tıklama paneli sabitler. Oturum bitince hap boşta.
- **Kısa süre** (`transient`): hap boşta kalır. Çalma, parça değişimi veya oynat/duraklat
  aynı paneli ~2,5 sn açar. O sırada tıklama sabitler.
- **Ses / parlaklık:** karıştırıcı veya parlaklık paneli açık değilken gelen değişiklik
  hapı ~1,6 sn ikon + yuvarlak sayaca çevirir (`AccentFillColorDefaultBrush`), sonra
  önceki hapa döner. Tıklama mevcut karıştırıcıyı veya parlaklık flyout'unu açar.

Sabit panel dış tıklama, Esc, hapa tekrar tıklama ve `CloseFlyouts` ile kapanır.
Panel açılışı hapın genişliğinden gövdeye doğru büyür (üst orta, solma, ~200 ms); aralık
açılmaz. Ölçek popup çocuğunda değil, iç yüzeydedir (`PanelSurface`): WPF yerleşimi
çocuğun dönüşmüş köşesini kullanır, ölçek çocukta kalırsa panel hapın solunda açılır ve
ölçek 1'e dönünce de orada kalır. Kapanış tersi ve biraz daha hızlıdır. Müzik ile pano
aynı popup içinde ~150 ms çapraz solar. Müzik panelindeki pano düğmesi gövdeyi listeye
çevirir; geri müzik. Ses/parlaklık sayacının dolgusu seviye değişince akar.

Kurallar `IslandPresence` (süre, yüz, HUD önceliği). Pencere tek atımlık zamanlayıcı kullanır.

## Sağ küme — durum şeridi (`StatusStrip`)

Varsayılan soldan sağa sıra `volume`, `brightness` ve `media` **içermez** (ada HUD ve
şimdi çalanı taşır). Üçü katalogda durur: sağ tık menüsü ve Üst Çubuk ayarı onları
yeniden açabilir. Kullanıcının kayıtlı `statusItems` listesi soyulmaz; görünürlerse
aşağıdaki flyout'lar açılır.

| id | Arayüz | Tıklama davranışı |
|----|--------|-------------------|
| `trayOverflow` | "Gizli simgeler" (glif `E70E`) | overflow bölgesi popup'ı (Win+B) |
| `dateTime` | saat+tarih tek satır, `tr-TR` kısaltmaları | sistem takvim/panel |
| `notifications` | zil glifi `EA8F` | Win+N (bildirim merkezi) |
| `controlCenter` | hızlı ayarlar glifi `E9CE` | `QuickSettingsFlyout` → **Main** sekmesi |
| `volume` | ses glifi `E767` | `QuickSettingsFlyout` → **Mixer** sekmesi |
| `brightness` | güneş glifi `E706` | `BrightnessFlyout` (monitör kartları, DDC/CI + WMI) |
| `media` | müzik notası glifi `E90B` | `MediaFlyout` (kapak + başlık/sanatçı + kontroller + oturum seçici); oturum yokken gizli |
| `network` | ağ glifi `E701` | `QuickSettingsFlyout` → **Network** sekmesi |
| `battery` | pil glifi + yüzde metni | yok (gösterim) |

## Durum şeridi sağ tık menüsü

`TopBarWindow.BuildStatusItemsMenu`:
- **Öğe göster/gizle** — tüm katalog onay kutuları (checkable)
- **Arka plan** (alt menü): Mica / Transparent / Tint radio'ları + **Opaklık slider'ı**
  (yalnızca Tint'te etkin, 0–100, debounced kaydetme)
- Tıklanan öğe varsa: **Sola taşı / Sağa taşı**
- **Varsayılana sıfırla** — `TopBarStatusItemIds.DefaultOrder`

## Hızlı ayarlar flyout

`QuickSettingsFlyout` — tek Fluent popup (`AllowsTransparency=true`, `StaysOpen=true`; ana pencerenin
Mica'sıyla ilgisi yok); Main / Mixer /
Network sekmeleri paylaşılır (ADR-006). Mixer: varsayılan aygıt + dropdown + uygulama
oturumları. Network: bağlantı kartı + canlı ↓/↑ trafik satırı + "Özellikler" bölümü
(adaptör, tür, IPv4/IPv6, ağ geçidi, DNS, MAC, bağlantı hızı — `NetworkDetailsService`,
sayfa açıkken 2 sn poll; Windows ağ özellikleri düzeni). Açılış noktası tıklanan ikona göre sekme seçer. Popup `VerticalOffset=10` ile
barın altında hafif boşluk bırakır; sağ kenarda da 12px boşluk (kök Border `Margin="0,0,12,0"`,
ekran sağ kenarına clamp'li açılır). Yüzey border'sız, yuvarlatılmış köşeler + yarı saydam
(`FlyoutSurface`: tema `ApplicationBackgroundBrush` rengi, %85 alfa — arkadaki içerik hafifçe
seçilir; her açılışta tazelenir). Açılışta Win11 tarzı
**yukarıdan aşağı** slide+fade animasyonu (`AnimateFlyoutOpen`: barın altından ~10px
kayarak iner, 170ms CubicEase EaseOut); kapanışta açılışın **tam tersi** animasyon
(`AnimateFlyoutClose`: içerik yukarı/barın altına doğru kayar + solar, 110ms — bir tık
daha hızlı, sonra `IsOpen=false`). Kapanış yolları (`CloseFlyouts` /
`CloseQuickSettings` / `CloseBrightness`): dış tıklama → `WM_ACTIVATEAPP` (topbar WndProc),
topbar'a tıklama (çapa butonları hariç — onlar toggle/re-target yapar), ESC
(`OnTopBarPreviewKeyDown`), toggle, AppMenu/tray/notification butonları. Popup yeniden
açılırsa bekleyen kapanış animasyonu `CancelPendingClose` ile iptal edilir (yeniden açılan
popup kapanmaz). Parlaklık flyout'u aynı açılış/kapanış animasyonlarını kullanır.

Panel açıkken sayfa değişimi (`ShowPage`, `QuickSettingsPageMotion`) 220 ms `CubicEase`
EaseOut ile kayar ve solar. İleri (Main → Mixer / Network / Wi-Fi / Bluetooth / Yansıt)
giden sayfayı sola, yenisini sağdan 20 DIP içeri alır; geri tam tersidir. Aynı derinlikteki
sayfalar (Mixer ↔ Network gibi) yalnızca çapraz solar. `PageHost` `ClipToBounds` ile taşmayı
kırpar; giden sayfa tıklama almaz. Geçiş bitmeden yeni bir sayfa seçilirse önceki çift
bırakılır ve animasyon yeniden başlar. Flyout ilk açılışında bu kayma yoktur — açılış
animasyonu paneli zaten gösterir.

### Wi-Fi sayfası (ağ listesi + şifre paneli)

`WifiService` (wlanapi.dll) canlı ağ listesi: SSID, sinyal çubukları (`WifiService.SignalBars`),
güvenlik, bağlı / kayıtlı profilli işaretleme (2 sn poll; yalnızca sayfa açıkken).
Korumalı ağa tık → aynı sayfada şifre paneli (SSID, şifre kutusu, Bağlan / İptal, hata metni);
başarılı bağlantıda panel kapanır, liste yenilenir. "Wi-Fi ayarlarını aç" → Windows Settings.

### Bluetooth sayfası (cihaz listesi)

`BluetoothService` eşleşmiş cihazları listeler (`BluetoothDevice.GetDeviceSelector`);
bağlı olanlar `ConnectionStatus` ile işaretlenir. Radyo açık/kapalı özet altta.
"Bluetooth ayarlarını aç" → Windows Settings.

### Yansıt sayfası (4 mod)

`ProjectionService` + `QueryDisplayConfig` ile mevcut topoloji seçili gösterilir;
tıklama `SetDisplayConfig` ile uygular: **Yalnızca PC ekranı / Çoğalt / Genişlet / Yalnızca 2. ekran**.

### Gece ışığı tile (gerçek durum)

`NightLightService` (CloudStore best-effort) gerçek durumu okur; tile aktiflik rengini ona
göre çizer, tıklama gerçek toggle yapar (önce oturum override, sonra registry yazımı).

### Tile animasyonları

Tile aktiflik değişiminde **in-place** animasyon: ikon 90 ms "pop" (aktif 1.15× / pasif 0.9×,
AutoReverse + CubicEase) + 250 ms renk geçişi (`ColorAnimation`, QuadraticEase) — arka plan
accent/base arasında yumuşatılır. Devre dışı tile'lar `QsTileDisabled` ile 0.45 opaklıkta.

## Parlaklık flyout

`BrightnessFlyout` — Monitorian tarzı: master "Tüm Ekranlar" slider'ı + bağımsız monitör
kartları; 50ms debounced arka plan kuyruğu. `MonitorBrightnessService` + `MonitorNative`
(DDC/CI + WMI hibrit).

## Medya flyout

`MediaFlyout` — Windows SMTC (`MediaSessionService`, ADR-008) üzerinden şu an çalan medya:
yuvarlatılmış kapak (kapsızsa müzik glifi `E90C` yer tutucu) + başlık (kalın, tek satır,
metadata alınamadıysa uygulama adı fallback) + sanatçı; altında `E892` Önceki /
`E768`–`E769` Oynat-Duraklat (duruma göre glif canlı değişir) / `E893` Sonraki.
Flyout sabit **340px** genişliktedir (`MinWidth=MaxWidth=340`) — içerik değişince
(ör. oturum adı) genişlik sıçramaz.
Birden çok oturum aktifse üstte seçici satırı belirir: `E76B` ‹ uygulama adı › `E76C`
chevron'lar seçili oturumu halkada ilerletir; kontroller seçili uygulamaya gider.
Uygulama adı AUMID'den çözülür (`MediaAppNameResolver`, katmanlı):
Start Menü kısayolu AUMID eşleşmesi Windows'un gösterdiği adı verir (örn. Zen'in
`F0DC299D809B9700` → "Zen", YouTube Music Desktop'ın `com.github.th-ch.youtube-music` →
"YouTube Music"); eşleşme yoksa çalışan sürecin `FileDescription`'ı (örn. `chrome.exe` →
"Google Chrome"); o da yoksa basitleştirilmiş exe adı (örn. "Microsoft.ZuneMusic").
Seçim politikası **en son aktif** oturumdur (`MediaSessionSelector`).

- Üst bar öğesi `media` yalnızca oturum varken görünür (`_mediaActive`; Battery/Volume
  deseni) — oturum bitince kaybolur; sağ tık menüsünden kalıcı göster/gizle + sıralama.
- Tooltip: "Başlık – Sanatçı" (metadata yoksa uygulama adı).
- Flyout oturum bitince **kapanmaz**; "Çalan medya yok" metnine döner (kapatma yalnızca
  kullanıcı: dış tıklama / ESC / tekrar tıklama — QS/Brightness ile aynı animasyonlu kapanış).
- Açılışta `FlyoutSurface` yüzeyi tazelenir; QS ile aynı yukarıdan-aşağı slide+fade animasyonu.
- Oturum (kaynak) değişince içerik yönlü kaydırma + fade ile gelir: chevron › sağdan, ‹
  soldan, otomatik değişimde yönsüz fade (~150ms, CubicEase EaseOut); aynı oturumdaki
  parça/oynat-durdur güncellemeleri animasyonsuzdur.
- SMTC başlatılamazsa servis pasif kalır (öğe görünmez); 10 sn sonra yeniden dener.

## Tuzaklar

- Arka plan modu değişikliği pencereyi yeniden oluşturabilir (sağ tık menüsü yeniden açılır).
- `AllowsTransparency` ctor'da `BackgroundMode`'dan set edilir — XAML'e yazılmaz.
- Tooltip fontları Segoe UI (`AppTextFontFamily`) — MDL2 tofu sorunu yok.