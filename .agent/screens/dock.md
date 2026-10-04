# Ekran: Dock

`src/WharfBar/Views/DockWindow.xaml` (+ `.xaml.cs`), `Views/Controls/DockStrip.cs`,
`Views/Controls/DockItemCell.xaml`.

macOS tarzı alt şerit. AppBar **değil** (ADR-001); `Topmost` overlay, fiziksel piksel
`SetWindowPos` ile yerleşir, WPF `Left/Top/Width` kullanılmaz. Auto-hide + hit-test
(`DockAutoHideController` + `DockHitTest`); tam ekran uygulamada gizlenir.

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

Bölümler enum sırasında (`Pinned`, `Running`, `Minimized`) ve aralarında 1 px ayraç slotuyla
ayrılır. Çalışan uygulama, yolu eşleşen **ilk** sabitlenen öğeye bağlanır — yani en yaygın
kutucuk tipi (sabitlenmiş **ve** çalışan) `Pinned` bölümündedir ve `IsRunning` taşır.
Windows görev çubuğu **okunmaz, yazılmaz** (ADR-005 revizyonu).

## Büyütme

Özel `DockStrip : Panel`. Fare konumu kesirli `CursorPosition` olarak okunur (16 ms kare
zamanlayıcısı, kare hızından bağımsız yumuşatma); ölçek `DockMagnification.ScaleAtDistance`,
slot ofseti `DockStripLayout.ComputeOffsets` ile hesaplanır — komşular yatayda **kayar**
(gerçek reflow, `MaxScale` büyüdükçe kayma artar). Varsayılan tavan **1.65×**; düşüş katsayısı
0.82, taban 1.0'a kelepçeli → etki yarıçapı 3 (aynı anda en fazla 7 ikon). `Phase` girişte
130 ms, çıkışta 180 ms. Eğri ve yerleşim saf sınıflarda; birim testli.

İkonlar alt-ortadan büyür (`RenderTransformOrigin="0.5,1"`); plaka dikeyde taban yüksekliğinde
sabit kalır, ikon üst kenarın üstüne taşar. Yatayda plaka iki yana büyüyüp ortalanır.
Çalışan göstergesi 14×4 DIP pill; tooltip sistem stili (250 ms gecikme, 8 sn).

Pencere baştan en kötü büyümeye göre boyutlanır; **hover sırasında `SetWindowPos` çalışmaz**,
yalnızca WPF ölçüm/yerleşim. ScrollViewer yoktur — çok sayıda ikon pencere genişliğine
kırpılır.

## Etkileşim

| Aksiyon | Davranış |
|---|---|
| Sol tık | Öne getir / çalıştır; ikinci tık küçült (`WindowActivationService`) |
| Sağ tık | Başlık kartı, Dock'a sabitle / Dock'tan kaldır, Görevi sonlandır, Pencereyi kapat, Yönetici, Dosya konumu, Yeni pencere, **Pencereler** |
| Sürükle | Sabit öğeleri sırala; accent çizgi hedefi gösterir; tıklama bastırılır |
| Bırakma | `.lnk/.exe/.bat/.cmd` ekler; yola göre tekilleştirir |

"Pencereler" alt menüsü yalnız `IsRunning` **ve** canlı pencere tutan kutucuklarda çıkar
(`Section`'a değil — pinli-ve-çalışan kutucuk da `IsRunning`'dır) ve `MinimizedWindowsSource`'un
önbelleğinden beslenir; UI thread'inde `EnumWindows` çağrılmaz.

## Ayarlar

[Ayarlar → Dock](../screens/settings.md#dock): `autoHide`, `iconSize` (16–36), `surface`
(Mica/Acrylic), `magnification` (1.0–2.0), `position` (alt-sol/orta/sağ), `showRunningApps`,
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
