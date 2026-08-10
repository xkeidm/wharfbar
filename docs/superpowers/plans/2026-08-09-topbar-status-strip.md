# Top-bar Status Strip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Üst bar durum şeridine `controlCenter` ekle, varsayılan sırayı ve tıklama yönlendirmesini onaylı tasarıma getir, saati tek satır Türkçe biçime çevir; config soft-migrate et.

**Architecture:** Mevcut `TopBarStatusItemIds` + tek `QuickSettingsPopup` (ADR-006) genişletilir. Yeni id katalogda; XAML’de ayrı düğme; volume → Mixer, controlCenter → Main, network → Network. Saat `StatusService` içinde `tr-TR` birleşik format. Eski config’ler `topBar.statusCatalogVersion` 0→1 ile `controlCenter`’ı `volume` önüne alır (gizleme kalıcı kalır).

**Tech Stack:** .NET 8 WPF, mevcut WharfBar servisleri; yeni paket yok.

## Global Constraints

- Mica + `AllowsTransparency=true` yasak
- Çıkışta `ABM_REMOVE` + görev çubuğu restore
- P/Invoke yalnızca `Native/` `internal`
- `Views/` tercih; `global.json` yok
- ADR-005: yabancı tepsi / bildirim içeriği aynası yok
- Commit yalnızca kullanıcı isterse (bu oturumda skip)
- Derleme: `dotnet build WharfBar.sln` (`dotnet run` AppBar riski — zorunlu değil)

---

## File map

| File | Role |
|------|------|
| `src/WharfBar/Models/TopBarStatusItemIds.cs` | `controlCenter` + yeni `DefaultOrder` + `EnsureControlCenter` |
| `src/WharfBar/Models/AppConfig.cs` | `TopBarConfig.StatusCatalogVersion` |
| `src/WharfBar/Services/ConfigService.cs` | Load normalize: catalog 0→1 migrate + dirty save |
| `src/WharfBar/Services/StatusService.cs` | Birleşik `tr-TR` saat metni |
| `src/WharfBar/Views/TopBarWindow.xaml` | CC düğmesi; tek satır saat; sıra yorumu |
| `src/WharfBar/Views/TopBarWindow.xaml.cs` | Map, click routing, menü header, layout |
| `docs/decisions/006-quick-settings-flyout.md` | Giriş → sayfa tablosu |
| `docs/01-architecture.md` | statusItems örneği + katalog notu |
| `docs/02-win32-appbar-dwm.md` | Sağ küme / saat açıklaması |
| `.agent/project.md` | Ürün özeti durum şeridi |
| `.agent/context/roadmap.md` | Status strip notu |

---

### Task 1: Katalog + config migrasyonu

**Files:**
- Modify: `src/WharfBar/Models/TopBarStatusItemIds.cs`
- Modify: `src/WharfBar/Models/AppConfig.cs`
- Modify: `src/WharfBar/Services/ConfigService.cs`

**Interfaces:**
- Produces: `TopBarStatusItemIds.ControlCenter = "controlCenter"`
- Produces: `DefaultOrder` = trayOverflow, dateTime, notifications, controlCenter, volume, network, battery
- Produces: `EnsureControlCenter(List<string> items)` — yoksa `volume` önüne insert
- Produces: `TopBarConfig.CurrentStatusCatalogVersion = 1`, `StatusCatalogVersion` property

- [ ] **Step 1: Update `TopBarStatusItemIds`**

```csharp
internal const string ControlCenter = "controlCenter";

internal static readonly IReadOnlyList<string> DefaultOrder =
[
    TrayOverflow,
    DateTime,
    Notifications,
    ControlCenter,
    Volume,
    Network,
    Battery,
];

/// <summary>Inserts <see cref="ControlCenter"/> before <see cref="Volume"/> when missing.</summary>
internal static void EnsureControlCenter(List<string> items)
{
    if (items.Contains(ControlCenter, StringComparer.Ordinal))
    {
        return;
    }

    int volumeIndex = items.FindIndex(static x =>
        string.Equals(x, Volume, StringComparison.Ordinal));
    if (volumeIndex >= 0)
    {
        items.Insert(volumeIndex, ControlCenter);
    }
    else
    {
        items.Add(ControlCenter);
    }
}
```

Keep `Normalize` behavior (known-only, empty → default). Do **not** call `EnsureControlCenter` inside `Normalize` (hide must stick after catalog v1).

- [ ] **Step 2: Add soft catalog version on `TopBarConfig`**

```csharp
internal const int CurrentStatusCatalogVersion = 1;

/// <summary>
/// Soft catalog revision (schemaVersion stays 1). 0 = pre-controlCenter configs.
/// </summary>
public int StatusCatalogVersion { get; set; }
```

`CreateDefault` / new `TopBarConfig` path: set `StatusCatalogVersion = CurrentStatusCatalogVersion`.

- [ ] **Step 3: Migrate in `ConfigService.Normalize`**

After `config.TopBar ??= new TopBarConfig()`:

```csharp
config.TopBar.StatusItems = TopBarStatusItemIds.Normalize(config.TopBar.StatusItems);
if (config.TopBar.StatusCatalogVersion < TopBarConfig.CurrentStatusCatalogVersion)
{
    TopBarStatusItemIds.EnsureControlCenter(config.TopBar.StatusItems);
    config.TopBar.StatusCatalogVersion = TopBarConfig.CurrentStatusCatalogVersion;
}
```

In `LoadOrCreate`, also treat catalog version change as dirty (save), e.g. compare original `StatusCatalogVersion` to `Current`.

In `CreateDefault`:

```csharp
TopBar = new TopBarConfig
{
    StatusCatalogVersion = TopBarConfig.CurrentStatusCatalogVersion,
    StatusItems = TopBarStatusItemIds.DefaultOrder.ToList(),
},
```

- [ ] **Step 4: Verify build still compiles after model changes**

Run: `dotnet build WharfBar.sln --no-restore 2>&1 | Select-Object -Last 30`  
(or with restore if needed). Expected: may still fail later UI refs; models alone OK.

---

### Task 2: Saat biçimi (`StatusService`)

**Files:**
- Modify: `src/WharfBar/Services/StatusService.cs`

**Interfaces:**
- Consumes: `CultureInfo("tr-TR")`
- Produces: `ClockText` = `d MMM ddd HH:mm` (ör. `9 Ağu Paz 21:57`); `DateText` = `""` (UI tek satır); tooltip tam `f`/`D` birleşik

- [ ] **Step 1: Replace FormatClock / FormatDate**

```csharp
private static readonly CultureInfo ClockCulture = CultureInfo.GetCultureInfo("tr-TR");

private static (string Text, string Tooltip) FormatClock(DateTime now)
{
    string text = now.ToString("d MMM ddd HH:mm", ClockCulture);
    string tip = now.ToString("f", ClockCulture);
    return (text, tip);
}

private static (string Text, string Tooltip) FormatDate(DateTime now)
{
    // Combined into ClockText; keep fields for StatusSnapshot shape.
    return (string.Empty, now.ToString("D", ClockCulture));
}
```

RefreshClock equality can keep comparing both fields.

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`  
Expected: OK or only unrelated UI errors from Task 3.

---

### Task 3: TopBar UI + click routing

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `QuickSettingsPage.Main|Mixer|Network`, `TopBarStatusItemIds.ControlCenter`
- Produces: `ControlCenterButton` click → Main; `VolumeButton` → Mixer; `NetworkButton` → Network

- [ ] **Step 1: XAML — controlCenter + single-line clock**

In `StatusStrip` (declaration order can stay; runtime `ApplyStatusItemLayout` reorders):

- Add `ControlCenterButton` Border with MDL2 `&#xE9CE;` (ViewAll), tooltip "Hızlı ayarlar"
- Replace vertical ClockTime/Date stack with single `ClockText` using `StatusLabel` (or slim one-line style); remove `DateText`
- Remove unused `ClockTimeLabel` / `ClockDateLabel` styles if unused
- Update comment: `overflow · dateTime · notifications · controlCenter · volume · network · battery`

- [ ] **Step 2: Code-behind map + handlers**

```csharp
[TopBarStatusItemIds.ControlCenter] = ControlCenterButton,
// ...existing...

ControlCenterButton.PreviewMouseLeftButtonUp += OnControlCenterClick;
// Volume:
ToggleQuickSettings(QuickSettingsPage.Mixer, VolumeButton);
// Network unchanged → Network
// New:
private void OnControlCenterClick(...) =>
    ToggleQuickSettings(QuickSettingsPage.Main, ControlCenterButton);
```

`ApplyStatus`: only `ClockText` + tooltip (drop `DateText` assignment).

`StatusItemHeader`: `ControlCenter => "Hızlı ayarlar"`.

`ApplyVolume` close-flyout: also if placement is `ControlCenterButton`.

Hook `ControlCenterButton` in `OnSourceInitialized`.

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`  
Expected: `Build succeeded`.

---

### Task 4: Doküman + `.agent` güncellemesi

**Files:**
- Modify: `docs/decisions/006-quick-settings-flyout.md`
- Modify: `docs/01-architecture.md`
- Modify: `docs/02-win32-appbar-dwm.md`
- Modify: `.agent/project.md`
- Modify: `.agent/context/roadmap.md`
- Optional note in: `docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md` (katalog listesine `controlCenter`)

- [ ] **Step 1: ADR-006 giriş tablosu**

| Giriş | Açılış sayfası |
| Ses ikonu | **Mixer** |
| Control Center ikonu | **Ana (Main)** |
| Ağ ikonu | Ağ |
| Karıştırıcı chevron (iç) | Mixer |

- [ ] **Step 2: architecture / win32 / project / roadmap**

- `statusItems` JSON örneği → yeni sıra + `statusCatalogVersion: 1`
- `TopBarStatusItemIds` “7 sabit id”
- Sağ küme metni: tek satır `tr-TR` saat; CC → Main; volume → Mixer
- Roadmap: status strip sırası / CC maddesi tamamlandı notu

- [ ] **Step 3: Final build**

Run: `dotnet build WharfBar.sln`  
Expected: succeeded, 0 errors.

- [ ] **Step 4: Commit** — **SKIP** unless user asks.

---

## Manual test (user)

1. Temiz / eski `%AppData%/WharfBar/config.json`: `controlCenter` `volume` önünde görünür; `statusCatalogVersion` 1.
2. CC → QS Main; ses → Mixer; ağ → Network; saat+zil → Win+N; overflow → native.
3. Saat tek satır `d MMM ddd HH:mm` (tr).
4. Sağ tık: CC gizle → kalıcı (yeniden başlatınca geri gelmez); Varsayılana sıfırla → tam yeni sıra.
5. Normal kapatış: AppBar + görev çubuğu restore.

## Spec coverage

| Spec | Task |
|------|------|
| Default LTR order | 1 |
| `controlCenter` id + glyph + Main | 1, 3 |
| Click routing (Mixer/Network/Win+N) | 3 |
| Single-line tr-TR clock | 2, 3 |
| Config migrate insert before volume | 1 |
| Docs/.agent | 4 |
| No second popup / no tray mirror | Global + 3 |
