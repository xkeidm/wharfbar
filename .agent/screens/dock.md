# Ekran: Dock

`src/WharfBar/Views/DockWindow.xaml` (+ `.xaml.cs`), `Views/Controls/DockStrip.cs`,
`Views/Controls/DockItemCell.xaml`.

Görev çubuğu (Windows 11) tarzı alt şerit. AppBar **değil** (ADR-001); `Topmost` overlay,
fiziksel piksel `SetWindowPos` ile yerleşir, WPF `Left/Top/Width` kullanılmaz. Auto-hide +
hit-test (`DockAutoHideController` + `DockHitTest`); tam ekran uygulamada gizlenir.

> **Levha pencerenin kendisidir.** `dock.backgroundMode`: `mica` (varsayılan) gerçek DWM
> Mica + sistem yuvarlak köşe (`DWMWCP_ROUND`), `AllowsTransparency=false`. `transparent` ve
> `tint` pencereyi yeniden kurar (`RecreateDock`); pencere boştur, yuvarlak dolgu `Surface`
> üzerindedir (köşe 14 DIP). İkisi birlikte kullanılmaz (`docs/04-gotchas.md`). Eski
> `dock.surface` (`mica`/`acrylic`) yüklemede `mica` moduna taşınır. Büyütme taşması yok:
> ölçek 1, pencere levhaya eşittir. Hover düğmenin içini doldurur.

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

## Levha

`DockPlateMetrics`. Varsayılan glif **24 DIP** (eski 28 yüklemede 24'e iner; 16–36 aralığındaki
diğer boyutlar kalır). Düğme = glif + 8+8, aralık 4, yatay pay 10, dikey pay 8. 24'te düğme 40,
levha yüksekliği 56. Hover, düğmeyi `ControlFillColorSecondaryBrush` ile doldurur (köşe 4).
Çalışan işareti düğmenin içinde **8×3 DIP** hap, alttan 3 DIP. Kart kenarı ve üst çizgi yok.

`DockMagnification` / `DockStripLayout` saf ve testli kalır; canlı şerit `MaxScale = 1` basar,
`dock.magnification` yüklenir ama uygulanmaz. Pencere `PlateWidth` / `PlateHeight` kadardır
(boş dock bir düğme bütçeler). Hover'da `SetWindowPos` yok. ScrollViewer yoktur.

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
   (`Section`'a göre değil — pinli-ve-çalışan kutucuk da `IsRunning`'dır). Küçültülmüş bir
   kutucuğun **kendi** penceresi listeden elenir, yani tek penceresi küçültülmüş bir uygulama
   boş alt menü göstermez. Menü `MinimizedWindowsSource`'un önbelleğinden beslenir; UI
   thread'inde `EnumWindows` çağrılmaz.

## Ayarlar

[Ayarlar → Dock](../screens/settings.md#dock): `autoHide`, `iconSize` (16–36, varsayılan 24),
`backgroundMode` (Mica / Saydam / Renkli) + `tintOpacity` (0–100, yalnız tint), `position`
(alt-sol/orta/sağ), `showRunningApps`, içerik listesi (ekle/sil/sürükleyerek sırala).
Mica ↔ saydam/renkli geçişi dock'u yeniden kurar. Konum yalnız **alt kenar** hizasıdır.

## Tuzaklar

`docs/04-gotchas.md` → "Dock'ta ..." girdirleri. Ayrıca:

- OLE bırakma kabulü **ölçülmedi**. Mica modu katmanlı değildir; saydam ve renkli modlar
  katmanlıdır. Katmanlı `Topmost` pencere hedef olarak kabul edilmezse dosya bırakma **ve**
  uygulama içi sıralama birlikte düşer (`RegisterDragDrop` HWND başına).
- Sürükleme ile tıklama aynı butonda; bastırma **anahtarsızdır** (`_suppressNextClick`), çünkü
  bırakma sırayı değiştirip serbest bırakılan hücrenin içeriğini değiştirir.
- Yeniden tasarım **fare ile hiç çalıştırılmadı**; büyütme/reflow/sıralama elle doğrulanmamıştır
  ([`conventions.md`](../conventions.md) §Test listesi).
