# ADR-011: Ada — şimdi çalıyor, ses/parlaklık HUD, tek popup

**Durum:** Kabul edildi (2026-10-07)
**İlişki:** [ADR-010](010-island-clipboard-drop.md) formunu korur. Bu karar kompakt göstergeyi ve paneli genişletir.

## Karar

| Konu | Seçim |
|------|--------|
| Yer | Kompakt haller **30 DIP AppBar'ın içinde** ortalanmış hap. AppBar / work area yeniden boyutlanmaz. İkinci `Topmost` pencere yok |
| Panel | Çok satırlı haller bugünkü **tek** `IslandPopup`. İlk kare, ölçülmüş genişlikle hapın yatay merkezine oturur (`IslandPopupAlign.WidthForPlacement`; XAML genişliği yedek değil, ölçüm yokken panel görünmez). Sonraki genişlik değişimi yeniden ortalar. Üst kenar hapla birleşir (~2 DIP bindirme, aynı dolgu). Müzik ve pano aynı gövdede yer değiştirir. İç içe `Popup` yok |
| Boşta | ~108 DIP gösterge (`IslandIdleDip`, 24 DIP yükseklik): pano glifi + **Pano**. Hover pano panelini açar; ayrılınca ~300 ms. Tıklama sabitler. Kayıt yoksa aynı yüzeyde kısa boş durum. Dosya bırakma hapta kalır |
| Müzik | `island.musicPresence`: `persistent` (**varsayılan**) veya `transient`. Soft alan; `schemaVersion` artmaz |
| `persistent` | Oturum varken hap nota + tek satır parça adı (ellipsis, `IslandPresence.TitleMaxWidth`). Hover paneli açar; ayrılınca ~300 ms. Tıklama sabitler |
| `transient` | Hap boşta kalır. Çalma başlangıcı, parça değişimi, oynat/duraklat paneli ~2,5 sn açar. O pencerede tıklama sabitler |
| Sabit panel | Dış tıklama, Esc, hapa tekrar tıklama, `CloseFlyouts` (ADR-010 kapanış yolları) |
| HUD | Karıştırıcı veya parlaklık paneli **açık değilken** ses/parlaklık değişimi hapı ~1,6 sn tek satır sayaca çevirir, sonra önceki hapa döner. Tıklama **mevcut** karıştırıcıyı veya parlaklık flyout'unu açar; ada onların UI'ını kopyalamaz |
| Pano | Müzik panelindeki düğme aynı popup gövdesini pano listesine çevirir (~150 ms çapraz solma); geri müzik. Boşta hover ve tıklama panoyu doğrudan açar |
| Sağ şerit | `volume`, `brightness`, `media` `DefaultOrder`'dan çıkar, katalogda kalır. Kayıtlı `statusItems` soyulmaz (`statusCatalogVersion` artmaz). Görünürlerse bugünkü flyout'lar açılır |
| Kapsam | Yalnızca birincil monitör (`AttachIsland`). Bildirim, takvim, AirDrop, dosya aktarımı, yansıtma, mini widget yok |

Zamanlama, hap yüzü ve HUD önceliği `IslandPresence` içindedir (WPF tipi yok, xUnit). Pencere tek atımlık süre sıfırlayan bir zamanlayıcı kullanır.

## Görsel

Win11 kontrol dolgusu + 1 px `ControlStrokeColorDefaultBrush`, hap `CornerRadius` ile 30 DIP barın içinde dikey ortada. Boşta genişlik tabandır (~108 DIP, etiket **Pano**); şimdi çalıyor ve sayaç bu tabanın üstüne genişler (~200 ms `CubicEase`). Hover dolgu değişir ve hap ~4 DIP genişler. Vurgu dolgusu `AccentFillColorDefaultBrush` (statik mavi `SystemAccentColorPrimaryBrush` değil); sayaç dolgusu seviye ile akar. Apple glifi / siyah kapsül yok. Açık panel hapla tek siluet: alt köşeler düz, dolgu `FlyoutSurface`, gövde hap genişliğinden büyür (~200 ms, üst orta, solma). Aralık yok. Kapanış tersi ve daha kısa; kapalıyken yine yalnız kapsül.

## Sonuçlar

- `volume` / `brightness` / `media` varsayılan şeritte görünmez; ada HUD ve şimdi çalanı taşır.
- Parlaklık tuşu `WmiMonitorBrightnessEvent` ile duyulur. Olayı olmayan dış monitör, bir sonraki `RefreshMonitors` farkına kadar HUD göstermeyebilir.
- Canlı ışık/koyu ekran görüntüsü bu kararla alınmadı.
