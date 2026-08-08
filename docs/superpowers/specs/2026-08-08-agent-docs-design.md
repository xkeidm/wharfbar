# WharfBar — Agent / Docs Scaffold Tasarımı

**Tarih:** 2026-08-08  
**Durum:** Onaylandı (yaklaşım B)  
**Kapsam:** Yalnızca dokümantasyon + agent yapılandırması; WPF uygulama iskeleti yok.

## Karar

**Yaklaşım B:** Ayrıntı `.agent/` + kök `AGENTS.md` + `docs/` içinde yaşar. Cursor `.cursor/rules/*.mdc` dosyaları kısa işaretçilerdir; Win32 rehberi Cursor kurallarına kopyalanmaz.

## Roller

| Katman | Rol |
|--------|-----|
| `AGENTS.md` | Kısa indeks; AI’nin ilk okuması |
| `.agent/` | AI için kanonik kaynak (brief, sınırlar, stack, roadmap) |
| `docs/` | İnsan + AI için yapılandırılmış teknik dokümanlar |
| `.cursor/rules/` | İnce kurallar; `.agent/` ve `docs/`’a yönlendirir |

## İçerik kaynağı

İndirilen `wharfbar-proje-notu.md` içeriği yapılandırılmış `docs/` dosyalarına katlanır; Downloads yolu kaynak değildir. Parent incelemesindeki teknik uyarılar (tray simülasyonu, dock overlay, WPF-UI backdrop, AllowUnsafeBlocks, tek monitör v1, hit-test / fullscreen / AppBar yeniden kayıt, config şeması, Mutex, Startup klasörü, Views/UI adı) dokümanlara işlenir.

## Bu geçişte oluşturulmayanlar

- `dotnet new` / WPF proje iskeleti
- Commit (kullanıcı istemedi)
