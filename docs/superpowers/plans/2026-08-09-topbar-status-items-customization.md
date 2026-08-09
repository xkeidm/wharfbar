# Top bar durum öğesi özelleştirme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Üst bar sağ durum şeridinde WharfBar’ın kendi öğelerini (`trayOverflow` … `notifications`) config ile göster/gizle + sırala; düzenleme sağ tık menü ile.

**Architecture:** `AppConfig.TopBar.StatusItems` (string id sırası = görünür sıra). Soft migrate `schemaVersion` 1’de. `TopBarWindow` mevcut named element’leri bir sözlükte tutar, `StatusStrip.Children`’ı id sırasına göre yeniden dizer ve görünürlüğü config ∩ runtime (pil yok / ses yok) ile kesiştirir. Sağ tık menü `ConfigService.Save` yazar. Quick Settings / mixer / flyout host’a **dokunulmaz**.

**Tech Stack:** .NET 8 WPF, mevcut `ConfigService` / `AppConfig`, yeni NuGet yok, test projesi yok.

**Spec:** [`docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md`](../specs/2026-08-09-topbar-shortcuts-customization-design.md)

## Global Constraints

- Target: `net8.0-windows10.0.19041.0`; `global.json` ekleme
- Yeni NuGet / test projesi yok; doğrulama = `dotnet build WharfBar.sln` (0 error) + manuel checklist
- `schemaVersion` **1** kalır; `topBar` soft alan (yoksa varsayılan)
- Breaking şema yok (`boundaries.md`)
- Yabancı tepsi ikonu çizme / aynalama **yasak** (ADR-005)
- QS flyout, `AudioMixerService`, `QuickSettingsService`, mixer sayfası: **bu planda değişmez**
- Önkoşul (çalıştırma kapısı): mevcut QS + mixer WIP manuel stabilize / doğrulanmış olmalı; bu plan o işi içermez
- Commit yalnızca kullanıcı istediğinde (görev sonundaki commit adımlarını kullanıcı istemedikçe atla)
- UI klasörü `Views/`; P/Invoke yok (bu özellik)

### Kilitlenen seçimler (spec)

| Konu | Seçim |
|------|--------|
| Panel | Ayrı Control Center yok; QS = ADR-006 |
| Katalog | `trayOverflow`, `network`, `battery`, `volume`, `dateTime`, `notifications` |
| Anlam | Dizideki id’ler = görünür + sıra; dizide olmayan = gizli |
| Düzenleme | Durum şeridi sağ tık → işaretli menü + sola/sağa taşı |
| Saat / bildirim | Bugün tek `DateTimeNotificationsButton`; v1’de **iki ayrı hit target**’a ayrılır (bağımsız gizle/sıra) |
| Layout | `ItemsControl` yok; mevcut element’ler + `Children` yeniden dizme (hafif) |
| Persist | Her menü değişikliğinde `ConfigService.Save` |

---

## File structure

| File | Responsibility |
|------|----------------|
| `src/WharfBar/Models/TopBarStatusItemIds.cs` | Sabit id string’leri + varsayılan sıra + normalize |
| `src/WharfBar/Models/AppConfig.cs` | `TopBarConfig` + `AppConfig.TopBar` |
| `src/WharfBar/Services/ConfigService.cs` | Default + `Normalize` soft migrate |
| `src/WharfBar/App.xaml.cs` | `TopBarWindow`’a `ConfigService` geçir |
| `src/WharfBar/Views/TopBarWindow.xaml` | Saat / bildirim ayrımı; `StatusStrip` ContextMenu host |
| `src/WharfBar/Views/TopBarWindow.xaml.cs` | Layout uygula; sağ tık menü; config kaydet |
| `docs/01-architecture.md` | Config şeması + ağaç |
| `.agent/context/roadmap.md` | Plan linki (8b) |
| Spec | Durum: plan yazıldı (isteğe bağlı tek satır) |

```
App.OnStartup
  └─ ConfigService.LoadOrCreate()
       └─ TopBarWindow(taskbar, configService)
            ├─ ApplyStatusItemLayout()  ← Task 3–4
            ├─ StatusStrip ContextMenu  ← Task 5
            └─ ApplyStatus / ApplyVolume ∩ layout visibility
```

---

### Task 1: Status item id’leri + normalize

**Files:**
- Create: `src/WharfBar/Models/TopBarStatusItemIds.cs`
- Modify: `src/WharfBar/Models/AppConfig.cs`

**Interfaces:**
- Consumes: yok
- Produces:
  - `TopBarStatusItemIds.TrayOverflow` = `"trayOverflow"` (ve diğer 5 sabit)
  - `TopBarStatusItemIds.DefaultOrder` → `IReadOnlyList<string>` (6 id, bugünkü sol→sağ sıra)
  - `TopBarStatusItemIds.Normalize(IEnumerable<string>? items) → List<string>`
  - `TopBarConfig` with `List<string> StatusItems`
  - `AppConfig.TopBar` property

- [ ] **Step 1: Add `TopBarStatusItemIds`**

```csharp
namespace WharfBar.Models;

/// <summary>Built-in top-bar status strip item ids (config <c>topBar.statusItems</c>).</summary>
internal static class TopBarStatusItemIds
{
    internal const string TrayOverflow = "trayOverflow";
    internal const string Network = "network";
    internal const string Battery = "battery";
    internal const string Volume = "volume";
    internal const string DateTime = "dateTime";
    internal const string Notifications = "notifications";

    internal static readonly IReadOnlyList<string> DefaultOrder =
    [
        TrayOverflow,
        Network,
        Battery,
        Volume,
        DateTime,
        Notifications,
    ];

    private static readonly HashSet<string> Known = new(DefaultOrder, StringComparer.Ordinal);

    /// <summary>
    /// Keeps first occurrence of known ids only; empty/null → default full set.
    /// Unknown ids dropped (soft).
    /// </summary>
    internal static List<string> Normalize(IEnumerable<string>? items)
    {
        if (items is null)
        {
            return DefaultOrder.ToList();
        }

        var result = new List<string>(DefaultOrder.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string? raw in items)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string id = raw.Trim();
            if (!Known.Contains(id) || !seen.Add(id))
            {
                continue;
            }

            result.Add(id);
        }

        return result.Count == 0 ? DefaultOrder.ToList() : result;
    }

    internal static bool IsKnown(string id) => Known.Contains(id);
}
```

- [ ] **Step 2: Extend `AppConfig`**

In `AppConfig.cs`, after `Dock` property add:

```csharp
public TopBarConfig TopBar { get; set; } = new();
```

Add class:

```csharp
/// <summary>Top bar settings under <see cref="AppConfig.TopBar"/>.</summary>
internal sealed class TopBarConfig
{
    /// <summary>
    /// Visible status item ids in left→right order. Omitted ids are hidden.
    /// Unknown ids stripped on load.
    /// </summary>
    public List<string> StatusItems { get; set; } = TopBarStatusItemIds.DefaultOrder.ToList();
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 errors

- [ ] **Step 4: Commit** (yalnızca kullanıcı isterse)

```bash
git add src/WharfBar/Models/TopBarStatusItemIds.cs src/WharfBar/Models/AppConfig.cs
git commit -m "$(cat <<'EOF'
Add topBar.statusItems model and id catalog.

EOF
)"
```

---

### Task 2: ConfigService soft migrate

**Files:**
- Modify: `src/WharfBar/Services/ConfigService.cs`

**Interfaces:**
- Consumes: `TopBarStatusItemIds.Normalize`, `TopBarConfig`
- Produces: `CreateDefault` / `Normalize` her zaman geçerli `TopBar.StatusItems` yazar; eksik `topBar` → default

- [ ] **Step 1: `CreateDefault` içine TopBar ekle**

```csharp
TopBar = new TopBarConfig
{
    StatusItems = TopBarStatusItemIds.DefaultOrder.ToList(),
},
```

(`PinnedApps` satırının yanına.)

- [ ] **Step 2: `Normalize` içinde soft migrate**

`config.Dock ??= new DockConfig();` sonrası:

```csharp
config.TopBar ??= new TopBarConfig();
config.TopBar.StatusItems = TopBarStatusItemIds.Normalize(config.TopBar.StatusItems);
```

- [ ] **Step 3: Soft persist when StatusItems changed by normalize**

In `LoadOrCreate`, after `Current = Normalize(loaded);`, today iconSize migration may `Save`. Extend so normalized status list also persists when it differed from raw:

```csharp
bool statusChanged = !StatusItemsEqual(
    loaded.TopBar?.StatusItems,
    Current.TopBar.StatusItems);
bool iconChanged = Math.Abs(Current.Dock.IconSize - originalIconSize) > 0.01;

if (iconChanged || statusChanged)
{
    Save(Current);
}
```

Helper (private static in `ConfigService`):

```csharp
private static bool StatusItemsEqual(IList<string>? a, IList<string> b)
{
    if (a is null)
    {
        return false; // missing → we wrote defaults; persist once
    }

    if (a.Count != b.Count)
    {
        return false;
    }

    for (int i = 0; i < b.Count; i++)
    {
        if (!string.Equals(a[i]?.Trim(), b[i], StringComparison.Ordinal))
        {
            return false;
        }
    }

    return true;
}
```

Note: `loaded.TopBar` null → `statusChanged` true → soft write defaults (iyi).

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 errors

- [ ] **Step 5: Commit** (isteğe bağlı)

```bash
git add src/WharfBar/Services/ConfigService.cs
git commit -m "$(cat <<'EOF'
Soft-migrate topBar.statusItems in ConfigService.

EOF
)"
```

---

### Task 3: TopBar’a ConfigService + saat/bildirim ayrımı

**Files:**
- Modify: `src/WharfBar/App.xaml.cs`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `ConfigService`
- Produces: `TopBarWindow(TaskbarVisibilityService taskbar, ConfigService config)`; `DateTimeButton` + `NotificationsButton` (eski birleşik buton kalkar)

- [ ] **Step 1: Constructor + App wiring**

`App.xaml.cs`:

```csharp
var topBar = new TopBarWindow(_taskbar, _config);
```

`TopBarWindow.xaml.cs` field + ctor:

```csharp
private readonly ConfigService _config;

internal TopBarWindow(TaskbarVisibilityService taskbar, ConfigService config)
{
    _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
    _config = config ?? throw new ArgumentNullException(nameof(config));
    _trayOverflow = new TrayOverflowInvoker(_taskbar);
    InitializeComponent();
    // ... existing Loaded handlers unchanged so far
}
```

- [ ] **Step 2: Split date/time and notifications in XAML**

Replace `DateTimeNotificationsButton` block with:

```xml
<Border x:Name="DateTimeButton"
        Style="{StaticResource StatusHit}"
        Padding="8,2,4,2"
        Margin="0">
    <Border.ToolTip>
        <ToolTip Content="Tarih ve saat" />
    </Border.ToolTip>
    <StackPanel Orientation="Vertical" VerticalAlignment="Center">
        <TextBlock x:Name="ClockText"
                   Style="{StaticResource ClockTimeLabel}"
                   Text="--:--" />
        <TextBlock x:Name="DateText"
                   Style="{StaticResource ClockDateLabel}"
                   Text="--" />
    </StackPanel>
</Border>

<Border x:Name="NotificationsButton"
        Style="{StaticResource StatusHit}"
        Padding="4,2,6,2"
        Margin="0">
    <Border.ToolTip>
        <ToolTip Content="Bildirimler" />
    </Border.ToolTip>
    <TextBlock x:Name="NotificationsGlyph"
               Style="{StaticResource StatusGlyph}"
               VerticalAlignment="Center"
               Text="&#xEA8F;" />
</Border>
```

- [ ] **Step 3: Wire both clicks to Notification Center**

Replace `OnDateTimeNotificationsClick` with shared handler; subscribe both:

```csharp
DateTimeButton.PreviewMouseLeftButtonUp += OnDateTimeOrNotificationsClick;
NotificationsButton.PreviewMouseLeftButtonUp += OnDateTimeOrNotificationsClick;
```

```csharp
private void OnDateTimeOrNotificationsClick(object sender, MouseButtonEventArgs e)
{
    e.Handled = true;
    CloseFlyouts();
    _ = Dispatcher.BeginInvoke(
        DispatcherPriority.Input,
        new Action(() => _notifications.TryOpen()));
}
```

Update `ApplyStatus` tooltip targets:

```csharp
SetPlainToolTip(DateTimeButton, snapshot.ClockTooltip);
```

(Remove `DateTimeNotificationsButton` references.)

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`  
Expected: 0 errors (XAML compile included)

- [ ] **Step 5: Commit** (isteğe bağlı)

```bash
git add src/WharfBar/App.xaml.cs src/WharfBar/Views/TopBarWindow.xaml src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "$(cat <<'EOF'
Split top-bar date/time and notifications hit targets; pass ConfigService.

EOF
)"
```

---

### Task 4: ApplyStatusItemLayout (görünürlük + sıra)

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `_config.Current.TopBar.StatusItems`, named UI elements
- Produces: `ApplyStatusItemLayout()`; `ApplyStatus` / `ApplyVolume` config ∩ runtime

- [ ] **Step 1: Element map + layout method**

After fields, add:

```csharp
private Dictionary<string, FrameworkElement>? _statusElements;
private bool _batteryPresent = true;
private bool _volumeAvailable = true;
```

In ctor after `InitializeComponent()`:

```csharp
_statusElements = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
{
    [TopBarStatusItemIds.TrayOverflow] = TrayOverflowButton,
    [TopBarStatusItemIds.Network] = NetworkButton,
    [TopBarStatusItemIds.Battery] = BatteryGroup,
    [TopBarStatusItemIds.Volume] = VolumeButton,
    [TopBarStatusItemIds.DateTime] = DateTimeButton,
    [TopBarStatusItemIds.Notifications] = NotificationsButton,
};
```

```csharp
private void ApplyStatusItemLayout()
{
    if (_statusElements is null)
    {
        return;
    }

    IReadOnlyList<string> order = TopBarStatusItemIds.Normalize(
        _config.Current.TopBar?.StatusItems);

    // Re-parent in order
    StatusStrip.Children.Clear();
    foreach (string id in order)
    {
        if (_statusElements.TryGetValue(id, out FrameworkElement? el))
        {
            StatusStrip.Children.Add(el);
        }
    }

    // Hidden catalog items: not in Children; keep alive via dictionary only.
    // Visibility for items in strip:
    foreach (string id in order)
    {
        FrameworkElement el = _statusElements[id];
        bool show = id switch
        {
            TopBarStatusItemIds.Battery => _batteryPresent,
            TopBarStatusItemIds.Volume => _volumeAvailable,
            _ => true,
        };
        el.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }
}
```

Call `ApplyStatusItemLayout()` at end of ctor (after map) and whenever config menu saves.

- [ ] **Step 2: Intersect ApplyStatus / ApplyVolume**

`ApplyStatus` — battery section **must not** `return` early in a way that skips layout; replace battery block:

```csharp
_batteryPresent = snapshot.IsBatteryPresent;
if (snapshot.IsBatteryPresent)
{
    BatteryText.Text = snapshot.BatteryText;
    SetPlainToolTip(BatteryGroup, snapshot.BatteryTooltip);
    BatteryGlyph.Text = ResolveBatteryGlyph(snapshot);
}

ApplyStatusItemLayout();
```

(Keep clock/network updates before this.)

`ApplyVolume`:

```csharp
_volumeAvailable = snapshot.IsAvailable;
if (snapshot.IsAvailable)
{
    VolumeGlyph.Text = /* existing glyph logic */;
    string tip = snapshot.IsMuted ? "Ses (sessiz)" : $"Ses %{snapshot.Percent}";
    SetPlainToolTip(VolumeButton, tip);
}
else if (ReferenceEquals(QuickSettingsPopup.PlacementTarget, VolumeButton))
{
    QuickSettingsPopup.IsOpen = false;
}

ApplyStatusItemLayout();
```

Do **not** set `VolumeButton.Visibility` / `BatteryGroup.Visibility` directly outside `ApplyStatusItemLayout`.

- [ ] **Step 3: Build + smoke**

Run: `dotnet build WharfBar.sln`  
Expected: 0 errors  
Manual: varsayılan config ile şerit bugünkü gibi (overflow→…→bildirim); desktop’ta pil gizli kalır.

- [ ] **Step 4: Commit** (isteğe bağlı)

```bash
git add src/WharfBar/Views/TopBarWindow.xaml.cs
git commit -m "$(cat <<'EOF'
Apply top-bar status item order and visibility from config.

EOF
)"
```

---

### Task 5: Sağ tık düzenleme menüsü

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml` (optional empty ContextMenu on `StatusStrip`)
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `ApplyStatusItemLayout`, `ConfigService.Save`
- Produces: Context menu: her katalog id için `IsCheckable`; tıklanan öğede “Sola taşı” / “Sağa taşı”; “Varsayılana sıfırla”

- [ ] **Step 1: Open menu on right-click**

On `StatusStrip` (and bubbling from children):

```csharp
StatusStrip.MouseRightButtonUp += OnStatusStripRightClick;
```

```csharp
private void OnStatusStripRightClick(object sender, MouseButtonEventArgs e)
{
    e.Handled = true;
    FrameworkElement? hit = FindStatusElement(e.OriginalSource as DependencyObject);
    ContextMenu menu = BuildStatusItemsMenu(hit);
    menu.PlacementTarget = StatusStrip;
    menu.IsOpen = true;
}

private FrameworkElement? FindStatusElement(DependencyObject? source)
{
    while (source is not null)
    {
        if (source is FrameworkElement fe
            && _statusElements is not null
            && _statusElements.Values.Contains(fe))
        {
            return fe;
        }

        source = VisualTreeHelper.GetParent(source);
    }

    return null;
}
```

- [ ] **Step 2: Build menu**

```csharp
private ContextMenu BuildStatusItemsMenu(FrameworkElement? hitItem)
{
    var menu = new ContextMenu();
    List<string> current = TopBarStatusItemIds.Normalize(_config.Current.TopBar.StatusItems);
    var visible = new HashSet<string>(current, StringComparer.Ordinal);

    foreach (string id in TopBarStatusItemIds.DefaultOrder)
    {
        var item = new MenuItem
        {
            Header = StatusItemHeader(id),
            IsCheckable = true,
            IsChecked = visible.Contains(id),
            Tag = id,
        };
        item.Click += OnToggleStatusItem;
        menu.Items.Add(item);
    }

    menu.Items.Add(new Separator());

    if (hitItem is not null
        && TryGetIdForElement(hitItem, out string hitId)
        && visible.Contains(hitId))
    {
        int index = current.IndexOf(hitId);
        var moveLeft = new MenuItem
        {
            Header = "Sola taşı",
            IsEnabled = index > 0,
            Tag = hitId,
        };
        moveLeft.Click += OnMoveStatusItemLeft;
        var moveRight = new MenuItem
        {
            Header = "Sağa taşı",
            IsEnabled = index >= 0 && index < current.Count - 1,
            Tag = hitId,
        };
        moveRight.Click += OnMoveStatusItemRight;
        menu.Items.Add(moveLeft);
        menu.Items.Add(moveRight);
        menu.Items.Add(new Separator());
    }

    var reset = new MenuItem { Header = "Varsayılana sıfırla" };
    reset.Click += OnResetStatusItems;
    menu.Items.Add(reset);
    return menu;
}

private static string StatusItemHeader(string id) => id switch
{
    TopBarStatusItemIds.TrayOverflow => "Gizli simgeler",
    TopBarStatusItemIds.Network => "Ağ",
    TopBarStatusItemIds.Battery => "Pil",
    TopBarStatusItemIds.Volume => "Ses",
    TopBarStatusItemIds.DateTime => "Tarih ve saat",
    TopBarStatusItemIds.Notifications => "Bildirimler",
    _ => id,
};

private bool TryGetIdForElement(FrameworkElement el, out string id)
{
    if (_statusElements is not null)
    {
        foreach (KeyValuePair<string, FrameworkElement> pair in _statusElements)
        {
            if (ReferenceEquals(pair.Value, el))
            {
                id = pair.Key;
                return true;
            }
        }
    }

    id = string.Empty;
    return false;
}
```

- [ ] **Step 3: Mutate + save**

```csharp
private void OnToggleStatusItem(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem { Tag: string id, IsChecked: bool isChecked })
    {
        return;
    }

    List<string> items = TopBarStatusItemIds.Normalize(_config.Current.TopBar.StatusItems);
    if (isChecked)
    {
        if (!items.Contains(id, StringComparer.Ordinal))
        {
            // Insert at default-relative position among currently visible
            items = InsertByDefaultOrder(items, id);
        }
    }
    else
    {
        items.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
        if (items.Count == 0)
        {
            // Never allow empty strip — keep at least dateTime
            items.Add(TopBarStatusItemIds.DateTime);
        }
    }

    PersistStatusItems(items);
}

private static List<string> InsertByDefaultOrder(List<string> current, string id)
{
    var set = new HashSet<string>(current, StringComparer.Ordinal) { id };
    return TopBarStatusItemIds.DefaultOrder.Where(set.Contains).ToList();
}

private void OnMoveStatusItemLeft(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem { Tag: string id })
    {
        return;
    }

    List<string> items = TopBarStatusItemIds.Normalize(_config.Current.TopBar.StatusItems);
    int i = items.IndexOf(id);
    if (i <= 0)
    {
        return;
    }

    (items[i - 1], items[i]) = (items[i], items[i - 1]);
    PersistStatusItems(items);
}

private void OnMoveStatusItemRight(object sender, RoutedEventArgs e)
{
    if (sender is not MenuItem { Tag: string id })
    {
        return;
    }

    List<string> items = TopBarStatusItemIds.Normalize(_config.Current.TopBar.StatusItems);
    int i = items.IndexOf(id);
    if (i < 0 || i >= items.Count - 1)
    {
        return;
    }

    (items[i + 1], items[i]) = (items[i], items[i + 1]);
    PersistStatusItems(items);
}

private void OnResetStatusItems(object sender, RoutedEventArgs e)
{
    PersistStatusItems(TopBarStatusItemIds.DefaultOrder.ToList());
}

private void PersistStatusItems(List<string> items)
{
    AppConfig cfg = _config.Current;
    cfg.TopBar ??= new TopBarConfig();
    cfg.TopBar.StatusItems = TopBarStatusItemIds.Normalize(items);
    _config.Save(cfg);
    ApplyStatusItemLayout();
}
```

- [ ] **Step 4: Build + manuel checklist**

Run: `dotnet build WharfBar.sln`  
Expected: 0 errors

Manuel:
1. Durum şeridine sağ tık → 6 işaretli öğe görünür
2. Ses’i kapat → ikon kaybolur; `config.json` içinde `volume` yok
3. Ses’i aç → geri gelir (default-order konumunda)
4. Ağ üzerinde sağ tık → Sola/Sağa taşı sıra değiştirir ve config güncellenir
5. Varsayılana sıfırla → tam set
6. Tümünü kapatmaya çalış → en az `dateTime` kalır
7. Sol tık davranışları (QS, Win+N, overflow) bozulmamış
8. QS / mixer sayfalarına dokunulmamış (diff’te `QuickSettings*` / `AudioMixer*` yok)

- [ ] **Step 5: Commit** (isteğe bağlı)

```bash
git add src/WharfBar/Views/TopBarWindow.xaml.cs src/WharfBar/Views/TopBarWindow.xaml
git commit -m "$(cat <<'EOF'
Add right-click menu to customize top-bar status items.

EOF
)"
```

---

### Task 6: Doküman

**Files:**
- Modify: `docs/01-architecture.md` (config şeması + Models ağacı)
- Modify: `.agent/context/roadmap.md` (8b altına plan linki)
- Modify: `docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md` (durum satırı: plan yazıldı)

**Interfaces:** yok (docs only)

- [ ] **Step 1: Architecture config örneği**

JSON’a ekle:

```json
"topBar": {
  "statusItems": [
    "trayOverflow",
    "network",
    "battery",
    "volume",
    "dateTime",
    "notifications"
  ]
}
```

Tablo satırı:

| `topBar.statusItems` | Görünür durum öğesi id’leri (sol→sağ sıra). Bilinmeyen id düşülür; boş/yok → varsayılan tam set. Soft alan (`schemaVersion` 1) |

Models ağacına: `TopBarStatusItemIds.cs`, `AppConfig` notunda `TopBarConfig`.

- [ ] **Step 2: Roadmap 8b**

Mevcut 8b maddesine plan linki ekle:

`docs/superpowers/plans/2026-08-09-topbar-status-items-customization.md`

- [ ] **Step 3: Spec durum satırı**

`**Durum:** Tasarım onaylandı … — uygulama planı: […plans/…]`  

Kod hâlâ “henüz yok” / çalıştırma öncesi QS stabilize kapısı durur.

- [ ] **Step 4: Commit** (isteğe bağlı)

```bash
git add docs/01-architecture.md .agent/context/roadmap.md docs/superpowers/specs/2026-08-09-topbar-shortcuts-customization-design.md docs/superpowers/plans/2026-08-09-topbar-status-items-customization.md
git commit -m "$(cat <<'EOF'
Document top-bar status item customization plan and config schema.

EOF
)"
```

---

## Spec coverage (self-review)

| Spec gereksinimi | Task |
|------------------|------|
| Ayrı Control Center yok / QS’e dokunma | Global Constraints + Task 5 checklist |
| `topBar.statusItems` soft `schemaVersion` 1 | Task 1–2 |
| Katalog 6 id | Task 1 |
| Varsayılan = bugünkü set | Task 1 `DefaultOrder` |
| Pil yokken collapse bağımsız | Task 4 `_batteryPresent` |
| Sağ tık menü | Task 5 |
| Reorder | Task 5 sola/sağa |
| Tepsi aynası yok | Global + katalog sabit |
| QS stabilize önce | Global önkoşul (plan dışı) |
| Docs | Task 6 |

Placeholder yok; `TopBarStatusItemIds` / `PersistStatusItems` imzaları task’lar arası tutarlı.
