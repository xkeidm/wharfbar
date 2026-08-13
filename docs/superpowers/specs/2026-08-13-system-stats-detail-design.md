# WharfBar — CPU / GPU / RAM detay alanları (tasarım)

**Tarih:** 2026-08-13  
**Durum:** Tasarım onaylandı (kullanıcı 2026-08-13) — uygulama planı:
  [`../plans/2026-08-13-system-stats-detail.md`](../plans/2026-08-13-system-stats-detail.md).  
**İlgili:** [`docs/01-architecture.md`](../../01-architecture.md),
[`.agent/project.md`](../../../.agent/project.md),
[`docs/02-win32-appbar-dwm.md`](../../02-win32-appbar-dwm.md) (yeni P/Invoke imzaları)

> Bu dosya **final ADR değildir**. Davranış uygulamada kilitlenince `docs/` + `.agent/`
> aynı değişiklikle güncellenir.

## Özet

Üst bardaki CPU / GPU / RAM rozetleri yüzdeyle sınırlı kalmaz. Her alan için kullanıcı
`Gizli` / `Yalnızca tooltip` / `Üst bar` seçer. Varsayılan kompakt görünüm korunur:
barda `%`, tooltip’te `%` + ekstra. Yeni NuGet paketi yok; sıcaklık / fan / kart adı yok.

## Karar (onaylı)

**Yaklaşım:** Alan başına tek yerleşim seçimi. `Üst bar` seçilince alan tooltip’te de
görünür (hover’da bilgi kaybolmaz). Tooltip = `Gizli` olmayan tüm **kullanılabilir** alanlar.

| Rozet | Alan | Config anahtarı | Varsayılan |
|--------|------|-----------------|------------|
| CPU | Kullanım `%` | `cpuPercentPlacement` | `bar` |
| CPU | Anlık hız (GHz) | `cpuGhzPlacement` | `tooltip` |
| RAM | Kullanım `%` | `ramPercentPlacement` | `bar` |
| RAM | Kullanılan / toplam GB | `ramUsedTotalPlacement` | `tooltip` |
| GPU | Kullanım `%` | `gpuPercentPlacement` | `bar` |
| GPU | VRAM kullanılan / toplam GB | `gpuVramPlacement` | `tooltip` |

Yerleşim değerleri (küçük harf, JSON camelCase): `bar` | `tooltip` | `hidden`.
Bilinmeyen / boş değer → o alanın tablodaki varsayılanı.

Mevcut ana anahtarlar değişmez: `showSystemStats`, `showCpuStats`, `showGpuStats`,
`showRamStats`. Ana rozet kapalıysa o rozetin yerleşim kontrolleri gizlenir ve rozet
çizilmez (yerleşim değerleri config’de durur).

## Rozet görünürlüğü

Sıra:

1. `showSystemStats == false` → üç rozet yok.
2. İlgili `show*Stats == false` → o rozet yok.
3. Rozetin tüm alanları `hidden` → o rozet yok.
4. `hidden` olmayan alanların hiçbiri okunamıyorsa (ör. GPU `%` ve VRAM ikisi de yok) → o rozet yok.
5. Aksi halde rozet görünür.

Bar metni: okunabilen ve yerleşimi `bar` olan alanlar, ` · ` (U+00B7, iki yanında boşluk)
ile birleşir. Hiç `bar` alanı yoksa (yalnızca tooltip) bar metni boş kalır; `CPU` / `GPU` /
`RAM` etiketi durur, detay tooltip’tedir.

## Metin biçimi

| Alan | Bar / tooltip metni | Not |
|------|---------------------|-----|
| CPU `%` | `32%` | 0–100 tamsayı |
| CPU GHz | `4.2 GHz` | 1 ondalık, `CurrentCulture` |
| RAM `%` | `48%` | `dwMemoryLoad` |
| RAM GB | `8.1 / 16 GB` | kullanılan 1 ondalık; toplam 0.15 içinde tam sayıysa 0 ondalık, değilse 1 |
| GPU `%` | `12%` | mevcut 3D motor toplamı, clamp 0–100 |
| GPU VRAM | `2.1 / 8 GB` | RAM GB ile aynı yuvarlama |

Tooltip satırları Türkçe, her alan ayrı satır (ör. `CPU kullanımı: 32%` / `CPU hızı: 4.2 GHz`).
Bar’da birden fazla alan varken etiket + birleşik metin: `CPU  32% · 4.2 GHz`.

## Ayarlar UI

`Views/Pages/GeneralPage.xaml` — mevcut CPU / GPU / RAM alt kartları genişler.

Ana “Sistem İstatistiklerini Göster” kapalıysa üç alt kart gizlenir (bugün gibi).
Alt kart açıkken (ana rozet toggle açık):

1. Satır: rozet aç/kapa (mevcut toggle).
2. Satır: “Kullanım yüzdesi” + ComboBox (`Gizli` / `Yalnızca tooltip` / `Üst bar`).
3. Satır: ekstra alan etiketi + aynı ComboBox.
   - CPU: “Anlık hız (GHz)”
   - RAM: “Kullanılan / toplam”
   - GPU: “VRAM (kullanılan / toplam)”

Rozet toggle kapalıyken yerleşim ComboBox satırları **gizlenir** (disabled bırakılmaz);
config değerleri silinmez. Donanım alanı okunamasa bile ComboBox durur (ör. VRAM yokken
VRAM satırı ayarda kalır). Değişiklik `Config.Save` + `TopBar.ApplyConfigChanges` ile
anında uygulanır.

## Config

`schemaVersion` **1** kalır. Eksik JSON anahtarları property varsayılanına düşer; `ConfigService.Normalize`
yerleşim string’lerini `StatPlacement.Normalize(value, default)` ile düzeltir.

`TopBarConfig` ek alanlar:

```csharp
public string CpuPercentPlacement { get; set; } = StatPlacement.Bar;
public string CpuGhzPlacement { get; set; } = StatPlacement.Tooltip;
public string RamPercentPlacement { get; set; } = StatPlacement.Bar;
public string RamUsedTotalPlacement { get; set; } = StatPlacement.Tooltip;
public string GpuPercentPlacement { get; set; } = StatPlacement.Bar;
public string GpuVramPlacement { get; set; } = StatPlacement.Tooltip;
```

`StatPlacement` (`Models/`, `TopBarBackgroundModes` deseni): sabitler `Bar` / `Tooltip` /
`Hidden`; `Normalize(string? value, string fallback)`.

Örnek JSON:

```json
"topBar": {
  "showSystemStats": true,
  "showCpuStats": true,
  "cpuPercentPlacement": "bar",
  "cpuGhzPlacement": "tooltip"
}
```

## Mimari birimler

| Birim | Görevi | Bağımlılık |
|-------|--------|------------|
| `SystemMonitorService` | Ham snapshot üretir (poll 1.5 sn, `Task.Run`) | `SystemInfoNative`, DXGI wrapper, mevcut GPU sayaçları |
| `SystemStatsSnapshot` | Yüzde + GB + GHz + kullanılabilirlik bayrakları; **önceden biçimli bar metni yok** | — |
| `SystemStatsFormatter` | Snapshot + yerleşimler → bar metni, tooltip, rozet görünürlüğü | `StatPlacement`, `TopBarConfig` |
| `SystemInfoNative` | `GetSystemTimes`, `GlobalMemoryStatusEx`, `CallNtPowerInformation` | `kernel32`, `powrprof` |
| `DxgiAdapterMemory` (`Native/`) | DXGI adaptör enum + local (dedicated) bellek sorgusu | `dxgi.dll` COM; yeni paket yok |
| `TopBarWindow.ApplySystemStats` | Formatter çıktısını XAML’e yazar | formatter + config |
| `GeneralPage` | Ana/rozet toggle + yerleşim ComboBox | `ConfigService` |

`SystemMonitorService` config okumaz. Poll her zaman GHz ve VRAM’i de dener; UI hangi
alanın çizileceğine karar verir. Başarısız okuma snapshot’ta `null` / `false` olur.

### Snapshot alanları (hedef)

```
CpuPercent: int
CpuGhz: double?                 // null = okunamadı
RamPercent, UsedRamGb, TotalRamGb
IsGpuUsageAvailable: bool
GpuPercent: int                 // yalnızca IsGpuUsageAvailable iken anlamlı
IsGpuVramAvailable: bool
GpuUsedVramGb, GpuTotalVramGb: double?  // null = yok
```

## Veri kaynakları

**CPU kullanımı:** mevcut `GetSystemTimes` delta (değişmez).

**CPU GHz:** `powrprof.dll` `CallNtPowerInformation` (`POWER_INFORMATION_LEVEL` =
`ProcessorInformation` = 11). Tampon: `sizeof(PROCESSOR_POWER_INFORMATION) * çekirdek sayısı`
(`GetSystemInfo.dwNumberOfProcessors`). `CurrentMhz` değerlerinin ortalaması / 1000.0.
`CurrentMhz == 0` veya API başarısız → `CpuGhz = null` (GHz alanı atlanır; `%` durur).
Yeni paket yok; `AllowUnsafeBlocks` yok. Tampon: yönetilen `PROCESSOR_POWER_INFORMATION[]`
+ `Marshal` (pin / `AllocHGlobal`); `unsafe` / `stackalloc` yok.

**RAM:** mevcut `GlobalMemoryStatusEx` (değişmez).

**GPU kullanımı:** mevcut `GPU Engine` / `engtype_3D` sayaçları (değişmez).

**GPU VRAM:** DXGI, paket yok.

- `IDXGIFactory1` oluştur; adaptörleri enumera et.
- `DXGI_ADAPTER_FLAG_SOFTWARE` ve “Microsoft Basic Render Driver” atlanır.
- `IDXGIAdapter3.QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL)`.
- Kullanılan = `CurrentUsage`; toplam = `DXGI_ADAPTER_DESC.DedicatedVideoMemory`
  (0 ise `Budget`).
- Birden fazla adaptör: **en büyük `DedicatedVideoMemory`** seçilir.
- GPU `%` hâlâ tüm 3D motor örneklerinin toplamıdır (adaptör filtresi yok, mevcut davranış).
- DXGI veya VRAM 0/`E_FAIL` → `IsGpuVramAvailable = false`; GPU `%` bağımsızdır.

Poll DXGI ve GHz’i UI thread dışında okur (mevcut `Task.Run`). DXGI COM aynı arka plan
işinde. `CreateDXGIFactory1` `CO_E_NOTINITIALIZED` dönerse o iş parçacığında bir kez
`CoInitializeEx(COINIT_MULTITHREADED)` çağrılır; poll sonunda `CoUninitialize`.

## Hata davranışı

| Durum | Sonuç |
|-------|--------|
| GHz API yok / 0 | GHz alanı yok sayılır; CPU `%` durur |
| VRAM okunamaz | VRAM alanı yok sayılır; GPU `%` durur |
| GPU `%` yok, VRAM var | GPU rozeti VRAM yerleşimine göre durabilir |
| GPU `%` yok, VRAM yok | GPU rozeti gizlenir (bugünkü `IsGpuAvailable` ile uyumlu) |
| Sayaç kategorisi yok | Mevcut `_gpuCounterFailed` gibi bir kez işaretle, tekrar deneme |

## Kısıtlar (değişmez)

- Yeni NuGet / LibreHardwareMonitor / NVAPI / ADL yok
- `AllowsTransparency=true` + Mica yok
- Çıkışta `ABM_REMOVE` + görev çubuğu restore
- Ham P/Invoke `Native/` içinde `internal`
- `global.json` yok
- `schemaVersion` bump yok
- Düşük kaynak: 1.5 sn poll; her tick’te yeni `PerformanceCounter` seli ekleme (GPU Engine
  yolu zaten pahalı — bu işte daha da kötüleştirme; DXGI hafif tutulur)

## Reddedilenler

1. Sıcaklık, fan, GPU saati, işlemci/kart adı (v1 dışı; seçenek B/C)
2. Alan başına iki bağımsız checkbox (bar + tooltip)
3. Tooltip’i her zaman tam detay, bar’ı ayrı tutan “yalnızca barda göster” modeli
4. Varsayılan olarak barda GHz / GB (kullanıcı kompakt varsayılan istedi)
5. `schemaVersion` 2

## Doküman (uygulama ile aynı değişiklik)

- `docs/01-architecture.md` — `SystemMonitorService` + `SystemStatsFormatter` + DXGI
- `docs/02-win32-appbar-dwm.md` bölüm (c) — `CallNtPowerInformation` + DXGI VRAM imzaları
- `.agent/project.md` — durum satırına GHz / VRAM / yerleşim ayarı

## Manuel doğrulama

- Varsayılan: bar `%`; tooltip `%` + ekstra (CPU GHz, RAM GB, GPU VRAM varsa).
- CPU GHz → Üst bar: `CPU  32% · 4.2 GHz`.
- RAM `%` → Gizli, GB → Üst bar: `RAM  8.1 / 16 GB`.
- CPU her iki alan Gizli: CPU rozeti yok.
- CPU `%` Gizli, GHz Yalnızca tooltip: rozet `CPU` etiketi, hover’da GHz.
- Ayar değişince üst bar anında güncellenir; `%AppData%/WharfBar/config.json` yazılır.
- GPU’suz / Basic Render: GPU rozeti yok veya yalnızca okunabilen alanlar.
- Uygulama kapanınca AppBar ve görev çubuğu restore (regresyon).
