# WharfBar — Faz 8 Dock Auto-Hide Tasarımı

**Tarih:** 2026-08-10  
**Durum:** Onaylandı (kullanıcı: Faz 1 / auto-hide)  
**Kapsam:** `dock.autoHide` davranışını tamamlamak; kenar proximity, slide animasyonu,
gizlikte click-through, fullscreen netliği. Görsel cila / sağ tık / magnification **yok**
(sonraki dock aşamaları).

Dayanak: [ADR-001](../../decisions/001-dock-appbar-vs-overlay.md),
[`docs/03-development-order.md`](../../03-development-order.md) Faz 8,
[`docs/02-win32-appbar-dwm.md`](../../02-win32-appbar-dwm.md) §i,
mevcut stub’lar: `DockAutoHideController`, `DockHitTest`.

## Hedef

`config.dock.autoHide == true` iken dock, fare birincil monitörün alt kenar bandına
yaklaşınca görünsün; dock ve band dışına çıkınca animasyonla gizlensin. Gizliyken tıklama
alttaki pencereye gitsin. `autoHide == false` iken dock her zaman görünür ve etkileşimli
(bugünkü stub davranışı).

## Kararlar

| Konu | Seçim |
|------|--------|
| Tetik | `GetCursorPos` + birincil monitör alt kenar bandı (DIP→px); `DispatcherTimer` poll |
| Kenar bandı | **8 DIP** (clamp 4–16); fiziksel px = `DipToPixels` |
| Gizleme geometrisi | Dock’u monitör alt kenarının **altına** kaydır (`SetWindowPos` Y); `Visibility=Collapsed` **kullanma** (auto-hide yolu) |
| Animasyon | ~180–220 ms, ease-out; Y pozisyon interpolate (WPF `AllowsTransparency` yok — Mica korunur) |
| Hide gecikmesi | Dock + band dışına çıkınca **500 ms** (titreme önleme) |
| Show gecikmesi | Band’e girince **0 ms** (anında) |
| Hit-test | Görünür / gösteriliyor: `DockHitTest.ApplyInteractive`; gizli / gizleniyor (off-screen): `ApplyPassthrough` (`WS_EX_TRANSPARENT`) |
| Fullscreen | Mevcut `FullscreenState` → `Visibility=Collapsed` + Topmost altta; auto-hide timer **durur**; çıkınca auto-hide state’e dön (`autoHide` true ise Hidden’dan başla veya Show — tercih: **Show** bir kez, sonra normal kurallar) |
| Config | `dock.autoHide` zaten var; şema kırılması yok |
| AppBar | Dock AppBar olmaz (ADR-001) |

## Durum makinesi

```
                    autoHide=false
                 ┌──────────────────────┐
                 │ AlwaysVisible        │
                 │ (interactive)        │
                 └──────────────────────┘

autoHide=true (ve !fullscreen):

  Hidden ──(cursor in edge band)──► Showing ──(anim done)──► Visible
    ▲                                                      │
    │                                                      │
    └──(anim done)── Hiding ◄──(left dock+band, 500ms)─────┘

FullscreenEntered → FullscreenHidden (Collapsed; timer off; passthrough N/A)
FullscreenExited  → Visible (+ interactive), sonra auto-hide kuralları
```

- `AlwaysVisible`: timer yok; `ApplyInteractive`; `ApplyPlacement` normal Y.
- `Hidden`: pencere monitör altına kaymış; `ApplyPassthrough`; poll devam.
- Fare dock hwnd üzerindeyken hide timer iptal / restart yok — dock üzerindeyken Visible kalır.

## Mimari

```
DockWindow
  ├─ DockAutoHideController   # state + timer + Show/Hide requests
  ├─ DockHitTest              # SetWindowLong WS_EX_TRANSPARENT toggle
  ├─ ApplyPlacement           # visible Y = monitor.Bottom - height
  └─ ApplyAutoHideOffset      # hidden Y = monitor.Bottom (+ peek 0)
       FullscreenState        # mevcut; auto-hide’ı bastırır
```

### `DockAutoHideController`

Sorumluluklar:

- `Enabled` ← config
- `IsEffectivelyVisible` (Visible/Showing vs Hidden/Hiding)
- `Changed` event (UI animasyon / hit-test / placement için)
- Pointer poll başlat/durdur (`Start` / `Stop`)
- Edge band + dock rect hit test (dock rect’i pencere sağlar)
- Hide debounce 500 ms

DockWindow sağlar:

- Primary monitor bounds + current dock rect (physical px)
- Animasyon ve `SetWindowPos`
- `DockHitTest` çağrıları
- Fullscreen bridge (mevcut)

### `DockHitTest`

- `ApplyInteractive(hwnd)`: `WS_EX_TRANSPARENT` temizle (`GetWindowLong` / `SetWindowLong`)
- `ApplyPassthrough(hwnd)`: `WS_EX_TRANSPARENT` set
- `WS_EX_LAYERED` zorunlu değil; `TRANSPARENT` tek başına click-through için yeterli (message’lar altına geçer)
- P/Invoke: `NativeMethods` içine `WS_EX_TRANSPARENT`, `SetWindowLong` (veya `SetWindowLongPtr` 64-bit güvenli)

### Yerleşim

Mevcut `ApplyPlacement` visible konumunu hesaplar. Auto-hide:

- Visible hedef Y: `monitor.Bottom - heightPx - bottomGap` (bugün gibi)
- Hidden hedef Y: `monitor.Bottom` (pencere tamamen ekranın altında; peek yok — v1 basit)
- Animasyon: mevcut Y → hedef Y; tamamlanınca hit-test güncelle

`WorkAreaChanged` / DPI: animasyonu iptal et, state’e göre anında doğru Y’ye oturt.

## Non-goals (bu faz)

- Hover magnification / komşu ölçek
- Dock arka plan kişiselleştirme
- Sağ tık / pin-unpin
- Minimize toggle
- Peek strip (1–2 px görünür dilim)
- Çoklu monitör
- Dock AppBar / work-area reserve

## Hata / kenar durumları

| Durum | Davranış |
|-------|----------|
| `GetCursorPos` fail | Poll turunu atla; state değiştirme |
| hwnd geçersiz | no-op |
| `autoHide` runtime değişimi | v1: yalnızca başlangıç config; hot-reload yok |
| Explorer / TaskbarCreated | Placement yenile; auto-hide state koru |

## Doğrulama (manuel)

1. `autoHide: true` — fare alt kenara → dock çıkar; uzaklaş → ~0.5 s sonra kaybolur
2. Dock üzerindeyken gizlenmez
3. Gizliyken dock bölgesine tık → alttaki pencere alır
4. Görünürken ikon tık → launch/focus
5. `autoHide: false` — her zaman görünür
6. Fullscreen app → dock yok; çıkış → dock geri
7. Maximize pencere + kenar hover — dock üstte kalır (Topmost)
8. `dotnet build WharfBar.sln` temiz

## Doküman güncellemesi (aynı değişiklik)

- `docs/03-development-order.md` Faz 8 checklist
- `docs/01-architecture.md` stub → gerçek davranış notu
- `.agent/project.md` / `.agent/context/roadmap.md` P0 satırı
- `docs/02-win32-appbar-dwm.md` §i — `WS_EX_TRANSPARENT` uygulandı notu (kısa)
