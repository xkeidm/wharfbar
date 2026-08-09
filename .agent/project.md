# WharfBar — Proje Brief (AI)

## Tek cümle

Windows’ta macOS tarzı **top bar + dock**: .NET 8 WPF + WPF-UI + Win32 P/Invoke.

## Öncelik (çakışmada)

1. Düşük kaynak tüketimi → 2. Windows Fluent native görünüm → 3. Basitlik.
   macOS marka/ikon dili yok; yalnızca düzen ilhamı. Ayrıntı: [`../docs/00-overview.md`](../docs/00-overview.md).

## Ürün

- **Top bar:** Sol Windows logo (çizim) + menü (Hakkında / Kapat); sağda gizli/ağ/pil/ses/
  **saat+tarih+bildirim** (tek Win11 kümesi; tıklanınca native `Win+N`); **Hızlı ayarlar**
  flyout (ses+ağ, ADR-006); **AppBar** + tepsi hibrit.
- **Dock:** Canlı TaskBar pinleri + çalışan uygulamalar (overlay; Topmost ± auto-hide);
  `config.pinnedApps` seed/fallback.
- **Görev çubuğu:** WharfBar çalışırken gizle/auto-hide; çıkışta geri yükle (ADR-005).
- **v1 monitör:** Yalnızca **birincil** monitör.
- **Global menü yok** — top bar File/Edit benzeri uygulama menüsü taşımaz.

## Stack (özet)

.NET 8 WPF, WPF-UI, `net8.0-windows10.0.19041.0`, Per-Monitor V2 DPI. Temel P/Invoke için `AllowUnsafeBlocks` gerekmez. Ayrıntı: [`context/stack.md`](context/stack.md).

## Mimari özeti

- UI klasörü: **`Views/`** (veya `UI/`) — `/Windows` kullanma (Win32 ile karışır).
- Native: `AppBar`, DWM (yedek), `ShellIcon`.
- Services: config, launcher, ikon, autostart (Startup klasörü), Mutex, status (.NET API).
- Config: `%AppData%/WharfBar/config.json` + `schemaVersion`.

Tam yapı: [`../docs/01-architecture.md`](../docs/01-architecture.md).

## Kritik teknik gerçekler (unutma)

1. **`Shell_NotifyIcon` ≠ sistem tepsi okuma.** Wi-Fi/pil/ses için .NET API + kendi ikonlar.
2. **Dock AppBar zorunlu değil** — overlay tercih (ADR-001). Top bar AppBar kullanır.
3. **WPF-UI backdrop önce**; ham DWM Mica ile çakıştırma. **`AllowsTransparency=true` + Mica yasak.**
4. **AppBar:** çıkışta `ABM_REMOVE`; callback’ler; explorer restart sonrası yeniden kayıt; DPI/display handler’lar.
5. **v1:** UWP ikonları sınırlı (placeholder); görev çubuğu gizleme + restore (ADR-005); tek örnek Mutex.
6. **Autostart:** Startup klasörü > Registry Run (`AutostartService` + `AppConfig.autostart`).
7. **Hit-testing + fullscreen hide** dock’ta uygulandı (stub + `FullscreenState` köprüsü); gerçek auto-hide sonraki faz (tercih Faz 5’te config’te).
8. **Durum:** saat / ağ / pil `.NET` + `GetSystemPowerStatus`; ses Core Audio; hızlı ayarlar
   (ADR-006); tepsi yalnızca kendi Quit menüsü.

## Doküman haritası

| Dosya | İçerik |
|-------|--------|
| `docs/00-overview.md` | Amaç / non-goals |
| `docs/01-architecture.md` | Klasörler, config şeması |
| `docs/02-win32-appbar-dwm.md` | AppBar/DWM/tray/ikon |
| `docs/03-development-order.md` | Fazlar |
| `docs/04-gotchas.md` | Tuzaklar |
| `docs/decisions/001-...` | Dock overlay kararı |
| `docs/decisions/002-...` | Proje yerleşimi, TFM, `global.json` yok |
| `docs/decisions/003-...` | Top bar yerleşimi, `ABM_REMOVE`, fullscreen |
| `docs/decisions/004-...` | Mica / WPF-UI, köşe `DONOTROUND`, tema yenileme |
| `docs/decisions/005-...` | Canlı dock, tepsi hibrit, görev çubuğu gizle/geri yükle |
| `docs/decisions/006-...` | Hızlı ayarlar flyout (ses karıştırıcı + ağ) |

## Mevcut repo durumu

**Faz 1–9 kodu çalışma ağacında** (commit + 9a/9b/9c manuel doğrulama bekliyor). Kod kökü
`src/WharfBar/`, çözüm kökte `WharfBar.sln`. Çalışan: top bar penceresi (DPI-farkında yerleşim),
AppBar kaydı + garantili `ABM_REMOVE`, `ABN_*` callback’leri, explorer restart sonrası yeniden kayıt,
tek örnek Mutex, WPF-UI Mica + sistem light/dark (veya config `theme`), kare köşeler
(`DWMWCP_DONOTROUND`, ADR-004), dock overlay (`DockWindow`, canlı pin/running birleşik UI,
hover ölçek, süreç başlatma / pencere odaklama, fullscreen gizleme, `WorkAreaChanged` yeniden
konumlanma, auto-hide / hit-test stub), `ConfigService` → `%AppData%/WharfBar/config.json`,
`AutostartService` → Startup klasörü `WharfBar.lnk` (`AppConfig.autostart`), saat + ağ + pil
(`StatusService`, üst bar sağ şerit; **Segoe MDL2** glifleri + overflow düğmesi), kendi tepsi ikonu + Quit (`TrayIconService`),
görev çubuğu gizle/restore (`TaskbarVisibilityService`; çıkışta restore → `ABM_REMOVE`),
canlı TaskBar pinleri + çalışan uygulamalar (`TaskbarPinnedAppsSource`, `RunningAppsSource`,
`DockAppsComposer`), üst bar tray overflow düğmesi (`TrayOverflowInvoker` / `Win+B`).
Dock varsayılan ikon **28 DIP** / yükseklik ~46 DIP, ikon aralığı 12 DIP, monitör alt kenarına
yaslı (`IShellItemImageFactory` ile DPI-keskin ikonlar). Üst bar tooltip’leri Segoe UI (MDL2 mirası tofu yok). Overflow: gizli taskbar’da peek-show.
Sol menü: Hakkında + Kapat (`Application.Shutdown` → restore + `ABM_REMOVE`). **Hızlı ayarlar**
(ADR-006): ses/ağ ikonundan paylaşılan flyout; EarTrumpet tarzı aygıt blokları + uygulama
sesleri; ağ durumu / Settings. Henüz yok: dock auto-hide polish (Faz 8).
Öncelikli yapılacaklar: [`context/roadmap.md`](context/roadmap.md).
Sıradaki ürün işi: Faz 9 manuel doğrulama; ardından Faz 8 auto-hide (9a/9b sonrası daha anlamlı).
Spec: [`../docs/superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md`](../docs/superpowers/specs/2026-08-09-live-dock-and-tray-hybrid-design.md).
Plan: [`../docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md`](../docs/superpowers/plans/2026-08-09-live-dock-and-tray-hybrid.md).
Üst bar özelleştirme (göster/gizle + sıra; sağ tık):
[`../docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md`](../docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md);
plan: [`../docs/superpowers/plans/2026-08-09-topbar-status-items-customization.md`](../docs/superpowers/plans/2026-08-09-topbar-status-items-customization.md).

Derleme: `dotnet build WharfBar.sln`. Ayrıntı: [`../docs/01-architecture.md`](../docs/01-architecture.md).

## Uygulama yapmadan önce

[`boundaries.md`](boundaries.md) + ilgili `docs/` dosyasını oku. Cursor kurallarını tek kaynak sanma.
