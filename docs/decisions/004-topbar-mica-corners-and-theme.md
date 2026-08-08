# ADR-004: Top bar Mica, köşe tercihi ve tema

**Durum:** Kabul edildi  
**Tarih:** 2026-08-09  
**Bağlam:** Faz 3 — üst bar görselliği (backdrop, dark mode, köşe)

## Karar

### 1. Backdrop: WPF-UI önce, DWM yedek / ince ayar

Tema sözlükleri ve `WindowBackdrop.ApplyBackdrop` **WPF-UI** üzerinden uygulanır.
Ham `DWMWA_SYSTEMBACKDROP_TYPE` yalnızca WPF-UI çağrısı başarısız olursa veya HRESULT
başarı dönüp attribute gerçekten yazılmadığında kullanılır. İki katman **aynı anda**
backdrop tipini sürmez.

**Gerekçe:** Proje tercihi ve `docs/02-win32-appbar-dwm.md` sırası. Çift sürücü
çakışması flat tint veya tutarsız görünüme yol açar.

### 2. Köşeler: `DWMWCP_DONOTROUND` (üst bar)

Üst bar için `DwmCornerPreference.DoNotRound`. DWM köşe tercihini dört köşeye birden
uygular; kenar-kenara üst şeritte yuvarlak köşe macOS menu bar’a uymaz ve monitör
kenarlarında boşluk / “yüzen kart” hissi verir.

`Native/Dwm.cs` API’si korunur — Faz 4 floating dock yuvarlak köşe kullanabilir.

**Gerekçe:** Ürün referansı kare kenarlı menu bar; DWM per-corner kontrol sunmaz.

### 3. `WindowStyle=None` + frame extend; `WindowChrome` değil

30 DIP AppBar şeridi caption’lı pencerenin minimum track yüksekliğinin (~39 px) altındadır.
`WindowChrome` `WS_CAPTION` bırakır; `WM_WINDOWPOSCHANGING` içinde `cy=30` yazılsa bile
`DefWindowProc` yüksekliği tekrar ~39’a çeker → pencere work area’ya taşar.

Bu yüzden üst bar **`WindowStyle=None`** kullanır. Mica için glass bölgesi
`DwmExtendFrameIntoClientArea` (`MARGINS=-1`) + WPF-UI backdrop ile sağlanır.
`AllowsTransparency=true` **kullanılmaz**. Backdrop doğrulanınca arka plan `Transparent`
brush; başarısızsa opak fallback.

> Erken denemede yalnızca `WindowStyle=None` (extend/backdrop tam bağlanmadan) flat tint
> üretti; asıl gereksinim frame extend + sistem backdrop, caption değil.

### 4. Sistem light/dark + runtime yenileme

`ApplicationThemeManager` sistem temasını uygular. `SystemThemeWatcher` **bilinçli
kullanılmaz**: watcher varsayılan Mica’yı `MainWindow`’a yeniden basıp istenen backdrop’u
sessizce ezer. Bunun yerine `TopBarWindow` `WM_SETTINGCHANGE` (`ImmersiveColorSet`),
`WM_THEMECHANGED`, `WM_SYSCOLORCHANGE`, `WM_DWMCOLORIZATIONCOLORCHANGED` ile
`WindowAppearance.Refresh()` çağırır. `DWMWA_USE_IMMERSIVE_DARK_MODE` de aynı yolda
güncellenir (plain `Window` üzerinde WPF-UI her zaman yazmayabilir).

## Sonuçlar

- Üst bar tam 30 DIP / AppBar ile hizalı; kare köşeli.
- Mica yolu WPF-UI odaklı; DWM köşe + frame extend + fallback/dark attribute için.
- Tema değişimi watcher’sız, WndProc üzerinden.
