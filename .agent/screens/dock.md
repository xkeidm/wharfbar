# Ekran: Dock

`src/WharfBar/Views/DockWindow.xaml` (+ `.xaml.cs`), `Views/Controls/DockStrip.cs`,
`Views/Controls/DockItemCell.xaml`.

Görev çubuğu (Windows 11) tarzı şerit. Varsayılan alt kenar. Ayarlardaki **Sol kenar**
(`dock.position` = `left`) plakayı sol kenarda dik dizer, `ABE_LEFT` ile o kalınlıkta
çalışma alanı ayırır ve oto-gizlemeyi sola kaydırır. Alt konumlar (`bottom-left` /
`bottom-center` / `bottom-right`) yatay kalır. Plaka AppBar **değil** (ADR-001); `Topmost` overlay,
fiziksel piksel `SetWindowPos` ile yerleşir, WPF `Left/Top/Width` kullanılmaz. Auto-hide +
hit-test (`DockAutoHideController` + `DockHitTest`); tam ekran uygulamada gizlenir
(`ABN_FULLSCREENAPP`, oto-gizle kapalıyken de — şerit topmost olduğu için videonun üstünde kalırdı).

Auto-hide **kapalıyken** kardeş bir boyasız `ABE_BOTTOM` (`DockWorkAreaReservation`, ham
pencere, alpha 0) plaka yüksekliğinde tam genişlik ayırır. Duvar kâğıdı durur. Büyütülmüş
pencere bu çizgide kalır; masaüstü ikonları aynı work area kenarını kullanır.
Auto-hide açıkken kayıt kalkar; pencereler altı tam kaplar.

> **Pencere = plaka.** Büyütme headroom'u yok; hover ikonu plakadan taşırmıyor.
> `dock.backgroundMode`: `mica` (varsayılan, `AllowsTransparency=false`, gerçek DWM Mica,
> sistem yuvarlak köşe `DWMWCP_ROUND`) veya `transparent` / `tint` (katmanlı pencere, WPF köşe yarıçapı
> 14 DIP, plakanın dışı boş). Mica ile `AllowsTransparency` birlikte değildir
> (`docs/04-gotchas.md`). Mod değişince pencere yeniden kurulur (`RecreateDock`). Eski `dock.surface`
> (`mica` / `acrylic`) ikisi de `mica` moduna yüklenir. **Gölge yok.** Hover düğmenin içini doldurur.

## İçerik

Windows görev çubuğu düzeni: uygulama başına **bir** ikon
([spec §0 #10](../../docs/superpowers/specs/2026-10-04-dock-macos-redesign-design.md)).

- **Sol — Pinned** — önce Windows görev çubuğunun `User Pinned\TaskBar` kısayolları,
  kapalı pinler dahil, görev çubuğu sırasında (`Taskband\Favorites` yalnız okunur;
  ad blob'da yoksa dosya adı). Ardından `dock.items` içinde olup görev çubuğunda
  olmayanlar. Aynı hedef bir kez görünür. Uygulama çalışınca **aynı kutuda** kalır
  (`IsRunning`). Kapalı pinlerde kapsül yoktur. Sağda bir daha belirmez.
  “Dock'a sabitle / kaldır” `dock.items` yazar ve hedef gerçek bir dosya veya klasörse `User Pinned\TaskBar` içine `.lnk` yazar. `Taskband` yazılmaz.
- **Sağ — Running** — sabitlenmemiş çalışan uygulamalar, ada göre sıralı (eşit adlarda
  pencere tutamacı). `showRunningApps == false` iken bu taraf hiç üretilmez; kapalı
  pinler solda kalır.
- Küçültülmüş veya arkadaki pencere, uygulaması zaten şeritteyse **ikinci kutu olmaz**.
  Kimlik geçişli birleşir (`RunningAppsSource.Collapse`): kanonik exe yolu, AppUserModelId
  veya süreç kimliği. Aynı exe'nin AUMID'siz penceresi ve ikinci süreci de aynı kutudur.
  Çalışan listesine hiç girmemiş bir süreç sağda **süreç başına bir** kutu olur
  (pencere başına değil). "Pencereler" alt menüsü kutunun **bütün süreçlerinin**
  pencerelerini listeler (`MinimizedWindowsSource` önbelleği; UI thread'inde `EnumWindows` yok).
- Birden fazla pencere veya süreç tek kutuda kalır. `WindowCount` > 1 iken ikonun sağ
  üstünde sayı rozeti durur; tooltip `Ad (N)` olur. Çalışan kapsül değişmez: masaüstünde
  açık pencere varsa uzun accent çizgi, hepsi küçültülmüşse kısa nötr çizgi.
  Rozet, çizgi ve küçük resim yalnız merkezi **bu** dock'un monitöründe olan pencereleri sayar
  (ADR-014). Küçültülmüş pencerede merkez simge noktasından değil, geri yükleme
  dikdörtgeninden (`rcNormalPosition`) okunur; yoksa sabitlenmemiş kutu kaybolurdu.
  Cloak'lı küçültülmüş pencere çalışan kalır. Başka sanal masaüstünün cloak'ı sayılmaz.
  Pin listesi her monitörde aynıdır.
- **Launchpad** şeridin en solundadır. Glif dört yuvarlak karedir. Sol tık, ekranın ortasında kutuyu açar; kutu tarama bitmeden görünür, uygulamalar harf başlığı altında gelir. Arkada duvar kâğıdı görünür. Telefon bağlıysa kutunun sağında dikey panel açılır. Ekran fotoğrafı telefon çerçevesinin içindedir. Bildirim satırında uygulama ikonu vardır. Windows logosu menüyü açar. Kutunun alt şeridinde hesap çipi her zaman durur. Belgeler, İndirilenler, Resimler, Ayarlar, Widget, Eklenti ve Güç `launchpad.tools` ile seçilir; şeride sağ tık işaretli menüyü açar ve seçim hemen kaydolur. Düğme 40 DIP’tir, glif ortadadır; kart payı köşe kıvrımının dışında kalır.
  Erişilemeyen `Programlar` bağlantısı atlanır. Widget ve eklenti galerisi kutunun altındadır. `dock.items` ve User Pinned
  klasörüne yazılmaz.
- **İndirilenler** pin grubunun hemen ardından, **Geri Dönüşüm Kutusu** şeridin en sağında
  durur. İkisi de `dock.items` ve User Pinned klasörüne yazılmaz.

Eşleşme (`PinMatches`): tam yol, büyük/küçük harf duyarsız (`\\?\` ve `\??\` önekleri
soyulur); pin yolu bir AppUserModelId ise çalışan `Id` veya grubun diğer AUMID'leri;
kayıt hâlâ `.lnk` ise çözülen hedef yol veya kısayolun AppUserModelId'si. Klasör pinleri
`explorer.exe` ile birleşmez. Aynı dosya adını paylaşan iki farklı exe birleşmez
(`System32` ile `SysWOW64` de ayrı tam yoldur). Farklı exe adları, ortak AppUserModelId
veya süreç kimliği yoksa ayrı kalır. `User Pinned` sabitlemede `.lnk` alır; `Taskband` yazılmaz (ADR-005).

Şerit **tek parça** — bölümler arasında ayraç yok (macOS denemesi reddedildi,
[spec §0 #9](../../docs/superpowers/specs/2026-10-04-dock-macos-redesign-design.md)).

## Plaka

Özel `DockStrip : Panel`. Slot genişliği düğmedir, glif değil. Ölçü `DockPlateMetrics`
(saf, testli): düğme = ikon + 16 DIP (her yanda 8), düğmeler arası **4 DIP**, plaka payı
**10×8 DIP**. Varsayılan ikon **24 DIP** → düğme 40, plaka yüksekliği **56 DIP**. Eski
28 DIP varsayılanı yüklemede 24'e çekilir; 16–36 aralığındaki diğer değerler kalır.

Hover dolgusu `ControlFillColorSecondaryBrush`, yarıçap 4 DIP (`HoverPlate`). Kare, düğmenin
üstünde durur; altta gösterge için **8 DIP** şerit kalır (plaka düğmeden 8 DIP kısadır). İkon
büyümez. Masaüstünde açık pencere (`IsOpenOnDesktop`): kalıcı kare + ikonun altında,
plakadan **3 DIP** aralıkla, **16×3 DIP** kapsül (24 DIP ikonda; genişlik glifle ölçeklenir),
alttan 2 DIP, renk `AccentFillColorDefaultBrush`. Alta alınmış uygulama (`IsRunning` ve
bütün pencereler `IsIconic`): kare yok, altta **8×3 DIP** nötr mini çizgi
(`TextFillColorPrimaryBrush`). Kapalı pinde çizgi yok; dolgu yalnız hover'da. Birden fazla pencere/süreçte çizginin
yanında değil, glifin sağ üstünde accent sayı rozeti (`WindowCount`) durur. Tıklama alta
alınmış pencereyi yine geri yükler. Tooltip sistem stili
(250 ms gecikme, 8 sn); sayı 1'den büyükse `Ad (N)`. Kart çerçevesi ve üst ışık çizgisi yok.

`DockMagnification` / `DockStripLayout` saf ve testli kalır; canlı şerit `MaxScale = 1` basar.
Pencere `PlateWidth` / `PlateHeight` kadardır (boş dock bir düğme bütçeler). Hover'da `SetWindowPos` yok.
ScrollViewer yoktur — çok sayıda ikon pencere genişliğine kırpılır. `dock.magnification` JSON'da durur ve okunur, uygulanmaz.
İkon belirme/kaybolma sırasında plaka genişliği şeridin canlı genişliğini izler (`DockMotion`, 160 ms).

## Hareket

Belirme, kaybolma, açık/kapalı kapsül, hover dolgusu ve sayı rozeti **160 ms** sürer
(`DockMotion`). Slot ve kapsül genişliği `CubicEase` / `EaseInOut`; opaklık girişte
`EaseOut`, çıkışta `EaseIn`. Yeni ikon slotuyla birlikte 0'dan 1'e ölçeklenir ve solar.
Hover ikonu büyütmez; canlı şerit ölçeği 1'dir. Dock'un ilk yerleşimi anındır; sonraki değişimler yumuşar.

## Etkileşim

| Aksiyon | Davranış |
|---|---|
| Sol tık | Öne getir / çalıştır; ikinci tık küçült (`WindowActivationService`) |
| Sol tık (klasör / İndirilenler) | Dizin yığını flyout'u. Dosya `ShellExecuteW` (kayıtlı fiil). Klasör bir alt düzeye iner |
| Sol tık (çöp) | `shell:RecycleBinFolder` |
| Hover (çalışan uygulama) | O uygulamanın pencereleri, yan yana DWM küçük resim. Tıklama `TryActivate` |
| Sağ tık (ikon) | Aşağıdaki menü. İndirilenler ve çöpte yalnız başlık + Aç |
| Sağ tık (boş levha) | Arka plan: Mica / Transparent / Tint + opaklık (tint) |
| Sürükle | Sabit öğeleri sırala; accent çizgi hedefi gösterir; tıklama bastırılır |
| Bırakma | `.lnk/.exe/.bat/.cmd` ekler; yola göre tekilleştirir |
| Bırakma (çöp) | `SHFileOperationW` `FO_DELETE` \| `FOF_ALLOWUNDO`. Reparse dizin reddedilir |

Sağ tık menüsü (`BuildContextMenuForItem`) — maddelerin koşulları koddan:

1. **Başlık kartı** — ikon + uygulama adı, her zaman (tıklanamaz).
2. **Dock'a sabitle / Dock'tan kaldır** — her zaman; metin pin durumuna göre.
3. **Görevi sonlandır** — her zaman görünür; **çalışıyorsa etkin**, değilse `IsEnabled=false`.
4. **Pencereyi kapat** — aynı koşul (3) ve aynı `IsEnabled` mantığı.
5. Ayraçdan sonra, **yalnız yol boş değilse ve dosya diskte varsa**
   (`!IsNullOrWhiteSpace(Path) && File.Exists(Path)`): **Yönetici olarak çalıştır** ·
   **Dosya konumunu aç** · **Yeni pencere aç**. Yol yoksa bu üçü **hiç görünmez**.
6. **Pencereler** — alt menü yalnız kutucuk `IsRunning` **ve** canlı pencere tutuyorsa
   (`MainWindowHandle != 0`) **ve** o süreç için **en az bir pencere** listede kaldıysa eklenir
   (`Section`'a göre değil — pinli-ve-çalışan kutucuk da `IsRunning`'dır). Menü
   `MinimizedWindowsSource`'un önbelleğinden beslenir; UI thread'inde `EnumWindows` çağrılmaz.

## Yüzey (ADR-014)

Tek flyout (`SurfaceFlyout`): hover'da pencere küçük resimleri, klasör tıklanınca dizin
yığını. İç içe `Popup` yok. DWM kaydı ayrılınca ve kapanışta düşer. Cloak'lı, küçültülmüş
veya DWM'nin reddettiği pencerede başlık satırı kalır, siyah kare yok.

İkonun altında ince ilerleme çubuğu, kabuğun görev çubuğu düğmesinde açtığı
`RangeValue` / `ItemStatus` okumasını boyar (normal yüzde, indeterminate, paused, error).
Okuma boşsa çubuk yoktur. Dikkat isteyen pencere ikonda ~1.6 sn accent nabız yapar;
ikinci flaş süreyi baştan başlatır, aktivasyon bitirir. Konum oynamaz.

Bir pencere küçülünce, yakalanan kare ~200 ms o ikona doğru ölçeklenir.
`SystemParameters.ClientAreaAnimation` kapalıysa veya `PrintWindow` boşsa animasyon yok.
Store uygulamasının logosu paket API'sinden gelir; dosya yoksa harf glifi.

Canlı şerit ölçeği 1'dir. Hover `SetWindowPos` çağırmaz. Plaka AppBar değildir.

## Ayarlar

[Ayarlar → Dock](settings.md#dock): `autoHide`, `iconSize` (16–36, varsayılan 24),
`backgroundMode` (Mica / Saydam / Renkli) + yalnız Renkli'de opaklık 0–100, `position`
(alt-sol/orta/sağ), `showRunningApps`, içerik listesi (ekle/sil/sürükleyerek sırala).
Aynı arka plan menüsü boş plakaya sağ tıkla açılır (ikonun kendi menüsü değil).
Mica ↔ saydam/renkli geçişi dock'u yeniden kurar. Konum yalnız **alt kenar**
hizasıdır; yan dock kapsam dışı.

## Tuzaklar

`docs/04-gotchas.md` → "Dock'ta ..." girdirleri. Ayrıca:

- OLE bırakma kabulü **ölçülmedi**. Mica modu katmanlı değildir; saydam ve renkli modlar
  katmanlıdır. Katmanlı `Topmost` pencere hedef olarak kabul edilmezse dosya bırakma **ve**
  uygulama içi sıralama birlikte düşer (`RegisterDragDrop` HWND başına).
- Sürükleme ile tıklama aynı butonda; bastırma **anahtarsızdır** (`_suppressNextClick`), çünkü
  bırakma sırayı değiştirip serbest bırakılan hücrenin içeriğini değiştirir.
- Plaka ritmi fare ile doğrulanmadı ([`conventions.md`](../conventions.md) §Test listesi).
