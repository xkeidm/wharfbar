# WharfBar — Proje Brief (AI)

## Tek cümle

Windows’ta macOS tarzı **top bar + dock**: .NET 8 WPF + WPF-UI + Win32 P/Invoke.

## Ürün

- **Top bar:** Saat ve durum göstergeleri; **AppBar** ile üst kenarda work area rezerve eder.
- **Dock:** Sabitlenmiş uygulamalar; **overlay** (Topmost ± auto-hide); tıklayınca süreç başlatır.
- **v1 monitör:** Yalnızca **birincil** monitör.

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
5. **v1:** UWP ikonları yok; görev çubuğuna dokunma; tek örnek Mutex.
6. **Autostart:** Startup klasörü > Registry Run.
7. **Hit-testing + fullscreen hide** dock’ta uygulandı (stub + `FullscreenState` köprüsü); tam auto-hide Faz 5+.

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

## Mevcut repo durumu

**Faz 1–4 uygulandı.** Kod kökü `src/WharfBar/`, çözüm kökte `WharfBar.sln`.
Çalışan: top bar penceresi (DPI-farkında yerleşim), AppBar kaydı + garantili `ABM_REMOVE`,
`ABN_*` callback’leri, explorer restart sonrası yeniden kayıt, tek örnek Mutex,
WPF-UI Mica + sistem light/dark, kare köşeler (`DWMWCP_DONOTROUND`, ADR-004),
dock overlay (`DockWindow`, demo pinler, hover ölçek, süreç başlatma, fullscreen gizleme,
`WorkAreaChanged` yeniden konumlanma, auto-hide / hit-test stub).
Henüz yok: config (Faz 5), autostart (Faz 6), saat/durum (Faz 7).

Derleme: `dotnet build WharfBar.sln`. Ayrıntı: [`../docs/01-architecture.md`](../docs/01-architecture.md).

## Uygulama yapmadan önce

[`boundaries.md`](boundaries.md) + ilgili `docs/` dosyasını oku. Cursor kurallarını tek kaynak sanma.
