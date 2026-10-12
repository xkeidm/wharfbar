# Boundaries — Yap / Önce Sor / Asla

## Yap (serbest)

- Doküman ve `.agent/` güncellemesi (bu iskelet kapsamında)
- Top bar AppBar akışı (NEW / QUERYPOS / SETPOS / REMOVE)
- Dock overlay (Topmost, auto-hide, animasyon)
- JSON config + şema versiyonu
- Mutex single-instance
- Startup klasörü autostart
- `SHGetFileInfo` ile klasik `.exe`/`.lnk` ikonları
- Durum göstergelerini .NET API ile okuma (hızlı ayarlar gerçek API'lerle: wlanapi + WinRT Radio/Bluetooth + SetDisplayConfig; ayrıntı docs/02)
- WPF-UI backdrop / tema
- DPI / display change / AppBar callback handler’ları
- Fullscreen gizleme (bar/dock)
- Explorer restart sonrası AppBar yeniden kayıt
- **Windows görev çubuğunu WharfBar çalışırken gizleme / auto-hide** — başlatmada durumu
  yakala; çıkış ve crash yollarında **önceki duruma geri yükle** (ADR-005; `ABM_REMOVE`
  ile aynı disiplin). Kalıcı (WharfBar kapalıyken) bozma yok.
- **Çoklu monitör** AppBar: monitör bazında top bar / dock (`monitors` config; per-monitor
  aç-kapat), her bar kendi monitöründe AppBar. **`SPI_SETWORKAREA` yedeği 25H2+’ta ölü**
  (API `ERROR_INVALID_PARAMETER` döndürür — `WorkAreaExpandPolicy` devre kesicisi atlar);
  work-area override’ı destekleyen shell çıkana dek AppBar olmayan kenar rezervasyonu yok.
- **Gizli görev çubuğu + maximize kaplaması (ADR-005 notu 5, 2026-09-30'da tekrar doğrulandı):**
  gizleme akışı `ABM_SETSTATE(ABS_AUTOHIDE)` verir ve çalışırken o state'te kalır —
  explorer çubuğu ~2px kıskaca park eder, alt rezervasyon work-area'dan çıkar, maximize
  pencereler tam ekran kaplar. WharfBar çalışırken ölçüldü: `ABM_GETSTATE=1`, görev çubuğu
  görünmez, `SPI_GETWORKAREA.Bottom`=1080 → **alt boşluk 0**; uygulama kapalıyken 1032/48.
  `SW_HIDE` tek başına yetmez (~48px hayalet bırakır). Maximize pencerelere harici `SetWindowPos`
  müdahalesi YASAK (Firefox/WinForms pencere yöneticisiyle 7 ms'lik savaş üretir — denendi,
  söküldü).
- **Ölçüm tuzağı:** `ABM_SETSTATE`/`ABM_SETPOS` **yalnızca kayıtlı bir AppBar** çağırdığında
  etkilidir; dışarıdan prob etmek `TRUE` dönüp hiçbir şey yapmaz ve "API ölü" yanlış sonucunu
  üretir (2026-09-30'da bu hataya düşüldü). `SPI_SETWORKAREA` ise gerçekten sessiz bir no-op'tur;
  devre kesicisi (`WorkAreaExpandPolicy`) bu yüzden dönüş değerine değil **etkiye** bakar.
- **Tepsi ikonu yakalama PEEK'in içinde olmak zorunda:** `PrintWindow(PW_RENDERFULLCONTENT)`
  tray `IsWindowVisible=False` iken DIB'e piksel yazmadan `TRUE` döner → siyah bitmap. alpha 0
  engel **değildir**; belirleyici peek'in açık olması. `TryRefreshViaBlip` ikonları
  `EndTrayPeek`'ten önce yakalar. `TrayCapture.IsBlankCapture` boş kırpımayı reddeder →
  harf glifi fallback. Yakalama peek dışına taşınırsa siyah kare geri gelir.
  Tray aynası parklı tray'de UIA ile okunamaz:
  okumalar desktop-root'tan özel STA dispatcher thread'inde; tazeleme/tıklama
  maximize-yokken ~0.5 sn'lik state blip'i ile; boş okuma aynayı temizlemez (son iyi
  snapshot donuk kalır). Zorla kill sonrası stale watchdog dosyası baseline state'i
  geri yükler (zehirlenmiş baseline zinciri — 2026-08-30 canlı yaşandı).
- Canlı dock: sol grup **görev çubuğu `User Pinned` kısayollarını okur** (kapalı pinler
  dahil, görev çubuğu sırası) + `dock.items` içinde olup görev çubuğunda olmayanlar + `EnumWindows`
  / AUMID. “Dock’a sabitle / Dock’tan kaldır” `dock.items` listesini değiştirir ve hedef
  dosya veya klasörse `User Pinned\TaskBar` içine `.lnk` yazar (ADR-005). Aynı uygulama (kanonik exe yolu, AppUserModelId veya
  süreç kimliği; geçişli) **tek kutudur** — arka plandaki ikinci pencere veya aynı exe'nin
  ikinci süreci sağda yeni ikon açmaz; sayı o kutunun rozetindedir
- **Görev çubuğu pin klasörü** (ADR-005, 2026-10-11): “Dock'a sabitle / kaldır” hedef dosya veya klasörse `User Pinned\TaskBar` içine `.lnk` yazar veya eşleşen kısayolu siler. Yol o klasörün dışına çıkamaz. `Taskband` yazılmaz. `TaskbarPinnedAppsSource` okumaya devam eder
- Dock plakası ikonla birlikte büyümez. `DockMagnification` / `DockStripLayout` saf ve testli
  durur; canlı şerit ölçeği 1'dir, pencere plaka boyutundadır, hover'da `SetWindowPos` çağrılmaz
  (docs/04-gotchas.md)
- Üst barda tek “tray overflow” düğmesi (`Win+B` / chevron) + **tray aynası (ADR-009):** üçüncü
  taraf tepsi ikonlarını **belgelenmiş comctl32 toolbar mesajlarıyla** (`TB_BUTTONCOUNT`,
  `TB_GETBUTTON` (index; `TBBUTTON` → `idCommand`/`iBitmap`), `TB_GETIMAGELIST` +
  `ImageList_GetIcon`, `TB_GETITEMRECT`, `TB_GETBUTTONTEXT` (command id)) salt-okunur okuyup üst
  barda çizmek + `SendMessage` ile tıklama forward. Gizli/overflow ikonlar kapsam dışı (yalnızca
  açık popup'ta var) → `Win+B` düğmesi korunur. **Ayna yalnızca birincil monitörün üst barında
  görünür** (ikincil barlarda `trayMirror` bölümü hiç oluşmaz). **25H2+ UIA fallback:** klasik
  `ToolbarWindow32`
  kaldırıldığında ayna UI Automation'a düşer (`SystemTray.NormalButton` filtre); gerçek ikonlar
  `PrintWindow(PW_RENDERFULLCONTENT)` ile park+cloak'lı taskbar'dan yakalanıp buton rect'inden
   kırpılır (flash yok); yakalama başarısızsa harf glifi fallback; sol tık `InvokePattern`, sağ/çift
   tık görünür peek + fiziksel tıklama — UIA sağ/çift tık ifade edemez. Sağ tık ayna ikonunda **düğme bırakılınca** iletilir (basılıyken üst bar capture'ı tıklamayı yutar); fiziksel tıklamadan hemen önce `AllowSetForegroundWindow(ASFW_ANY)`. Menü içeriği sahibin HMENU / flyout'udur — WharfBar çizmez. Stealth peek'te DWM cloak
   reddedilirse **WS_EX_LAYERED + alpha 0** yedeği kullanılır (UIA görünürlüğü korunur, flash yok);
   o da başarısızsa peek açılmaz. **Popup re-anchor:** fiziksel tık sonrası tıklanan butonun
   çevresinde yeni beliren popup penceresi (`TrayMenuReanchor`) ayna ikonunun altına taşınır —
   belgelenmiş `SetWindowPos` yalnız; bellek okuma/enjeksiyon yok. Eşleşme **geometrik** (tık
   bölgesi + boyut + `#32768` menü sınıfı; tam ekran overlay'ler hariç): 25H2 XAML tray
   butonlarının hepsi explorer pid'i raporlar, pid ile eşleşme ayırt edemez.
- **Island / clipboard geçmişi (ADR-010):** belgelenmiş pano P/Invoke'ları
  (`OpenClipboard`/`GetClipboardData`/`SetClipboardData`/`EmptyClipboard`/
  `AddClipboardFormatListener`/`RegisterClipboardFormatW`/`GlobalAlloc`) + **mesaj-only**
  `HwndSource` dinleyicisi (mevcut `MonitorMessageWindow` kalıbı) + ayrı **atomik store**
  (`island.json` + `Drop\` klasörü — `config.json`'a pano geçmişi yazılmaz, `ConfigService.Save`
  dosyayı UI thread'inde baştan yazar) + mevcut flyout kalıbı (`Popup` +
  `AnimateFlyoutOpen/Close`, **iç içe `Popup` yok**).
  - **`CanIncludeInClipboardHistory` opt-out'una saygı:** format DWORD'u 0 ise kayıt alınmaz;
     format id'i **alınamıyorsa da** kayıt alınmaz (fail-closed). "Panoya geri koy" yazması da
     aynı işareti yazar, yoksa island kendi kopyasını geri yakalar.
  - `System.Windows.Clipboard` **kullanılmaz** — pano okuması ayrı STA thread'indedir
     (`OpenClipboard` bloklar), dosya kopyalama/silme de STA'dadır.
  - `Clear()` bırakılan dosya kopyalarını **silmez** (bilinçli); yalnız `DeleteDroppedFiles()`
     siler ve `Drop` klasörünü tarayarak yetimleri de kapsar. Silme yolları `Drop` önekiyle
     doğrulanır (elle düzenlenmiş `island.json` kullanıcının dosyasını silemez).
  - Pano geçmişi yalnız **birincil** monitörün barında görünür (servis tek örnek). İkincil hap müzik ve sayaçtır: pano listesi, dosya bırakma ve pano uydusu yok.
  - Raf (`CanOpenShelfFile`) yol saklar ve varsayılan uygulamayla açabilir. `Drop` klasörünün dışındaki bir dosyayı silmek yasaktır; silme yolu `Drop` önekini doğrular.
  - **Kompakt ada 30 DIP barın içinde kalır** (ADR-011, ADR-012, ADR-013): boşta / şimdi çalıyor / ses-parlaklık-pil sayacı / en fazla iki uydu hapı AppBar'ı büyütmez, ikinci bir `Topmost` pencere açmaz, iç içe `Popup` kurmaz. HUD sırasında kalıcı müzik ve pano uydusu birlikte durabilir. Hap odaklıyken Sol/Sağ uydu seçer. Düşük pil eşiği `lowBatteryPercent` (varsayılan 20, 5–50). Çok satırlı panel bugünkü tek `IslandPopup`'tır. Saat paneli (bildirim metni + ay) ve istatistik kıvılcımı ada hapında değildir; kıvılcım metnin üzerine gelince açılır, bar 30 DIP kalır. Enerji tasarrufu karosu API veya sistem bayrağı açıksa yanar; tıklama “Her zaman enerji tasarrufu kullan” anahtarını UI Automation ile çevirir, pencere ekranda kalmaz, belgelenmemiş COM/WNF yok (ADR-013).
  - **Yeni paket yok.**


## Önce sor (kullanıcı onayı)

- **UWP/Store** ikon desteği (tam AppxManifest yolu)
- Registry `Run` ile autostart (tercih Startup klasörü)
- `AllowUnsafeBlocks` açmak
- Ham DWM Mica’yı WPF-UI backdrop yerine birincil yapmak
- Dock plakasını AppBar yapmak (ADR-001; görünür dock’un şeridini ayıran kardeş
  `ABE_BOTTOM` penceresi serbesttir — `DockWorkAreaReservation`, 2026-10-10)
- Dış bağımlılık ekleme (WPF-UI dışı yeni paketler)
- `global.json` eklemek / SDK sürümü sabitlemek (makinede yalnızca .NET 9 SDK var; 8.x’e
  sabitleme derlemeyi kırar)
- Breaking config şema değişikliği (migrasyon planı olmadan)
- Görev çubuğu gizleme stratejisini kalıcı varsayılan dışı yapmak veya restore’suz denemek

## Asla

- Çıkış yollarında **`ABM_REMOVE` atlamak** (kayıtlı AppBar bırakmak)
- Çıkış / crash yollarında **görev çubuğu durumunu geri yüklememek** (gizlenmiş / auto-hide
  bırakmak) — ADR-005
- **`AllowsTransparency=true` ile Mica/DWM backdrop** birlikte
- Sistem / üçüncü taraf tepsi ikonlarını **`ReadProcessMemory`, enjeksiyon/subclass veya
  belgelenmemiş API ile** “çözmek” — `Shell_NotifyIcon` yalnızca WharfBar’ın kendi ikonu için.
  (Belgelenmiş comctl32 `TB_*` mesajlarıyla salt-okunur ayna ADR-009 ile serbesttir.)
- Kullanıcıya sormadan görev çubuğunu **kalıcı** bozmak (WharfBar kapalıyken de)
- v1’de UWP ikon yolunu “yarım” bırakıp kırık deneyim sunmak
- Cursor kurallarına Win32 rehberinin tamamını yapıştırmak (kanonik kaynak `.agent/` + `docs/`)
- Gizli anahtar / kişisel config commit etmek
- Dokümantasyonu güncellemeden mimari kararı sessizce tersine çevirmek
- `Taskband` değerini yazmak. `User Pinned\TaskBar` dışına `.lnk` yazmak veya o klasörün dışındaki bir dosyayı pin kaldırma diye silmek (ADR-005)
