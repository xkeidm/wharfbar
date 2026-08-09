# WharfBar — Ses karıştırıcı (tek aygıt + dropdown) tasarımı

**Tarih:** 2026-08-09  
**Durum:** Onaylandı (kullanıcı 2026-08-09) — uygulama planı henüz yazılmadı.  
**İlgili:** [ADR-006](../../decisions/006-quick-settings-flyout.md),
[status strip spec](2026-08-09-topbar-status-strip-design.md)

## Özet

QS flyout **Mixer** sayfası, çoklu aygıt blokları yerine EarTrumpet’e yakın ama
WharfBar Fluent varsayılanına uygun **tek aygıt** düzenine geçer:

1. Çıkış cihazı seçici (kapalıyken yalnız seçili ad)
2. Seçili aygıtın master ses seviyesi
3. Seçili aygıttaki uygulama oturumları (ses vermese de listede)

Ayrı volume popup yok; tek QS host (ADR-006) korunur. EarTrumpet mor teması
kopyalanmaz.

## Kararlar (brainstorm)

| Konu | Karar |
|------|--------|
| Yaklaşım | Tek aygıt + ComboBox / expander; mevcut flyout içinde |
| Oturum listesi | Seçili aygıttaki **tüm** uygulama oturumları (EarTrumpet gibi) |
| Aygıt seçimi | Sistem varsayılan çıkışını değiştirir (`IPolicyConfig.SetDefaultEndpoint`) |
| Tema | Fluent / WPF-UI dinamik fırçalar; mor accent yok |

## Düzen (UX)

Giriş: üst bar `volume` veya Main’deki karıştırıcı chevron → `QuickSettingsPage.Mixer`.

Yukarıdan aşağı:

1. **Başlık** — geri düğmesi + “Ses karıştırıcı” (Main’e dönüş)
2. **Çıkış cihazı** — küçük etiket; altında seçili aygıt adı + chevron.
   Kapalı: yalnızca seçili aygıt. Açık: aktif render aygıtları listesi; seçim →
   sistem default
3. **Master satır** — mute ikonu + slider + sayısal % (varsayılan aygıt =
   `VolumeService` ile senkron)
4. İnce ayırıcı
5. **Uygulama oturumları** — ikon + slider + %; boşsa kısa “Uygulama oturumu yok”
   metni

Genişlik ~360–380 DIP; scroll gerektiğinde oturum listesi kayar. Max yükseklik
mevcut Mixer scroll sınırına yakın (~420 DIP).

## Mimari / veri

- **Host:** `QuickSettingsFlyout` Mixer sayfası (XAML + code-behind yenilenir)
- **Servis:** `AudioMixerService` snapshot modeli sadeleşir:
  - `Devices` — dropdown için aktif render listesi
  - `Selected` — varsayılan render (Id, Name, Percent, IsMuted)
  - `Sessions` — yalnız seçili aygıt (`CoreAudioSessions.ListSessionsOnDevice`)
- **Seçim:** mevcut `TrySetDefaultDevice` → poll yeniler → üst bar ses ikonu
  default’a bağlı kalır
- **Poll:** mixer açıkken ~750 ms (değişmez)
- **Native:** `CoreAudioDevices` / `CoreAudioSessions` / `AudioEndpointVolume`
  kalır; çoklu-blok UI kaldırılır

Uygulama sırasında [ADR-006](../../decisions/006-quick-settings-flyout.md) “çoklu
aygıt blokları” metni bu tek-aygıt + dropdown kararına güncellenir; `.agent/` /
`docs/01-architecture.md` mixer özeti senkronlanır.

## Kenar durumlar

| Durum | Davranış |
|-------|----------|
| Aktif render aygıtı yok | “Çıkış aygıtı bulunamadı”; master ve oturumlar boş / disabled |
| Oturum yok | Master çalışır; “Uygulama oturumu yok” |
| `SetDefaultEndpoint` başarısız | Seçim geri alınır; mevcut varsayılan kalır |
| Sistem sesleri oturumu | Listede kalır (EarTrumpet gibi) |

## Non-goals

- Ayrı `VolumeMixerPopup` / ikinci host
- EarTrumpet mor / marka kopyası
- Yabancı tepsi ikon aynası (ADR-005)
- Yalnızca “şu an ses veren” oturum filtresi (bilinçli olarak reddedildi)
- Yeni NuGet bağımlılığı
- Config şeması değişikliği

## Doğrulama (manuel)

1. `dotnet build WharfBar.sln`
2. Ses ikonu → Mixer: tek seçili cihaz + master + oturumlar
3. Dropdown ile aygıt değiştir → Windows default değişir; üst bar ses seviyesi
   yeni default’a uyum sağlar
4. Master / oturum slider ve mute çalışır
5. Oturumsuz aygıtta boş durum metni görünür

## Başarı ölçütü

Ses karıştırıcı açıldığında kullanıcı yalnızca seçili çıkış cihazını, onun
master seviyesini ve o aygıttaki uygulama oturumlarını görür; diğer cihazlar
yalnızca çıkış cihazı satırına tıklanınca listelenir ve seçilebilir.
