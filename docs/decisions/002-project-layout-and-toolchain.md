# ADR-002: Proje yerleşimi ve araç zinciri

**Durum:** Kabul edildi  
**Tarih:** 2026-08-08  
**Bağlam:** Faz 1 scaffold — kod kökü, çözüm dosyası ve SDK sürümü

## Karar

| Konu | Karar |
|------|-------|
| Kod kökü | `src/WharfBar/` |
| Çözüm | Kökte `WharfBar.sln`, tek proje |
| TFM | `net8.0-windows10.0.19041.0` (dokümanda belirtildiği gibi) |
| `global.json` | **Eklenmez** |
| `AllowUnsafeBlocks` | Kapalı |
| WPF-UI | Paket eklendi (4.3.0), tema/backdrop bağlanmadı |

## Gerekçe

**`src/` altında tek proje.** Repo kökü doküman ağırlıklı (`docs/`, `.agent/`); kaynak kodu
`src/` altında toplamak ikisini ayırır. Çözüm dosyası kökte durur ki `dotnet build WharfBar.sln`
depo kökünden çalışsın.

**`global.json` yok.** Geliştirme makinesinde yalnızca **.NET 9 SDK (9.0.316)** kurulu; 8.x SDK
yok. SDK 9, `net8.0-windows10.0.19041.0` hedefini sorunsuz derler (8.0.x runtime’lar da kurulu).
`global.json` ile 8.x’e sabitlemek derlemeyi anında kırardı. Hedef çerçeve dokümanların
söylediği gibi net8.0 kalır — sabitlenen SDK değil, hedeftir.

**`AllowUnsafeBlocks` kapalı.** `LibraryImport` kaynak üreticisi çoğu imza için `unsafe` bağlam
üretir; bu da `.agent/boundaries.md`’de onay gerektiren bir maddeyi tetiklerdi. Klasik
`[DllImport]` kullanıldı — `docs/02-win32-appbar-dwm.md` zaten bu deseni gösteriyor ve AppBar
imzaları için ölçülebilir bir maliyeti yok.

**WPF-UI eklendi ama bağlanmadı.** Faz 1 listesi paketi istiyor; tema sözlüklerini `App.xaml`’e
merge etmek görsel bir karar ve Faz 3’e ait. Paket şimdilik yalnızca referans.

## Sonuçlar

- Yeni proje eklenirse (test vb.) `src/` altında açılır ve `WharfBar.sln`’e eklenir.
- Makineye 8.x SDK kurulsa bile `global.json` eklemek için ayrı gerekçe gerekir.
- Gerçekten `unsafe` kod gerekirse önce kullanıcıya sorulur (`boundaries.md`).

## Alternatifler (reddedilen)

1. **Kod doğrudan repo kökünde** — doküman ağacıyla karışır.
2. **`global.json` ile 8.0 pinleme** — kurulu SDK olmadığı için derleme kırılır.
3. **`LibraryImport` + `AllowUnsafeBlocks`** — sınır ihlali; kazancı yok.
