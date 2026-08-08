# 00 — Genel Bakış

## Amaç

WharfBar, Windows masaüstüne macOS’a benzer bir **üst bar** ve **dock** ekler:

- **Top bar:** Saat ve durum göstergeleri; ekran alanını AppBar ile rezerve eder.
- **Dock:** Sabitlenmiş uygulamalar; tıklanınca süreç başlatır; hover’da hafif büyüme animasyonu.

## Hedefler (v1)

| Hedef | Not |
|-------|-----|
| Üst AppBar | `ABE_TOP`, work area rezervasyonu |
| Dock overlay | Topmost; AppBar şart değil (ADR-001) |
| Pinned apps | Klasik `.exe` / `.lnk`; `SHGetFileInfo` ikonları |
| Config | JSON, `%AppData%/WharfBar/config.json` |
| Autostart | Startup klasörü kısayolu |
| DPI | Per-Monitor V2 (`app.manifest`) |
| Single-instance | Named Mutex |
| Fullscreen | Tam ekran uygulamada bar/dock gizleme (hit-test / durum) |
| Dayanıklılık | Çıkışta `ABM_REMOVE`; explorer yeniden başlayınca AppBar yeniden kayıt |

## Non-goals (v1)

- İkincil monitörlerde AppBar / work-area rezervasyonu
- UWP / Store paket ikonları (AppxManifest yolu)
- Sistem tepsisi ikonlarını (Wi-Fi, ses, pil) doğrudan okuma
- Windows görev çubuğunu gizleme veya `ABM_SETSTATE` ile değiştirme
- Tema mağazası, eklenti sistemi, bulut senkronizasyonu

## Başarı kriterleri (v1)

- Maximize edilmiş pencereler top bar altına girmiyor (birincil monitör).
- Uygulama temiz kapanınca rezerve alan geri geliyor.
- Dock’tan pinned app açılıyor; config yeniden başlatmada korunuyor.
- İkinci örnek başlatılmıyor (Mutex).
- DPI / ekran değişiminde üst bar pozisyonu bozulmuyor (temel handler’lar).

## Kaynak not

İlk teknik notlar proje notundan yapılandırılmış dokümanlara taşındı. Güncel kaynak bu `docs/` ağacı ve `.agent/` dosyalarıdır.
