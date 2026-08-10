# WharfBar — Üst bar durum şeridi (status strip) tasarımı

**Tarih:** 2026-08-09  
**Durum:** Onaylandı (kullanıcı 2026-08-09) — uygulama planı:
  [`../plans/2026-08-09-topbar-status-strip.md`](../plans/2026-08-09-topbar-status-strip.md).  
**İlgili:** [ADR-005](../../decisions/005-shell-replace-live-dock-tray-hybrid.md),
[ADR-006](../../decisions/006-quick-settings-flyout.md),
[özelleştirme spec](2026-08-09-topbar-shortcuts-customization-design.md)

## Özet

Mevcut durum kataloğu + tek Quick Settings flyout (ADR-006) genişletilir. İkinci popup
host yok; bildirim içeriği aynası yok; yabancı tepsi ikon aynası yok (ADR-005).

## Varsayılan sıra (LTR)

`trayOverflow` → `dateTime` → `notifications` → `controlCenter` → `volume` → `network` → `battery`

## Yeni durum id: `controlCenter`

- Katalog: `TopBarStatusItemIds.ControlCenter`
- XAML: ayrı hit-target düğme; Segoe MDL2 glif (Control Center / switcher benzeri, örn. ViewAll `E9CE`)
- Sağ tık özelleştirme menüsünde görünür (göster/gizle + sıra)
- Tıklama → mevcut QS flyout **Main** sayfası

## Tıklama yönlendirme

| Id | Davranış |
|----|----------|
| `dateTime` | Native `Win+N` (`NotificationCenterInvoker`) |
| `notifications` | Native `Win+N` |
| `controlCenter` | QS **Main** |
| `volume` | QS **Mixer** (EarTrumpet: aygıtlar + uygulama oturumları) — Main değil |
| `network` | QS **Network** |
| `trayOverflow` | Mevcut native overflow (`TrayOverflowInvoker`) |
| `battery` | Yalnızca gösterim (mevcut) |

Tek QS host korunur; sayfa seçimi mevcut `QuickSettingsPage` + `ToggleQuickSettings` ile.

## Saat / tarih UI

- İki satırlı Win11 üst-üste yığın kaldırılır.
- Tek satır birleşik etiket, zil yanında: örn. `9 Ağu Paz 21:57`
- Biçim: `CultureInfo("tr-TR")` — kısaltılmış ay + haftanın günü + saat
- `dateTime` ve `notifications` ayrı hit target kalır; ikisi de `Win+N`

## Config / migrasyon

- `schemaVersion` 1 kalır (breaking bump yok).
- Yeni varsayılan katalog sırası yukarıdaki LTR set.
- Soft alan: `topBar.statusCatalogVersion` (yoksa `0`). `0` → `1` geçişinde
  `controlCenter` yoksa `volume` önüne eklenir; ardından sürüm `1` yazılır.
  Böylece eski config’ler CC kazanır; kullanıcı CC’yi gizledikten sonra yeniden
  zorlanmaz.
- Bilinmeyen id’ler `Normalize` ile düşülür; boş liste → tam varsayılan.

## Kısıtlar (değişmez)

- Mica + `AllowsTransparency=true` yok
- Çıkışta `ABM_REMOVE` + görev çubuğu restore
- Ham P/Invoke `Native/` içinde `internal`
- `Views/` tercih; `global.json` yok
- Yabancı tepsi / bildirim içeriği aynası yok

## Reddedilenler

1. İkinci Control Center / QS popup hostu
2. Bildirim merkezi içerik aynası
3. Yabancı NotifyIcon çizimi / click-forward (ADR-005)
4. Volume → QS Main (eski davranış; artık Mixer)
