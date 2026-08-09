# WharfBar — Top bar kısayol paneli + ikon özelleştirme (tasarım notu)

**Tarih:** 2026-08-09  
**Durum:** Tasarım onaylandı (kullanıcı 2026-08-09) — v1 kod landed; manuel UI checklist bekliyor; plan:
  [`../plans/2026-08-09-topbar-status-items-customization.md`](../plans/2026-08-09-topbar-status-items-customization.md).  
**İlgili:** [ADR-006](../../decisions/006-quick-settings-flyout.md), [ADR-005](../../decisions/005-shell-replace-live-dock-tray-hybrid.md), [roadmap](../../../.agent/context/roadmap.md)

> Bu dosya **final ADR değildir**. Uygulama planı sonrası istenirse ayrı ADR veya mevcut
> ADR-006 güncellemesi yazılır.

## Kullanıcı isteği (özet)

macOS’taki gibi bir **kısayol / Control Center paneli** ve/veya üst barda **ikon ekle–çıkar**
(özelleştirilebilir menu bar öğeleri). Soru: “koyalım mı?” — tasarım kararı.

İki ilgili ama ayrı fikir:

| | Fikir | macOS karşılığı | WharfBar’da bugün |
|---|--------|-----------------|-------------------|
| **A** | Kısayol paneli (karolar: Wi‑Fi, BT, Focus, …) | Control Center | **ADR-006 Quick Settings** (zaten var / WIP) |
| **B** | Üst barda hangi ikonların görüneceği | Menu bar → Hold + sürükle / gizle | Sabit XAML şerit; config yok |

## Mevcut üst bar envanteri

Sol → sağ (sağ durum şeridi):

| Öğе | Rol | Tıklama |
|-----|-----|---------|
| Tray overflow | Native gizli tepsi | `Win+B` / chevron (ADR-005) |
| Ağ | Durum glifi | Quick Settings → **Ağ** sayfası |
| Pil | Glif + % (yoksa gizlenir) | (durum; panel yok) |
| Ses | Glif | Quick Settings → **Ana** (+ karıştırıcı) |
| Saat / tarih | Win11 tarzı yan yana | Takvim `Popup` |
| Bildirimler | Glif | Native `Win+N` |

Sol: Windows logo menü (Hakkında / Kapat).

Quick Settings ana sayfa karoları (ADR-006): Wi‑Fi, Bluetooth, Uçak, Yansıt, Gece ışığı,
Enerji tasarrufu + master slider + mixer chevron.

## Çakışma ve sınırlar

1. **A ≈ ADR-006.** Ayrı bir “macOS Control Center” penceresi ikinci panel demektir;
   Win11 dilinden ve mevcut flyout’tan uzaklaşır; bakım maliyeti çift olur.
2. **ADR-005:** Yabancı / sistem tepsi ikonlarını WharfBar içinde çizme veya click-forward **yasak**.
   “İstediğimiz ikonları ekle” **üçüncü taraf tepsi aynası** anlamına gelmemeli.
3. **WIP:** Quick Settings + EarTrumpet tarzı mixer (`AudioMixerService` / Core Audio) hâlâ
   olgunlaşıyor. Panel host’unu veya mixer’ı yeniden tasarlamak bu işi bozar.
4. **Non-goal (overview):** Eklenti sistemi / tema mağazası v1 dışı.
5. **Config:** `schemaVersion` 1; yeni alan → alan bazlı varsayılanlarla soft migrate tercih
   (breaking şema için onay gerekir — `boundaries.md`).

## Karar (onaylı)

**Yaklaşım 3** — fazlı: QS = Control Center + hafif bar özelleştirme.

- **Panel:** Yeni pencere yok; ADR-006 Quick Settings = Control Center benzeri yüzey.
- **Özelleştirme v1:** Yalnızca yerleşik durum öğeleri — show/hide + reorder.
- **Düzenleme UI:** Durum şeridi **sağ tık** → işaretli menü (göster/gizle; sıra için yukarı/aşağı
  veya menü sırası yeterli). Logo diyaloğu v1 gerekmez; yalnız `config.json` yetersiz.
- **“İkon ekle”:** Yalnızca yerleşik katalog. Kısayol slot = v2 adayı; tepsi aynası = red (ADR-005).
- **Asla v1:** Yabancı NotifyIcon çizimi; rastgele uygulama ikonlarını bar’a “pin” (dock’un işi);
  eklenti host’u.

### Uygulama sırası (onaylı)

```
1. Mevcut QS + mixer WIP’ini stabilize et / manuel doğrula
   (host / karo / mixer’ı yeniden yazma — yalnızca bitir)

2. v1 bar özelleştirme (bu özellik)
   └─ config: topBar.statusItems görünürlük + sıra
   └─ düzenleme: durum şeridi sağ tık menü
   └─ QS / mixer koduna dokunma (yalnızca mevcut giriş noktaları)

3. Sonra — QS cila (roadmap P2 #9)
   └─ isteğe bağlı: “Hızlı ayarlar” tek giriş ikonu (birleşik CC düğmesi)
   └─ isteğe bağlı: kullanıcı kısayol slot’ları (yalnızca ms-settings / Process.Start —
      tepsi aynası değil)
```

| Artı | Eksi |
|------|------|
| Her iki kullanıcı cümlesini net ayırır | Küçük config + TopBar refactor |
| ADR-005/006 ile uyumlu | “Her şeyi ekle” beklentisini bilinçli kısıtlar |
| Mixer WIP ile paralel güvenli | — |

## Reddedilen yaklaşımlar (özet)

1. Yalnızca QS cila, bar sabit — “ikon ekle/çıkar” isteğini karşılamaz.
2. Ayrı Control Center flyout — çift UX; ADR-006 ile çakışır.
3. Tepsi ikon aynası — ADR-005.

## v1 kapsamı (somut)

**Dahil**

- `AppConfig` altında `topBar.statusItems: string[]` (id sırası = görünen sıra).
- Desteklenen id’ler (sabit katalog): `trayOverflow`, `network`, `battery`, `volume`,
  `dateTime`, `notifications`.
- Varsayılan = bugünkü tam set (geriye uyumlu).
- Pil yokken zaten collapse — özelleştirmeden bağımsız kalır.
- Düzenleme UI: durum şeridinde sağ tık → öğe işaretli menü (+ sıra).
- `schemaVersion` 1’de soft alan; yoksa varsayılan.

**Hariç (sonra / asla)**

- İkinci Control Center flyout
- QS karo düzenini yeniden yazma; mixer API değişiklikleri
- Üçüncü taraf tepsi / belgelenmemiş enumeration
- Focus / DND / gerçek Gece ışığı API (P2 cila)
- Kullanıcı tanımlı “kısayol slot” uygulamaları (v2 adayı)
- Sol Windows menü özelleştirme
- Logo menüsünden ayarlar diyaloğu (v1; keşfedilebilirlik sonrası eklenebilir)

## Sonra (v2+)

1. QS cila (roadmap #9).
2. Tek “Hızlı ayarlar” bar ikonu (ağ+ses birleşik giriş; macOS CC düğmesine yakın).
3. İsteğe bağlı kullanıcı kısayolları: yalnızca `path` / `ms-settings:` — dock’tan ayrı ince şerit;
   ADR gerekir.
4. Tam sürükle-bırak menu bar edit modu (macOS Command-sürükle) — düşük öncelik.
5. Logo menüsünden “Üst bar öğeleri…” diyaloğu (sağ tık’a ek keşif).

## WIP ile ilişki

| Çalışma | Bu tasarım |
|---------|------------|
| Quick Settings flyout (ADR-006) | **Dokunma** — host/karo/mixer’ı yeniden tasarlama; önce stabilize |
| EarTrumpet mixer | **Dokunma** — ses sayfası ayrı iş |
| TopBarWindow sabit XAML şerit | v1 özelleştirmede **ItemsControl / görünürlük** refactor adayı — QS stabilize olduktan sonra |
| Faz 8 dock auto-hide / Faz 9 doğrulama | Bağımsız; öncelik sırasını bozma |

## Kilitlenen sorular

| # | Soru | Karar |
|---|------|-------|
| 1 | Öncelik | Fazlı: QS stabilize → bar özelleştirme → QS cila |
| 2 | Düzenleme UI | Durum şeridi sağ tık menü |
| 3 | “İkon ekle” | Yalnızca yerleşik durum öğeleri |

## Sonraki adım

1. Plan uygulandı: [`../plans/2026-08-09-topbar-status-items-customization.md`](../plans/2026-08-09-topbar-status-items-customization.md).
2. Manuel UI checklist (sağ tık göster/gizle, sıra, reset, sol tık regresyon).
3. İstenirse ayrı ADR veya ADR-006 güncellemesi.
