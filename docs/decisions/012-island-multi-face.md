# ADR-012: Ada — metin yuvası, uydular, ikincil HUD, raf

**Durum:** Kabul edildi (2026-10-11). [ADR-013](013-topbar-depth.md) HUD sırasında iki uyduyu, uydu klavyesini, `lowBatteryPercent` eşiğini, SMTC aramasını ve üst bar derinliğini (saat paneli, kıvılcım, enerji tasarrufu okuması) ekler; aşağıdaki karar tablosu olduğu gibi durur.
**İlişki:** [ADR-010](010-island-clipboard-drop.md) formu ve [ADR-011](011-island-presence.md) karar tablosu durur. Bu karar hap yüzlerini, ikincil monitörü ve bırakma rafını genişletir.

## Karar

Hapta tek bir metin yuvası vardır. Boşta `island.idleLabel` seçer: varsayılan `clipboard` başlığı boş bırakır, pencere **Pano** yazar; `lastClip` son metin kaydının önizlemesini `Title` içine koyar. Yeni kopya, dosya bırakma ve sayaçlar aynı yuvayı kısa süre kullanır. Müzik ve HUD altta duran işi silmez: sayaç bitince hap önceki yüze döner.

Uydular haptaki ek gliflerdir (`IslandSatellite`). En fazla iki tane, `FirstSatellite` ve `SecondSatellite`. Müzik `E90C`, pano `E8C8` (Segoe MDL2). Uydu tıklaması `ClickSatellite` çağırır ve hapın ana tıklamasına düşmez. Genişlik kromu uyduyu da sayar; ortalanmış hap `IslandPresence` yan boşluğunu korur. Boşta genişlik uydu kadar artabilir. AppBar 30 DIP kalır. İkinci `Topmost` pencere ve iç içe `Popup` yok. `AllowsTransparency` Mica ile eşleşmez.

Hover paneli 150 ms gecikmeyle açılır (`IslandPresence.HoverOpenDelayMs`). Pencere ikinci bir hover zamanlayıcısı kurmaz; mevcut ada saati `NextDeadlineMs` gelene kadar tikler ve bu süreye hover açılışı da girer.

Canlı yüzler, alt işi silmeden kısa süre görünür:

- Yeni metin kopyası (`NoteNewClip`, 40 karakter `IslandPruner` önizlemesi). Her kopyada nabız yok.
- Pil: takılınca ve yüzde bir kez %20 altına inince. Pil paneli yok. İpucu yüzdeyi içerir. Okuma mevcut `StatusService` / `PowerStatus` turundadır; ikinci bir anket yok.
- Dosya bırakma (`NoteFileDrop`, dosya adı). Bırakma nabzı durur. Yalnız birincil hap kabul eder ve `AddDroppedFiles` çağırır.

Müzik paneli ilerleme çubuğunu yalnız SMTC hem `Position` hem `Duration` verdiğinde ve süre 0'dan büyükken çizer. Çip ipucu aynı süre metnini kullanır; yüzde uydurulmaz. Panel açıkken oturum yaklaşık 1 sn'de bir yeniden okunur. Panel kapalıyken zaman çizelgesi okunmaz. Arama (`TryChangePlaybackPositionAsync`) eklenmedi. Birden çok oturumda mevcut chevron durur; çip başlığı seçili oturumun adıdır.

İkincil monitör kendi `IslandPresence` örneğini alır. Pano yüzeyi yalnız birincildedir (`SetClipboardSurface`). İkincil hap ada açıkken ve yüz boşta değilken ya da panel açıkken görünür: müzik ve ses/parlaklık/pil. Pano listesi, dosya bırakma ve pano uydusu yoktur. İkinci `IslandService` ve ikinci pano dinleyicisi yok.

Raf yolları saklar, kopyalamaz. Açma `IslandService.CanOpenShelfFile` kapısındandır. `Drop` dışını silmek hâlâ yasaktır; silme yolu `Drop` önekini doğrular.

`idleLabel` `lastClip` iken ve boşta yüz boş olmayan bir önizleme gösterirken hap kendi penceresine alınır ve `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` yalnız o pencereye uygulanır. Üst çubuk karede kalır. Sabit "Pano", müzik ve HUD sırasında hap çubuğa döner.

`SystemParameters.ClientAreaAnimation` false ise hap genişliği, hover ölçeği ve çapraz solma süreleri 0'dır. Değer animasyon başlarken okunur.

Klavye: hap odaklanabilir; erişilebilir ad yüzü söyler (Pano, parça adı, Ses %, Parlaklık %, kopya/pil/bırakma metni). Odak konturu görünür. Enter ve Space tıklama ile aynı yolu açar. Esc mevcut kapanış yolunu kullanır. Müzik gövdesinde Sol önceki, Sağ sonraki parçadır (`MediaSessionService.Previous` / `Next`). Pano arama kutusu odaktayken oklar çalınmaz.

`island.idleLabel` soft stringdir (`clipboard` / `lastClip`). Bilinmeyen değer `clipboard` olur. `schemaVersion` artmaz.

## Kapsam dışı

Bildirim önizlemesi, takvim, kamera aynası, yansıtma, söz, görselleştirici, Bluetooth bağlanma bildirimi, indirme ilerlemesi, eklenti galerisi, saati veya bildirim zilini adaya taşımak ve "medyayı durum şeridinde yalnız çalarken göster" bu kararda yoktur.
