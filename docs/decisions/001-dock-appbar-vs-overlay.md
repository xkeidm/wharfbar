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
- **2026-10-10:** `SPI_SETWORKAREA` 25H2'de ölü olduğu için görünür dock (auto-hide kapalı) alt şeridi ayrı bir **görünmez** `ABE_BOTTOM` AppBar rezerve eder (`DockWorkAreaReservation`). Plaka HWND'i AppBar değildir — `ABM_QUERYPOS` onu monitör genişliğine gererdi. Şerit tam genişlik, yükseklik plaka yüksekliğidir; maximize pencereler ve masaüstü ikonları bu çizgide durur. Oto-gizle `ABM_REMOVE` ile şeridi geri verir. Çıkışta `ABM_REMOVE` zorunlu.

## Alternatifler (reddedilen / ertelenen)

1. **Her iki yüzey AppBar** — daha “Windows taskbar” hissi; macOS dock’a uzak.
2. **Hiç AppBar yok (yalnızca Topmost)** — top bar altında pencereler kalır; kabul edilemez.
