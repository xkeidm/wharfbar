# ADR-016: Launchpad yüzleri

**Durum:** Kabul edildi
**Tarih:** 2026-10-12

## Karar

Launchpad tek penceredir. Yüz tektir: ortada açılan kutu. Eski `mac` ve `win8` yüzleri yoktur. `launchpad.style` eski config için durur; arayüz onu değiştirmez.

| Yüz | Yerleşim |
|-----|----------|
| tek kutu | Ekranın ortasında. Arkada duvar kâğıdı, üstünde tema renginde saydam örtü. Arama, sabitlenenler, son dosyalar, harf başlıklı uygulama grupları; altta hesap çipi ve seçilen araçlar. Telefon bağlıysa kutunun sağında dikey panel |

Açılış: kutu tarama bitmeden ekrana gelir. Başlat menüsü ve son dosyalar arka planda okunur, liste 16’şar uygulama eklenir. Dock’taki Launchpad karosu pencereyi açar veya kapatır. Windows logosu menüyü açar. Boşluğa tıklamak kapatır.

Başlat menüsü taraması dizin dizin yapılır. `ReparsePoint` ve okunamayan klasör atlanır (ADR-015 çökmesi).

Telefon paneli kutunun içinde değil, sağındadır ve yalnız gerçekten bağlı bir cihaz varken çizilir. Karttaki `ImageSet` iki görüntü taşır: ilki ekran fotoğrafı, ikincisi saydam telefon çerçevesidir. Fotoğraf çerçevenin iç deliğine üstten hizalanır; çerçeve yoksa fotoğraf çizilen bir telefon gövdesinin içinde kalır. Bildirim satırı karttaki `data:image` ikonunu kullanır; ikon yoksa harf kalır. Okuma salt okunurdur: önce `Microsoft.YourPhone` paketindeki `LocalState\StartMenu\StartMenuCompanion.json` (cihaz adı, `%` pil metni, Bluetooth `altText`, `ms-phone:filetransfer` varsa dosya eylemi, en fazla dört “N yeni …” özeti). Kart yoksa aynı paketin, yoksa `MicrosoftWindows.CrossDevice` paketinin `LocalCache\DeviceMetadataStorage.json` içindeki bağlı Android / `LTW` `DisplayName`. O da yoksa `HKCU\Software\Microsoft\YourPhone` `DeviceName`. Pil, Bluetooth ve bildirim bu kayıtlarda yoksa uydurulmaz; satırda o alan hiç yer almaz. Mesaj gövdesi, numara ve sertifika okunmaz.

## Alt şerit

Hesap çipi her zaman durur. Araçlar `LaunchpadConfig.Tools` (`launchpad.tools`) listesindedir: `documents`, `downloads`, `pictures`, `settings`, `widgets`, `plugins`, `power`. Liste yoksa hepsi görünür. Boş ve tanınmayan kimlikler atılır. Boş liste şeridi gizler; en az bir araç zorlanmaz.

Şeride sağ tık, Türkçe etiketli işaretli menüyü açar. Seçim `ConfigService.Save` ile aynı `config.json` dosyasına hemen yazılır. Güç açıksa sol tık mevcut güç menüsünü açar.

Kartın `CornerRadius` değeri 16 içeriği kıvrıma kırpar. Kart `Padding` değeri yanda 22, altta 18 DIP’tir. Düğme kutusu 40 DIP’tir; glif ortalanır.

## Reddedilen

- Üç ayrı süreç veya üç kalıcı pencere.
- Logoyu launchpad yapmak. Çıkış ve güç logo menüsünde durur; Windows 11 yüzünün güç düğmesi aynı `PowerActions` çağrılarını kullanır.
