# Ekran: Dock

`src/WharfBar/Views/DockWindow.xaml` (+ `.xaml.cs`), `Views/Controls/DockStrip.cs`,
`Views/Controls/DockItemCell.xaml`.

Görev çubuğu (Windows 11) tarzı alt şerit. AppBar **değil** (ADR-001); `Topmost` overlay,
fiziksel piksel `SetWindowPos` ile yerleşir, WPF `Left/Top/Width` kullanılmaz. Auto-hide +
hit-test (`DockAutoHideController` + `DockHitTest`); tam ekran uygulamada gizlenir.

> **Katmanlı pencere:** `AllowsTransparency=True` — plaka yüzeyi WPF çizimli (`Surface`
> border fırçası, köşe yarıçapı 12 DIP); pencere kenarları (büyütme headroom'u) gerçekten
> **şeffaf + tıklama geçirir**, böylece dock çevresinde görünür bir kutu olmaz. Gerçek
> DWM Mica/Acrylic **kullanılamaz** (`AllowsTransparency` ile birlikte yanlıştır —
> `docs/04-gotchas.md:7`); `dock.surface` yalnızca iki WPF dolgusu arasında seçim yapar.
> **Gölge yok** — `DropShadowEffect` büyütme animasyonunda kare başına yazılım maliyeti;
> karar: [2026-10-04 spec §2/§9](../../docs/superpowers/specs/2026-10-04-dock-macos-redesign-design.md).

## İçerik

- **Pinned** — kullanıcının kendi listesi (`dock.items`), sıra config sırası.
- **Running** — sabitlenmemiş çalışan uygulamalar, ada göre sıralı.
- **Minimized** — küçültülmüş her pencere için ayrı tik, uygulama ikonuyla (thumbnail yok).

Bölümler enum sırasında (`Pinned`, `Running`, `Minimized`) sıralanır ve **tek sürekli şerit**
olarak çizilir — aralarında **ayraç slotu yok** (Windows görev çubuğu davranışı; macOS
denemesi reddedildi, [spec §0 #9](../../docs/superpowers/specs/2026-10-04-dock-macos-redesign-design.md)).
`DockSection` yine de sırayı ve çalışan/küçültülmüş eşleşmesini belirler, yalnızca çizilmez.
Çalışan uygulama, yolu eşleşen **ilk** sabitlenen öğeye bağlanır — yani en yaygın
kutucuk tipi (sabitlenmiş **ve** çalışan) `Pinned` bölümündedir ve `IsRunning` taşır.
Windows görev çubuğunun **pin tarafına dokunulmuyor**: `User Pinned` klasörü okunmuyor,
görev çubuğuna `.lnk` yazılmıyor (ADR-005 revizyonu). Görev çubuğunu gizleme/geri yükleme
bu kararın dışında — o hâlâ §3'ün konusu.

## Büyütme

Özel `DockStrip : Panel`. Fare konumu kesirli `CursorPosition` olarak okunur (16 ms kare
zamanlayıcısı, kare hızından bağımsız yumuşatma); ölçek `DockMagnification.ScaleAtDistance`,
slot ofseti `DockStripLayout.ComputeOffsets` ile hesaplanır. Varsayılan tavan **1.15×**
(görev çubuğu hafifliği; macOS'un 1.65× reflow'u reddedildi): düşüş katsayısı 0.82 ve taban
1.0'a kelepçeli olduğu için `1.15 × 0.82 = 0.943` → **1.0**, yani **yalnız imlecin üstündeki
ikon** büyür ve komşular **hiç kaymaz** — reflow etkisi özel duruma düşmeden aritmetikle
biter. `RadiusFor(1.15) = 1` yalnızca tampon boyutlandırması içindir. Kaydırıcı aralığı
**1.0–1.5** (adım 0.05); `Clamp` üst sınırı hâlâ 2.0, yani eski bir config'deki 1.65 değeri
okunur ama kaydırıcıda 1.50'ye kırpılır. `Phase` girişte 130 ms, çıkışta 180 ms. Eğri ve
yerleşim saf sınıflarda; birim testli.

İkonlar alt-ortadan büyür (`RenderTransformOrigin="0.5,1"`); plaka dikeyde taban yüksekliğinde
sabit kalır, ikon üst kenarın üstüne taşar. Yatayda plaka iki yana büyüyüp ortalanır (pay
1.15×'te 4.2 px, gözle fark edilmez). Çalışan göstergesi **16×3 DIP** pill, `CornerRadius=2`,
ikonun altında 4 DIP; tooltip sistem stili (250 ms gecikme, 8 sn).

Pencere baştan en kötü büyümeye göre boyutlanır; **hover sırasında `SetWindowPos` çalışmaz**,
yalnızca WPF ölçüm/yerleşim. ScrollViewer yoktur — çok sayıda ikon pencere genişliğine
kırpılır.

## Etkileşim

| Aksiyon | Davranış |
|---|---|
| Sol tık | Öne getir / çalıştır; ikinci tık küçült (`WindowActivationService`) |
| Sağ tık | Aşağıdaki menü |
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
   (`Section`'a göre değil — pinli-ve-çalışan kutucuk da `IsRunning`'dır). Küçültülmüş bir
   kutucuğun **kendi** penceresi listeden elenir, yani tek penceresi küçültülmüş bir uygulama
   boş alt menü göstermez. Menü `MinimizedWindowsSource`'un önbelleğinden beslenir; UI
   thread'inde `EnumWindows` çağrılmaz.

## Ayarlar

[Ayarlar → Dock](../screens/settings.md#dock): `autoHide`, `iconSize` (16–36), `surface`
(Mica/Acrylic), `magnification` (kaydırıcı 1.0–1.5), `position` (alt-sol/orta/sağ), `showRunningApps`,
içerik listesi (ekle/sil/sürükleyerek sırala). Konum yalnız **alt kenar** hizasıdır; yan dock
kapsam dışı.

## Tuzaklar

`docs/04-gotchas.md` → "Dock'ta ..." girdirleri. Ayrıca:

- Büyütme açıkken pencerenin saydam kenarı plakadan geniş olduğu için tıklamayı yutar (mevcut davranış).
- OLE bırakma kabulü **ölçülmedi**: katmanlı `Topmost` pencere hedef olarak kabul edilmezse
  dosya bırakma **ve** uygulama içi sıralama birlikte düşer (`RegisterDragDrop` HWND başına).
- Sürükleme ile tıklama aynı butonda; bastırma **anahtarsızdır** (`_suppressNextClick`), çünkü
  bırakma sırayı değiştirip serbest bırakılan hücrenin içeriğini değiştirir.
- Yeniden tasarım **fare ile hiç çalıştırılmadı**; büyütme/reflow/sıralama elle doğrulanmamıştır
  ([`conventions.md`](../conventions.md) §Test listesi).
