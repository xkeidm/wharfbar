# WharfBar — Agent Index

**WharfBar**, Windows’ta macOS tarzı üst bar (menu bar) + dock sunan bir masaüstü uygulamasıdır.

## Stack

- **.NET 8** + **WPF** + **WPF-UI** (Fluent / Mica)
- **Win32 P/Invoke** (`SHAppBarMessage`, DWM, `SHGetFileInfo`, vb.)
- Hedef: `net8.0-windows10.0.19041.0`, Per-Monitor V2 DPI

## Kanonik kaynak

**`.agent/` AI için kaynak gerçektir.** Cursor `.cursor/rules/*.mdc` dosyaları yalnızca kısa işaretçidir; ayrıntıyı Cursor kurallarına kopyalama.

## Kodlamadan önce oku

1. [`.agent/project.md`](.agent/project.md) — proje özeti
2. [`.agent/boundaries.md`](.agent/boundaries.md) — yap / sor / asla
3. [`.agent/conventions.md`](.agent/conventions.md) — kod ve doküman kuralları
4. [`docs/00-overview.md`](docs/00-overview.md) — amaç, hedefler, non-goals
5. [`docs/01-architecture.md`](docs/01-architecture.md) — planlanan yapı
6. İlgili teknik doküman:
   - Win32 / AppBar / DWM → [`docs/02-win32-appbar-dwm.md`](docs/02-win32-appbar-dwm.md)
   - Geliştirme sırası → [`docs/03-development-order.md`](docs/03-development-order.md)
   - Tuzaklar → [`docs/04-gotchas.md`](docs/04-gotchas.md)
7. Bağlam: [`.agent/context/stack.md`](.agent/context/stack.md), [`.agent/context/roadmap.md`](.agent/context/roadmap.md)

## Build / run

Kod kökü: **`src/WharfBar/`**, çözüm dosyası kökte (`WharfBar.sln`).

```bash
dotnet build WharfBar.sln
dotnet run --project src/WharfBar
```

> **Uyarı:** Uygulamayı çalıştırmak üst kenarda AppBar alanı rezerve eder. Süreci zorla
> sonlandırma (`Stop-Process -Force`, hata ayıklayıcıyı koparma) `ABM_REMOVE` yolunu atlar ve
> rezerve alan asılı kalır; düzeltmek için `explorer.exe` yeniden başlatılır. Pencereyi normal
> yoldan kapat.

Makinede yalnızca .NET 9 SDK kurulu olabilir; bu sorun değil, `net8.0-windows10.0.19041.0`
hedefini derleyebilir. **`global.json` ile 8.x’e sabitleme** — 8.x SDK yoksa derleme kırılır.

## Boundaries (özet)

- **Asla:** Çıkışta AppBar kaydını (`ABM_REMOVE`) veya görev çubuğu restore’unu unutma; Mica ile `AllowsTransparency=true` birlikte kullanma; yabancı tepsi ikonlarını bellek/undocumented yolla okuma.
- **Yap (ADR-005):** WharfBar çalışırken görev çubuğunu gizle/auto-hide; çıkış/crash’te önceki duruma geri yükle. Dock: canlı TaskBar pin + running. Tepsi: hibrit (native overflow düğmesi; tam ayna yok).
- **v1:** Yalnızca birincil monitör; UWP/Store ikonları sınırlı; durum göstergeleri .NET API ile simüle edilir.
- Ayrıntı: [`.agent/boundaries.md`](.agent/boundaries.md)

## Doküman güncelleme

Davranış veya mimari değişirse ilgili `docs/` ve `.agent/` dosyalarını aynı değişiklikle güncelle. Cursor kurallarını şişirme.
