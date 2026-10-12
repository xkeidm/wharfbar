# ADR-015: Launchpad, galeri, sol dock, ada yüzleri

**Durum:** Kabul edildi
**Tarih:** 2026-10-11

## Karar

| Konu | Seçim |
|------|--------|
| Pin klasörü | “Dock'a sabitle” hedef dosya veya klasörse `User Pinned\TaskBar` içine `.lnk` yazar. `Taskband` yazılmaz |
| Sol dock | `dock.position` = `left`. Plaka dik, `ABE_LEFT`, oto-gizleme sola kayar. Alt konumlar yatay kalır |
| Launchpad | Dock’un en sol karosu. Başlat menüsü kısayolları, arama, tıklayınca açılır |
| Widget | Saat ve takvim masaüstü pencereleridir. Ada hapına girmez |
| Eklenti | Süreç içi anahtar: görselleştirici, Bluetooth bildirimi, kamera aynası. Dış derleme yüklenmez |
| Görselleştirici | Müzik çalarken hapta beş çubuk. Yükseklik varsayılan çıkışın tepe değeridir |
| Bluetooth | Yeni bağlanan eşleşmiş cihazın adı hapta kısa süre durur. Hızlı ayar karosu durur |
| Kamera | Galeriden açılır. Kare hapın içindedir. İzin yoksa veya kamera meşgulse yazı kalır, sahte kare yok |
| Hap görüntüsü | Son kayıt önizlemesi hapın kendi penceresindedir. Yakalama yalnız o pencereyi düşürür |

## Reddedilen

- Odaktaki uygulamanın Dosya/Düzen menüsünü 30 DIP çubuğa taşımak. Çoğu Win32 uygulaması menüsünü dışarı vermez.
- Stage Manager. Pencereleri takımlara ayırıp kenarda küçük resim olarak bekletmek bu sürümde yok.
