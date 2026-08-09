# ADR-006: Hızlı ayarlar flyout (ses karıştırıcı + ağ)

**Durum:** Kabul edildi (2026-08-09; karıştırıcı UX güncellemesi aynı gün)  
**Tarih:** 2026-08-09  
**Bağlam:** Üst barda ses ve ethernet/ağ tıklamaları için Win11 Quick Settings benzeri
panel; EarTrumpet tarzı **çoklu çıkış aygıtı + uygulama sesleri**; ağ seçenekleri
belgelenmiş .NET / WinRT yollarıyla (ADR-005 tepsi aynası yasağı korunur).

## Karar

**Tek paylaşılan Quick Settings flyout** (`QuickSettingsFlyout` + `QuickSettingsPopup`):

| Giriş | Açılış sayfası |
|-------|----------------|
| Ses ikonu | Ana (2×3 karo + master slider + karıştırıcı chevron) |
| Ağ / ethernet ikonu | Ağ sayfası (durum + Ayarlar URI’leri) |
| Karıştırıcı chevron | Ses karıştırıcı (EarTrumpet düzeni) |

Popup **opak Fluent** (`AllowsTransparency=false`); Mica ile şeffaflık eşleştirilmez
(ADR-004 / gotcha). **Ayrı bir ses popup’ı yok** — karıştırıcı derinliği QS sayfası
olarak kalır (aşağıdaki UX notu).

### Ses karıştırıcı (EarTrumpet düzeni)

Tek ComboBox + düz oturum listesi yerine:

- Her aktif **render** aygıtı ayrı blok: başlık (ad + daralt/genişlet chevron), master
  satır (hoparlör ikonu + slider + sayısal %), altında o aygıta bağlı uygulama satırları
  (ikon + slider + %)
- Varsayılan aygıt üstte; başlığa tıklayınca `IPolicyConfig.SetDefaultEndpoint`
- Master seviye: aygıt başına `IAudioEndpointVolume` (varsayılan aygıt `VolumeService` ile
  senkron)
- Uygulama oturumları: her aygıtta `IAudioSessionManager2` (`CoreAudioSessions`); ikonlar
  süreç yolu / `GetIconPath` + `AppIconService` / `SHGetFileInfo` (yoksa MDL2 glif)
- Canlı yenileme: flyout mixer sayfası açıkken ~750 ms poll

### Ağ / hızlı karolar

- Durum: `.NET NetworkInformation` (tepisi okuma yok)
- Wi-Fi / Bluetooth / uçak: WinRT `Windows.Devices.Radios` (erişim yoksa Settings URI)
- Yansıt: `Win+P` (`SendInputKeys`)
- Gece ışığı / enerji tasarrufu: `ms-settings:` URI (doğrudan toggle v1’de yok)
- Ağ sayfası: ethernet/Wi-Fi Settings deep-link

## Gerekçe

Ayrı ses ve ağ pop-up’ları Win11 dilinden uzaklaşır; tek host hem paylaşımı hem de
karıştırıcı derinliğini karşılar. Belgelenmemiş tepsi aynası yine yasak; PolicyConfig yalnızca
varsayılan ses aygıtı için dar kapsamda kabul edilir. EarTrumpet referans ekranları çoklu
aygıt bloklarını gösterdiği için mixer **sayfa** UI’sı genişletildi; mimari (tek flyout)
değişmedi.

## Sonuçlar

- `Views/QuickSettingsFlyout`, `Services/AudioMixerService`, `Services/QuickSettingsService`,
  `Native/CoreAudioDevices`, `Native/CoreAudioSessions`, `Native/AudioEndpointVolume`
- Eski tek-slider `VolumePopup` kaldırıldı
- Doküman: `docs/01-architecture.md`, geliştirme sırası / roadmap güncellenir

## Alternatifler (reddedilen)

1. **İki ayrı Popup** (ses vs ağ) — çift UX; reddedildi.
2. **Yalnızca `ms-settings:sound`** — EarTrumpet ihtiyacını karşılamaz.
3. **Yabancı tepsi ikon forward** — ADR-005; reddedildi.
4. **Ayrı EarTrumpet popup (QS dışı)** — ADR-006 tek-host kararını böler; reddedildi;
   QS mixer sayfası yeterince derinleştirildi.
