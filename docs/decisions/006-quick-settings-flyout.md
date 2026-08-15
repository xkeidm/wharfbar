# ADR-006: Hızlı ayarlar flyout (ses karıştırıcı + ağ)

**Durum:** Kabul edildi (2026-08-09; [tek-aygıt karıştırıcı UX güncellemesi](../superpowers/specs/2026-08-09-mixer-single-device-design.md) aynı gün)  
**Tarih:** 2026-08-09  
**Bağlam:** Üst barda ses ve ethernet/ağ tıklamaları için Win11 Quick Settings benzeri
panel; **tek aygıt + dropdown + uygulama oturumları** (EarTrumpet ilhamı); ağ seçenekleri
belgelenmiş .NET / WinRT yollarıyla (ADR-005 tepsi aynası yasağı korunur).

## Karar

**Tek paylaşılan Quick Settings flyout** (`QuickSettingsFlyout` + `QuickSettingsPopup`):

| Giriş | Açılış sayfası |
|-------|----------------|
| Control Center / hızlı ayarlar ikonu | Ana (2×3 karo + master slider + karıştırıcı chevron) |
| Ses ikonu | Ses karıştırıcı (tek aygıt + dropdown) |
| Ağ / ethernet ikonu | Ağ sayfası (durum + Ayarlar URI'leri) |
| Karıştırıcı chevron (flyout içi) | Ses karıştırıcı |

Popup `AllowsTransparency=true` + `PopupAnimation=Fade` (Win11 Quick Settings benzeri
geçiş); popup ayrı bir pencere olduğundan Mica kuralı etkilenmez (ADR-004 / gotcha —
kural **ana pencere** içindir). **Ayrı bir ses popup'ı yok** — karıştırıcı derinliği QS sayfası
olarak kalır (aşağıdaki UX notu). Üst bar varsayılan sıra ve `controlCenter` id:
[`../superpowers/specs/2026-08-09-topbar-status-strip-design.md`](../superpowers/specs/2026-08-09-topbar-status-strip-design.md).

### Ses karıştırıcı (tek aygıt + dropdown)

- Üstte **Çıkış cihazı** etiketi + seçili aygıt adı (tıklanınca aktif render
  listesi; seçim → `IPolicyConfig.SetDefaultEndpoint`)
- Altında master mute + slider + % (`VolumeService` / varsayılan endpoint)
- Altında seçili aygıttaki **tüm** uygulama oturumları (ikon + slider + %;
  `CoreAudioSessions.ListSessionsOnDevice`)
- Canlı yenileme: flyout mixer sayfası açıkken ~750 ms poll
- Fluent tema; EarTrumpet mor kopyası yok; ayrı volume popup yok

### Ağ / hızlı karolar

- Durum: `.NET NetworkInformation` (tepisi okuma yok)
- Wi-Fi / Bluetooth / uçak: WinRT `Windows.Devices.Radios` (erişim yoksa Settings URI)
- Yansıt: `Win+P` (`SendInputKeys`)
- Gece ışığı / enerji tasarrufu: `ms-settings:` URI (doğrudan toggle v1'de yok)
- Ağ sayfası: ethernet/Wi-Fi Settings deep-link

## Gerekçe

Ayrı ses ve ağ pop-up'ları Win11 dilinden uzaklaşır; tek host hem paylaşımı hem de
karıştırıcı derinliğini karşılar. Belgelenmemiş tepsi aynası yine yasak; PolicyConfig yalnızca
varsayılan ses aygıtı için dar kapsamda kabul edilir. Mixer **sayfa** UI'sı tek aygıt +
dropdown + oturum listesine sadeleştirildi; mimari (tek flyout) değişmedi.

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
