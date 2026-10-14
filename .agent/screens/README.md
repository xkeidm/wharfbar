# Ekranlar — özet indeks

WharfBar'ın kullanıcı arayüzü turu: her ekranın ayrıntılı sayfası aşağıda. Kod tarafı:
`src/WharfBar/Views/`; config karşılıkları `src/WharfBar/Models/AppConfig.cs`.

| Ekran | Sayfa | Özet |
|-------|-------|------|
| **Top bar** | [`topbar.md`](topbar.md) | 30 DIP AppBar; sol: Windows logo menüsü; orta: ada hapı (boşta Pano veya son kayıt, şimdi çalıyor, ses/parlaklık/pil, uydular; pano birincil, ikincil yalnız müzik ve sayaç); sağ: durum şeridi (sağ tık ile göster/gizle, sırala, arka plan + opaklık). |
| **Dock** | [`dock.md`](dock.md) | Alt kenar overlay; solda salt okunur görev çubuğu pinleri (kapalılar dahil) + `dock.items`, sağda sabitlenmemiş çalışanlar, uygulama başına bir ikon; plaka düğme ritmi (ikon 24, düğme 40, aralık 4), büyütme yok, arka plan `mica`/`transparent`/`tint`, sürükle-sırala, dosya bırakma, alt-sol/orta/sağ konum. |
| **Ayarlar** | [`settings.md`](settings.md) | Logo/tepsi menüsünden açılır; Genel · Görünüm · Dock · Ekranlar · Üst Çubuk · Kaplama · Ada · Eklentiler · Hakkında (WPF-UI NavigationView). |
| **Hızlı ayarlar flyout** | [`topbar.md`](topbar.md#hızlı-ayarlar-flyout) | `controlCenter`/ses/ağ ikonlarının paylaştığı tek popup: Main / Mixer / Network sekmeleri (ADR-006); Wi-Fi (canlı ağ listesi + şifre paneli), Bluetooth (cihaz listesi), Yansıt (4 mod), gece ışığı tile (gerçek durum + toggle), kaplama tile (renk overlay aç/kapa, `\uE790`; ADR-007), tile animasyonları. |
| **Parlaklık flyout** | [`topbar.md`](topbar.md#parlaklık-flyout) | Monitorian tarzı DDC/CI + WMI; master slider + monitör kartları. |
| **Medya flyout** | [`topbar.md`](topbar.md#medya-flyout) | SMTC şu an çalan: kapak, başlık/sanatçı, Oynat/Duraklat + İleri/Geri, çoklu oturum seçici; öğe yalnızca oturum aktifken görünür |

Ortak notlar:

- Durum öğeleri / seçenekler config'ten canlı uygulanır (`ApplyConfigChanges`).
- Sağ tık menüleri WPF-UI `BasedOn` stillerini kullanır (App.xaml; Aero2 ikon olukları ezilmez).
- v1 yalnızca **birincil monitör**; tüm pencereler Per-Monitor V2 DPI.