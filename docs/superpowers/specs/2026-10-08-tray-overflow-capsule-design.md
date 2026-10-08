# Üst bar tepsi kapsülü — elle seçilen ikonlar, kalanı hapta

**Tarih:** 2026-10-08
**Durum:** Onaylandı (davranış, kapsül görünüşü, kayıt, bağ, hatalar)
**Kapsam:** Birincil üst barda aynalanan tepsi ikonlarının `trayPromoted` ile bölünmesi; `trayOverflow` okunun kendi kapsülünü açması
**İlgili:** [ADR-009](../../decisions/009-tray-mirror-toolbar-enumeration.md) revize edilir. Windows’un kendi gizli tepsi ikonları hâlâ okunmaz.

## Kararlar

| Konu | Seçim |
|------|--------|
| Kaynak | Bugünkü ayna anlık görüntüsü. Yeni okuma yok |
| Bölme | Seçilmemiş ikon kapsülde başlar. Üst barda yalnız `trayPromoted` |
| Seçme | Sürükle-bırak ve sağ tık menüsü |
| Ok | `trayOverflow` Windows gizli simgeler penceresini açmaz. Kapsülü açar |
| Görünüş | Okun altından büyüyen hap. İkonlar tek sıra. Ad, üzerine gelince |
| Kayıt | `topBar.trayPromoted`, soldan sağa. `schemaVersion` 1 kalır |
| Monitör | Yalnız birincil bar |

## Hedef

Üst barda her aynalanmış tepsi ikonu yan yana durmasın. Kullanıcı hangilerinin barda kalacağını seçsin. Kalanlar, okun açtığı kapsülde dursun. Sol tık, çift tık ve uygulamanın kendi menüsü bugünkü iletimle çalışsın.

## Kapsam dışı

- Windows’un kendi gizli (overflow) ikonlarını okumak veya o pencereyi açmak
- Ayna kaynağını, sistem ikonu filtresini veya tıklama iletiminin Win32 yolunu değiştirmek
- İkincil monitörde kapsül
- Ayar sayfasında ikon listesi
- Birden çok ikonu tek sürüklemede taşımak

## Görünüş

Ok, tepsi kümesinin sağında durur. Glif bugünkü `E70E` kalır. Kapsül okun altında, okun yatay merkezine hizalı açılır. Çalışma alanına clamp edilir.

Tek satırda hapdır (`CornerRadius` yüksekliğin yarısı). Satır çalışma alanına sığmazsa alta sarılır ve köşeleri yuvarlak bir dikdörtgen olur. Dolgu, mevcut flyout yüzeyiyle aynıdır. Açılış ve kapanış, diğer üst bar flyout’larının kayarak solma animasyonudur.

İkonun adı tooltip’tir. Kapsülde ikon yokken gövde tek satırdır: **Gizli simge yok**.

## Kayıt

`TopBarConfig.TrayPromoted` : `List<string>`. JSON alanı `topBar.trayPromoted`. Eksik alan boş listeye çözülür. Katalog sürümü değişmez.

Anahtar bugünkü `TrayMirrorItem.Key` değeridir. Klasik tepside komut numarası ve ipucu, 25H2’de düğme adı. Karşılaştırma ordinal’dir. İpucu veya ad değişirse ikon yeni anahtar alır ve kapsülde başlar. Eski anahtar listede kalır.

Listede olup anlık görüntüde olmayan anahtar durur. İkon geri gelince listedeki yerine oturur. Yinelenen anahtarın ilki geçerlidir, sonrakiler yok sayılır.

Bölme, UI iş parçacığında, yayımlanan anlık görüntüye uygulanır:

- Üst bar: anlık görüntüde bulunan `trayPromoted` anahtarları, liste sırasıyla
- Kapsül: anlık görüntüde olup listede olmayanlar, anlık görüntü sırasıyla

## Ok ve monitör

Kapsül ve bölünmüş ayna yalnız birincil bardadır. Ok, üçü birden doğruysa görünür: bar birincil, `trayOverflow` katalogda açık, anlık görüntüde en az bir ikon var. Diğer barlarda ok yoktur.

Katalogdan `trayOverflow` kapatılırsa kapsül açılamaz. Üst barda kalmış ikonlar durur. `trayMirror` kapatılırsa tutulan ikonlar barda çizilmez; kapsüldekiler okla açılır.

## Bağ

Ok, kapsülü açar ve kapatır. Dışarı tıklama, Esc ve oka tekrar basmak kapatır. `TrayOverflowInvoker` bu düğmeden çağrılmaz. Başka çağıranı kalmazsa invoker bu değişiklikle silinir.

Sol tık ve çift tık, barda ve kapsülde, bugünkü `TrayMirrorService.Click` yolunu kullanır. Çapa, tıklanan hücrenin ekran dikdörtgenidir. İletimden sonra kapsül kapanır.

Sağ tık her iki yerde de WharfBar menüsünü açar. Kapsül açık kalır.

- Barda: **Gizle**
- Kapsülde: **Üst barda tut**
- Her ikisinde: **Uygulama menüsü**

Tut ve gizle listeyi günceller ve mevcut `ConfigService.Save` yolunu çağırır. Uygulama menüsü kapsülü kapatır ve bugünkü sağ tık iletimini, hücrenin çapasıyla çalıştırır.

Sürükleme tıklama sayılmaz. Bırakma bitince veya iptal olunca gelen tıklama yutulur.

- Kapsülden tepsi bölgesine bırakmak, anahtarı imlece en yakın aralığa ekler. Aralık, ilk ikondan önce, ikonların arası ve son ikondan sonradır
- Üst bardaki bir ikonu başka bir aralığa bırakmak, anahtarı listede taşır
- Üst bardan okun üstüne veya açık kapsüle bırakmak, anahtarı listeden çıkarır
- Kapsüldeki ikonu okun üstüne bırakmak listeyi değiştirmez
- Hedefin dışına bırakmak listeyi değiştirmez
- Menüden gizlenen ikon kapsülü kendiliğinden açmaz
- Başarılı tutma veya gizlemeden sonra kapsül, içinde ikon kaldıysa açık kalır

Bar ve kapsül aynı hücreyi kullanır. Tıklama, menü ve sürükleme tek yoldan gider.

## Hatalar

Boş veya başarısız tepsi okuması bugünkü son-iyi-anlık-görüntü kuralına uyar. `trayPromoted` silinmez. Sürükleme sırasında ikon anlık görüntüden düşerse bırakma iptal olur, listeye yazılmaz. Kapsül açıkken anlık görüntü yenilenirse kapsül kapanmaz; ikonlar yerinde tazelenir. Kayıt yazması başarısız olursa bar çökmez. Seçim bu oturumda bellekte kalır. Sonraki başarılı kayıt onu yazar.

## Test

Saf bölme fonksiyonu, anlık görüntü anahtarları ve `trayPromoted` alır. Şunlar kilitlenir:

- Boş kayıtta hepsi kapsüldedir
- Üst bar, liste sırasını korur
- Kapsül, anlık görüntü sırasını korur
- Anlık görüntüde olmayan anahtar listede kalır
- Yinelenen anahtar bir kez çizilir
- Boş okuma, kayıtlı listeyi silmez
- Ok görünürlüğü: birincil bar, katalog açık ve en az bir ikon
- Eksik `trayPromoted` boş listeye döner
- `schemaVersion` 1 kalır

Mevcut tıklama iletimi testleri aynı kalır. Sarma, saf bir genişlik hesabıyla kilitlenir: adım genişliği ve tavan verilince satır sayısı.

## Doküman

Uygulama aynı değişiklikle şunları günceller:

- ADR-009: ok artık yerel overflow açmaz. Ayna ikonları bar ve kapsül diye bölünür. Windows’un kendi gizli ikonları okunmaz
- `.agent/boundaries.md`, `.agent/screens/topbar.md`, `.agent/project.md`
- `docs/00-overview.md` ve tepsiyi anlatan mimari satırlar

## Birimler

| Birim | İş |
|-------|----|
| `TrayPromotedSplit` | Anahtar listesini bar ve kapsül sırasına böler. Satır sayısını hesaplar. WPF tipi yok |
| `TopBarConfig.TrayPromoted` | Kayıt. Eksik JSON boş liste |
| Birincil `TopBarWindow` | Hücre, kapsül, menü, sürükleme, ok görünürlüğü |
| `ConfigService` | Alanı mevcut kayıt yoluyla yazar |
