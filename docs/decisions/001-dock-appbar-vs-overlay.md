# ADR-001: Dock — AppBar vs Overlay

**Durum:** Kabul edildi  
**Tarih:** 2026-08-08  
**Bağlam:** Top bar ve dock için Win32 AppBar (`SHAppBarMessage`) kullanımı

## Karar

| Yüzey | Strateji |
|-------|----------|
| **Top bar** | AppBar (`ABE_TOP`) — work area rezervasyonu |
| **Dock** | Overlay: `Topmost` + isteğe bağlı auto-hide; **AppBar zorunlu değil** |

## Gerekçe

- macOS dock ekranı “kesmez”; üzerine biner / otomatik gizlenir.
- Alt kenar AppBar, maximize edilmiş pencerelerin altını sürekli keser; macOS hissinden uzaklaşır.
- AppBar çoklu monitör ve shell restart karmaşıklığını artırır; dock’ta bu maliyeti v1’de ödemeye gerek yok.
- Top bar menü çubuğu gibi sürekli yer kaplamalıdır → rezervasyon mantıklı.

## Sonuçlar

- Dock için hit-testing, auto-hide ve fullscreen gizleme ayrıca tasarlanmalı.
- İleride kullanıcı “dock her zaman yer kaplasın” isterse alt AppBar opsiyonel özellik olarak eklenebilir (ayrı ADR).
- **2026-08-15:** `dock.autoHide=false` iken dock şeridi AppBar ile değil, mevcut `SPI_SETWORKAREA` mekanizması üzerinden rezerve edilir (alt kenar = dock tepesi). AppBar kararı değişmedi; dock hâlâ overlay'dir. Oto-gizle modunda rezervasyon yoktur — maximize pencereler altı tam kaplar. Detay: `docs/04-gotchas.md`.

## Alternatifler (reddedilen / ertelenen)

1. **Her iki yüzey AppBar** — daha “Windows taskbar” hissi; macOS dock’a uzak.
2. **Hiç AppBar yok (yalnızca Topmost)** — top bar altında pencereler kalır; kabul edilemez.
