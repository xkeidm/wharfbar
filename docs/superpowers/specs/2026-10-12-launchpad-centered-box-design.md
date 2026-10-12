# Launchpad: ortadaki tek kutu

**Tarih:** 2026-10-12
**Durum:** Onaylandı (tarayıcı seçimi A, “Böyle kalsın”)

## Amaç

Launchpad tek yüzdür. Mac ve Windows 8 yüzleri yoktur. Kutu ekranın ortasında açılır. Arkada duvar kâğıdı görünür; üstünde tema renginde saydam bir örtü vardır. İçerik sakin bir Windows 11 + Mac karışımıdır: arama, sabitlenenler, son dosyalar, uygulama ızgarası. Gösterişli bir geçiş yoktur.

## Yerleşim

- Pencere birincil monitörü kaplar. Katmanlı pencerede Mica yoktur; sistem örtüsü `Dwm` `None` ve saydam kompozisyonla temizlenir. Tema, DPI veya ekran değişince örtü yeniden kurulur.
- Örtü, `SolidBackgroundFillColorBaseBrush` rengini alfa 88 ile kullanır. Tema fırçası yoksa taban `rgb(28, 28, 32)` olur.
- Kart ortadadır. Genişlik 680, en fazla yükseklik 740, köşe yarıçapı 16. Arka plan opak tema dolgusudur; üstte hafif vurgu parıltısı ve gölge vardır.
- Açılış 280 ms, `CubicEase` EaseOut: opaklık 0’dan 1’e, ölçek 0.98’den 1’e, Y 18’den 0’a.
- Örtüye tıklamak ve Escape kutuyu kapatır. Odak kaybında kapanma, kapanış sürerken ikinci `Close` çağırmaz.

## İçerik

Sıra yukarıdan aşağı:

1. Arama. Yer tutucu: “Uygulama, ayar ve belge ara”.
2. Sabitlendi. Kaynak `dock.items`. En fazla 8 büyük karo. Boş yol atlanır. Sabit yoksa bölüm gizlenir.
3. En son. `SpecialFolder.Recent` içindeki kısayollar, yeniden eskiye. Önizlemede 6 öğe; fazlası “Tümünü göster” / “Daha az göster”. Yaş metni Türkçe dilimlerdedir.
4. Tümü. Uygulamalar ilk harfin altında gruplanır (`A`, sonra o harfin uygulamaları). Harf olmayan adlar `#` altındadır. Izgara varsayılandır; “Görünüm: Liste” aynı harf başlıklarıyla satır gösterir. Arama sabitleri, son dosyaları ve uygulamaları süzer. Eşleşme yoksa “Eşleşen uygulama yok”.

Kutu, katalog bitmeden açılır. Tarama UI iş parçacığının dışında biter; ikonlar 16’şar eklenir. Aynı süreçte ikinci açılış, daha önce okunmuş ikonları yeniden diskten çekmez.

Alt şerit: kullanıcı harfi ve `Environment.UserName`, klasörler (Belgeler, İndirilenler, Resimler), Ayarlar, Widget, Eklenti, güç menüsü. Telefon paneli kutunun sağındadır ve yalnız Phone Link gerçekten bağlıysa görünür. Ekran fotoğrafı telefon çerçevesinin içinde durur.

## Giriş

- Dock’taki Launchpad karosu pencereyi açar veya kapatır.
- Sol üst Windows logosu her tıklamada liste menüsünü açar (Ayarlar, Profil, kilitle, uyku, kapat, yeniden başlat, WharfBar Ayarları, hakkında, WharfBar’ı kapat). Logo launchpad açmaz.
- `launchpad.style` eski config için durur ve `win11` / `mac` / `win8` olarak normalize edilir. Arayüz bu değere göre yüz değiştirmez.

## Katalog

Başlat menüsü `*.lnk` taraması dizin dizin yapılır. `ReparsePoint` ve okunamayan klasör atlanır. `SearchOption.AllDirectories` kullanılmaz.

## Kapsam dışı

- İkinci veya üçüncü yüz.
- Apple logosu veya SF Symbols.
- Telefon satırını bağlantı yokken göstermek.
- Logodan launchpad açmak.
