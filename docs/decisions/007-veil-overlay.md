# ADR-007: Renk kaplaması (veil) — katmanlı pencere yaklaşımı

**Durum:** Kabul edildi (2026-08-18)  
**Tarih:** 2026-08-18  
**Kapsam:** ColorVeil tarzı tam ekran renk kaplaması
(spec: [2026-08-18-veil-overlay-design](../superpowers/specs/2026-08-18-veil-overlay-design.md))

## Karar

- Overlay: tek katmanlı WPF penceresi (`VeilWindow`) — `AllowsTransparency=True` düz renk dolgu,
  tam ekran rect, `Topmost`, `WS_EX_TRANSPARENT` passthrough, 280 ms fade. Magnification API ve
  gamma ramp reddedildi (belgelenmemiş/istikrarsız; admin gerekli; renk uzayı dönüşümü).
- Kontrol: Hızlı Ayarlar karosu + global kısayol (`RegisterHotKey`, varsayılan Ctrl+F11, config'te değiştirilebilir).
- Kaplama her zaman en üstte (tam ekran uygulamalar dahil). Exclusive fullscreen (DWM dışı render)
  üzerinde görünmez — ColorVeil'de de aynı sınır.
- Parlaklık azaltma ayrı kontrol değildir: siyah + yüksek opaklık aynı mekanizmadır.
- Çıkışta `UnregisterHotKey` garantisi (`VeilService.Dispose` → `App.OnExit`).

## Gerekçe

DockWindow'un kanıtlanmış deseni (katmanlı pencere + click-through) renk kaplamasına birebir
uyar; yeni bağımlılık gerekmez. Magnification API ve gamma ramp belgelenmemiş/stabilite riskli
olduğu için reddedildi. Kontrol noktaları (karosu + kısayol) mevcut QS altyapısı ve
`RegisterHotKey` ile karşılanır; `schemaVersion` değişmez (eklenebilir `veil` bloğu).

## Sonuçlar

- Yeni bağımlılık yok; kaynak maliyeti ~0 (statik pencere).
- Elevated pencerelerin üzerine kaplama çizilemez (süreç sınırı).
- v1 yalnızca birincil monitör.
- Yeni dosyalar: `Views/VeilWindow`, `Services/VeilService`, `Native/Hotkey.cs`,
  `Views/Pages/VeilPage`; `QuickSettingsService`'e `veil` karosu.
- Doküman: `docs/01-architecture.md`, `docs/02-win32-appbar-dwm.md`, `.agent/*` güncellenir.

## Alternatifler (reddedilen)

1. **Magnification API** — belgelenmemiş / istikrarsız; reddedildi.
2. **Gamma ramp (`SetDeviceGammaRamp`)** — admin gerekli; renk uzayı dönüşümü; reddedildi.
3. **Exclusive fullscreen'e zorla kaplama** — DWM dışı render üzerine çizim mümkün değil; sınır olarak belgelenir (ColorVeil'de de aynı).
