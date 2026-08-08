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

- Klasör: `Views/` (veya `UI/`), `Native/`, `Models/`, `Services/` — **`Windows/` klasör adı yok**.
- P/Invoke: `Native/` altında ince wrapper’lar; UI’dan ham DllImport çağırma.
- WPF-UI backdrop önce; DWM yedek.
- Config modellerinde `schemaVersion` zorunlu alan.
- AppBar yaşam döngüsü tek yerde (register/unregister/re-register).
- `unsafe` / `AllowUnsafeBlocks` yalnızca gerçek ihtiyaçta.
- Anlamlı isimler; gereksiz abstraction yok.

## Test / doğrulama (v1 manuel)

- AppBar: maximize pencere top bar altına girmiyor mu?
- Çıkış: work area geri geliyor mu?
- Crash simülasyonu sonrası explorer düzeltmesi bilinçli
- Mutex: ikinci örnek açılmıyor mu?
- DPI değişiminde pozisyon

## Git

- Commit yalnızca kullanıcı istediğinde.
- Gizli dosya / `.env` commit etme.
