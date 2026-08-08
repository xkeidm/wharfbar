# ADR-003: Top bar yerleşimi ve AppBar yaşam döngüsü

**Durum:** Kabul edildi  
**Tarih:** 2026-08-08  
**Bağlam:** Faz 2 — `TopBarWindow` konumlandırma, `ABM_REMOVE` garantisi, fullscreen davranışı

## Karar

### 1. Yerleşim fiziksel pikselle, `SetWindowPos` üzerinden

WPF’in `Window.Left/Top/Width/Height` özellikleri **kullanılmaz**. Bar rect’i
`GetDpiForWindow` + `GetMonitorInfo` ile fiziksel piksel olarak hesaplanır ve `SetWindowPos`
ile uygulanır.

**Gerekçe:** WPF’in DIP koordinatları birincil monitörün ölçeğine göre yorumlanır. Per-Monitor
V2 altında ölçeği %100 olmayan bir kurulumda bar yanlış konumlanır veya yanlış yükseklikte olur.
AppBar API’si zaten fiziksel piksel konuşuyor; iki koordinat sistemi arasında gidip gelmek
yerine tek sistemde kalınır.

### 2. `ABM_REMOVE` idempotent + çok yollu

`AppBar.Remove()` `Interlocked.Exchange` ile korunur; yalnızca ilk çağrı shell’e ulaşır.
Bağlandığı yollar: `Window.OnClosing`, `Application.Exit`, `Application.SessionEnding`,
`AppDomain.ProcessExit`, `DispatcherUnhandledException`, `AppDomain.UnhandledException`.

**Gerekçe:** Kaçırılan bir `ABM_REMOVE` kullanıcının ekranında asılı bir şerit bırakır ve
`explorer.exe` yeniden başlatmayı gerektirir (`docs/04-gotchas.md`). Tek bir “doğru” çıkış yolu
seçip ona güvenmek yerine hepsi bağlanır; idempotentlik çakışmayı zararsız kılar.

`Remove()` çağrıldıktan sonra `Register()` reddedilir. Bu, kapanış sırasında gelen geç bir
`TaskbarCreated` mesajının AppBar’ı yeniden kaydetmesini engeller.

### 3. Explorer restart’ta yeniden kayıt: `TaskbarCreated`

`RegisterWindowMessage("TaskbarCreated")` dinlenir; mesaj gelince `ABM_NEW` tekrar çalıştırılır.
Önce `ABM_REMOVE` **yapılmaz** — eski kayıt ölen shell örneğiyle birlikte gitmiştir.

**Gerekçe:** Periyodik sağlık kontrolüne göre olay tabanlı, anlık ve bedava.

### 4. Fullscreen: gizleme değil, z-order düşürme

`ABN_FULLSCREENAPP` geldiğinde pencere `HWND_BOTTOM`’a alınır; bittiğinde `HWND_TOPMOST`’a
döner. Pencere `Hide()` edilmez ve **AppBar kaydı korunur**.

**Gerekçe:** Kaydı bırakıp geri almak work area’yı iki kez değiştirir; bu da masaüstü ikonlarının
ve maximize pencerelerin her tam ekran geçişinde yeniden düzenlenmesine yol açar. Z-order
değişimi tam ekran uygulamayı serbest bırakmak için yeterli ve görünür yan etkisi yok.
Bu, görev çubuğunun kendi davranışıyla da tutarlıdır.

### 5. `DockWindow` Faz 1’e alınmadı

`docs/03-development-order.md` Faz 1’de `DockWindow`’u da anıyordu; Faz 4’e ertelendi.

**Gerekçe:** Boş bir dock penceresi ölü iskele olurdu; hit-testing ve auto-hide kararları
(ADR-001) dock içeriğiyle birlikte tasarlanmalı.

## Sonuçlar

- Faz 3’te backdrop eklenirken `SetWindowPos` tabanlı yerleşime dokunulmamalı.
- Çoklu monitör desteği (v1 dışı) `Display.GetPrimaryMonitorBounds` noktasından genişler.
- Süreç zorla öldürülürse (`Stop-Process -Force`, güç kesintisi) hiçbir yönetilen çıkış yolu
  çalışmaz; bu senaryoda asılı work area kaçınılmazdır ve çözümü explorer restart’tır.
  README ve `AGENTS.md` bu uyarıyı içerir.

## Alternatifler (reddedilen)

1. **WPF `Left/Top` + `SystemParameters`** — DPI ölçeklemesinde kırılır.
2. **Yalnızca `OnClosing`’de `ABM_REMOVE`** — çöküş ve oturum kapatma yollarını kaçırır.
3. **Fullscreen’de `ABM_REMOVE` + sonra `ABM_NEW`** — work area çalkalanması, ikon kayması.
4. **Periyodik timer ile AppBar sağlık kontrolü** — `TaskbarCreated` varken gereksiz.
