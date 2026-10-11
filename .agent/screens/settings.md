# Ekran: Ayarlar penceresi

`src/WharfBar/Views/SettingsWindow.xaml` + `Views/Pages/*`.

Açılış: top bar Windows logosu → "WharfBar Ayarları" (veya tepsi menüsü). WPF-UI
`NavigationView`, Mica, sol gezinme: **Genel · Görünüm · Dock · Üst Çubuk** + alt
**Hakkında**.

## Genel (`GeneralPage`)

- **Windows ile Başlat** — Startup klasörü `WharfBar.lnk` (Registry Run değil;
  `AutostartService`, config `autostart`)

## Görünüm (`AppearancePage`)

- **Vurgu Rengi Stili** — Sistem Vurgu Rengi / Sabit Tema Modu (config `useSystemAccentColor`)
- **Uygulama Teması** — Sistem varsayılanı / Açık / Koyu (config `theme`)
- **Üst Çubuk Arka Planı** — Mica (Bulanık) / Saydam (Transparent) / Renkli (Tint)
  (config `backgroundMode`) + **Opaklık** slider'ı 0–100 (yalnızca Tint'te etkin, config `tintOpacity`)

## Dock (`DockPage`)

- **İçerik listesi** — `dock.items` satırları (etiket + yol); `Ekle` (dosya seçici), `×` ile kaldır,
  `⋮⋮` tutamacından sürükleyerek sırala
- **Otomatik Gizle** toggle (config `dock.autoHide`)
- **Simge Boyutu** slider'ı 16–36 DIP (config `dock.iconSize`; varsayılan 24). Eski 28 varsayılanı yüklemede 24'e çekilir.
- **Arka Plan** ComboBox'u (config `dock.backgroundMode`: `mica` / `transparent` / `tint`; üst çubuktan bağımsız) + **Opaklık** 0–100 (yalnız `tint`, config `dock.tintOpacity`, varsayılan 40). Mica ↔ saydam/renkli geçişi dock penceresini yeniden kurar. Eski `dock.surface` (`mica`/`acrylic`) yüklemede `mica` olur.
- **Konum** ComboBox'u (config `dock.position`: Sol / Orta / Sağ; yalnız alt kenar)
- **Çalışan Uygulamaları Göster** toggle (config `dock.showRunningApps`; `false` iken sağdaki
  sabitlenmemiş çalışanlar hiç üretilmez)

## Üst Çubuk (`TopBarPage`)

- **Durum öğeleri kartları** — her öğe için onay kutusu (göster/gizle) + ▲▼ sıralama
  (yalnızca görünür öğelerde) + "Varsayılana Sıfırla" düğmesi (config `topBar.statusItems`)

## Hakkında (`AboutPage`)

- Sürüm / künye (`AboutWindow` ile eşdeğer içerik).

## Notlar

- Değişiklikler anında config'e yazılır (`ConfigService.Save`) ve top bar'a
  `ApplyConfigChanges` ile canlı uygulanır.
- Uzun sayfalar kendi `ScrollViewer`'ında kayar. Her `Page` kökünde
  `ScrollViewer.CanContentScroll="False"` zorunlu — aksi halde `NavigationView`
  sayfayı sonsuz yükseklikte ölçer ve tekerlek hiçbir şeyi kaydırmaz
  (`docs/04-gotchas.md`).
- Sayfalar `PowerToysCard` stilinde kartlar kullanır (Fluent `CardBackgroundFillColorDefaultBrush`).
- Kart başlık ikonları / Windows logosu accent renginde: `AccentFillColorDefaultBrush`
  (WPF-UI 4.3.0'un `SystemAccentColorPrimaryBrush`'ı sabit mavidir ve accent uygulanınca güncellenmez —
  accent rengi ikonlar için kullanmayın; detay: `docs/04-gotchas.md`).
- Durum öğesi listesi boşaltılamaz: son öğe gizlenirse `dateTime` otomatik eklenir.