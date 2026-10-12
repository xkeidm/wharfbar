# ADR-013: Üst bar derinliği — iki uydu, saat paneli, kıvılcım

**Durum:** Kabul edildi (2026-10-11)
**İlişki:** [ADR-012](012-island-multi-face.md) hap yüzlerini korur. Bu karar HUD sırasında ikinci uyduyu, hap klavyesini, pil eşiğini, ada müzik aramasını ve adanın dışında kalan üst bar derinliğini ekler. Dock bu kararda yoktur.

## Karar

| Konu | Seçim |
|------|--------|
| İki uydu | HUD (`Volume`, `Brightness`, `Clip`, `Battery`, `Drop`) + kalıcı müzik oturumu + pano yüzeyi: `FirstSatellite = Music`, `SecondSatellite = Clipboard`. Aynı HUD ikincil barda (pano yüzeyi yok): yalnız `Music`. HUD + kalıcı müzik yok + pano yüzeyi: yalnız `Clipboard`. Şimdi çalıyor + pano yüzeyi: yalnız `Clipboard`. Boşta ikisi de `None`. Aynı tür iki kez yazılmaz. Tavan iki. `transient` müzik uydu sayılmaz |
| Uydu klavyesi | Hap odaklıyken Sol/Sağ görünür uydular arasında seçim yürür (`None` atlanır). Seçili uydu varsa Enter `ClickSatellite` çağırır. Seçim yoksa Enter ve Space bugünkü hap tıklamasıdır. Seçili uyduda vurgu konturu. Müzik gövdesi odaklıyken Sol/Sağ önceki/sonraki parça kalır. Pano arama kutusu okları yemez. Esc paneli kapatır |
| `lowBatteryPercent` | Soft `int`. Varsayılan 20. Yüklemede bilinmeyen veya 5–50 dışı değer 20 olur. Kullanıcının yazdığı değer 5–50 aralığına sıkıştırılır. `schemaVersion` artmaz. `SetLowBatteryPercent` eşiği değiştirince kenarı yeniden kurar: bir sonraki `>=` → `<` geçişi bir kez HUD ateşler. Eşik değişimi kendi başına HUD açmaz. Eşik altındaki sonraki örnekler tekrar açmaz. Takılma bir HUD'dur. İlk örnek yalnız tohumdur. Yalnız fiş çekmek HUD değildir |
| Arama | Ada müzik paneli kaydırıcıyı yalnız `Position` ve `Duration` birlikte varken ve SMTC `IsPlaybackPositionEnabled` ise çizer. Aksi halde bugünkü salt okunur çubuk, o da yalnız iki süre varken. Süre yoksa yüzde uydurulmaz. Sürükleme `TryChangePlaybackPositionAsync` çağırır. Oturum aramayı desteklemiyorsa `CanSeek` false ve `TrySeek` no-op'tur. Panel kapalıyken zaman çizelgesi okunmaz |
| Medya şeridi | `media` zaten `_mediaActive` ile gizlenir (`ApplyStatusItemLayout`). İkinci bir düğme yok |
| Dar hap | Parça adı, uydu kromu + içerik payı + hover payı düşülerek daha erken kesilir. `IslandPresence.SideClearanceDip` durur. AppBar 30 DIP kalır. Bar penceresi başlığa göre büyütülmez |
| Saat paneli | Saat tıklaması tek WharfBar paneli açar (`Popup` + `AnimateFlyoutOpen` / `Close`, iç içe `Popup` yok). İçinde son bildirimlerin başlığı ve gövdesi (`UserNotificationListener`, yaklaşık 8) ve yerel saatin ay takvimi (`tr-TR` gün adları, bugün vurgulu). Gün tıklaması olay yaratmaz. Outlook/Google yok. Erişim yoksa kısa Türkçe cümle, uydurma satır yok. İzin, panel açılırken ve henüz verilmemişse istenir |
| Zil | `NotificationCenterInvoker` / Win+N durur. Zil kalkmaz. Win+N penceresi kazınmaz. Bildirim listesi ada hapına konmaz. Saat, zil, takvim ada hapına taşınmaz |
| Kıvılcım | `SystemStatsService` örneğinin zaten ürettiği CPU / RAM / GPU serisi. Çizgi, 30 DIP barın içine metinle birlikte sığmayacağı için istatistik metninin üzerine gelince açılan karttadır; bar büyümez. GPU sayacı yoksa GPU satırı gizlenir. Sıcaklık yok |
| Ağ hızı | Aynı 2 sn istatistik aralığında `NetworkInterface` sayaçlarından bayt/sn. Ayrı zamanlayıcı yok |
| Enerji tasarrufu | Karo `PowerManager.EnergySaverStatus` (`On`) veya `SYSTEM_POWER_STATUS.SystemStatusFlag` ile yanar. `EnergySaverStatus2` bu hedefin WinRT projeksiyonunda yok. Belgelenmiş yazma yoktur; belgelenmemiş COM/IOCTL/WNF çağrılmaz. Tıklama Ayarlar’daki `SystemSettings_PowerAndBattery_EnergySaverAlwaysOn_ToggleSwitch` anahtarını UI Automation ile çevirir, pencereyi ekran dışına alır ve bu çağrı açtıysa kapatır. Anahtarın geri okuması `On` ise karo yanar — pilsiz masaüstünde API `Disabled` kalsa bile. Yazma tutmazsa karo yanmaz, `ms-settings:` açılmaz. Wi-Fi, Bluetooth, gece ışığı ve yansıt karolarına dokunulmaz |

## Yazma

`PowerManager.EnergySaverStatus` salt okunurdur. `PowerWriteACValueIndex` canlı aç/kapa değildir. `HKLM\...\EnergySaverState` yönetici ister ve bu makinede anında durumu değiştirmez. Ayarlar satırı “Her zaman enerji tasarrufu kullan” (`EnergySaverAlwaysOn_ToggleSwitch`) UI Automation ile çevrilir. Pilsiz masaüstünde anahtar `On` olunca satır metni “Açık” olur; `EnergySaverStatus` `Disabled` ve `SystemStatusFlag` 0 kalabilir. Karo bu durumda anahtarın geri okunmuş haline bakar.

## Sonuçlar

- `IslandPresence.ResolveSatellites`, `SetLowBatteryPercent`
- `IslandConfig.LowBatteryPercent`, `IslandNormalizer.NormalizeLowBatteryPercent` / `ClampLowBatteryPercent`
- Ada ayarında "Düşük pil eşiği"
- `MediaSessionService.CanSeek` / `TrySeek`
- `ClockPanelFlyout` — bildirim metni + ay
- İstatistik hover kartı: `StatsHoverCard`, `Sparkline`, `NetworkRate`
- `QuickSettingsService` enerji tasarrufu karosu: okuma + `EnergySaverAlwaysOn` anahtarı
