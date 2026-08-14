# ADR-007: Üçüncü taraf sistem tepsisi ikonlarının üst barda gösterilmesi (Tray Icon Mirroring)

**Durum:** Kabul edildi (Win11 UIA uygulaması)  
**Tarih:** 2026-08-11  
**Güncelleme:** 2026-08-13 — Başka süreçten `Shell_TrayWnd` DWM cloak başarısız; UIA taraması peek’i saniyelerce tutup varsayılan görev çubuğunu görünür bırakıyordu. Arka plan poll kaldırıldı; ikon yakalama yalnızca kullanıcı overflow / tıklama peek’inde.  
**Bağlam:** Kullanıcının Windows sistem tepsisinde (Notification Area / System Tray) yer alan 3. parti uygulamaların (AnyDesk, NVIDIA, AMD, Logitech G HUB vb.) simgelerini doğrudan WharfBar üst barında canlı olarak görüp etkileşime girmesi talebi.

## Karar

1. **Deneysel `TrayIconMirrorService`** (`mirroredTray` durum öğesi):
   - **Win11 22H2+ (XAML tepsi):** UI Automation ile `Shell_TrayWnd` altında `AutomationId=NotifyItemIcon` olan `SystemTray.NormalButton` öğeleri taranır. Sistem omni ikonları (`SystemTrayIcon`) atlanır; WharfBar’ın kendi tepsi ikonu ada göre elenir.
   - Görev çubuğu WharfBar tarafından `SW_HIDE` iken UIA ağacı boştur. Başka süreçten DWM cloak işe yaramaz; UIA taraması peek’i saniyelerce açık tutar → varsayılan görev çubuğu görünür kalır. Bu yüzden **arka plan poll yok**. İkon `BitBlt` yalnızca kullanıcı overflow / tıklama peek’inde (`PollWhilePeeked`). Overflow düğmesi yedek erişim olarak kalır.
   - Peek refcount: overflow + mirror iç içe gelebilir; `EndTrayPeek` erken kapatmaz.
   - `mirroredTray` durum öğesi kapalıysa poll timer durur (gereksiz peek yok).
   - Sol tık: UIA `InvokePattern` (yoksa ekran koordinatında mouse). Sağ tık: ekran koordinatında sağ tık; peek kısa süre açık kalır ki menü görünsün.

2. **Klasik RPM yolu (`TB_GETBUTTON` + `ReadProcessMemory`):** Win11 22H2+’te `ToolbarWindow32` / `SysPager` yok; bu makinede (25H2) doğrulanmış. Eski yol kaldırıldı.

3. **Mimari sınır:** ADR-005’in “yabancı tepsi çizme” yasağına kullanıcı onaylı istisna (deneysel). UIA başarısızsa şerit boş kalır; uygulama çökmez. Overflow düğmesi (`trayOverflow`) yedek erişim olarak kalır.

## Sonuçlar

- `.agent/boundaries.md`, `docs/04-gotchas.md`, `docs/01-architecture.md` UIA + peek ile güncellenir.
- `TopBarStatusItemIds.MirroredTray` varsayılan sırada `trayOverflow` sonrasındadır.
- Kullanıcı sağ tık / Ayarlar ile `mirroredTray` öğesini gizleyebilir.
