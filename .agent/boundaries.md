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
- **Windows görev çubuğunu WharfBar çalışırken gizleme / auto-hide** — başlatmada durumu
  yakala; çıkış ve crash yollarında **önceki duruma geri yükle** (ADR-005; `ABM_REMOVE`
  ile aynı disiplin). Kalıcı (WharfBar kapalıyken) bozma yok.
- Canlı dock: TaskBar `User Pinned` `.lnk` tarama + `EnumWindows` / AUMID (ADR-005)
- Üst barda tek “tray overflow” düğmesi (`Win+B` / chevron) + **tray aynası (ADR-009):** üçüncü
  taraf tepsi ikonlarını **belgelenmiş comctl32 toolbar mesajlarıyla** (`TB_BUTTONCOUNT`,
  `TB_GETBUTTON` (index; `TBBUTTON` → `idCommand`/`iBitmap`), `TB_GETIMAGELIST` +
  `ImageList_GetIcon`, `TB_GETITEMRECT`, `TB_GETBUTTONTEXT` (command id)) salt-okunur okuyup üst
  barda çizmek + `SendMessage` ile tıklama forward. Gizli/overflow ikonlar kapsam dışı (yalnızca
  açık popup'ta var) → `Win+B` düğmesi korunur. **25H2+ UIA fallback:** klasik `ToolbarWindow32`
  kaldırıldığında ayna UI Automation'a düşer (`SystemTray.NormalButton` filtre; harf glifi; sol
  tık `InvokePattern`, sağ/çift tık görünür peek + fiziksel tıklama — UIA sağ/çift tık ifade
  edemez).


## Önce sor (kullanıcı onayı)

- v1’de **çoklu monitör** AppBar / `SPI_SETWORKAREA` work-area hack’i
- **UWP/Store** ikon desteği (tam AppxManifest yolu)
- Registry `Run` ile autostart (tercih Startup klasörü)
- `AllowUnsafeBlocks` açmak
- Ham DWM Mica’yı WPF-UI backdrop yerine birincil yapmak
- Dock’u AppBar’a çevirmek (ADR-001 değişikliği)
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
