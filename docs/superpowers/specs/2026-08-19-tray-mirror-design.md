# WharfBar — Üst bar tray aynası (ToolbarWindow32 enumeration)

**Tarih:** 2026-08-19
**Durum:** Uygulandı (main); 25H2 hibrit UIA fallback dahil — bkz. "Ek (25H2 uygulaması)"
**ADR:** [009-tray-mirror-toolbar-enumeration.md](../../decisions/009-tray-mirror-toolbar-enumeration.md)
**Kapsam:** Üçüncü taraf tepsi ikonlarının üst bar durum şeridinde salt-okunur aynası +
tıklama forward; ADR-005'in "yabancı tepsi ikonları çizilmez / click-forward yok" kararının
yeniden açılması (belgelenmiş comctl32 toolbar mesajları ile).

## Kararlar (özet)

| Konu | Seçim |
|------|--------|
| Kaynak | `Shell_TrayWnd` → `TrayNotifyWnd` → `SysPager` → **`ToolbarWindow32`** (ana tray alanı) |
| Enumeration | Belgelenmiş comctl32 mesajları: `TB_BUTTONCOUNT`, `TB_GETBUTTON`, `TB_GETIMAGELIST` + `ImageList_GetIcon`, `TB_GETITEMRECT`, `TB_GETBUTTONTEXT` |
| Yasaklar (değişmez) | `ReadProcessMemory`, explorer'a enjeksiyon/subclass, `Shell_NotifyIcon` ile okuma, belgelenmemiş API (Windhawk benzeri hook) — **Asla** |
| Sistem ikonu filtresi | Tooltip metninden bilinen sistem adları düşülür (tr/en: Ses/Volume, Ağ/Network, Pil/Battery, Saat/Clock, Giriş Dili/Input, Güvenlik/Security…); WharfBar'ın kendi Quit ikonu da düşülür |
| Gizli/overflow ikonlar | **Kapsam dışı** — Win11'de gizli ikonlar yalnızca açılan overflow popup'ında yaşar; kalıcı okunamaz. Mevcut `trayOverflow` düğmesi (`TrayOverflowInvoker` / `Win+B`) kalır |
| İkon güncelleme | Arka plan `System.Threading.Timer` 1.5 sn poll + snapshot diff; UI'a değişiklik olayı |
| Tıklama forward | `TB_GETITEMRECT` → item rect merkezi → `SendMessage(WM_LBUTTONDOWN/UP)` / `WM_RBUTTONDOWN/UP` / çift tık — explorer'ın kendi tray subclass'ı işler; görev çubuğu gizliyken çalışır, peek-show/SendInput gerekmez |
| Tooltip | `TB_GETBUTTONTEXT`; WPF tooltip |
| UI | Yeni durum şeridi katalog öğesi `trayMirror` (varsayılan görünür; `trayOverflow`'un soluna, sağ tık menüsünden gizle/göster + sıralama — mevcut katalog mekanizması) |
| Config | `schemaVersion` değişmez; katalog **v5** (`trayMirror` soft eklenir) |
| Tepsi ikon boyutu | Win11 native 20px (DPI-keskin `BitmapSource`; dock ikon akışı deseni) |

## Hedefler

1. WharfBar görev çubuğunu gizlerken üçüncü taraf tepsi ikonları üst barda **canlı** görünsün
   (sistem ikonları hariç — durum şeridi zaten simüle ediyor).
2. Tıklama davranışı birebir: sol tık uygulamanın normal işlemini, sağ tık bağlam menüsünü, çift
   tık ikincil eylemi tetiklesin.
3. Shell durumu hiçbir yolda bozulmasın: salt-okunur ayna; çıkışta ekstra restore yok.

## Non-goals

- Gizli/overflow ikonların kalıcı aynası (teknik olarak imkânsız; `trayOverflow` düğmesi korunur)
- `ReadProcessMemory`, enjeksiyon, undocumented hook (Asla kategorisi sabit)
- Tray ikon düzeni/sırasını değiştirmek (salt-okunur; sürükle-bırak yok)
- İkon güncellemelerinde olay bazlı bildirim (polling yeterli; API yok)
- Sistem ikonlarının aynası (çift görünüm istemiyoruz)
- İkincil monitör

## Mimari

```
App.OnStartup (mevcut akış)
  ├─ TrayMirrorService (TopBarWindow.OnSourceInitialized'ta kurulur — StatusService deseni)
  │    ├─ TrayToolbarLocator      # Shell_TrayWnd→TrayNotifyWnd→SysPager→ToolbarWindow32 HWND
  │    │                          # (FindWindow/FindWindowEx; TaskbarCreated sonrası yeniden bul)
  │    ├─ TrayButtonsReader       # TB_* mesajları → TrayButtonInfo listesi (saf record)
  │    ├─ systemFilter            # tooltip → sistem ikonu eşleme (tr/en; test edilebilir saf fonksiyon)
  │    └─ 1.5 sn System.Threading.Timer poll → TraySnapshotChanged olayı (fark: eklenen/kaldırılan/değişen)
  ├─ TopBarWindow
  │    ├─ StatusStrip → yeni öğe `trayMirror`
  │    │    ├─ IconHost (ItemsControl: 20px Image + tooltip)
  │    │    └─ TrayClickForwarder  # item rect → SendMessage mouse mesajları
  │    └─ trayOverflow (mevcut; kalır)
  └─ TrayIconService (kendi Quit ikonu; değişmez)
```

Yeni servisler `Services/`; ham P/Invoke `Native/TrayMirrorNative.cs` (veya `Native/TrayToolbar.cs`)
içinde `internal` kalır; UI `NativeMethods`'a doğrudan bağlanmaz (mevcut kural).

### Enumeration detayı

1. `FindWindow("Shell_TrayWnd", …)` → `FindWindowEx(…, "TrayNotifyWnd")` → `FindWindowEx(…, "SysPager")` → `FindWindowEx(…, "ToolbarWindow32")`. HWND bulunamazsa (explorer restart penceresi) retry; `TaskbarCreated` alınınca yeniden.
2. `SendMessage(hwnd, TB_BUTTONCOUNT, 0, 0)` → N.
3. Her buton için `TB_GETBUTTON` (index wParam; dönen `TBBUTTON` → `idCommand` + `iBitmap`) ve `TB_GETBUTTONTEXT` (command id wParam; tooltip).
4. İkon: `TB_GETIMAGELIST` → `ImageList_GetIcon(himl, iImage, ILD_TRANSPARENT)` → HICON → `BitmapSource` (32bpp; `Freeze()`). HICON `DestroyIcon` ile serbest bırakılır (her poll'da sızıntı yok).
5. İkon boyutu: `ImageList_GetIconSize` → Win11 20px; DPI ölçekli çizim (Per-Monitor V2).

### Filtre

- `TrayButtonInfo.Tooltip` normalize edilir (küçük harf, sözcük eşleşmesi).
- Sistem adları seti (tr/en + kaynak sabitleri): ses, volume, ağ, network, internet, pil,
  battery, saat, clock, tarih, date, giriş dili, input, güvenlik, security, gizli ikonları
  göster, show hidden icons, görev çubuğu, taskbar, bildirim, notification.
  Filtre yalnızca **bilinen sabit set** üzerinde çalışır; yanlış filtre → ikon gizli kalır,
  veri kaybı yok (yalnızca görünüm).
- WharfBar kendi ikonu: `Shell_NotifyIcon` eklerken kullandığı benzersiz tooltip (örn. "WharfBar")
  setten ayrıca düşülür.
- Sınır: yalnızca **bilinen** sistem adları düşülür; tanınmayan ikon gösterilir.

### Tıklama forward

- `TB_GETITEMRECT(hwnd, index, &rect)` (client coords) → merkez noktası.
- Sol tık: `SendMessage(hwnd, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(x,y))` + `WM_LBUTTONUP`.
- Sağ tık: `WM_RBUTTONDOWN/UP` (explorer'ın tray subclass'ı uygulamanın `WM_CONTEXTMENU`
  callback'ini tetikler → bağlam menüsü).
- Çift tık: `WM_LBUTTONDBLCLK` + `WM_LBUTTONUP`.
- Modifier (Ctrl/Shift/Alt) `SendMessage` wParam'ına eklenir (toolbar aktif iken daha da
  gerçekçi).
- Çalışmazsa (dönüş değeri/etki yok) debug log; UI no-op — kırılganlık sessiz degrade.

### UI

- `trayMirror` öğesi: sabit ~24px genişlik aralığında ikon sırası; `ItemsControl` (Virtualizing
  gerekmez — N < 20).
- Tooltip: WPF tooltip (poll'da güncellenen metin).
- Erişilebilirlik: her ikon `AutomationProperties.Name` = tooltip.
- Boş listeyse öğe gizlenir (dinamik görünürlük — Battery/Volume deseni).
- Görsel arka plan: diğer durum öğeleriyle aynı hover deseni.

## Config etkisi

| Alan | Değişiklik |
|------|------------|
| `schemaVersion` | 1 kalır (breaking yok) |
| `topBar.statusItems` | Katalog **v5**: `trayMirror` `trayOverflow`'un hemen soluna soft eklenir (`EnsureTrayMirror`); kullanıcı gizlerse "hide must stick" |

## Riskler

| Risk | Azaltma |
|------|---------|
| Windows güncellemesi tray HWND yapısını değiştirir | Locator bulamazsa retry + sessiz gizle (öğe Collapsed); HWND yolu Fallback: `TrayNotifyWnd` doğrudan toolbar arama |
| İkon değişikliği poll aralığında kaçar | 1.5 sn yeterli (bildirimler anlık olmaz; kabul) |
| `TB_*` mesajları gelecekte davranış değiştirir | Salt-okunur + koordinat-mesaj click; bozulursa debug log + no-op |
| HICON sızıntısı (her poll) | `DestroyIcon`; snapshot diff yalnızca değişenleri yeniden çözer |
| Yanlış sistem filtresi | Yalnızca bilinen sabit set; kayıp yalnızca görünümde |
| Tıklama forward explorer'da yan etki | `SendMessage` senkron + explorer'ın kendi işleyişi; işlem dışı sürece mesaj yok |
| Explorer restart | `TaskbarCreated` sonrası yeniden enumerate (AppBar yeniden kayıt deseni) |

## Test / doğrulama notları

Birim (xUnit — tray'den bağımsız saf kısımlar):

1. `systemFilter`: tr/en tooltip külliyatı → doğru düşme; bilinmeyen korunur.
2. Tooltip normalizasyon (boşluk/büyük harf/önek sökme).
3. Snapshot diff: eklenen/kaldırılan/değişen butonları doğru raporlar.
4. HICON→BitmapSource dönüşümü + `DestroyIcon` dengeleme (ölçülemez, kod inceleme).
5. `TrayButtonInfo` sıralama/kimlik (idCommand + tooltip) kararlılığı.

Manuel:

1. Başlat → üçüncü taraf tepsi ikonları üst barda görünür; sistem ikonları yok.
2. Sol tık → uygulama aksiyonu; sağ tık → bağlam menüsü; çift tık → ikincil eylem.
3. İkon ekleme/kaldırma (uygulama kur/kaldır, `Shell_NotifyIcon` gönderen demo) → ≤1.5 sn içinde bar güncellenir.
4. Rozet/bildirim güncellemesi (ör. mesajlaşma uygulaması) → ikon görseli güncellenir.
5. Tray ikonu olan bir uygulamayı kapat → ikon kaybolur.
6. Explorer restart → ayna yeniden başlar; çökme yok.
7. Görev çubuğu gizliyken ikonlar tıklanabilir; sağ tık menüsü açılır.
8. Katalog: `trayMirror` gizlenebilir/sıralanabilir; mevcut config'ler kırılmaz.
9. WharfBar'ın kendi tepsi Quit ikonu aynada görünmez.

## Doküman güncellemeleri (bu değişiklikle)

- Yeni ADR-009 (ADR-005'i "yabancı tepsi ikonları" maddesinde günceller)
- `.agent/boundaries.md` ("Asla" daraltılır: RPM/enjeksiyon sabit; belgelenmiş comctl32 mesajları ile
  salt-okunur ayna "Yap"a taşınır), `.agent/project.md`, `.agent/context/roadmap.md`
- `docs/00-overview.md` (non-goals), `docs/02-win32-appbar-dwm.md` (tray bölümü),
  `docs/03-development-order.md`, `docs/04-gotchas.md`

## Ek (25H2 uygulaması): UIA fallback

**Durum güncellemesi (2026-08-19, uygulama sonrası):** Windows 11 25H2'de klasik `ToolbarWindow32`
tepsi kaldırıldı (`TrayNotifyWnd` altında `SysPager`/`ToolbarWindow32` yok — ikonlar XAML
composition ağacında; HWND/`TB_*` ile erişilemez). Uygulama hibritleşti:

| Konu | Seçim |
|------|--------|
| Kaynak seçimi | `FindToolbarHwnd()` doluysa TB_* (gerçek ikonlar; <25H2); boşsa **UIA fallback** |
| UIA okuma | `Shell_TrayWnd → AutomationElement` → `FindAll(Descendants, ClassName=="SystemTray.NormalButton")` → (ad, element). Sistem ikonları farklı class'larda (`AccentButton`/`OmniButton*`/`ShowDesktopButton`); giriş göstergesi tooltip filtreyle elenir (`IsSystemIcon` + `IsWharfBarIcon`) |
| İkon | UIA'da bitmap yok → `TrayCapture` `PrintWindow(PW_RENDERFULLCONTENT)` ile park+cloak'lı taskbar'ı çizer, butonun `Image` çocuk rect'ini kırpar (gerçek ikon, flash yok); yakalama başarısızsa `TrayLetterGlyph` (ilk harf, 20px) fallback |
| UIA görünürlüğü | UIA çocukları yalnızca taskbar `SW_SHOW` iken açığa çıkar → her poll `BeginTrayPeek(stealth:true)` (cloak + ekran dışı park, flash yok) → oku → `EndTrayPeek()` |
| Sol tık | UIA `InvokePattern.Invoke()` (stealth-peek içinde; flash yok) |
| Sağ/çift tık | UIA ifade edemez → görünür peek + `SendInputMouse` fiziksel tıklama (kısa flash; overflow deseniyle aynı; kabul edilen ödünleşim) |
| Filtre token'ları | `TraySystemIconFilter`'a `tepsi girişi`, `input indicator` eklendi |

Doğrulama (25H2, manuel): 12 üçüncü taraf ikonu gerçek piksellerle yakalanıp görünür (16x16
sıkı kırpma, boş değil); sol tık açar; sağ tık menüsü çıkar (taskbar kısa görünür); sistem
ikonları filtrelenir.

## Self-review (2026-08-19)

- Placeholder yok; tüm teknik yol seçili (TB_* mesajları + koordinat SendMessage).
- ADR-009 + boundaries ile tutarlı: RPM/enjeksiyon/undocumented API hâlâ Asla; yalnızca
  belgelenmiş comctl32 mesaj yüzeyi açılıyor.
- Kapsam onaya uyuyor: sistem ikonları filtrelenir; overflow düğmesi kalır; `trayMirror` katalog öğesi.
- Risk tablosu gerçek; sessiz degrade disiplini mevcut gotcha kültürüyle uyumlu.
