# WharfBar

Windows’ta bir menü çubuğu ve bir dock: ekran alanını ayıran ince bir üst şerit, sabitlenmiş ve çalışan uygulamalar için bir dock. WharfBar, Windows, macOS ve Linux masaüstülerinden esinlenir. Görünüm Windows Fluent’tir; Apple markası ve ikon dili yoktur.

**English:** [README.md](README.md)

## Nedir

WharfBar, Windows masaüstüne kendi üst çubuğunu ve dock’unu çizer.

Üst çubuk gerçek bir AppBar’dır (`ABE_TOP`, 30 DIP). Büyütülmüş pencereler onun altında kalır. Dock her zaman üstte duran bir örtüdür. WharfBar açıkken Windows görev çubuğu gizlenir; normal çıkışta önceki haline döner.

Her uygulama kendi menüsünü taşır. Üst çubukta saat, durum, tepsi ve kontroller vardır. Bir seçim çakışırsa sıra şöyledir: düşük kaynak kullanımı, sonra Windows’a özgü Fluent görünüm, sonra basitlik.

## Neden çalıştırılır

- Saat, CPU / GPU / RAM, ağ, pil, bildirimler ve hızlı ayarlar tek şeritte durur; çalışma alanı gerçekten ayrılır.
- Kapalı olanlar dahil sabitlenmiş uygulamalar ve çalışan uygulamalar aynı dock’ta, uygulama başına bir kutuda durur. Windows görev çubuğu o sırada görünmez.
- Pano geçmişi, şimdi çalan parça ve kısa ses, parlaklık, pil okumaları ortadaki haptadır.
- Wi-Fi, ekran yansıtma, monitör parlaklığı ve medya kontrolleri sık işleri çubuktan karşılar.
- Boşta yapılan iş seyrektir. Bütün dock’lar tek bir pencere sayımını paylaşır (yaklaşık 3 saniyede bir). İstatistikler 4 saniyede bir, arayüz iş parçacığının dışında alınır ve kapatılınca durur. Tepsi aynası 4 saniyede bir okunur; ikon görüntüsü her turda yeniden çekilmez.

## Özellikler

### Üst çubuk

Açık olan her monitörün kendi çubuğu vardır ve her çubuk kendi monitöründe AppBar olarak kaydolur. Görünürlük `monitors` ayarındadır.

- **Sol:** çizilmiş Windows logosu menüsü (Windows Ayarları, profil, kilitle, uyku, kapat, yeniden başlat, WharfBar ayarları, Hakkında ve çıkış) ve tek satır `CPU · GPU · RAM`. Üzerine gelince kıvılcım ve ağ hızı açılır. Birden fazla GPU ayrı yazılabilir (`topBar.showGpuPerAdapter`). GPU sayacı yoksa o bölüm görünmez. Sıcaklık yoktur.
- **Orta:** ada hapı.
- **Sağ:** sağ tık menüsünden veya Ayarlar → Üst Çubuk’tan gösterilip gizlenen ve sıralanan durum şeridi. Varsayılan sıra: tepsi aynası, gizli simgeler oku, saat, bildirimler, hızlı ayarlar, ağ, pil. Ses, parlaklık ve medya katalogdadır ve başta kapalıdır; ada bunları zaten gösterir. Kayıtlı sıra korunur.
- Saat tek satır `tr-TR` biçimindedir. Tıklanınca bildirim metni ve aylık takvim açılır. Zil `Win+N` gönderir.
- Arka plan Mica, saydam veya renkli olabilir; renkli modda opaklık kaydırıcısı vardır. Mica ile `AllowsTransparency` birlikte kullanılmaz.

### Ada

Birincil çubukta hap boşta **Pano** yazar. `island.idleLabel` değeri `lastClip` ise son kaydı gösterir. Şimdi çalan parçada genişler (`island.musicPresence` varsayılanı `persistent`, diğer seçenek `transient`). Ses, parlaklık ve pil için kısa süre sayaç olur. Yanında en fazla iki uydu durur. Pano geçmişi ve dosya bırakma birincil hapa aittir. İkincil monitörün hapı yalnızca müziği ve bu sayaçları gösterir. Çubuk 30 DIP kalır; panel hapın altında açılır.

Düşük pil eşiği `island.lowBatteryPercent` (varsayılan 20, kabul edilen aralık 5–50).

### Dock

Her zaman üstte duran bir örtü: alt-sol, alt-orta, alt-sağ veya sol kenar. Otomatik gizlenebilir; başka bir uygulama tam ekranken gizlenir.

- **Sol:** Windows görev çubuğunun `User Pinned\TaskBar` kısayolları, görev çubuğu sırasında, çalışmayanlar dahil. Ardından `dock.items` içinde olup orada olmayanlar. Çalışan bir pin aynı kutuda kalır.
- **Sağ:** sabitlenmemiş çalışan uygulamalar. Uygulama başına bir kutu (kanonik yol, AppUserModelID veya süreç kimliği). Fazla pencereler sayı rozeti ve **Pencereler** alt menüsüdür. Küçültülmüş pencere ikinci kutu olmaz.
- Simge sabit boydadır. Varsayılan glif 24 DIP, düğme 40, aralık 4 DIP. Üzerine gelince düğmenin içi dolar; bu monitördeki pencerelerin DWM küçük resmi açılabilir. UI Automation ilerleme bildirdiğinde ikonun altında çubuk belirir. Dikkat isteyen simge kısa bir vurgu nabzı alır.
- Sabitle ve kaldır `dock.items` yazar. Hedef dosya veya klasörse `User Pinned\TaskBar` içine `.lnk` de yazılır. `Taskband`’a dokunulmaz.
- Sürükleyerek sıralanır. Explorer’dan `.lnk`, `.exe`, `.bat` veya `.cmd` bırakılır. Sağ tık: başlık kartı, sabitle veya kaldır, görevi sonlandır, pencereyi kapat, yönetici olarak çalıştır, dosya konumunu aç, yeni pencere, pencere listesi.
- Arka plan Mica, saydam veya renkli olabilir ve üst çubuktan bağımsız seçilir.
- **Launchpad** (en solda), **İndirilenler** ve **Geri Dönüşüm Kutusu** (en sağda) yerleşik karolardır; pin klasörüne yazılmaz. Klasör pini yığın açar. Store paketinin simgesi çözülürse o simge, çözülmezse harf kullanılır. Rozet, çalışan işareti ve küçük resim yalnızca o dock’un monitöründeki pencereleri sayar.

### Görev çubuğu

WharfBar çalışırken Windows görev çubuğu gizlidir. Normal çıkış, açılıştan önceki durumu geri yükler. Süreç zorla öldürülürse bir watchdog görev çubuğunu yaklaşık yarım saniyede geri getirir. Üstte ayrılan şerit ayrı bir konudur; aşağıdaki uyarıya bakın.

### Hızlı ayarlar

Hızlı ayarlar, ses veya ağ simgesinden açılan tek flyout: **Main**, **Mixer** ve **Network**.

- Wi-Fi: `wlanapi` ile canlı ağ listesi, bağlanma ve kopma. Profiller düz metin olarak kaydedilir. Radyo kapalıyken liste boştur. Gizli ve kurumsal ağlar Windows Ayarları’na bırakılır.
- Bluetooth: radyo ve eşleşmiş klasik ile LE cihazlar. Cihaza bağlanmak Windows Ayarları’ndadır.
- Gece ışığı, ayar yazılabiliyorsa gerçek durumu izler. Yazılamıyorsa karo Windows Ayarları’nı açar.
- Yansıt: yalnızca PC ekranı, çoğalt, genişlet veya yalnızca ikinci ekran.
- Enerji tasarrufu karosu sistem anahtarını izler ve onu çevirir.
- Kaplama karosu renk örtüsünü açıp kapatır.

### Parlaklık

Ayrı bir flyout: bütün ekranlar için bir ana kaydırıcı ve monitör başına bir kart. Masaüstü monitörler DDC/CI, dahili paneller WMI kullanır. Yazma, 50 ms gecikmeli bir arka plan kuyruğundan geçer.

### Medya

Windows SMTC. Durum şeridindeki medya simgesi yalnızca bir oturum varken görünür. Flyout kapak, başlık, sanatçı, oynat/duraklat, önceki ve sonraki düğmelerini gösterir. Birden fazla uygulama aktifse oturum seçici çıkar. Ada, şimdi çalanı hapta tutabilir.

### Kaplama

Birincil monitörde tıklamayı geçiren tam ekran renk örtüsü. Hızlı ayarlardan veya kısayolla açılır (varsayılan `Ctrl+F11`). Renk, opaklık ve kısayol Kaplama ayar sayfasındadır.

### Tepsi

WharfBar’ın kendi bildirim simgesinde çıkış maddesi **WharfBar'ı kapat** vardır. Birincil çubukta üçüncü taraf tepsi ikonları belgelenmiş araç çubuğu mesajlarıyla okunur ve tıklama iletilir. Seçilen simgeler okun arkasındaki bir hapta durabilir; ok, Windows’un gizli simgelerini de `Win+B` ile açar. Sistem simgeleri elenir. Windows’un taşma alanında tuttuğu simgeler orada kalır. Bu araç çubuğunun kalktığı Windows 11 25H2 ve sonrasında ayna UI Automation kullanır. İkincil çubukta yalnız ok vardır. İkonlar başka sürecin belleğinden okunmaz.

### Launchpad ve eklentiler

Launchpad karosu duvar kâğıdının üstünde, ortada tek bir yüzey açar. Uygulama taraması bitmeden açılabilir. Uygulamalar harf başlıklarının altında gruplanır. Windows logosu Launchpad’i değil, logo menüsünü açar. Telefon sütunu yalnız bir telefon bağlıyken görünür. Ad, pil ve bildirim sayısı Your Phone başlangıç kartından gelir; ekran görüntüsü telefon çerçevesinin içindedir. Eksik alan gizlenir.

Widget ve Eklenti, Ayarlar → Eklentiler ile aynı kataloğu açar. Çalışan parçalar ada görselleştiricisi, Bluetooth bağlantı bildirimi, kamera aynası ve masaüstü saat ile takvimdir. `%AppData%/WharfBar/plugins/<klasör>/manifest.json` bir aç/kapa kaydıdır. Eklenti derlemeleri yüklenmez. Üst çubuk ve dock yuvaları kaydedilir, çizilmez.

### Yapılandırma, başlangıç, tek örnek, DPI

- Yapılandırma `%AppData%/WharfBar/config.json` dosyasındadır (`schemaVersion`). Ayar sayfaları: Genel, Görünüm, Dock, Ekranlar, Üst Çubuk, Kaplama, Ada, Eklentiler, Hakkında. Değişiklikler hemen uygulanır.
- Otomatik başlatma, Startup klasöründeki `WharfBar.lnk` kısayoludur (`AppConfig.autostart`). Registry Run anahtarı kullanılmaz.
- Adlandırılmış bir mutex ikinci örneğin çıkmasını sağlar.
- DPI farkındalığı Per-Monitor V2’dir (`app.manifest`).

## Gereksinimler

- Windows 10 sürüm 19041 veya üstü. Proje hedefi `net8.0-windows10.0.19041.0`.
-.NET SDK 8 veya 9. SDK 9 kurulumu `net8.0` hedefini derleyebilir.
- `global.json` bilinçli olarak yoktur. SDK’yı 8.x’e sabitlemek, makinede yalnız .NET 9 SDK varsa derlemeyi kırar.

## Derleme ve çalıştırma

```bash
git clone https://github.com/xkeidm/wharfbar.git
cd wharfbar

dotnet build WharfBar.sln
dotnet run --project src/WharfBar
```

Depo kökündeki `run.bat` dosyasına da çift tıklanabilir. Derler, ardından `WharfBar.exe`’yi ayrı süreç olarak başlatır; o konsolu kapatmak uygulamayı öldürmez.

> **Uyarı.** Uygulama açıkken ekranın üstünde bir şerit ayırır ve Windows görev çubuğunu gizler. Zorla sonlandırma (`Stop-Process -Force`, hata ayıklayıcıyı koparma, ham `dotnet run` konsolunu kapatma) `ABM_REMOVE` yolunu atlar. Görev çubuğu watchdog’u çubuğu yaklaşık 0,5 saniyede geri getirir. Ayrılan şerit, `explorer.exe` yeniden başlatılana kadar asılı kalabilir. Normal çıkış (tepsi **WharfBar'ı kapat** veya Alt+F4) ikisini de geri yükler. `dotnet run` penceresini kapatmak yerine `run.bat` kullanın.

İkinci örnek, adlandırılmış mutex yüzünden çıkar.

`dotnet test`, `tests/WharfBar.Tests` projesini çalıştırır (xUnit).

## Nasıl kapatılır

WharfBar bildirim simgesine tıklayıp **WharfBar'ı kapat** seçin veya çubuğa odaklanıp **Alt+F4** tuşlayın. Windows logosu menüsünde de aynı çıkış maddesi vardır.

Bu süreçte `Process.MainWindowHandle` `0` döner (`ShowInTaskbar=false` pencereye gizli bir sahip verir), bu yüzden bir betikten `CloseMainWindow` işe yaramaz. Pencereyi PID ile bulup `WM_CLOSE` gönderin.

## Proje yerleşimi

```
WharfBar.sln
src/WharfBar/
  Views/          TopBarWindow, DockWindow, SettingsWindow, flyout'lar
  Views/Pages/    General, Appearance, Dock, Displays, TopBar, Veil, Island, Plugins, About
  Native/         AppBar, Dwm, ShellIcon, tepsi, DDC/CI
  Models/         AppConfig ve ilgili kayıtlar
  Services/       config, dock, durum, tepsi, ada, hızlı ayarlar
  app.manifest    Per-Monitor V2 DPI
tests/WharfBar.Tests/
docs/
```

Kod kökü `src/WharfBar/`. Arayüz `Views/` altındadır; klasör adı Win32 ile karışmasın diye bu ad kullanılır.

## Dokümanlar

- [docs/00-overview.md](docs/00-overview.md) — amaç, hedefler ve kapsam
- [docs/01-architecture.md](docs/01-architecture.md) — klasörler, servisler ve config
- [docs/04-gotchas.md](docs/04-gotchas.md) — AppBar, Mica ve DPI tuzakları
- [AGENTS.md](AGENTS.md) — derleme komutları ve ajan dizini

## Katkı

Katkı rehberi ve davranış kuralları İngilizcedir: [CONTRIBUTING.md](CONTRIBUTING.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).
Hata ve özellik istekleri GitHub issue şablonlarını kullanır.

## Durum

Geliştirme aşamasında. Ürün bu çalışma ağacıdır; numaralı bir sürüm yoktur.

## Lisans

[MIT](LICENSE). [WPF-UI](https://github.com/lepoco/wpfui) de MIT’tir.
