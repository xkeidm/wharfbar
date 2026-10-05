# Conventions

## Dil

- **İnsan dokümanları** (`README.md`, `docs/*.md`): tercihen Türkçe; API/tür adları İngilizce bırakılır.
- **Kod** (ileride): İngilizce tanımlayıcılar, XML doc / yorumlar İngilizce veya kısa Türkçe — tutarlı ol.
- **Commit mesajları:** İngilizce veya Türkçe; kısa, “why” odaklı (repo stiline uy).

## Doküman

- Davranış değişince `docs/` + `.agent/` birlikte güncellenir.
- Cursor `.mdc` dosyalarına uzun rehber yapıştırma; işaretçi tut.
- Yeni ADR: `docs/decisions/NNN-slug.md`.
- Downloads veya dış yoldaki notlar kaynak değildir; içeriği `docs/`’a taşı.

## Kod (hedef, scaffold sonrası)

- C#: `PascalCase` tür/üye, private field `_camelCase`; nullable açık.
- Klasör: `Views/` (veya `UI/`), `Native/`, `Models/`, `Services/` — **`Windows/` klasör adı yok**.
- P/Invoke: `Native/` altında ince wrapper’lar; UI’dan ham DllImport çağırma.
- Win32 struct’lar (`APPBARDATA` vb.) `Native/` içinde `internal`; servis katmanı domain modellerine çevirir.
- Yeni Win32 çağrısı: imzayı `docs/02-win32-appbar-dwm.md`’ye ekle; hata dönüşünü / `GetLastWin32Error`’ı sessizce yutma (en azından debug log).
- WPF-UI backdrop önce; DWM yedek.
- Config modellerinde `schemaVersion` zorunlu alan; alan bazlı varsayılanlarla kırılmadan okuma tercih.
- AppBar yaşam döngüsü tek yerde (register/unregister/re-register); `ABM_NEW` / `ABM_REMOVE` simetrik.
- `unsafe` / `AllowUnsafeBlocks` yalnızca gerçek ihtiyaçta.
- Anlamlı isimler; gereksiz abstraction yok.

## Test / doğrulama (v1 manuel)

- AppBar: maximize pencere top bar altına girmiyor mu?
- Çıkış: work area geri geliyor mu?
- Crash simülasyonu sonrası explorer düzeltmesi bilinçli
- Mutex: ikinci örnek açılmıyor mu?
- DPI değişiminde pozisyon
- Dock: fare ile komşu büyümesi var mı, pencere yerinden oynamıyor mu?
- Dock: sürükleyerek sıralama çalışıyor mu ve uygulama açılmıyor mu?
- Dock: Explorer'dan bırakma kabul ediliyor mu (değilse ayar listesinden ekleniyor mu)?
- Dock: **otomatik gizleme kapalıyken** Explorer'dan bir dosyayı plakanın yanındaki saydam banda
  (plakadan ~30 px öte) sürükle — dock ekliyor mu? Bırakma bölgesinin gerçek sınırı hiç ölçülmedi;
  `DockWindow.xaml` kök `Grid` yorumu da iddia etmiyor. Eklemiyorsa sonraki adım bölgenin
  daraltılmasına karar vermek, yorumu yeniden yazmak değil.
- Dock: simge boyutu 16/36 ve büyütme 1.0/2.0 uçlarında şerit taşmıyor mu?
- Dock: görev çubuğu pinleri WharfBar'yi etkilemiyor, WharfBar da görev çubuğunu etkilemiyor mu?
  (ADR-005 revizyonu: hiçbir yönde yazma/okuma olmamalı)

## Git

- Commit yalnızca kullanıcı istediğinde.
- Gizli dosya / `.env` commit etme.
