# WharfBar — Üst bar arka plan kişiselleştirme (tasarım)

**Tarih:** 2026-08-09  
**Durum:** Tasarım onaylandı (kullanıcı 2026-08-09) — uygulama planı henüz yok.  
**İlgili:** [ADR-004](../../decisions/004-topbar-mica-corners-and-theme.md),
[özelleştirme spec](2026-08-09-topbar-shortcuts-customization-design.md),
[`docs/04-gotchas.md`](../../04-gotchas.md)

> Bu dosya **final ADR değildir**. Uygulama planı sonrası istenirse ayrı ADR veya ADR-004
> güncellemesi yazılır.

## Özet

Üst bar arka planı kullanıcı tarafından seçilebilir. v1 kapsamı **yalnızca arka plan**
(Mica / transparent / tint + kaydırıcı). Dock, tema mağazası, özel hex renk ve Acrylic ayrı
mod **yok**.

Kısıt (ADR-004 / boundaries): **`AllowsTransparency=true` ile Mica birlikte kullanılmaz.**

## Karar (onaylı)

**Yaklaşım:** İki appearance boru hattı.

| Mod | Anlam | Kaydırıcı |
|-----|--------|-----------|
| `mica` | Bugünkü WPF-UI Mica yolu (`AllowsTransparency=false`) | Devre dışı |
| `transparent` | Masaüstü görünür cam bar | Devre dışı |
| `tint` | Tema rengi (light/dark) + alpha; transparent ile mevcut modun yüzdeli karışımı | 0–100, canlı önizleme |

Tint skalası: **0 ≈ transparent**, **100 ≈ opak tema dolgusu**. Varsayılan opacity: **40**.

## Config

`schemaVersion` **1** kalır (soft alan migrate). `TopBarConfig` altına:

| Alan | Tip | Varsayılan | Normalize |
|------|-----|------------|-----------|
| `backgroundMode` | string | `mica` | Bilinmeyen → `mica` |
| `tintOpacity` | int 0–100 | `40` | Clamp 0–100 |

Örnek:

```json
"topBar": {
  "backgroundMode": "tint",
  "tintOpacity": 40,
  "statusItems": [ "..." ]
}
```

`AppConfig.Theme` (`system` / `light` / `dark`) ayrı kalır; tint fırça rengi onu takip eder.
Dock arka planına dokunulmaz.

## UI — sağ tık menü

Mevcut durum şeridi sağ tık menüsüne (**B**) **“Arka plan”** bölümü eklenir:

1. Durum öğeleri (göster/gizle) — mevcut
2. Separator
3. **Arka plan** başlığı + radio: Mica / Transparent / Tint
4. Opaklık kaydırıcısı + yüzde metni — **yalnızca Tint** seçiliyken etkin; sürüklerken bar anında güncellenir; config `ValueChanged` / hafif debounce ile yazılır
5. Separator (gerekirse) + Sola/Sağa taşı + “Varsayılana sıfırla”

**“Varsayılana sıfırla”** yalnız status sıra/görünürlüğe uygulanır; arka planı değiştirmez.
Ayrı “Arka planı sıfırla” menü öğesi v1’de **yok** (`mica` + config yeterli).

## Teknik boru hattı

| Mod | Pencere | Backdrop | Dolgu |
|-----|---------|----------|--------|
| `mica` | `AllowsTransparency=false` | Mica (`WindowAppearance`) | Frame extend; brush transparent |
| `transparent` | `AllowsTransparency=true` | Yok (Mica kaldır) | Tam şeffaf brush |
| `tint` | `AllowsTransparency=true` | Yok | Light/dark tema rengi + alpha(`tintOpacity`) |

### Geçiş kuralları

- WPF’te `AllowsTransparency` gösterim sonrası güvenilir değiştirilemez → **Mica ↔ transparent/tint** geçişinde güvenli yol: `ABM_REMOVE` → görünümü yeniden bağla / gerekirse hwnd yenile → `ABM_NEW` + `SETPOS`. Çıkış/crash yollarında görev çubuğu restore + `ABM_REMOVE` disiplini bozulmaz (ADR-005).
- Tint içinde kaydırıcı: aynı boru hattı; yalnızca brush alpha — AppBar yeniden kaydı **yok**.
- Sistem / config tema değişince tint rengi `AppThemePreference` ile yenilenir.
- Backdrop veya geçiş başarısız → opak tema fallback; AppBar kayıtlı kalır.

Sahiplik: `TopBarWindow` + `WindowAppearance` (veya ince bir top-bar appearance yardımcısı). Dock
`WindowAppearance` yolu değişmez.

## Non-goals (v1)

- Dock arka plan kişiselleştirme
- Tema mağazası / eklenti
- Özel hex / accent renk seçici
- Acrylic’i ayrı üçüncü “blur” modu olarak sunmak
- Arka plan “sıfırla” menü öğesi
- Status özelleştirme spec’ini yeniden açmak (yalnızca menüye bölüm ekleme)

## Manuel doğrulama

1. Varsayılan `mica` = bugünkü görünüm
2. `transparent` → masaüstü görünür; ikon/metin okunur
3. `tint` kaydırıcı 0→100 canlı; restart sonrası aynı değer
4. Light/dark (veya `theme`) değişince tint rengi uyumlu
5. Mod değişince AppBar asılı work area bırakmaz; Quit → taskbar restore + `ABM_REMOVE`

## Uygulama sırası (öneri)

1. Config alanları + normalize (`AppConfig` / `ConfigService`)
2. Appearance boru hattı (mica vs translucent) + güvenli AppBar geçişi
3. Sağ tık menü UI (radio + slider) + canlı tint
4. Manuel doğrulama listesi
