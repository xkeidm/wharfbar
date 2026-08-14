# Ekran: Top bar

`src/WharfBar/Views/TopBarWindow.xaml` (+ `.xaml.cs`).

Yükseklik 30 DIP, **AppBar** (work area rezerve), birincil monitör, her zaman üstte.
Arka plan modu config'den: `mica` (varsayılan, `AllowsTransparency=false`),
`transparent` / `tint` (`AllowsTransparency=true` — Mica ile asla birlikte değil).

## Sol küme

- **Windows logosu** (geometri çizimi, font glifi değil — Mica'da kaybolmaz) → sol tık menü:
  - **WharfBar Ayarları** → [`settings.md`](settings.md)
  - **WharfBar hakkında** → `AboutWindow`
  - **WharfBar'ı kapat** → düzgün çıkış (görev çubuğu restore + `ABM_REMOVE`)

## Sağ küme — durum şeridi (`StatusStrip`)

Varsayılan soldan sağa sıra ve davranış (config `topBar.statusItems`):

| id | Arayüz | Tıklama davranışı |
|----|--------|-------------------|
| `trayOverflow` | "Gizli simgeler" (glif `E70E`) | overflow bölgesi popup'ı (Win+B) |
| `dateTime` | saat+tarih tek satır, `tr-TR` kısaltmaları | sistem takvim/panel |
| `notifications` | zil glifi `EA8F` | Win+N (bildirim merkezi) |
| `controlCenter` | hızlı ayarlar glifi `E9CE` | `QuickSettingsFlyout` → **Main** sekmesi |
| `volume` | ses glifi `E767` | `QuickSettingsFlyout` → **Mixer** sekmesi |
| `brightness` | güneş glifi `E706` | `BrightnessFlyout` (monitör kartları, DDC/CI + WMI) |
| `network` | ağ glifi `E701` | `QuickSettingsFlyout` → **Network** sekmesi |
| `battery` | pil glifi + yüzde metni | yok (gösterim) |

## Durum şeridi sağ tık menüsü

`TopBarWindow.BuildStatusItemsMenu`:
- **Öğe göster/gizle** — tüm katalog onay kutuları (checkable)
- **Arka plan** (alt menü): Mica / Transparent / Tint radio'ları + **Opaklık slider'ı**
  (yalnızca Tint'te etkin, 0–100, debounced kaydetme)
- Tıklanan öğe varsa: **Sola taşı / Sağa taşı**
- **Varsayılana sıfırla** — `TopBarStatusItemIds.DefaultOrder`

## Hızlı ayarlar flyout

`QuickSettingsFlyout` — tek opak Fluent popup (`AllowsTransparency=false`); Main / Mixer /
Network sekmeleri paylaşılır (ADR-006). Mixer: varsayılan aygıt + dropdown + uygulama
oturumları. Açılış noktası tıklanan ikona göre sekme seçer.

## Parlaklık flyout

`BrightnessFlyout` — Monitorian tarzı: master "Tüm Ekranlar" slider'ı + bağımsız monitör
kartları; 50ms debounced arka plan kuyruğu. `MonitorBrightnessService` + `MonitorNative`
(DDC/CI + WMI hibrit).

## Tuzaklar

- Arka plan modu değişikliği pencereyi yeniden oluşturabilir (sağ tık menüsü yeniden açılır).
- `AllowsTransparency` ctor'da `BackgroundMode`'dan set edilir — XAML'e yazılmaz.
- Tooltip fontları Segoe UI (`AppTextFontFamily`) — MDL2 tofu sorunu yok.