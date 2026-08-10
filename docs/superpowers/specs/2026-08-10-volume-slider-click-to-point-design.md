# WharfBar — Ses çubuğu tıklayınca noktaya git (click-to-point)

**Tarih:** 2026-08-10  
**Durum:** Onaylandı (kullanıcı 2026-08-10) — uygulama planı:
  [`../plans/2026-08-10-volume-slider-click-to-point.md`](../plans/2026-08-10-volume-slider-click-to-point.md).  
**İlgili:** [mixer single-device](2026-08-09-mixer-single-device-design.md),
[ADR-006](../../decisions/006-quick-settings-flyout.md)

## Özet

Quick Settings içindeki tüm **ses** `Slider` kontrollerinde, track’in herhangi
bir yerine sol tıklanınca thumb tıklanan konuma gider (ve sürükleme o
noktadan devam eder). Varsayılan WPF davranışı (yalnızca `LargeChange`
adımı) değiştirilir.

## Kararlar

| Konu | Karar |
|------|--------|
| Yaklaşım | WPF `Slider.IsMoveToPointEnabled = true` |
| Kapsam | Main master, Mixer master, uygulama oturum slider’ları |
| Hariç | Üst bar arka plan opaklık menü slider’ı (ses değil) |
| Özel stil / handler | Yok — yerleşik özellik yeterli |

## Değişiklik noktaları

1. `QuickSettingsFlyout.xaml` — `MasterVolumeSlider`, `MixerMasterSlider`:
   `IsMoveToPointEnabled="True"`
2. `QuickSettingsFlyout.xaml.cs` — `BuildSessionRow` içinde oluşturulan
   `Slider`: `IsMoveToPointEnabled = true`

Mevcut `ValueChanged`, drag started/completed ve mute/disable mantığı
değişmez. `IsSnapToTickEnabled` / `TickFrequency=1` korunur; tıklama sonrası
değer en yakın tamsayı yüzdeye oturur.

## Non-goals

- Opaklık veya ses dışı diğer slider’lar
- Özel track template / click hit-test handler
- Klavye / mouse-wheel davranışı değişikliği

## Doğrulama

- Main QS: master track’e tıkla → seviye o yüzdeye gider; sistem sesi güncellenir
- Mixer: master ve bir oturum track’ine tıkla → aynı
- Tıklayıp sürükle → thumb tıklanan noktadan sürüklenmeye devam eder
- Sessiz (muted) slider disabled ise tıklama etki etmez
