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

- **Otomatik Gizle** toggle (config `dock.autoHide`)
- **Simge Boyutu** slider'ı 16–36 DIP (config `dock.iconSize`; varsayılan 28)

## Üst Çubuk (`TopBarPage`)

- **Durum öğeleri kartları** — her öğe için onay kutusu (göster/gizle) + ▲▼ sıralama
  (yalnızca görünür öğelerde) + "Varsayılana Sıfırla" düğmesi (config `topBar.statusItems`)

## Hakkında (`AboutPage`)

- Sürüm / künye (`AboutWindow` ile eşdeğer içerik).

## Notlar

- Değişiklikler anında config'e yazılır (`ConfigService.Save`) ve top bar'a
  `ApplyConfigChanges` ile canlı uygulanır.
- Sayfalar `PowerToysCard` stilinde kartlar kullanır (Fluent `CardBackgroundFillColorDefaultBrush`).
- Kart başlık ikonları / Windows logosu accent renginde: `AccentFillColorDefaultBrush`
  (WPF-UI 4.3.0'un `SystemAccentColorPrimaryBrush`'ı sabit mavidir ve accent uygulanınca güncellenmez —
  accent rengi ikonlar için kullanmayın; detay: `docs/04-gotchas.md`).
- Durum öğesi listesi boşaltılamaz: son öğe gizlenirse `dateTime` otomatik eklenir.