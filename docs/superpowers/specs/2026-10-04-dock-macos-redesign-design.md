# Dock yeniden tasarım — macOS mantığı, bağımsız liste, büyütme motoru

**Tarih:** 2026-10-04
**Durum:** Onaylandı (görsel yön + mimari + kapsam M) — **uygulandı; aşağıdaki “Sapmalar” bölümü koda göre düzeltilmiştir**
**Kapsam:** `src/WharfBar/Views/DockWindow.*`, `Services/Dock*`, `Models/DockConfig`, `Views/Pages/DockPage.*`, `Native/DockHitTest`, `Services/MonitorManager`, `Services/TaskbarVisibilityService`
**İlgili ADR:** `docs/decisions/001-dock-appbar-vs-overlay.md` (değişmiyor), `docs/decisions/005-shell-replace-live-dock-tray-hybrid.md` (**revize edildi**)

---

## 0. Uygulama sonrası sapmalar (koda göre düzeltilmiştir)

Bu spec uygulama öncesi yazıldı. Aşağıdaki maddeler **koda aykırıydı** ve koda uyarlandı;
gerekçeleri ve kalıcı tuzaklarıyla birlikte burada bırakıldı, çünkü hepsi bir sonraki okuyucunun
(veya bir sonraki düzeltmenin) geri eski haline döndürme riski taşıyor.

| # | Spec'te yazan | Kodun yaptığı | Neden |
|---|---|---|---|
| 1 | §8.3 tıklama bastırma **anahtar tabanlı**: `_suppressClickFor` seti, bırakılan öğenin `Id`'siyle karşılaştırma | **Anahtarsız** `_suppressNextClick` bayrağı; `MouseLeftButtonUp` hiçbir anahtar karşılaştırması yapmaz | Bırakma sırayı değiştirir → composer `Changed` → `RebuildStrip` → `SetRows` hücreleri **indikse göre** yeniden eşler. Yani bırakmanın düştüğü hücre o anda **başka bir kutucuğu** tutuyor olabilir; anahtar karşılaştırması ıskalar ve sürükleme uygulamayı açar. Bayrak: her yeni `PreviewMouseLeftButtonDown`'da temizlenir, `DoDragDrop` dönünce **koşulsuz** kurulur (reddedilen sürükleme de sürüklemedir), ilk bırakmada tüketilir |
| 2 | §8.2 alt menü koşulu `Section == Running \|\| Section == Minimized` | `item.IsRunning && item.MainWindowHandle != IntPtr.Zero` | **Pinli ve çalışan** kutucuk `DockSection.Pinned` taşır (`DockAppsComposer` eşleşmeyi `Section`'ı değiştirmeden yapar) ama `IsRunning` + canlı hwnd alır — yani en yaygın kutucuk tipi spec'in koşuluna **düşmez** ve özellik erişilemez olurdu |
| 3 | §5.3 `DockItemStore.ReplaceAll(IEnumerable<PinnedApp>)` | Yok. Ayar sayfası `TryAdd` / `Remove` / `Move` kullanır; `Reload` yalnız dışarıdan yapılan değişikliği almak içindir | Liste tek tek mutasyonla yönetiliyor; toplu değiştirme yolu hiçbir akışta gerekmedi (kapsam şişmesi) |
| 4 | §6.3 / §12 panelin adı `DockItemPanel` | `DockStrip` (`Views/Controls/DockStrip.cs`) | “Panel” soneki, tek dosyalık özel `Panel` için fazla geliyordu; `DockStripBuilder` ile isim çakışmasını da kırıyor |
| 5 | §6.1/§8.2 alt menü başlığı **"Pencere listesi"**, girdilerde `Etkinleştir` + `Kapat` | Başlık **"Pencereler"**, her girdi yalnız **etkinleştirir** | Kapatma zaten ayrı bir üst düzey öğede var (`Pencereyi kapat`, `WM_CLOSE`); ikinci yol aynı işi iki yerde sunuyordu |
| 6 | §9 ayraç: 1 px çubuk + **6 DIP** kenar boşluğu (`CreateSeparator`) | Slot taban genişliği `DockStripBuilder.SeparatorSlotDip` = **1 DIP**; çizilen çubuk sabit 1 px, hücrede dikey boşluk 2 DIP | Reflow'da ayraç da bir slot olduğu için geniş bir boşluk şeridi gereksiz yere açıyor ve bütçe hesabını zorlaştırıyordu. `MaxExtentWidth` ayraçları **bilerek** ikon genişliğiyle ücretlendirir (aşırı tahmin) |
| 7 | §7.1 "`DockItemPanel` `CompositionTarget.Rendering` üzerinden sürer" | 16 ms'lik `DispatcherTimer` (`Render` önceliğinde) + `DockMagnification.Eased` | Composition-rendering olayı yalnız büyüte animasyonu sürdü; imleç hedefi **her karede** değişiyor ve yeniden ölçüm gerektiriyor. `Eased` kare hızından bağımsız olduğu için zamanlayıcı de yeterli |
| 8 | §12 madde 5: roadmap'deki bayat `ScrollViewer` iddiası "zaten `b027a02`'de kaldırılmıştı" | İddia hâlâ `roadmap.md`'de duruyordu; bu değişiklikte **kaldırıldı** | Doğrulama yapılmadan "zaten kalkmıştı" denmişti |

Ayrıca §12'deki doküman listesi **genişletildi**: `docs/00-overview.md` (canlı pin iddiaları),
`docs/01-architecture.md` config şema örneği (`schemaVersion` 2 + `dock.items`),
`docs/03-development-order.md`, `.agent/project.md` ve `.agent/screens/README.md` de aynı
değişiklikte güncellendi. `04-gotchas.md`'ye beş kayıt yazılması planlanmıştı; ikisi zaten
Tasks 7–9'da mevcuttu ve **daha doğru** yazılmıştı, bu yüzden yalnız üçü eklendi
(kare başına `SetWindowPos`, ayraç slotu, DPI bayatlığı). Gölge kararı §2/§9'da zaten vardı.

---

## 1. Özet

WharfBar'ın dock'u bugün Windows görev çubuğunun `User Pinned` klasörünü **yansıtır** ve hover'da yalnızca tek ikonu 1.3× büyütür. Bu tasarım dock'u macOS mantığıyla yeniden kurar:

1. **Bağımsız liste.** Dock'un tek kaynağı WharfBar'ın kendi kalıcı listesidir; görev çubuğuyla hiçbir yazma/okuma bağı kalmaz.
2. **Gerçek reflow'lı büyütme.** Özel bir `ItemsPanel` slot genişliklerini değiştirir; imlecin üstündeki ikon ~1.65× büyür, iki komşu yumuşak düşüşle büyür.
3. **Kullanılabilir etkileşim.** Sürükleyerek sıralama, Explorer'dan bırakma, sağ tıkta pencere listesi, ayarlarda liste editörü, konum seçimi, büyütme ayarı.

Pencere boyutu **baştan** maksimum büyümeye göre sabitlenir; kare başına `SetWindowPos` çağrısı yapılmaz. Bu, `DockWindow`'un bugünkü sabit-boyut kuralının (`DockWindow.xaml.cs:19-22`, `908-922`) nedeni olduğu stutter sorununu tasarımın içinde çözer.

## 2. Onaylı görsel kararlar

| Karar | Seçim | Not |
|---|---|---|
| Yüzey | **Mica** | koyu saydam plaka + ince üst ışık çizgisi |
| Büyütme şiddeti | **Orta ~1.65×** | imlecin üstü + iki komşu, yumuşak düşüş |
| Çalışan göstergesi | **W11 çizgi (pill)** | 14×4 DIP |
| Hover etiket | **Sistem tooltip** | mevcut davranış korunur |
| Plaka köşe yarıçapı | **12 DIP** | WharfBar flyout token'ı |
| Gölge | **Yok** | `DropShadowEffect` kare başına yazılım maliyeti |
| Konum | alt-sol / alt-orta / alt-sağ | yan dock kapsam dışı |

## 3. Mevcut durum ve boşluklar

- `DockAppsComposer` (`Services/DockAppsComposer.cs:32`) pin kaynağı olarak `TaskbarPinnedAppsSource`'ı kullanır; `AppConfig.PinnedApps` yalnızca fallback'tir.
- `DockWindow`, ikonları imperatif C# ile üretir (`DockWindow.xaml.cs:358-409`); XAML'de yalnızca şablonsuz bir `ItemsControl` + `StackPanel` vardır (`DockWindow.xaml:22-38`).
- Büyütme yalnızca `RenderTransform` ölçeğidir (`:51`, `:380-381`, `:660-668`); komşu ölçeklenmez, reflow yok, `MouseMove` hiç kullanılmaz.
- "Dock'tan kaldır" gerçek görev çubuğuna `.lnk` yazar (`TogglePin`, `:581-636`).
- Sürükle-bırak, sıralama ve liste editörü yoktur.
- Ayarlar yalnızca 3 alan sunar: `autoHide`, `iconSize`, `surface` (`Models/AppConfig.cs:86-109`).
- `RebuildStrip` (`:340`) sondaki hariç her ikona trailing margin verir — bu bir hata.
- `Monitor.Dpi` yapılandırmada sabitlenir ve `WM_DPICHANGED` içinde tazelenmez (`:996-1000`).

## 4. Kapsam

### 4.1 Bu işin içinde

- Bağımsız dock listesi + soft migration
- `DockAppsComposer` yeniden yazımı; `MinimizedWindowsSource` (yeni)
- `DockItemPanel` + `DockMagnification` + `DockStripLayout` (saf, test edilebilir)
- `DockItemCell` (XAML `UserControl`) + `DockItemViewModel`
- Sürükle-sırala, dosya bırakma, "Pencere listesi" alt menüsü
- Konum: alt-sol / alt-orta / alt-sağ
- Yeni ayarlar: büyütme kaydırıcısı, konum, çalışan uygulamaları göster, içerik editörü
- `WM_DPICHANGED` ile DPI tazeleme
- Testler + doküman güncellemeleri

### 4.2 Kapsam dışı

- Yan dock (sol/sağ kenar) — mevcut alt iş-alanı rezervasyonu ve aşağı kayma gizleme bozulmasın
- UWP/Store ikonları (`.agent/boundaries.md:86` — ayrı onay gerekir)
- Bitmap pencere küçültmeleri (`DwmRegisterThumbnail`) — küçültülmüş pencere uygulama ikonuyla temsil edilir
- Gerçek DWM Mica/Acrylic (`AllowsTransparency=true` ile yasak, `docs/04-gotchas.md:7`)
- Dock'u alt AppBar'a çevirmek (`docs/decisions/001`, `boundaries.md:90`)
- Sürükleyip çöpe atma, "masaüstünü göster", saat, ikon yansıması, opaklık/gap ayarları
- Konum değişikliği sonrası iş-alanı rezervasyonunun yan kenar varyantı

## 5. Veri modeli

### 5.1 `DockConfig` — yeni alanlar

```csharp
public List<PinnedApp> Items { get; set; } = [];
public string Position { get; set; } = DockPositions.BottomCenter;   // bottom-left|bottom-center|bottom-right
public double Magnification { get; set; } = DockMagnification.Default;  // 1.65
public bool ShowRunningApps { get; set; } = true;
```

`DockPositions.Normalize(string?)` geçersiz/boş değerde `BottomCenter` döndürür — `DockSurfaceStyles.NormalizeStyle` deseni.

`ConfigService` yükleme zamanı yumuşak normalizasyon:
- `Magnification` `[1.0, 2.0]` aralığına kırpılır.
- `Position` normalize edilir.
- `AppConfig.PinnedApps` boş değilse ve `Dock.Items` boşsa: içerik `Dock.Items`'e **kopyalanır**. `AppConfig.PinnedApps` şemada legacy seed olarak kalır, yalnızca bu göç tarafından okunur. `schemaVersion` **2'de kalır** (alan bazlı kırılmadan okuma tercihi, `conventions.md:24`).

`PinnedApp` değişmez: `record PinnedApp(string Path, string Args, string Label)`.

### 5.2 `DockAppItem` ve bölümler

```csharp
internal enum DockSection { Pinned, Running, Minimized }

internal sealed record DockAppItem(
    string Id, string Path, string Args, string Label,
    bool IsPinned, bool IsRunning, IntPtr MainWindowHandle,
    DockSection Section);
```

Kimlik kuralları:
- Sabitlenen öğe: `Id` = normalize edilmiş tam yol (`Path.GetFullPath`, case-insensitive).
- Çalışan öğe: `Id` = AUMID ?? yürütülebilir yol (bugünkü `RunningAppsSource` kuralı korunur).
- Küçültülmüş pencere: `Id` = `"hwnd:<hwnd>"` — zaten `RunningAppsSource`'ta var, çakışma yaratmaz.

Düzen: `Pinned | ayraç | Running | ayraç | Minimized`. `ShowRunningApps == false` ise `Running` ve `Minimized` bölümleri hiç üretilmez.

Eşleme: çalışan uygulama, yolu case-insensitive eşleşen **ilk** sabitlenen öğeye bağlanır (`DockAppsComposer.PathsMatch` korunur) ve `IsRunning`/`MainWindowHandle` dolar. Bağlanamayan çalışan uygulamalar `Running` bölümüne, `Label` sırasına göre eklenir.

### 5.3 `DockItemStore`

Yeni servis. Sorumluluğu tek cümle: **sıralı, tekilleştirilmiş, kalıcı sabit listeyi tutar.**

```csharp
internal sealed class DockItemStore
{
    IReadOnlyList<PinnedApp> Items { get; }
    event EventHandler? Changed;
    bool TryAdd(string path, string args, string label);   // normalize + dedupe
    bool Remove(string id);
    bool Move(string id, int toIndex);
    void Reload();                                        // ReplaceAll yerine geçti (§0 #3)
}
```

- `TryAdd` normalize edilmiş yola göre tekilleştirir; eklemezse `false` döner.
- `Move` hedef indeksi normalize eder, listeyi yeniden sıralar, `configService.Save` çağırır, `Changed` yayar.
- `Move`/`Remove`/`TryAdd` yazma sonrası `Changed` yayar; `DockAppsComposer` bunu dinleyip yeniden üretir.

## 6. Bileşenler

### 6.1 Veri kaynakları

| Tür | Sorumluluk |
|---|---|
| `DockItemStore` (yeni) | kalıcı sabit listesi (§5.3). `App` tek örneğini sahiplenir |
| `RunningAppsSource` (mevcut) | çalışan uygulama listesi — **değişmez** |
| `MinimizedWindowsSource` (yeni) | tüm üst düzey pencerenin tablosu + `IsIconic(hwnd)` alt kümesi; 800 ms yoklama, arka iş parçacığı; `ForProcess(pid)` alt kümesi sunar |
| `TaskbarPinnedAppsSource` | **kaldırıldı ve silindi** — görev çubuğu artık hiç okunmuyor (referans taraması temiz çıktı) |

`MinimizedWindowsSource` ve `RunningAppsSource` aynı pencere filtresini kullanır. `MinimizedWindowsSource.Scan()` **tek** `EnumWindows` geçişiyle (`WindowEnumeration.GetTopLevelCandidates()`) tüm pencereleri alır ve küçültülmüş alt kümesini pencere başına `IsIconic(hwnd)` ile türetir — ikinci bir tarama yapılmaz.

> **Sapma (gerekçeli):** `RunningAppsSource.WindowsFor(id)` yerine `MinimizedWindowsSource.Windows` önbelleği kullanılır. "Pencereler" menüsü UI iş parçacığında açılır ve `EnumWindows` donmuş HWND'de UI'yi kilitler (`WindowEnumeration` uyarısı). Zaten 800 ms'de bir kez taranan tabloyu filtrelemek hem doğru hem bedavadır.
> **Uygulandığı hâliyle doğrulandı:** menü `MinimizedWindowsSource.ForProcess(Windows, pid)` ile
> süzer, görünür pencereler önce gelsin diye `IsMinimized`'e göre sıralar ve o kutucuğun kendi
> penceresini (varsa) listeden çıkarır — tek penceresi küçültülmüş bir uygulama boş alt menü göstermez.

### 6.2 `DockAppsComposer` (yeniden yazım)

Girdi: `DockItemStore` + `RunningAppsSource` + `MinimizedWindowsSource` + `DockConfig`.
Çıktı: `IReadOnlyList<DockAppItem>` ve `Changed`.

Saf çekirdek `Compose(pins, running, minimized, showRunning)` olarak ayrılır ve doğrudan test edilir.
`ItemsEqual` genişletilir: `Section` ve `WindowTitle` de karşılaştırılır. Yeniden üretim yalnızca
gerçek değişiklikte olur (mevcut desen).

### 6.3 Görsel katman

`DockStrip : Panel` hücreleri kendisi sahiplenir ve `DockWindow`'a `Cells` listesi olarak açar. `DockItemCell` bir `UserControl`'dür ve XAML ile tanımlıdır; `DockItemViewModel` (`Changed` üzerinden yeniden üretilen anlık görüntü) hücreye atanır.

> **Sapma (gerekçeli):** tasarımın ilk hali `ItemsControl` + `ItemTemplate` kullanıyordu. Her hücreye imperatif tooltip yerleşimi, bağlam menüsü ve sürükle/bırak bağlanması gerektiği için `ItemsControl` hücrelere erişim vermiyordu; `DockStrip` hem dekoratif değer hem de erişim sahibidir. Hücreler yeniden kullanıldığı için tooltip/menü/olay bağlama `RebuildStrip` içinde **tek blokta** ve her yeniden kurulumda bir kez yapılır.

- `Image` — `Source = IconResolver.GetIcon(path, maxIconPx)`, `RenderTransformOrigin = (0.5, 1.0)`, `BitmapScalingMode.HighQuality`
- Çalışan göstergesi — `Border` 14×4, `CornerRadius=2`, `TextFillColorPrimaryBrush`; görünürlük `IsRunning`'a bağlı
- Ayraç — aynı hücre sınıfının ayrı varyantı: 1 px `TextFillColorPrimaryBrush` @ 0.3 çubuk
- `ToolTip` — `DockAppItem.Tooltip`, mevcut özel yerleşim korunur (250 ms gecikme, 8 sn, ikon üstünde ortalanmış 25 DIP)
- `ContextMenu` — §8.2
- Hücre içi yerleşim: ikon alt-ortaya hizalı ve `indicatorGap + indicatorSize` kadar yukarıda; gösterge altta `indicatorGap` boşlukla

Hücre düzeni taban boyutta kalır; büyüme `RenderTransform` ile yapılır, komşu kayması panelin `ArrangeOverride`'ı ile (§7.1).

### 6.4 `DockWindow`

- `RebuildStrip` imperatif ikon üretimi yerine `DockStripViewModel` satırları üretip `_strip.SetRows(...)` çağırır (§6.3).
- `RebuildStrip`'daki trailing-margin hatası (§3) hücre şablonuna taşınarak **düzeltilir**.
- `TogglePin` (§ `581-636`) yerine `DockItemStore.Remove`/`TryAdd` gelir. **Görev çubuğu klasörüne hiçbir yazma yapılmaz.**
- `TryGetPlacementGeometry` X hesabı `Position`'a göre hizalar (alt-sol/sağ → `monitor` kenarı + 12 DIP, alt-orta → mevcut ortalama).
- `WM_DPICHANGED` yüksek kelimesinden `_monitorDpi` tazelenir (§7.3); `MonitorInfo.Dpi` yapılandırmada sabitlenir.
- `WM_WINDOWPOSCHANGING` clamp'i, önbelleklenmiş yerleşim ve `Measure()` içermeme kuralı **aynen korunur** (`docs/04-gotchas.md:41`).
- `BuildEdgeProbe` yalnızca önbelleklenmiş geometriyi okumaya devam eder.

## 7. Büyütme motoru

### 7.1 Saf yardımcılar

> **Düzeltme (§0 #4, #7):** bu bölümdeki `DockItemPanel` adı kodda **`DockStrip`**
> (`Views/Controls/DockStrip.cs`), imleç sürücüsü ise `CompositionTarget.Rendering` değil
> 16 ms'lik bir `DispatcherTimer` (`Render` önceliğinde) + `DockMagnification.Eased`.

İki küçük, bağımsız, test edilebilir birim (`DockConfig` bu sabitleri doğrudan okur, tek doğruluk kaynağı):

```csharp
internal static class DockMagnification
{
    public const double Default = 1.65;
    public const double Min = 1.0;
    public const double Max = 2.0;
    internal const double Falloff = 0.82;

    public static double Clamp(double value);
    public static double ScaleAtDistance(double distance, double maxScale);
    public static double Eased(double current, double target, double dtMs);
    public static int RadiusFor(double maxScale);
}

internal static class DockStripLayout
{
    // Ölçekler yeniden kullanılan tampona yazılır: kare başında ayırma (allocation) yok.
    public static void ComputeScales(int count, double cursorPosition, double phase, double maxScale, double[] into);
    public static void ComputeOffsets(int count, double[] baseWidths, double[] scales, double gap, double[] offsets, out double totalWidth);
    public static double MaxExtentWidth(int count, double widestSlot, double gap, double maxScale);
    public static double MaxExtentHeight(double icon, double indicatorGap, double indicatorSize, double verticalPadding, double maxScale);
}
```

> Sabit bir üst sınır (`MaxRadius`) **yok**: `maxScale = 2.0` iken 3. adımdaki 1.103 ile 4. adımdaki 1.0 arasında süreksizlik yaratırdı. Yarıçap `RadiusFor(maxScale)` ile hesaplanır, `1.0` taban kelepçesi düşüşü doğal olarak bitirir. Tampon boyutlandırma bu yarıçapı kullanır.
>
> `MaxExtentWidth` **panel** genişliğini döndürür (yatay dolguyu `DockWindow` ekler); `baseWidths` ayrımı ayraç slotlarının 1 px genişlikte sayılmasını sağlar.

**Eğri.** `ScaleAtDistance(d, max) = d <= 0 ? max : max(1.0, max * 0.82^d)`. `1.0` kelepçesi: ikon taban boyutundan **küçük hiçbir zaman olmaz**. Bu, onaylanan mockuptaki değerleri üretir:

| d | 0 | 1 | 2 | 3 |
|---|---|---|---|---|
| ölçek | 1.650 | 1.353 | 1.110 | 1.000 (kelepçe) |

Etki yarıçapı `max * 0.82^r <= 1.0` için `r = ceil(log(1/max)/log(0.82))`; `max = 1.65` → `r = 3`. Aynı anda en fazla **7 ikon** ölçeklenir.

**Yumuşatma.** `Eased(cur, tgt, dt) = cur + (tgt - cur) * (1 - exp(-dt/tau))`, `tau = 55 ms`. Kare hızından bağımsızdır; `DockItemPanel` `CompositionTarget.Rendering` (UI iş parçacığı, dispatcher önceliği `Render`) üzerinden `CursorPosition`'ı bu yumuşatmayla hedefe sürer. `Phase` girişte 130 ms, çıkışta 180 ms `CubicEase/EaseOut` ile `DoubleAnimation` ile sürülür.

**Yerleşim.** `ComputeOffsets` kümülatif kaydırma üretir:

```
x_i = Σ_{j<i} (iconPx * s_j + gapPx)
```

`DockItemPanel.ArrangeOverride` her hücreyi `Rect(x_i, 0, iconPx * s_i, baseCellHeight)` ile yerleştirir; hücrenin içindeki ikon taban boyutta kalır ve `ScaleTransform(s_i, s_i)` ile alt-ortadan büyür — böylece slot `iconPx * s_i` ile birebir dolar. Panel `DesiredSize.Width` = canlı toplam genişlik döndürür; `Surface` bunu sarar ve pencere içinde ortalanır.

Panel, canlı genişlik `Surface`'e yayılması gerektiği için `InvalidateMeasure()` çağırır. Ölçüm ucuzdur: **çocuk boyutları hiç değişmez**, yalnızca slot `x` konumları ve ikonun `RenderTransform`'u değişir. Kare başında `Image` yeniden ölçülmez/rasterize edilmez.

### 7.2 Sabit pencere boyutu

Pencere **max büyümeye göre** bir kez boyutlandırılır; kare başına `SetWindowPos` **yoktur**.

`iconPx = 28 @ 96 DPI`, `max = 1.65`:

| Hesap | Değer |
|---|---|
| taban genişlik (6 ikon) | `2*12 + 6*28 + 5*12` = **252 px** |
| büyüme payı | `Σ_d iconPx*(s_d − 1)`, d=0:18.2 · d=1:9.88 · d=2:3.08 (d=3 kelepçe) → `18.2 + 2*(9.88+3.08)` = **44.1 px** |
| max genişlik | `252 + 44` = **296 px** |
| taban plaka yüksekliği | `2*6 + 28 + 4 + 2` = **46 px** |
| pencere yüksekliği | `46 + 28*(1.65−1)` = **64 px** |

Dikeyde plaka **taban yüksekliğinde sabittir**; büyüyen ikonlar plakanın üst kenarının üstüne taşar (macOS davranışı). Yatayda plaka her karede iki yana doğru büyüyüp/gerileyip pencerede ortalanır.

İş-alanı rezervasyonu `geo.HeightPx` (max yükseklik) kullanmaya devam eder — bugün de böyledir.

**Bilinen yan etki:** pencere max genişlikte olduğu için plakadan dışarıdaki saydam kenar genişliği bir miktar artar (~18 px/side) ve tıklamayı yutar. Bugün mevcut davranış; bu işte değiştirilmiyor.

### 7.3 DPI

`WM_DPICHANGED` içinde `HIWORD(wParam)` okunur ve `MonitorInfo.Dpi` güncellenir; `ApplyPlacement` yeni DPI ile yeniden çalışır. `Monitor` kaydı mutasyona uğraması için `sealed record` yerine gerekirse `Dpi` alanı mutable bir kopya olarak taşınır.

## 8. Etkileşim

### 8.1 Sol tık

Değişmiyor: `PreviewMouseLeftButtonDown` → `_activation.CaptureForegroundHint()`, `MouseLeftButtonUp` → `_activation.LaunchOrActivate(item, _launcher)`.

### 8.2 Sağ tık menüsü

Mevcut 7 öğe aynen korunur (başlık kartı, Dock'tan kaldır, Görevi sonlandır, Pencereyi kapat, Yönetici olarak çalıştır, Dosya konumunu aç, Yeni pencere aç). Ek olarak, **`IsRunning` ve canlı pencere tutan** öğelerde **"Pencereler"** alt menüsü: her pencere için başlık, tıklanınca etkinleştirir.

> **Düzeltme (§0 #2, #5):** koşul `Section == Running || Section == Minimized` **değil**,
> `IsRunning && MainWindowHandle != IntPtr.Zero`'dur — pinli-ve-çalışan kutucuk `Section == Pinned`
> taşır. Menü başlığı "Pencere listesi" değil **"Pencereler"**, girdilerde `Kapat` yok
> (üst düzeydeki `Pencereyi kapat` zaten `WM_CLOSE` gönderiyor).

### 8.3 Sürükleyerek sıralama

`TopBarPage.xaml.cs:125-180` deseninin (`DoDragDrop`, string id) birebir kopyası. `AllowDrop`/`DragOver`/`Drop`/`PreviewMouseLeftButtonDown`/`PreviewMouseMove` **tek blokta** bağlanır (proje kuralı, `TopBarWindow.xaml:156-157`).

- `DragOver`: hedef indeksi hesaplar, 2 px accent çizgi gösterir, `e.Handled = true`.
- `Drop`: `DockItemStore.Move(id, toIndex)`.
- **Çakışma çözümü:** sürükleme `SystemParameters.MinimumHorizontalDragDistance` eşiği aşılınca başlar; `DoDragDrop` dönünce **koşulsuz** bir "sürükleme oldu" bayrağı kurulur ve bir sonraki `MouseLeftButtonUp` yutulur. Aksi hâlde sürükleme uygulamayı açar.

> **Düzeltme (§0 #1):** bayrak **anahtarsızdır**; `_suppressClickFor` seti ve `Id` karşılaştırması
> **kullanılmadı**. Bırakma sırayı değiştirdiği için bırakmanın düştüğü hücre o anda başka bir
> kutucuğu tutuyor olabilir (`RebuildStrip` → `SetRows` hücreleri indikse göre yeniden eşler) ve
> anahtar karşılaştırması ıskalardı. Ek olarak sürükleme yükü `Id` değil **normalize yol**
> (`ReorderKeyFor` → `DockItemStore.Normalize(item.Path)`) olmalıdır: pinli-ve-çalışan kutucuğun
> `Id`'si çalışan uygulamadan gelir (Store app'te AUMID) ve store yolla anahtarlar.

### 8.4 Dosya bırakma

`Surface.AllowDrop = true`. `DragOver` yalnızca `DataFormats.FileDrop` kabul eder (`Copy`). `Drop`: uzantı süzgeci `.lnk/.exe/.bat/.cmd`; `.lnk` için `ShortcutResolver.TryResolve`; `DockItemStore.TryAdd` ile yola göre tekilleştirilerek sona eklenir.

### 8.5 Konum

`bottom-center` (mevcut), `bottom-left`, `bottom-right`. Yalnızca X hesabını ve otomatik gizleme kenar bandının yatay sınırını etkiler. Alt iş-alanı rezervasyonu ve aşağı kayma değişmez.

## 9. Görsel dil

| Öğe | Karar |
|---|---|
| Mica dolgu | koyu `#202020` @ `%60` (`0x99`), açık `#FAFAFA` @ `%85` (`0xD9`) |
| Acrylic dolgu | koyu `#202020` @ `0xCC`, açık `#FAFAFA` @ `0xE6` |
| Plaka köşe | 12 DIP |
| Kenar | 1 px `#1AFFFFFF` + iç üst highlight `#24FFFFFF` |
| Gölge | yok |
| Çalışan göstergesi | 14×4 DIP pill, `CornerRadius=2`, `TextFillColorPrimaryBrush`, ikonun altında 2 DIP boşluk (`RunningIndicatorGapDip`) |
| Ayraç | 1 px, `TextFillColorPrimaryBrush` @ `0.3`, 6 DIP kenar boşluğu (mevcut `CreateSeparator`) |
| Tooltip | mevcut sistem stili ve yerleşimi |
| Hover wash | yok |

## 10. Ayarlar (`DockPage`)

- **Yeni "İçerik" kartı:** öğe listesi; `+` ile dosya seç, `×` ile kaldır, sürükleyerek sırala. Altında ipucu: *"Uygulamayı Explorer'dan doğrudan dock'a da sürükleyebilirsin."*
- **"Görünüm" kartı:** simge boyutu kaydırıcısı (mevcut) · yüzey ComboBox'u (mevcut) · **büyütme kaydırıcısı** (1.0–2.0, adım 0.05, canlı `1.65×` etiketi) · **konum** ComboBox'u · **çalışan uygulamaları göster** anahtarı
- **"Davranış" kartı:** `autoHide` (mevcut)
- `SaveAndApply` mevcut `Config.Save` + `WharfApp.ApplyDockChanges()` yolunu kullanır; liste değişiklikleri anında kalıcıdır.

## 11. Testler

Repo düzeni: `Method_WhenCondition_IsOutcome`, `public class`, xUnit `[Fact]`, saf statik mantığı test et.

| Suit | Kapsam |
|---|---|
| `DockMagnificationTests` | mesafe 0 → `max`; uzak mesafe 1.0'a kelepçelenir; `RadiusFor(1.65) == 3`; `Eased` kare hızından bağımsız ve monotonik; `Eased(cur, cur, dt) == cur` |
| `DockStripLayoutTests` | ölçekler kesirli imleç konumunda simetrik; komşu kaymaları kümülatif ve artan; `MaxExtentWidth` ek payı doğru; `MaxExtentHeight` plaka + büyüme payı; bütçe her canlı genişliği kapsar |
| `DockAppsComposerTests` | sabit sırası korunur; bölüm ayraçları doğru yerde; yolla eşleşen çalışan uygulama `IsRunning` dolar; eşleşmeyen çalışan uygulamalar `Running` bölümünde etikete göre sıralı; `ShowRunningApps=false` bölümleri üretmez; `ItemsEqual` değişikliği algılar |
| `DockItemStoreTests` | `TryAdd` tekilleştirir; `Move` hedef indeksi normalize eder; kalıcılık turu |
| `ConfigService` göç testleri | `Magnification` kırpma, `Position` normalize, eski config eksiksiz okunur |
| `AppBarPlacementTests` | mevcut — **değişmez** |

## 12. Doküman güncellemeleri

Aynı değişiklikte (`boundaries.md:110`, `conventions.md:11`):

1. **`docs/decisions/005-...`** — revizyon notu: dock'un pin kaynağı artık `Quick Launch\User Pinned` taraması **değildir**; görev çubuğu tarafına `.lnk` yazma kaldırılmıştır. Çalışan uygulama tespiti (ADR-005 §1'in `EnumWindows` satırı — burada "§2" denmişti, ADR'nin §2'si tepsi hibritidir) değişmez.
2. **`docs/01-architecture.md`** — `DockWindow` sorumluluk satırı (`:185`), `DockAppsComposer` (`:190`), şema tablosu (`:419-421`, `:436`).
3. **`docs/02-win32-appbar-dwm.md:310-320`** — *"macOS tipi büyütme yok"* ifadesi düşer; yeni motor, sabit-extent kuralı ve `SetWindowPos`-in kare başına çağrılmaması kuralı yazılır.
4. **`.agent/screens/dock.md`** — yeniden yazım: içerik modeli, büyütme, etkileşim, yeni ayarlar.
5. **`.agent/context/roadmap.md:34`** — bayat "ScrollViewer + yatay kaydırma" iddiası silinir (zaten `b027a02`'de kaldırılmıştı).
6. **`docs/04-gotchas.md`** — yeni tuzaklar: katmanlı pencerede OLE bırakma, sürükle/tık çakışması, `SetWindowPos` kare başına dönmemesi, DPI tazeleme.
7. **`.agent/boundaries.md`** — dock'un görev çubuğu pinlerine dokunmadığı ve `TogglePin` yazma yolunun kaldırıldığı notu.
8. **`.agent/context/stack.md`** — yeni tip adları (`DockItemStore`, `DockStrip`, `DockItemCell`, `DockMagnification`, `DockStripLayout`, `MinimizedWindowsSource`).

## 13. Riskler ve azaltma

| # | Risk | Azaltma |
|---|---|---|
| R1 | Katmanlı `Topmost` pencerenin OLE bırakma hedefi kabulü hiç ölçülmedi (`docs/04-gotchas.md:65`) | Ayarlardaki liste editörü aynı işi yapıyor; bırakma tek kırılma noktası değil. Uygulama sonrası ölçülür, çalışmıyorsa `DragOver` sessizce reddeder |
| R2 | Sürükleme ile tıklama çakışması uygulamayı açar | Eşik + bastırma (§8.3); saf kural test edilebilir |
| R3 | Çok ikonda kare maliyeti (`BitmapScalingMode.HighQuality`) | İkon bitmap'i baştan **max** büyüme boyutunda istenir (`ResolveIconPixelSize` deseni); gerekirse büyüyen ikonlarda `Linear` |
| R4 | Bayat DPI karışık ölçekli kurulumda bozulma | §7.3 |
| R5 | Reflow her karede `DesiredSize`'ı değiştirir; ölçüm maliyeti | `InvalidateMeasure` + yeniden kullanılan tampon (kare başında ayırma yok); çocuk boyutları sabit olduğu için `Image` yeniden ölçülmez/rasterize edilmez |
| R6 | `TaskbarPinnedAppsSource` silinirken başka bir referans kalır | Silmeden önce repo taraması; referans varsa `TaskbarPinnedAppsSource` korunur ve yalnızca composer'dan çıkarılır |

## 14. Uygulama sırası

1. `DockMagnification` + `DockStripLayout` (saf) + testleri
2. `DockItemStore` + config göçü + testleri
3. `MinimizedWindowsSource`
4. `DockAppsComposer` yeniden yazımı + testleri
5. `DockItemViewModel` + `DockItemCell` + `DockStrip` (XAML/WPF tarafı)
6. `DockWindow`: boyut bütçesi, konum, DPI, `RebuildStrip` düzeltmesi
7. Etkileşim: sağ tık "Pencere listesi", sürükle-sırala, dosya bırakma
8. `DockPage` ayarları
9. Doküman güncellemeleri + ADR-005 revizyonu
10. Elle doğrulama: `conventions.md:29-35` listesine dock maddeleri eklenir

## 15. Karar kaydı

- Bağımsız liste, görev çubuğu yansıması yerine — onaylandı.
- `PinnedApps` → `Dock.Items` göçü, `schemaVersion` 2'de kalır — onaylandı.
- Görev çubuğu `.lnk` yazma yolunun kaldırılması — onaylandı.
- Küçültülmüş pencereler uygulama ikonuyla temsil edilir, bitmap thumbnail yok — onaylandı.
- Konum yalnızca alt-sol/orta/sağ; yan dock kapsam dışı — onaylandı.
- Plaka gölgesi yok; ileride tek seferde üretilmiş `ImageBrush` ile eklenebilir — onaylandı.
- Köşe yarıçapı 12 DIP (mockuptaki 14 yerine, WharfBar flyout token'ı) — onaylandı.
