# ADR-017: Eklenti kataloğu

**Durum:** Kabul edildi
**Tarih:** 2026-10-13

## Karar

Launchpad'deki Widget ve Eklenti araçları ile Ayarlar → Eklentiler aynı kataloğu gösterir.
Katalog iki kaynaktan gelir:

| Kaynak | Ne yapar |
|--------|----------|
| Yerleşik | Görselleştirici, Bluetooth bildirimi ve kamera aynası ada hapında çalışır. Saat ve takvim masaüstü penceresidir (`island.openWidgets`). Ada hapına girmez (ADR-015) |
| Yerel klasör | `%AppData%/WharfBar/plugins/<klasör>/manifest.json`. Alanlar: `id`, `name`, `description`, `kind` (`plugin` \| `widget`), `slot` (`topBar` \| `dock` \| `desktop` \| `island`) |

Yerel bir id'yi açmak `plugins.enabled` listesine yazar. Bu bir kayıttır. WharfBar o dosyadan derleme yüklemez, URL okumaz, süreç başlatmaz. `assembly`, `entryPoint`, `url` ve `download` alanları yok sayılır.

Üst çubuk (`topBar`) ve dock (`dock`) yuvaları metadata'dır. v1 bu yuvalara widget çizmez. Kart üzerindeki "yalnız kayıt" satırı bunu söyler.

`plugins` bloğu soft alandır. `schemaVersion` 2 kalır. Eksik anahtar boş liste demektir. Yerleşik id'ler `plugins.enabled` içine yazılmaz; ada bayrakları ve `openWidgets` durur.

Keşif yalnız bir alt klasöre bakar. Kökteki `manifest.json`, 16 KB üstü dosya, bozuk JSON, yerleşik id çakışması ve yeniden ayrıştırma noktası (junction) atlanır.

## Reddedilen

- İnternetten paket indirip çalıştırmak.
- `Assembly.Load` veya benzeri bir eklenti çalışma zamanı.
- Üst çubuğu veya dock'u yerel bir widget için yeniden çizmek. Bu, o yüzeylerin yaşam döngüsüne karışır; yuva kaydı yeter.
