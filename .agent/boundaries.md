# Boundaries — Yap / Önce Sor / Asla

## Yap (serbest)

- Doküman ve `.agent/` güncellemesi (bu iskelet kapsamında)
- Top bar AppBar akışı (NEW / QUERYPOS / SETPOS / REMOVE)
- Dock overlay (Topmost, auto-hide, animasyon)
- JSON config + şema versiyonu
- Mutex single-instance
- Startup klasörü autostart
- `SHGetFileInfo` ile klasik `.exe`/`.lnk` ikonları
- Durum göstergelerini .NET API ile simüle etme
- WPF-UI backdrop / tema
- DPI / display change / AppBar callback handler’ları
- Fullscreen gizleme (bar/dock)
- Explorer restart sonrası AppBar yeniden kayıt

## Önce sor (kullanıcı onayı)

- Windows **görev çubuğunu** gizleme veya `ABM_SETSTATE` ile auto-hide değiştirme
- v1’de **çoklu monitör** AppBar / `SPI_SETWORKAREA` work-area hack’i
- **UWP/Store** ikon desteği
- Registry `Run` ile autostart (tercih Startup klasörü)
- `AllowUnsafeBlocks` açmak
- Ham DWM Mica’yı WPF-UI backdrop yerine birincil yapmak
- Dock’u AppBar’a çevirmek (ADR-001 değişikliği)
- Dış bağımlılık ekleme (WPF-UI dışı yeni paketler)
- `global.json` eklemek / SDK sürümü sabitlemek (makinede yalnızca .NET 9 SDK var; 8.x’e
  sabitleme derlemeyi kırar)
- Breaking config şema değişikliği (migrasyon planı olmadan)

## Asla

- Çıkış yollarında **`ABM_REMOVE` atlamak** (kayıtlı AppBar bırakmak)
- **`AllowsTransparency=true` ile Mica/DWM backdrop** birlikte
- Sistem tepsi ikonlarını (Wi-Fi/pil/ses) **okunuyor gibi** `Shell_NotifyIcon` ile “çözmek”
- Kullanıcıya sormadan görev çubuğunu kalıcı bozmak
- v1’de UWP ikon yolunu “yarım” bırakıp kırık deneyim sunmak
- Cursor kurallarına Win32 rehberinin tamamını yapıştırmak (kanonik kaynak `.agent/` + `docs/`)
- Gizli anahtar / kişisel config commit etmek
- Dokümantasyonu güncellemeden mimari kararı sessizce tersine çevirmek
