# Top-bar Background Personalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Üst bar arka planını kullanıcı seçebilir yap: `mica` / `transparent` / `tint` + 0–100 canlı kaydırıcı; sağ tık menüden.

**Architecture:** İki appearance boru hattı. `mica` → mevcut `WindowAppearance` Mica + `AllowsTransparency=false`. `transparent`/`tint` → Mica yok, `AllowsTransparency=true`, brush alpha. Tint kaydırıcısı aynı hwnd’de brush günceller. Mica ↔ translucent geçişi TopBar penceresini AppBar-güvenli yeniden oluşturur (görev çubuğu restore **edilmez**). Config soft alanlar; `schemaVersion` 1.

**Tech Stack:** .NET 8 WPF, WPF-UI, mevcut `WindowAppearance` / `AppBar` / `ConfigService`; yeni NuGet yok.

## Global Constraints

- Mica + `AllowsTransparency=true` yasak (ADR-004 / boundaries)
- Çıkışta `ABM_REMOVE` + görev çubuğu restore; **mod recreate** sırasında yalnızca `ABM_REMOVE`, taskbar restore yok
- P/Invoke yalnızca `Native/` `internal`
- `Views/` tercih; `global.json` yok
- v1: yalnız üst bar arka planı; dock / hex / Acrylic ayrı mod yok
- Commit yalnızca kullanıcı isterse
- Derleme: `dotnet build WharfBar.sln` (`dotnet run` AppBar riski — manuel doğrulamada dikkat)

---

## File map

| File | Role |
|------|------|
| `src/WharfBar/Models/AppConfig.cs` | `TopBarConfig.BackgroundMode`, `TintOpacity` + sabitler |
| `src/WharfBar/Models/TopBarBackgroundModes.cs` | Mod sabitleri + `NormalizeMode` / `ClampOpacity` |
| `src/WharfBar/Services/ConfigService.cs` | Load normalize + `CreateDefault` |
| `src/WharfBar/Services/WindowAppearance.cs` | Translucent yolu: backdrop off + tint/transparent brush; Mica yolu korunur |
| `src/WharfBar/Views/TopBarWindow.xaml` | `AllowsTransparency` XAML’den kaldır (ctor’da set) |
| `src/WharfBar/Views/TopBarWindow.xaml.cs` | Apply background; menü UI; recreate isteği |
| `src/WharfBar/App.xaml.cs` | `RecreateTopBar` (MainWindow swap; taskbar servisi paylaşılır) |
| `docs/04-gotchas.md` | Translucent / recreate notu |
| `docs/01-architecture.md` | `topBar.backgroundMode` örneği |
| `.agent/project.md` | Kısa ürün notu |
| Spec: `docs/superpowers/specs/2026-08-09-topbar-background-personalization-design.md` | Plan linki |

---

### Task 1: Config alanları + normalize

**Files:**
- Create: `src/WharfBar/Models/TopBarBackgroundModes.cs`
- Modify: `src/WharfBar/Models/AppConfig.cs`
- Modify: `src/WharfBar/Services/ConfigService.cs`

**Interfaces:**
- Produces: `TopBarBackgroundModes.Mica = "mica"`, `Transparent = "transparent"`, `Tint = "tint"`
- Produces: `TopBarBackgroundModes.DefaultOpacity = 40`
- Produces: `string NormalizeMode(string? mode)` → bilinmeyen/`null` → `mica`
- Produces: `int ClampOpacity(int value)` → 0–100
- Produces: `TopBarConfig.BackgroundMode` (string, default `mica`), `TintOpacity` (int, default 40)
- Produces: `bool NeedsTransparency(string mode)` → true iff transparent veya tint

- [ ] **Step 1: Add `TopBarBackgroundModes`**

```csharp
namespace WharfBar.Models;

internal static class TopBarBackgroundModes
{
    internal const string Mica = "mica";
    internal const string Transparent = "transparent";
    internal const string Tint = "tint";
    internal const int DefaultOpacity = 40;
    internal const int MinOpacity = 0;
    internal const int MaxOpacity = 100;

    internal static string NormalizeMode(string? mode)
    {
        if (string.Equals(mode, Transparent, StringComparison.OrdinalIgnoreCase))
        {
            return Transparent;
        }

        if (string.Equals(mode, Tint, StringComparison.OrdinalIgnoreCase))
        {
            return Tint;
        }

        return Mica;
    }

    internal static int ClampOpacity(int value) =>
        Math.Clamp(value, MinOpacity, MaxOpacity);

    internal static bool NeedsTransparency(string mode)
    {
        mode = NormalizeMode(mode);
        return mode is Transparent or Tint;
    }
}
```

- [ ] **Step 2: Extend `TopBarConfig` in `AppConfig.cs`**

```csharp
/// <summary><c>mica</c>, <c>transparent</c>, or <c>tint</c>.</summary>
public string BackgroundMode { get; set; } = TopBarBackgroundModes.Mica;

/// <summary>0–100; used only when <see cref="BackgroundMode"/> is <c>tint</c>.</summary>
public int TintOpacity { get; set; } = TopBarBackgroundModes.DefaultOpacity;
```

- [ ] **Step 3: Normalize in `ConfigService`**

In `CreateDefault()` TopBar init, set:

```csharp
BackgroundMode = TopBarBackgroundModes.Mica,
TintOpacity = TopBarBackgroundModes.DefaultOpacity,
```

In `Normalize`, after `config.TopBar ??= new TopBarConfig()`:

```csharp
config.TopBar.BackgroundMode = TopBarBackgroundModes.NormalizeMode(config.TopBar.BackgroundMode);
config.TopBar.TintOpacity = TopBarBackgroundModes.ClampOpacity(config.TopBar.TintOpacity);
```

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 error

- [ ] **Step 5: Commit** (yalnızca kullanıcı isterse)

```bash
git add src/WharfBar/Models/TopBarBackgroundModes.cs src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs
git commit -m "feat(topbar): add backgroundMode and tintOpacity config."
```

---

### Task 2: WindowAppearance — translucent boru hattı

**Files:**
- Modify: `src/WharfBar/Services/WindowAppearance.cs`

**Interfaces:**
- Consumes: `TopBarBackgroundModes`, `AppThemePreference`
- Produces: `ApplyMica()` — mevcut `Apply()` davranışı (dock için `Apply()` alias kalabilir)
- Produces: `ApplyTranslucent(int? tintOpacity)` — backdrop None; `tintOpacity` null → tam şeffaf; aksi halde tema rengi + alpha
- Produces: `ClearSystemBackdrop()` — DWM None + frame extend iptali / güvenli temizleme
- Dock: mevcut `new WindowAppearance(..., Mica, ...)` + `Apply()` **değişmeden** çalışır

- [ ] **Step 1: Keep `Apply()` as Mica path; add translucent API**

Refactor so `Apply()` remains the Mica/dock path (same body as today).

Add:

```csharp
/// <summary>
/// No system backdrop. Window must already have AllowsTransparency=true.
/// <paramref name="tintOpacity"/> null = fully clear; else 0–100 theme tint.
/// </summary>
internal void ApplyTranslucent(int? tintOpacity)
{
    Dwm.SetCornerPreference(_hwnd, _corners);

    ApplicationTheme theme = AppThemePreference.Resolve();
    ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: false);
    Dwm.SetImmersiveDarkMode(_hwnd, theme == ApplicationTheme.Dark);

    ClearSystemBackdrop();
    IsBackdropActive = false;

    if (tintOpacity is null)
    {
        _window.SetCurrentValue(Control.BackgroundProperty, Brushes.Transparent);
        return;
    }

    int opacity = TopBarBackgroundModes.ClampOpacity(tintOpacity.Value);
    byte a = (byte)(int)Math.Round(opacity / 100.0 * 255.0);
    Color baseColor = theme == ApplicationTheme.Dark
        ? Color.FromRgb(0x20, 0x20, 0x20)
        : Color.FromRgb(0xFA, 0xFA, 0xFA);
    var brush = new SolidColorBrush(Color.FromArgb(a, baseColor.R, baseColor.G, baseColor.B));
    brush.Freeze();
    _window.SetCurrentValue(Control.BackgroundProperty, brush);
}

private void ClearSystemBackdrop()
{
    if (_hwnd == IntPtr.Zero)
    {
        return;
    }

    Dwm.SetSystemBackdrop(_hwnd, DwmSystemBackdrop.None);
    // Do not call WindowBackdrop.RemoveBackground here for translucent — we own the brush.
}
```

Theme colors match existing `ApplyFallbackBackground` RGB values so tint “mevcut mod” ile uyumlu.

- [ ] **Step 2: Ensure Mica `Apply()` never runs with translucent brush left over**

At start of successful Mica `TryApplyBackdrop` path, existing `RemoveBackground` stays. On Mica fallback, `ApplyFallbackBackground` stays opaque.

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 error

- [ ] **Step 4: Commit** (kullanıcı isterse)

```bash
git add src/WharfBar/Services/WindowAppearance.cs
git commit -m "feat(appearance): add translucent top-bar background path."
```

---

### Task 3: TopBar apply + App recreate (Mica ↔ translucent)

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml` — remove any future AllowsTransparency attribute dependency; comment update
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`
- Modify: `src/WharfBar/App.xaml.cs`

**Interfaces:**
- Consumes: `TopBarBackgroundModes`, `WindowAppearance.Apply` / `ApplyTranslucent`
- Produces: `TopBarWindow` ctor sets `AllowsTransparency` from config **before** `InitializeComponent` is too late for some WPF cases — set immediately after `InitializeComponent` is **wrong**; set in ctor **before** `InitializeComponent()`:

```csharp
AllowsTransparency = TopBarBackgroundModes.NeedsTransparency(
    config.Current.TopBar.BackgroundMode);
InitializeComponent();
```

- Produces: `ApplyBackgroundAppearance()` — reads config; mica → `_appearance.Apply()`; transparent → `ApplyTranslucent(null)`; tint → `ApplyTranslucent(opacity)`
- Produces: `RequestBackgroundModeChange(string mode)` — persist; if transparency need flipped vs current `AllowsTransparency`, call App recreate; else `ApplyBackgroundAppearance()`
- Produces: `App.RecreateTopBar()` — **does not** restore taskbar
- Produces: `TopBarWindow.PrepareForRecreate()` — AppBar `Remove` only; dispose status/volume/mixer/quick; **do not** call `_taskbar.Restore()`
- Produces: `_isRecreateClose` — `OnClosing` skips `ReleaseShellState` entirely (AppBar zaten kaldırıldı). `ReleaseShellState` her zaman taskbar restore eder (Quit yolu). Eski pencerenin `Exit`/`ProcessExit` handler’ları `PrepareForRecreate` içinde **unhook** edilir (aksi halde ölü pencere Exit’te çift restore / sızıntı)

**Critical recreate sequence:**

```
1. Persist new BackgroundMode / TintOpacity
2. old.PrepareForRecreate()  // ABM_REMOVE; unhook shutdown; no taskbar restore
3. var next = new TopBarWindow(_taskbar, _config);
4. Application.Current.MainWindow = next;
5. next.Show();
6. old.Close() with _isRecreateClose → OnClosing skips ReleaseShellState
7. Dock untouched; TaskbarVisibilityService instance shared
```

- [ ] **Step 1: Ctor AllowsTransparency + ApplyBackgroundAppearance**

In ctor, before `InitializeComponent()`:

```csharp
AllowsTransparency = TopBarBackgroundModes.NeedsTransparency(
    _config.Current.TopBar?.BackgroundMode);
```

Replace fixed Mica-only apply in `OnSourceInitialized` with:

```csharp
_appearance = new WindowAppearance(this, _source.Handle, Backdrop, Corners);
ApplyBackgroundAppearance();
```

```csharp
private void ApplyBackgroundAppearance()
{
    if (_appearance is null)
    {
        return;
    }

    TopBarConfig top = _config.Current.TopBar ?? new TopBarConfig();
    string mode = TopBarBackgroundModes.NormalizeMode(top.BackgroundMode);
    switch (mode)
    {
        case TopBarBackgroundModes.Transparent:
            _appearance.ApplyTranslucent(null);
            break;
        case TopBarBackgroundModes.Tint:
            _appearance.ApplyTranslucent(TopBarBackgroundModes.ClampOpacity(top.TintOpacity));
            break;
        default:
            _appearance.Apply();
            break;
    }
}
```

Theme refresh (`Refresh` / WndProc theme messages): call `ApplyBackgroundAppearance()` instead of bare `_appearance.Refresh()` so tint tracks theme.

- [ ] **Step 2: Recreate plumbing**

Refactor `HookShutdownPaths` to store delegates so they can be removed. Add to `TopBarWindow`:

```csharp
private bool _isRecreateClose;
private EventHandler? _onAppExit;
private SessionEndingCancelEventHandler? _onSessionEnding;
private DispatcherUnhandledExceptionEventHandler? _onDispatcherUnhandled;
private EventHandler? _onProcessExit;
private UnhandledExceptionEventHandler? _onUnhandledException;

internal void PrepareForRecreate()
{
    _isRecreateClose = true;
    UnhookShutdownPaths();
    CloseFlyouts();
    QuickSettingsHost.Detach();
    _status?.Dispose();
    _status = null;
    _mixer?.Dispose();
    _mixer = null;
    _quick?.Dispose();
    _quick = null;
    _volume?.Dispose();
    _volume = null;
    _appBar?.Remove();
}

protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
{
    if (_isRecreateClose)
    {
        // Services + AppBar already released in PrepareForRecreate; never restore taskbar here.
        base.OnClosing(e);
        return;
    }

    CloseFlyouts();
    QuickSettingsHost.Detach();
    _status?.Dispose();
    _status = null;
    _mixer?.Dispose();
    _mixer = null;
    _quick?.Dispose();
    _quick = null;
    _volume?.Dispose();
    _volume = null;

    ReleaseShellState(); // taskbar.Restore + AppBar.Remove
    base.OnClosing(e);
}

private void ReleaseShellState()
{
    _taskbar.Restore();
    _appBar?.Remove();
}
```

Add to `App`:

```csharp
internal void RecreateTopBar()
{
    if (_config is null || _taskbar is null)
    {
        return;
    }

    if (MainWindow is not TopBarWindow old)
    {
        return;
    }

    old.PrepareForRecreate();
    var next = new TopBarWindow(_taskbar, _config);
    MainWindow = next;
    next.Show();
    old.Close();
}
```

Call site: `((App)Application.Current).RecreateTopBar();`

- [ ] **Step 3: Persist helpers**

```csharp
private void PersistBackground(string mode, int? opacity = null)
{
    AppConfig cfg = _config.Current;
    cfg.TopBar ??= new TopBarConfig();
    cfg.TopBar.BackgroundMode = TopBarBackgroundModes.NormalizeMode(mode);
    if (opacity is not null)
    {
        cfg.TopBar.TintOpacity = TopBarBackgroundModes.ClampOpacity(opacity.Value);
    }

    _config.Save(cfg);
}

private void SetBackgroundMode(string mode)
{
    string normalized = TopBarBackgroundModes.NormalizeMode(mode);
    bool needTx = TopBarBackgroundModes.NeedsTransparency(normalized);
    PersistBackground(normalized);

    if (needTx != AllowsTransparency)
    {
        ((App)Application.Current).RecreateTopBar();
        return;
    }

    ApplyBackgroundAppearance();
}

private void SetTintOpacity(int opacity)
{
    PersistBackground(TopBarBackgroundModes.Tint, opacity);
    if (AllowsTransparency
        && TopBarBackgroundModes.NormalizeMode(_config.Current.TopBar.BackgroundMode)
           == TopBarBackgroundModes.Tint)
    {
        _appearance?.ApplyTranslucent(TopBarBackgroundModes.ClampOpacity(opacity));
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 error

- [ ] **Step 5: Commit** (kullanıcı isterse)

```bash
git add src/WharfBar/Views/TopBarWindow.xaml src/WharfBar/Views/TopBarWindow.xaml.cs src/WharfBar/App.xaml.cs
git commit -m "feat(topbar): apply mica/translucent backgrounds with safe recreate."
```

---

### Task 4: Sağ tık menü — Arka plan bölümü + kaydırıcı

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` — `BuildStatusItemsMenu`

**Interfaces:**
- Consumes: `SetBackgroundMode`, `SetTintOpacity`
- Produces: Menüde “Arka plan” header + 3 radio `MenuItem` + `Slider` (0–100) + yüzde `TextBlock`/`MenuItem.Header` güncellemesi

- [ ] **Step 1: Insert background section in `BuildStatusItemsMenu`**

After status check items (before move/reset block), add:

```csharp
menu.Items.Add(new Separator());

menu.Items.Add(new System.Windows.Controls.MenuItem
{
    Header = "Arka plan",
    IsEnabled = false,
});

string mode = TopBarBackgroundModes.NormalizeMode(_config.Current.TopBar.BackgroundMode);
int opacity = TopBarBackgroundModes.ClampOpacity(_config.Current.TopBar.TintOpacity);

AddBackgroundRadio(menu, "Mica", TopBarBackgroundModes.Mica, mode);
AddBackgroundRadio(menu, "Transparent", TopBarBackgroundModes.Transparent, mode);
AddBackgroundRadio(menu, "Tint", TopBarBackgroundModes.Tint, mode);

var opacityHost = new System.Windows.Controls.MenuItem
{
    StaysOpenOnClick = true,
    IsEnabled = mode == TopBarBackgroundModes.Tint,
};
var row = new StackPanel { Orientation = Orientation.Horizontal };
row.Children.Add(new TextBlock
{
    Text = $"Opaklık {opacity}%",
    VerticalAlignment = VerticalAlignment.Center,
    Margin = new Thickness(0, 0, 8, 0),
    Width = 88,
});
var label = (TextBlock)row.Children[0];
var slider = new Slider
{
    Minimum = 0,
    Maximum = 100,
    Width = 120,
    Value = opacity,
    IsSnapToTickEnabled = true,
    TickFrequency = 1,
    VerticalAlignment = VerticalAlignment.Center,
};
slider.ValueChanged += (_, args) =>
{
    int v = (int)Math.Round(args.NewValue);
    label.Text = $"Opaklık {v}%";
    SetTintOpacity(v);
};
row.Children.Add(slider);
opacityHost.Header = row;
menu.Items.Add(opacityHost);
```

`AddBackgroundRadio`:

```csharp
private void AddBackgroundRadio(ContextMenu menu, string header, string mode, string current)
{
    var item = new System.Windows.Controls.MenuItem
    {
        Header = header,
        IsCheckable = true,
        IsChecked = current == mode,
        StaysOpenOnClick = true,
    };
    item.Click += (_, _) =>
    {
        SetBackgroundMode(mode);
        // Re-open optional; simplest: close menu (default) — recreate will dismiss anyway
    };
    menu.Items.Add(item);
}
```

Keep “Varsayılana sıfırla” **status-only** (do not reset background).

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 error

- [ ] **Step 3: Manual smoke (AppBar dikkat)**

1. Varsayılan Mica  
2. Transparent — masaüstü görünür  
3. Tint — kaydırıcı canlı; restart aynı  
4. Mica’ya dönüş — AppBar asılı değil; taskbar hâlâ gizli (WharfBar çalışırken)  
5. Quit — taskbar restore + ABM_REMOVE  

- [ ] **Step 4: Commit** (kullanıcı isterse)

```bash
git add src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "feat(topbar): add background controls to status context menu."
```

---

### Task 5: Doküman senkronu

**Files:**
- Modify: `docs/04-gotchas.md` — satır: translucent bar / recreate / taskbar restore atlama
- Modify: `docs/01-architecture.md` — `topBar.backgroundMode` / `tintOpacity` örneği (varsa config bölümü)
- Modify: `.agent/project.md` — üst bar arka plan kişiselleştirme bir cümle
- Modify: `docs/superpowers/specs/2026-08-09-topbar-background-personalization-design.md` — durum + plan linki

- [ ] **Step 1: Gotcha satırı**

| Translucent üst bar / Mica geçişi | `AllowsTransparency` ctor’da; Mica↔translucent = TopBar recreate; recreate’de **taskbar restore yok**, yalnız `ABM_REMOVE`+yeni `ABM_NEW`; Mica+`AllowsTransparency` yasak |

- [ ] **Step 2: Spec durum satırını güncelle**

`Durum: Tasarım onaylandı — uygulama planı: [../plans/2026-08-09-topbar-background-personalization.md](../plans/2026-08-09-topbar-background-personalization.md).`

- [ ] **Step 3: Build not required for docs-only; skim links**

- [ ] **Step 4: Commit** (kullanıcı isterse)

```bash
git add docs/04-gotchas.md docs/01-architecture.md .agent/project.md docs/superpowers/specs/2026-08-09-topbar-background-personalization-design.md
git commit -m "docs: sync top bar background personalization notes."
```

---

## Spec coverage checklist

| Spec requirement | Task |
|------------------|------|
| `backgroundMode` mica/transparent/tint + default mica | 1 |
| `tintOpacity` 0–100 default 40 | 1 |
| Soft normalize, schemaVersion 1 | 1 |
| Mica pipeline unchanged for default | 2–3 |
| Transparent / tint pipelines | 2–3 |
| Slider live preview | 4 |
| Right-click menu section | 4 |
| Safe AppBar transition; no taskbar restore on recreate | 3 |
| Theme follows tint color | 3 (`ApplyBackgroundAppearance` on theme msg) |
| Non-goals (dock/hex/Acrylic/reset bg) | — not implemented |
| Manual verification list | Task 4 Step 3 |
| Docs | 5 |
