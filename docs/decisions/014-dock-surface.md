# ADR-014: Dock yüzeyi — küçük resim, ilerleme, dikkat, monitör, yığın, çöp, küçültme, UWP

**Durum:** Kabul edildi
**Tarih:** 2026-10-11
**Kapsam:** Dock plakası. Üst bar saati, istatistik, hızlı ayarlar ve island hapı bu kararın dışında. Plaka AppBar yapılmaz; canlı ölçek 1 kalır (ADR-001, `docs/04-gotchas.md`).

## Karar

| Konu | Seçim |
|------|--------|
| Küçük resim | Hover'da tek `Popup`. Her canlı pencere için bir `DwmRegisterThumbnail` (`DwmUpdateThumbnailProperties` / `DwmUnregisterThumbnail`). Ayrılınca ve kapanışta kayıt düşer. Cloak'lı veya küçültülmüş pencerede, ya da DWM reddederse, siyah kare yok — yalnız başlık satırı. Tıklama mevcut `ShowWindow` + `SetForegroundWindow` yolu (`WindowActivationService.TryActivate`) |
| İlerleme | Belgelenmiş `GetProgressValue` yok. Görev çubuğu düğmesi, tepsi aynasıyla aynı UI Automation sınıfı üzerinden okunur (`RangeValue`, `ItemStatus`: normal yüzde / indeterminate / paused / error). Gizli çubuk hiçbir şey açmazsa çubuk **yoktur**; yüzde uydurulmaz, son iyi değer saklanmaz. Yoklama çalışan uygulama ve açık tepsi düğmesi varken 2 sn, aksi halde 20 sn (2026-10-12; önceki 800 ms UI Automation yürüyüşü boşta da sürüyordu) |
| Dikkat | Dock HWND'si zaten mesaj alır: `RegisterShellHookWindow`, `HSHELL_FLASH` = `HSHELL_REDRAW \| HSHELL_HIGHBIT`. Accent nabız ~1.6 sn, konum oynamaz. İkinci flaş süreyi baştan başlatır. Aktivasyon temizler. WharfBar pid'i yok sayılır |
| Monitör | Pin listesi (User Pinned salt okunur + `dock.items`) her dock'ta aynıdır. Rozet, çalışan/küçültülmüş çizgi ve küçük resim, merkezi **o** monitörün dikdörtgeninde olan pencereleri sayar. Küçültülmüş pencerede merkez `GetWindowRect` simge noktası değil, `GetWindowPlacement.rcNormalPosition` olur. Cloak'lı küçültülmüş pencere çalışan kalır; başka sanal masaüstünün cloak'ı sayılmaz. Uygulama başına bir karo durur. Pin klasörüne yazılmaz |
| İndirilenler | `FOLDERID_Downloads` (`SHGetKnownFolderPath`) her zaman kendi karosudur. `dock.items` ve User Pinned klasörüne yazılmaz. Tıklama dizin çocuklarını açar |
| Klasör yığını | Pinli klasör aynı flyout'u açar. Alt klasör bir sonraki düzeye iner veya Explorer açılır. Dosya `ShellExecuteW` ile kayıtlı fiille açılır (`lpOperation` null). `ReparsePoint` ve okunamayan öznitelik fail-closed atlanır. Görünen satır 12 ile sınırlıdır |
| Çöp | Sabitlenmemiş çalışanların sağında, şeridin sonunda. Tıklama `shell:RecycleBinFolder`. Bırakma `SHFileOperationW` `FO_DELETE` \| `FOF_ALLOWUNDO`. Kalıcı silme yok. Reparse dizin reddedilir. API hata veya iptal dönerse orijinal ayrıca silinmez ve başarı gösterilmez |
| Küçültme | `EVENT_SYSTEM_MINIMIZESTART`, `WINEVENT_OUTOFCONTEXT \| WINEVENT_SKIPOWNPROCESS` (enjeksiyon yok). O anın `PrintWindow` karesi ~200 ms ikona doğru ölçeklenir. Yakalama başarısızsa animasyon yok. `SystemParameters.ClientAreaAnimation == false` ise animasyon yok. Yabancı pencereye `SetWindowPos` döngüsü yok |
| UWP logo | `Windows.Management.Deployment.PackageManager` logo URI'si, dosya adayı ve `SHLoadIndirectString`. Bu TFM projeksiyonunda `Package.GetLogo` yok. Varlık yoksa veya okuma patlarsa mevcut harf glifi. Kırık görüntü yolu bırakılmaz. Yeni NuGet paketi yok |

## Gerekçe

- Küçük resim DWM'nin belgelenmiş thumbnail API'sidir. WPF `Image` içine HWND havadan çizilmez; tek flyout'un child HWND'i hedef olur, ikinci bir `Popup` açılmaz.
- Görev çubuğu ilerlemesi `ITaskbarList3::SetProgressValue` ile yazılır, okunmaz. UI Automation, tepsi aynasında zaten kullanılan belgelenmiş yüzeydir. Parklı çubuk düğme açmazsa boş kalmak, uydurma yüzdeden daha dürüsttür.
- `HSHELL_FLASH` kabuğun flaş bildirimidir ve dock'un kendi penceresine gelir. Süreç içine subclass veya `ReadProcessMemory` gerekmez.
- Pencere merkezi, monitör dikdörtgeninde yarı-açık sağ/alt kenarla hesaplanır. Böylece monitör B'deki pencere A'nın rozetine yazılmaz; pin sırası değişmez.
- İndirilenler ve çöp config şemasına girmez. Pin yazma yasağı (ADR-005) durur.
- Küçültme animasyonu yabancı pencerenin konumunu oynatmaz. Kare alınamazsa jest atlanır.
- Store ikonu kullanıcı isteğiyle tam çözücüdür. Manifest'i yarım ayrıştırmak, normal bir Store uygulamasında patlayan bir yol bırakmaz.

## Sonuçlar

- Saf türler: `DockProgress`, `DockAttentionTracker`, `DockMonitorWindowFilter`, `DockFolderListing`, `DockRecyclePolicy`, `DockMinimizeMotion`, `PackageLogoSelection`, `DockBuiltInTiles`.
- Canlı uçlar: `DwmThumbnail`, `DockShellHook`, `DockWindowSnapshot`, `RecycleBinNative`, `DockKnownFolder`, `PackageIconNative`, `PackageIconResolver`, `DockTaskbarProgress`.
- Hücre ilerleme durumunu ve nabzı boyar. Şerit ölçeği 1'dir.

## Alternatifler (reddedilen)

1. **`GetProgressValue` veya başka sürecin belleği** — belgelenmiş okuma yok; `ReadProcessMemory` yasak.
2. **İlerleme okunamazsa son yüzdeyi tutmak** — tepsi anlık görüntüsünün tersine, burada boş okuma "çubuk yok" demektir.
3. **Dock'u AppBar yapmak veya hover'da `SetWindowPos`** — ADR-001 ve büyütme tuzağı durur.
4. **Kalıcı silme (`FO_DELETE` without `FOF_ALLOWUNDO`) veya API düşünce `File.Delete`** — orijinal kaybolur, başarı yalan olur.
