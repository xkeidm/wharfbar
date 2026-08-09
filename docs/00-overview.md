# 00 — Genel Bakış

## Amaç

WharfBar, Windows masaüstüne macOS’a benzer bir **üst bar** ve **dock** ekler:

- **Top bar:** Saat ve durum göstergeleri; ekran alanını AppBar ile rezerve eder.
- **Dock:** Sabitlenmiş uygulamalar; tıklanınca süreç başlatır; hover’da hafif büyüme animasyonu.

Görsel dil **Windows Fluent** (Mica, sistem teması) olmalıdır; macOS ikon dili / blur / marka öğeleri
kullanılmaz — yalnızca düzen kavramı (üst bar + dock) ilham alınır.

## Öncelik sırası (çakışmada)

1. **Düşük kaynak tüketimi** — RAM/CPU ayak izini büyüten kararlar sessizce kabul edilmez
2. **Windows’a native görünüm** — Fluent Design; “macOS klonu” değil
3. **Basitlik** — kişisel proje; önce çalışan iskelet, sonra cila

Kişisel kullanım için başladı; olgunlaştıkça private → public açılması planlanıyor (MIT).

## Hedefler (v1)

| Hedef | Not |
|-------|-----|
| Üst AppBar | `ABE_TOP`, work area rezervasyonu |
| Dock overlay | Topmost; AppBar şart değil (ADR-001) |
| Pinned / running apps | Canlı TaskBar `.lnk` + `EnumWindows`/AUMID; `SHGetFileInfo`; config seed/fallback (ADR-005) |
| Tepsi hibrit | Durum simülasyonu + native overflow düğmesi; yabancı ikon çizimi yok (ADR-005) |
| Görev çubuğu | WharfBar çalışırken gizle/auto-hide; çıkışta önceki duruma geri yükle (ADR-005) |
| Config | JSON, `%AppData%/WharfBar/config.json` |
| Autostart | Startup klasörü kısayolu |
| DPI | Per-Monitor V2 (`app.manifest`) |
| Single-instance | Named Mutex |
| Fullscreen | Tam ekran uygulamada bar/dock gizleme (hit-test / durum) |
| Dayanıklılık | Çıkışta `ABM_REMOVE` + görev çubuğu restore; explorer yeniden başlayınca yeniden kayıt / gizleme |

## Non-goals (v1)

- İkincil monitörlerde AppBar / work-area rezervasyonu
- UWP / Store paket ikonları (AppxManifest yolu)
- Sistem / üçüncü taraf tepsi ikonlarını doğrudan okuma, WharfBar içinde çizme veya
  click-forward (`ReadProcessMemory` / belgelenmemiş NotifyIcon enumeration dahil)
- Görev çubuğunu **kalıcı** bozmak (WharfBar kapalıyken de gizli / bozuk bırakmak).
  Çalışma anında gizleme/auto-hide + restore **hedeftir** (ADR-005); blanket yasak değildir.
- macOS tarzı **global uygulama menüsü** (File/Edit/View) — Windows’ta her pencere kendi menüsünü taşır; top bar yalnızca saat / durum / kontroller
- Apple / macOS marka öğeleri (isim, logo, ikon dili)
- Tema mağazası, eklenti sistemi, bulut senkronizasyonu
- Electron / Flutter gibi gömülü render motorları (kaynak önceliği; bkz. mimari)

## Başarı kriterleri (v1)

- Maximize edilmiş pencereler top bar altına girmiyor (birincil monitör).
- Uygulama temiz kapanınca rezerve alan geri geliyor; görev çubuğu önceki durumuna dönüyor (ADR-005).
- Dock, TaskBar pinlerini / çalışan uygulamaları yansıtıyor; config fallback boş klasörde devreye giriyor.
- Tray düğmesi native overflow açıyor; WharfBar içinde yabancı tepsi ikonu yok.
- İkinci örnek başlatılmıyor (Mutex).
- DPI / ekran değişiminde üst bar pozisyonu bozulmuyor (temel handler’lar).

## Lisans

MIT (WPF-UI de MIT; uyumluluk sorunu yok).

## Kaynak not

İlk teknik notlar (Downloads / proje notu) yapılandırılmış `docs/` + `.agent/` dosyalarına taşındı.
Dış notlar kaynak değildir; güncel gerçek bu ağaçtır. Erken notlarda dock’un da AppBar olduğu,
`/Windows` klasörü ve `AllowUnsafeBlocks=true` varsayımları vardı — bunlar ADR-001 / ADR-002 /
`Views/` kararıyla **geçersiz** sayılır.
