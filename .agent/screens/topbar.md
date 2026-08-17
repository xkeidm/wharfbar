# Ekran: Top bar

`src/WharfBar/Views/TopBarWindow.xaml` (+ `.xaml.cs`).

Yükseklik 30 DIP, **AppBar** (work area rezerve), birincil monitör, her zaman üstte.
Arka plan modu config'den: `mica` (varsayılan, `AllowsTransparency=false`),
`transparent` / `tint` (`AllowsTransparency=true` — Mica ile asla birlikte değil).

## Sol küme

- **Windows logosu** (geometri çizimi, font glifi değil — Mica'da kaybolmaz) → sol tık menü:
  - **Ayarlar (PC)** → `ms-settings:` · **Profil** → `ms-settings:yourinfo` · **Bilgisayar hakkında** → `ms-settings:about`
  - **Bilgisayarı kilitle** / **Uyku** → `PowerActions` (LockWorkStation / SetSuspendState)
  - **Kapat** / **Yeniden başlat** → `PowerActions` (shutdown.exe `/s|/r /t 0`)
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

`QuickSettingsFlyout` — tek Fluent popup (`AllowsTransparency=true`; ana pencerenin
Mica'sıyla ilgisi yok); Main / Mixer /
Network sekmeleri paylaşılır (ADR-006). Mixer: varsayılan aygıt + dropdown + uygulama
oturumları. Açılış noktası tıklanan ikona göre sekme seçer. Açılışta Win11 tarzı
yukarıdan aşağı slide+fade animasyonu (`AnimateFlyoutOpen`: ~10px kayma, 170ms
CubicEase EaseOut; parlaklık flyout'u da aynı animasyonu kullanır).

### Wi-Fi sayfası (ağ listesi + şifre paneli)

`WifiService` (wlanapi.dll) canlı ağ listesi: SSID, sinyal çubukları (`WifiService.SignalBars`),
güvenlik, bağlı / kayıtlı profilli işaretleme (2 sn poll; yalnızca sayfa açıkken).
Korumalı ağa tık → aynı sayfada şifre paneli (SSID, şifre kutusu, Bağlan / İptal, hata metni);
başarılı bağlantıda panel kapanır, liste yenilenir. "Wi-Fi ayarlarını aç" → Windows Settings.

### Bluetooth sayfası (cihaz listesi)

`BluetoothService` eşleşmiş cihazları listeler (`BluetoothDevice.GetDeviceSelector`);
bağlı olanlar `ConnectionStatus` ile işaretlenir. Radyo açık/kapalı özet altta.
"Bluetooth ayarlarını aç" → Windows Settings.

### Yansıt sayfası (4 mod)

`ProjectionService` + `QueryDisplayConfig` ile mevcut topoloji seçili gösterilir;
tıklama `SetDisplayConfig` ile uygular: **Yalnızca PC ekranı / Çoğalt / Genişlet / Yalnızca 2. ekran**.

### Gece ışığı tile (gerçek durum)

`NightLightService` (CloudStore best-effort) gerçek durumu okur; tile aktiflik rengini ona
göre çizer, tıklama gerçek toggle yapar (önce oturum override, sonra registry yazımı).

### Tile animasyonları

Tile aktiflik değişiminde **in-place** animasyon: ikon 90 ms "pop" (aktif 1.15× / pasif 0.9×,
AutoReverse + CubicEase) + 250 ms renk geçişi (`ColorAnimation`, QuadraticEase) — arka plan
accent/base arasında yumuşatılır. Devre dışı tile'lar `QsTileDisabled` ile 0.45 opaklıkta.

## Parlaklık flyout

`BrightnessFlyout` — Monitorian tarzı: master "Tüm Ekranlar" slider'ı + bağımsız monitör
kartları; 50ms debounced arka plan kuyruğu. `MonitorBrightnessService` + `MonitorNative`
(DDC/CI + WMI hibrit).

## Tuzaklar

- Arka plan modu değişikliği pencereyi yeniden oluşturabilir (sağ tık menüsü yeniden açılır).
- `AllowsTransparency` ctor'da `BackgroundMode`'dan set edilir — XAML'e yazılmaz.
- Tooltip fontları Segoe UI (`AppTextFontFamily`) — MDL2 tofu sorunu yok.