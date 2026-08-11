# ADR-007: Üçüncü taraf sistem tepsisi ikonlarının üst barda gösterilmesi (Tray Icon Mirroring)

**Durum:** Kabul edildi (Win11 UIA uygulaması)  
**Tarih:** 2026-08-11  
**Güncelleme:** 2026-08-11 — Win11 22H2+ için `ToolbarWindow32` / RPM yolu terk edildi; arka plan poll stealth peek (cloak + off-screen) ile görev çubuğu flaşı giderildi.  
**Bağlam:** Kullanıcının Windows sistem tepsisinde (Notification Area / System Tray) yer alan 3. parti uygulamaların (AnyDesk, NVIDIA, AMD, Logitech G HUB vb.) simgelerini doğrudan WharfBar üst barında canlı olarak görüp etkileşime girmesi talebi.

## Karar

1. **Deneysel `TrayIconMirrorService`** (`mirroredTray` durum öğesi):
   - **Win11 22H2+ (XAML tepsi):** UI Automation ile `Shell_TrayWnd` altında `AutomationId=NotifyItemIcon` olan `SystemTray.NormalButton` öğeleri taranır. Sistem omni ikonları (`SystemTrayIcon`) atlanır; WharfBar’ın kendi tepsi ikonu ada göre elenir.
   - Görev çubuğu WharfBar tarafından `SW_HIDE` iken UIA ağacı boştur → arka plan tarama `BeginTrayPeek(stealth: true)` ile yapılır: DWM cloak + tepsi monitörün altına park + `SW_SHOW` (görünür flaş yok). İkon `BitBlt` **görünür** peek ister: enable’da bir kez seed + overflow/tıklama + eksik glif için throttle’lı görünür peek; stealth poll önceki ikonları id ile korur.
   - Peek refcount: overflow + mirror iç içe gelebilir; `EndTrayPeek` erken kapatmaz.
   - `mirroredTray` durum öğesi kapalıysa poll timer durur (gereksiz peek yok).
   - Sol tık: UIA `InvokePattern` (yoksa ekran koordinatında mouse). Sağ tık: ekran koordinatında sağ tık; peek kısa süre açık kalır ki menü görünsün.

2. **Klasik RPM yolu (`TB_GETBUTTON` + `ReadProcessMemory`):** Win11 22H2+’te `ToolbarWindow32` / `SysPager` yok; bu makinede (25H2) doğrulanmış. Eski yol kaldırıldı.

3. **Mimari sınır:** ADR-005’in “yabancı tepsi çizme” yasağına kullanıcı onaylı istisna (deneysel). UIA başarısızsa şerit boş kalır; uygulama çökmez. Overflow düğmesi (`trayOverflow`) yedek erişim olarak kalır.

## Sonuçlar

- `.agent/boundaries.md`, `docs/04-gotchas.md`, `docs/01-architecture.md` UIA + peek ile güncellenir.
- `TopBarStatusItemIds.MirroredTray` varsayılan sırada `trayOverflow` sonrasındadır.
- Kullanıcı sağ tık / Ayarlar ile `mirroredTray` öğesini gizleyebilir.
