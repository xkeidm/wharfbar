# Ekran: Dock

`src/WharfBar/Views/DockWindow.xaml` (+ `.xaml.cs`), `Views/Controls/DockStrip.cs`,
`Views/Controls/DockItemCell.xaml`.

Görev çubuğu (Windows 11) tarzı alt şerit. AppBar **değil** (ADR-001); `Topmost` overlay,
fiziksel piksel `SetWindowPos` ile yerleşir, WPF `Left/Top/Width` kullanılmaz. Auto-hide +
hit-test (`DockAutoHideController` + `DockHitTest`); tam ekran uygulamada gizlenir.

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
  **salt okunur**, kapalı pinler dahil, görev çubuğu sırasında (`Taskband\Favorites`;
  ad blob'da yoksa dosya adı). Ardından `dock.items` içinde olup görev çubuğunda
  olmayanlar. Aynı hedef bir kez görünür. Uygulama çalışınca **aynı kutuda** kalır
  (`IsRunning`). Kapalı pinlerde kapsül yoktur. Sağda bir daha belirmez.
  “Dock'a sabitle / kaldır” yalnız `dock.items` yazar; görev çubuğu klasörüne yazılmaz.
- **Sağ — Running** — sabitlenmemiş çalışan uygulamalar, ada göre sıralı (eşit adlarda
  pencere tutamacı). `showRunningApps == false` iken bu taraf hiç üretilmez; kapalı
  pinler solda kalır.
- Küçültülmüş pencere, uygulaması zaten şeritteyse **ikinci kutu olmaz**. Çalışan
  listesine hiç girmemiş bir sürecin pencereleri sağda **süreç başına bir** kutu olur
  (pencere başına değil). "Pencereler" alt menüsü pencereleri listelemeye devam eder
  (`MinimizedWindowsSource` önbelleği; UI thread'inde `EnumWindows` yok).

Eşleşme (`PinMatches`): tam yol, büyük/küçük harf duyarsız; pin yolu bir AppUserModelId
ise çalışan `Id`; kayıt hâlâ `.lnk` ise çözülen hedef yol veya kısayolun AppUserModelId'si.
Klasör pinleri `explorer.exe` ile birleşmez. Aynı dosya adını paylaşan iki farklı exe
birleşmez. `User Pinned` **okunur, yazılmaz** (ADR-005, 2026-10-07).

Şerit **tek parça** — bölümler arasında ayraç yok (macOS denemesi reddedildi,
[spec §0 #9](../../docs/superpowers/specs/2026-10-04-dock-macos-redesign-design.md)).

## Plaka

Özel `DockStrip : Panel`. Slot genişliği düğmedir, glif değil. Ölçü `DockPlateMetrics`
(saf, testli): düğme = ikon + 16 DIP (her yanda 8), düğmeler arası **4 DIP**, plaka payı
**10×8 DIP**. Varsayılan ikon **24 DIP** → düğme 40, plaka yüksekliği **56 DIP**. Eski
28 DIP varsayılanı yüklemede 24'e çekilir; 16–36 aralığındaki diğer değerler kalır.

Hover, düğmeyi `ControlFillColorSecondaryBrush` ile boyar (yarıçap 4 DIP, `HoverPlate`). İkon büyümez. Çalışan göstergesi
düğmenin içinde **8×3 DIP** kapsül, alttan 3 DIP; yalnız `IsRunning`. Tooltip sistem stili
(250 ms gecikme, 8 sn). Kart çerçevesi ve üst ışık çizgisi yok.

`DockMagnification` / `DockStripLayout` saf ve testli kalır; canlı şerit `MaxScale = 1` basar.
Pencere `PlateWidth` / `PlateHeight` kadardır (boş dock bir düğme bütçeler). Hover'da `SetWindowPos` yok.
ScrollViewer yoktur — çok sayıda ikon pencere genişliğine kırpılır. `dock.magnification` JSON'da durur ve okunur, uygulanmaz.

## Etkileşim

| Aksiyon | Davranış |
|---|---|
| Sol tık | Öne getir / çalıştır; ikinci tık küçült (`WindowActivationService`) |
| Sağ tık (ikon) | Aşağıdaki menü |
| Sağ tık (boş levha) | Arka plan: Mica / Transparent / Tint + opaklık (tint) |
| Sürükle | Sabit öğeleri sırala; accent çizgi hedefi gösterir; tıklama bastırılır |
| Bırakma | `.lnk/.exe/.bat/.cmd` ekler; yola göre tekilleştirir |

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
