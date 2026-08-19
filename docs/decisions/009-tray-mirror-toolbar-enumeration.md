# ADR-009: Üst bar tray aynası — belgelenmiş toolbar mesajları ile salt-okunur enumeration

**Durum:** Kabul edildi (2026-08-19)
**Tarih:** 2026-08-19
**Kapsam:** Üçüncü taraf tepsi ikonlarının üst barda aynası + tıklama forward; ADR-005'in
"yabancı tepsi ikonları çizilmez / click-forward yok" kararını sınırlı ölçüde yeniden açar
(spec: [2026-08-19-tray-mirror-design](../superpowers/specs/2026-08-19-tray-mirror-design.md))

## Karar

| Konu | Seçim |
|------|--------|
| Kaynak | Ana tray alanı: `Shell_TrayWnd` → `TrayNotifyWnd` → `SysPager` → `ToolbarWindow32` |
| Enumeration | **Belgelenmiş comctl32 mesajları** (`TB_BUTTONCOUNT`, `TB_GETBUTTON` (index wParam; `TBBUTTON` → `idCommand`/`iBitmap`), `TB_GETIMAGELIST` + `ImageList_GetIcon`, `TB_GETITEMRECT`, `TB_GETBUTTONTEXT` (command id wParam)) — bunlar standart toolbar kontrol mesajlarıdır; tray bunları barındıran bir ToolbarWindow32 olduğundan aynı mesaj yüzeyi kullanılır |
| Tıklama | Item rect → `SendMessage` mouse mesajları (WM_LBUTTONDOWN/UP, WM_RBUTTONDOWN/UP, çift tık) — explorer'ın kendi tray subclass'ı işler; `SendInput` / peek-show gerekmez |
| Güncelleme | 1.5 sn arka plan `System.Threading.Timer` poll + snapshot diff (olay bildirimi yok; Windows böyle bir API sunmaz) |
| Filtre | Bilinen sistem ikonları tooltip adıyla düşülür; WharfBar kendi Quit ikonu düşülür; tanınmayanlar gösterilir |
| Kapsam dışı | Gizli/overflow ikonlar (yalnızca açık popup'ta var; kalıcı okunamaz) → mevcut `trayOverflow` düğmesi korunur |

## Gerekçe

- WharfBar görev çubuğunu gizlediğinde üçüncü taraf tepsi ikonları kullanıcıdan kopar; tek
  Win+B overflow düğmesi yetersiz kaldı (kullanıcı geri bildirimi).
- Resmi bir "tepsiyi listele" API'si yoktur (`Shell_NotifyIcon` yalnızca kendi ikonunu
  yönetir). Uygulanabilir tek yol, tray'in gerçekten bir comctl32 `ToolbarWindow32` olması
  gerçeğine dayanır: **belgelenmiş** toolbar mesajları aynı kontrol yüzeyini okur. Bu,
  `ReadProcessMemory`, enjeksiyon veya undocumented hook değildir; StartAllBack / SysTrayX
  benzeri araçların yıllardır kullandığı taşıma sınırında durur.
- Kırılganlık kabul edilir: Windows tray HWND yapısını değiştirirse ayna sessizce gizlenir;
  shell durumu hiçbir yolda bozulmaz (salt-okunur, geri yüklenecek bir şey yok).

## Sonuçlar

- `.agent/boundaries.md` "Asla" daraltılır: RPM / enjeksiyon / undocumented API **sabit yasak**;
  belgelenmiş comctl32 mesajlarıyla salt-okunur ayna + `SendMessage` click-forward **"Yap"** olur.
- ADR-005'in 2. maddesi (üst bar — tepsi hibrit) güncellenir; "çizilmez / click-forward yok"
  satırı bu ADR ile değiştirilir.
- Yeni dosyalar: `Native/TrayToolbar.cs`, `Services/TrayMirrorService.cs`,
  `Services/TraySystemIconFilter.cs`, `Views/TopBar` `trayMirror` öğesi + click forwarder.
- Config katalog v5 (`trayMirror`); `schemaVersion` 1 kalır.
- Doküman: `docs/00-overview.md`, `docs/02-win32-appbar-dwm.md`, `docs/03-development-order.md`,
  `docs/04-gotchas.md`, `.agent/*` güncellenir.

## Alternatifler (reddedilen)

1. **Tam tepsi aynası + olay bildirimi** (explorer'a DLL enjeksiyonu / `SetWindowSubclass`
   cross-process / Windhawk benzeri hook) — enjeksiyon "Asla" kategorisi; reddedildi.
2. **UIA (UI Automation)** — desteklenen API, tıklama çalışır, ancak **ikon görseli** UIA'da
   yok; glif/placeholder deneyimi zayıf; reddedildi.
3. **`SendInput` ile gerçek tıklama** — gizli görev çubuğunda koordinatlar geçersiz;
   peek-show gerekir; `SendMessage` (aynı süreç içi, explorer kendi subclass'ı) daha güvenilir.
4. **Yalnızca mevcut `Win+B` overflow** — kullanıcı isteğini karşılamadı; ADR-005 durumu.