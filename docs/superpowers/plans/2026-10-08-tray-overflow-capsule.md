# Tray Overflow Capsule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Üst barda yalnız elle tutulan tepsi ikonları kalsın; ok, Windows gizli simgeler penceresi yerine kalan ikonların kapsülünü açsın.

**Architecture:** `TrayPromotedSplit` anlık görüntü anahtarlarını ve `topBar.trayPromoted` listesini bar, kapsül ve saklanan sıra diye böler. WPF tipi yoktur. Birincil `TopBarWindow` bu sonucu çizer, kapsülü açar, menü ve sürüklemeyi aynı hücreden yürütür. `TrayOverflowInvoker` bu düğmeden çağrılmaz ve silinir. `TrayOverflowNative` kalır: `TrayMirrorService` hâlâ `IsOverflowVisible` kullanır.

**Tech Stack:** .NET 8, WPF, xUnit, mevcut `ConfigService` JSON (camelCase). Yeni paket yok.

## Global Constraints

- Windows’un kendi gizli tepsi ikonları okunmaz. Yeni tray okuması yok.
- `ReadProcessMemory`, enjeksiyon ve belgelenmemiş API yok.
- Kapsül ve bölünmüş ayna yalnız birincil bardadır.
- `AppConfig.CurrentSchemaVersion` değişmez (kodda şu an `2`). Spec’teki “schemaVersion 1 kalır” cümlesi sürüm yükseltme yasağıdır; sabiti `1` yapma.
- Katalog sürümü (`TopBarConfig.CurrentStatusCatalogVersion`, `5`) değişmez.
- Anahtar karşılaştırması `StringComparison.Ordinal`.
- Metinler: **Üst barda tut**, **Gizle**, **Uygulama menüsü**, **Gizli simge yok**. Ok glifi `E70E`.
- Ayar sayfasında ikon listesi yok. Çoklu sürükleme yok.
- `AllowsTransparency` yalnız kapsül `Popup`’ındadır. Ana pencere Mica’sına yazılmaz.

---

### Task 1: TrayPromotedSplit

**Files:**
- Create: `src/WharfBar/Services/TrayPromotedSplit.cs`
- Test: `tests/WharfBar.Tests/TrayPromotedSplitTests.cs`

**Interfaces:**
- Consumes: yok
- Produces:
  - `internal enum TrayIconPlace { Bar, Capsule }`
  - `internal enum TrayDropTarget { Outside, TrayGap, Chevron, Capsule }`
  - `internal readonly record struct TrayPromotedLayout(IReadOnlyList<string> BarKeys, IReadOnlyList<string> CapsuleKeys, IReadOnlyList<string> StoredKeys)`
  - `internal readonly record struct TrayDropPlan(bool ChangesList, IReadOnlyList<string> Next)`
  - `TrayPromotedSplit.Split(IReadOnlyList<string> snapshotKeys, IReadOnlyList<string>? promoted)`
  - `TrayPromotedSplit.ChevronVisible(bool isPrimary, bool inCatalog, int snapshotCount)`
  - `TrayPromotedSplit.IconRowCount(int iconCount, double strideDip, double maxWidthDip)`
  - `TrayPromotedSplit.NearestGap(double pointerX, IReadOnlyList<double> cellCenters)`
  - `TrayPromotedSplit.IsSameSlot(int currentIndex, int gapIndex)`
  - `TrayPromotedSplit.CanCommit(string? key, IReadOnlyList<string> snapshotKeys)`
  - `TrayPromotedSplit.PlanDrop(TrayIconPlace source, TrayDropTarget target, string key, int gapIndex, IReadOnlyList<string>? promoted, IReadOnlyList<string> snapshotKeys)`

- [ ] **Step 1: Write the failing test**

`tests/WharfBar.Tests/TrayPromotedSplitTests.cs`:

```csharp
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayPromotedSplitTests
{
    [Fact]
    public void EmptyPromoted_PutsEverySnapshotKeyInTheCapsule()
    {
        TrayPromotedLayout layout = TrayPromotedSplit.Split(["a", "b"], promoted: null);

        Assert.Empty(layout.BarKeys);
        Assert.Equal(["a", "b"], layout.CapsuleKeys);
        Assert.Empty(layout.StoredKeys);
    }

    [Fact]
    public void Bar_FollowsPromotedOrder_Capsule_FollowsSnapshotOrder()
    {
        TrayPromotedLayout layout = TrayPromotedSplit.Split(
            ["a", "b", "c", "d"],
            ["c", "a"]);

        Assert.Equal(["c", "a"], layout.BarKeys);
        Assert.Equal(["b", "d"], layout.CapsuleKeys);
    }

    [Fact]
    public void AbsentPromotedKey_StaysStored()
    {
        TrayPromotedLayout layout = TrayPromotedSplit.Split(["a"], ["gone", "a"]);

        Assert.Equal(["a"], layout.BarKeys);
        Assert.Equal(["gone", "a"], layout.StoredKeys);
    }

    [Fact]
    public void DuplicatePromotedKey_IsDrawnOnce()
    {
        TrayPromotedLayout layout = TrayPromotedSplit.Split(["a", "b"], ["a", "a", "b"]);

        Assert.Equal(["a", "b"], layout.BarKeys);
        Assert.Equal(["a", "b"], layout.StoredKeys);
        Assert.Empty(layout.CapsuleKeys);
    }

    [Fact]
    public void EmptySnapshot_DoesNotDeleteStoredKeys()
    {
        TrayPromotedLayout layout = TrayPromotedSplit.Split([], ["a", "b"]);

        Assert.Empty(layout.BarKeys);
        Assert.Empty(layout.CapsuleKeys);
        Assert.Equal(["a", "b"], layout.StoredKeys);
    }

    [Theory]
    [InlineData(false, true, 2, false)]
    [InlineData(true, false, 2, false)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, true, 1, true)]
    public void ChevronVisible_RequiresPrimaryCatalogAndAnIcon(
        bool isPrimary, bool inCatalog, int snapshotCount, bool visible)
    {
        Assert.Equal(visible, TrayPromotedSplit.ChevronVisible(isPrimary, inCatalog, snapshotCount));
    }

    [Theory]
    [InlineData(0, 30, 200, 0)]
    [InlineData(3, 30, 200, 1)]
    [InlineData(4, 30, 90, 2)]
    [InlineData(3, 30, 20, 3)]
    public void IconRowCount_WrapsWhenStrideExceedsTheCeiling(
        int iconCount, double stride, double maxWidth, int rows)
    {
        Assert.Equal(rows, TrayPromotedSplit.IconRowCount(iconCount, stride, maxWidth));
    }

    [Fact]
    public void NearestGap_UsesCellCenters()
    {
        double[] centers = [10, 40, 70];
        Assert.Equal(0, TrayPromotedSplit.NearestGap(9, centers));
        Assert.Equal(1, TrayPromotedSplit.NearestGap(10, centers));
        Assert.Equal(2, TrayPromotedSplit.NearestGap(55, centers));
        Assert.Equal(3, TrayPromotedSplit.NearestGap(70, centers));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(1, 0, false)]
    [InlineData(1, 3, false)]
    public void IsSameSlot_IsTheGapBeforeOrAfterTheItem(int current, int gap, bool same)
    {
        Assert.Equal(same, TrayPromotedSplit.IsSameSlot(current, gap));
    }

    [Fact]
    public void PlanDrop_InsertsCapsuleKeyAtTheGap()
    {
        TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
            TrayIconPlace.Capsule, TrayDropTarget.TrayGap, "b", 1, ["a"], ["a", "b"]);

        Assert.True(plan.ChangesList);
        Assert.Equal(["a", "b"], plan.Next);
    }

    [Fact]
    public void PlanDrop_MovesBarKeyToAnotherGap()
    {
        TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
            TrayIconPlace.Bar, TrayDropTarget.TrayGap, "a", 2, ["a", "b"], ["a", "b"]);

        Assert.True(plan.ChangesList);
        Assert.Equal(["b", "a"], plan.Next);
    }

    [Fact]
    public void PlanDrop_SameSlot_DoesNotChange()
    {
        TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
            TrayIconPlace.Bar, TrayDropTarget.TrayGap, "a", 1, ["a", "b"], ["a", "b"]);

        Assert.False(plan.ChangesList);
        Assert.Equal(["a", "b"], plan.Next);
    }

    [Fact]
    public void PlanDrop_BarOntoChevron_RemovesKey()
    {
        TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
            TrayIconPlace.Bar, TrayDropTarget.Chevron, "a", 0, ["a", "gone"], ["a"]);

        Assert.True(plan.ChangesList);
        Assert.Equal(["gone"], plan.Next);
    }

    [Fact]
    public void PlanDrop_CapsuleOntoChevron_DoesNotChange()
    {
        TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
            TrayIconPlace.Capsule, TrayDropTarget.Chevron, "b", 0, ["a"], ["a", "b"]);

        Assert.False(plan.ChangesList);
        Assert.Equal(["a"], plan.Next);
    }

    [Fact]
    public void PlanDrop_Outside_DoesNotChange()
    {
        TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
            TrayIconPlace.Bar, TrayDropTarget.Outside, "a", 0, ["a"], ["a"]);

        Assert.False(plan.ChangesList);
    }

    [Fact]
    public void PlanDrop_KeyMissingFromSnapshot_DoesNotWrite()
    {
        Assert.False(TrayPromotedSplit.CanCommit("gone", ["a"]));
        TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
            TrayIconPlace.Bar, TrayDropTarget.Capsule, "gone", 0, ["gone", "a"], ["a"]);

        Assert.False(plan.ChangesList);
        Assert.Equal(["gone", "a"], plan.Next);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayPromotedSplitTests`

Expected: FAIL. `TrayPromotedSplit` tipi yok.

- [ ] **Step 3: Write minimal implementation**

`src/WharfBar/Services/TrayPromotedSplit.cs`:

```csharp
namespace WharfBar.Services;

internal enum TrayIconPlace
{
    Bar,
    Capsule,
}

internal enum TrayDropTarget
{
    Outside,
    TrayGap,
    Chevron,
    Capsule,
}

internal readonly record struct TrayPromotedLayout(
    IReadOnlyList<string> BarKeys,
    IReadOnlyList<string> CapsuleKeys,
    IReadOnlyList<string> StoredKeys);

internal readonly record struct TrayDropPlan(bool ChangesList, IReadOnlyList<string> Next);

internal static class TrayPromotedSplit
{
    internal static TrayPromotedLayout Split(
        IReadOnlyList<string> snapshotKeys,
        IReadOnlyList<string>? promoted)
    {
        List<string> stored = Distinct(promoted);
        var snapshotSet = new HashSet<string>(Distinct(snapshotKeys), StringComparer.Ordinal);
        var storedSet = new HashSet<string>(stored, StringComparer.Ordinal);

        var bar = new List<string>();
        foreach (string key in stored)
        {
            if (snapshotSet.Contains(key))
            {
                bar.Add(key);
            }
        }

        var capsule = new List<string>();
        foreach (string key in Distinct(snapshotKeys))
        {
            if (!storedSet.Contains(key))
            {
                capsule.Add(key);
            }
        }

        return new TrayPromotedLayout(bar, capsule, stored);
    }

    internal static bool ChevronVisible(bool isPrimary, bool inCatalog, int snapshotCount) =>
        isPrimary && inCatalog && snapshotCount > 0;

    internal static int IconRowCount(int iconCount, double strideDip, double maxWidthDip)
    {
        if (iconCount <= 0)
        {
            return 0;
        }

        if (strideDip <= 0 || maxWidthDip <= 0
            || double.IsNaN(strideDip) || double.IsNaN(maxWidthDip))
        {
            return 1;
        }

        int perRow = Math.Max(1, (int)Math.Floor(maxWidthDip / strideDip));
        return (iconCount + perRow - 1) / perRow;
    }

    internal static int NearestGap(double pointerX, IReadOnlyList<double> cellCenters)
    {
        for (int i = 0; i < cellCenters.Count; i++)
        {
            if (pointerX < cellCenters[i])
            {
                return i;
            }
        }

        return cellCenters.Count;
    }

    internal static bool IsSameSlot(int currentIndex, int gapIndex) =>
        gapIndex == currentIndex || gapIndex == currentIndex + 1;

    internal static bool CanCommit(string? key, IReadOnlyList<string> snapshotKeys) =>
        !string.IsNullOrEmpty(key)
        && snapshotKeys.Contains(key, StringComparer.Ordinal);

    internal static TrayDropPlan PlanDrop(
        TrayIconPlace source,
        TrayDropTarget target,
        string key,
        int gapIndex,
        IReadOnlyList<string>? promoted,
        IReadOnlyList<string> snapshotKeys)
    {
        IReadOnlyList<string> stored = Split(snapshotKeys, promoted).StoredKeys;
        if (!CanCommit(key, snapshotKeys) || target == TrayDropTarget.Outside)
        {
            return new TrayDropPlan(false, stored);
        }

        if (source == TrayIconPlace.Capsule
            && (target == TrayDropTarget.Chevron || target == TrayDropTarget.Capsule))
        {
            return new TrayDropPlan(false, stored);
        }

        if (target == TrayDropTarget.TrayGap)
        {
            int current = IndexOf(stored, key);
            if (source == TrayIconPlace.Bar && current >= 0 && IsSameSlot(current, gapIndex))
            {
                return new TrayDropPlan(false, stored);
            }

            return new TrayDropPlan(true, Insert(stored, key, gapIndex));
        }

        if (source == TrayIconPlace.Bar
            && (target == TrayDropTarget.Chevron || target == TrayDropTarget.Capsule))
        {
            if (IndexOf(stored, key) < 0)
            {
                return new TrayDropPlan(false, stored);
            }

            return new TrayDropPlan(true, Remove(stored, key));
        }

        return new TrayDropPlan(false, stored);
    }

    private static List<string> Distinct(IReadOnlyList<string>? keys)
    {
        var list = new List<string>();
        if (keys is null)
        {
            return list;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (string.IsNullOrEmpty(key) || !seen.Add(key))
            {
                continue;
            }

            list.Add(key);
        }

        return list;
    }

    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> Insert(IReadOnlyList<string> keys, string key, int gapIndex)
    {
        var list = new List<string>(keys);
        int current = IndexOf(list, key);
        if (current >= 0)
        {
            list.RemoveAt(current);
            if (current < gapIndex)
            {
                gapIndex--;
            }
        }

        gapIndex = Math.Clamp(gapIndex, 0, list.Count);
        list.Insert(gapIndex, key);
        return list;
    }

    private static List<string> Remove(IReadOnlyList<string> keys, string key)
    {
        var list = new List<string>(keys);
        int current = IndexOf(list, key);
        if (current >= 0)
        {
            list.RemoveAt(current);
        }

        return list;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayPromotedSplitTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Services/TrayPromotedSplit.cs tests/WharfBar.Tests/TrayPromotedSplitTests.cs
git commit -m "feat(tray): promote listesini bar ve kapsul sirasina bol"
```

---

### Task 2: trayPromoted kaydı

**Files:**
- Modify: `src/WharfBar/Models/AppConfig.cs` (`TopBarConfig`, `TrayPromoted` özelliği `StatusItems` alanının altına)
- Modify: `src/WharfBar/Services/ConfigService.cs` (`Normalize`, `config.TopBar.StatusItems = ...` satırından hemen sonra)
- Test: `tests/WharfBar.Tests/TrayPromotedConfigTests.cs`

**Interfaces:**
- Consumes: `TopBarConfig`
- Produces: `TopBarConfig.TrayPromoted` (`List<string>`, JSON `topBar.trayPromoted`). Eksik veya `null` alan `LoadOrCreate` sonrası boş liste.

- [ ] **Step 1: Write the failing test**

`tests/WharfBar.Tests/TrayPromotedConfigTests.cs`:

```csharp
using System.IO;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayPromotedConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-tray-promoted-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void MissingField_LoadsEmpty_AndDoesNotBumpSchema()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "config.json"), "{\"schemaVersion\":2,\"topBar\":{}}");

        AppConfig loaded = new ConfigService(_root).LoadOrCreate();

        Assert.Empty(loaded.TopBar.TrayPromoted);
        Assert.Equal(AppConfig.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(TopBarConfig.CurrentStatusCatalogVersion, loaded.TopBar.StatusCatalogVersion);
    }

    [Fact]
    public void NullField_LoadsEmpty()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "config.json"),
            "{\"schemaVersion\":2,\"topBar\":{\"trayPromoted\":null}}");

        AppConfig loaded = new ConfigService(_root).LoadOrCreate();

        Assert.Empty(loaded.TopBar.TrayPromoted);
    }

    [Fact]
    public void Save_RoundTripsOrdinalOrder()
    {
        var service = new ConfigService(_root);
        AppConfig config = service.LoadOrCreate();
        config.TopBar.TrayPromoted = ["b", "a"];
        service.Save(config);

        AppConfig again = new ConfigService(_root).LoadOrCreate();

        Assert.Equal(["b", "a"], again.TopBar.TrayPromoted);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayPromotedConfigTests`

Expected: FAIL. `TrayPromoted` yok.

- [ ] **Step 3: Write minimal implementation**

`TopBarConfig` içine, `StatusItems` özelliğinin altına:

```csharp
/// <summary>
/// Tray icon keys kept on the primary bar, left to right. Missing keys stay
/// here until the icon returns. Icons absent from this list open in the capsule.
/// </summary>
public List<string> TrayPromoted { get; set; } = [];
```

`ConfigService.Normalize` içinde `config.TopBar.StatusItems = TopBarStatusItemIds.Normalize(...)` satırından hemen sonra:

```csharp
config.TopBar.TrayPromoted ??= [];
```

`schemaVersion` ve `StatusCatalogVersion` atamalarına dokunma.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayPromotedConfigTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs tests/WharfBar.Tests/TrayPromotedConfigTests.cs
git commit -m "feat(tray): tutulan tepsi anahtarlarini config'e yaz"
```

---

### Task 3: Sol tık bırakmada iletilir

Sürükleme, basışta iletilen sol tıkla yarışır. Sol tık da sağ tık gibi bırakmada iletilir. `TrayClickPlan` mesaj dizisi değişmez.

**Files:**
- Modify: `src/WharfBar/Native/TrayToolbar.cs` (`TrayGestureMap.TryForward` ve üstündeki yorum)
- Modify: `tests/WharfBar.Tests/TrayClickPlanTests.cs` (`LeftDown_ForwardsLeft_AndDoubleOnSecondPress`, `LeftRelease_DoesNotForward`)

**Interfaces:**
- Consumes: `TrayGestureMap.TryForward(bool isRightButton, int clickCount, bool isRelease, out TrayClick kind)`
- Produces: sol ve çift sol yalnız `isRelease: true` iken döner. `isRelease: false` sol tıkta `false`. Sağ tık kuralı aynı kalır.

- [ ] **Step 1: Replace the two left-click tests**

`LeftDown_ForwardsLeft_AndDoubleOnSecondPress` metodunu sil. Yerine:

```csharp
[Fact]
public void LeftRelease_ForwardsLeft_AndDoubleOnSecondRelease()
{
    Assert.True(TrayGestureMap.TryForward(false, 1, true, out TrayClick single));
    Assert.Equal(TrayClick.Left, single);
    Assert.True(TrayGestureMap.TryForward(false, 2, true, out TrayClick doubled));
    Assert.Equal(TrayClick.DoubleLeft, doubled);
}

[Fact]
public void LeftDown_DoesNotForward()
{
    bool forward = TrayGestureMap.TryForward(isRightButton: false, clickCount: 1, isRelease: false, out _);
    Assert.False(forward);
}
```

`LeftRelease_DoesNotForward` metodunu sil. `RightDown_DoesNotForward` ve `RightRelease_ForwardsRight` kalsın. `TrayClickPlan.Build` testlerine dokunma.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayClickPlanTests`

Expected: FAIL. `LeftRelease_ForwardsLeft_AndDoubleOnSecondRelease` ve `LeftDown_DoesNotForward` kırmızı.

- [ ] **Step 3: Update TrayGestureMap**

`TrayGestureMap` yorumundaki “Left clicks stay on down.” cümlesini şununla değiştir: “Left and double-left forward on release so a drag can swallow the click. Right-click stays on release because the top bar still owns capture on the way down.”

`TryForward` gövdesini değiştir:

```csharp
internal static bool TryForward(bool isRightButton, int clickCount, bool isRelease, out TrayClick kind)
{
    if (!isRelease)
    {
        kind = default;
        return false;
    }

    if (isRightButton)
    {
        kind = TrayClick.Right;
        return true;
    }

    kind = clickCount >= 2 ? TrayClick.DoubleLeft : TrayClick.Left;
    return true;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayClickPlanTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Native/TrayToolbar.cs tests/WharfBar.Tests/TrayClickPlanTests.cs
git commit -m "fix(tray): sol tik birakmada iletilir, surukleme tiklamayi yutabilsin"
```

---

### Task 4: Bar, ok ve kapsül gövdesi

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml` (`TrayOverflowButton` hemen ardından, diğer `Popup`’ların yanına)
- Create: `src/WharfBar/Views/TopBarWindow.Tray.cs`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` (`ApplyTrayMirror`, `ApplyStatusItemLayout`, `OnTrayOverflowClick`, `CloseFlyouts`)

**Interfaces:**
- Consumes: `TrayPromotedSplit.Split`, `TrayPromotedSplit.ChevronVisible`, `TrayPromotedSplit.IconRowCount`, `IslandPopupAlign.HorizontalOffset`, `FlyoutSurface.Create`, `AnimateFlyoutOpen`, `AnimateFlyoutClose`
- Produces: birincil barda `ApplyTrayMirror` bar anahtarlarını `TrayMirrorHost`’a, diğerlerini kapsüle yazar. Ok tıklaması `TrayCapsulePopup`’ı açar/kapatır. `_engine.TrayOverflow.TryOpen` bu metottan kalkar.

Hücre adımı `30` DIP. Tek satır (veya boş gövde) `CornerRadius` yüksekliğin yarısıdır. `IconRowCount` `1`’den büyükse `CornerRadius` `12`. Boş gövde metni `Gizli simge yok`.

- [ ] **Step 1: Add the popup**

`TopBarWindow.xaml` içinde `MediaPopup` kapanışından sonra:

```xml
<Popup x:Name="TrayCapsulePopup"
       Placement="Bottom"
       PlacementTarget="{Binding ElementName=TrayOverflowButton}"
       StaysOpen="True"
       AllowsTransparency="True"
       PopupAnimation="None"
       VerticalOffset="2">
    <Border x:Name="TrayCapsuleRoot"
            Padding="8,6"
            CornerRadius="14"
            Opacity="0">
        <StackPanel>
            <TextBlock x:Name="TrayCapsuleEmpty"
                       Text="Gizli simge yok"
                       Visibility="Collapsed"
                       FontFamily="{StaticResource AppTextFontFamily}"
                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
            <ItemsControl x:Name="TrayCapsuleItems">
                <ItemsControl.ItemsPanel>
                    <ItemsPanelTemplate>
                        <WrapPanel x:Name="TrayCapsuleWrap" />
                    </ItemsPanelTemplate>
                </ItemsControl.ItemsPanel>
            </ItemsControl>
        </StackPanel>
    </Border>
</Popup>
```

`WrapPanel`’e `x:Name` vermek, `ItemsPanelTemplate` içinde pencere alanına bağlanmaz. Adı XAML’den sil. Satır genişliği kodda `TrayCapsuleItems.Width` ile verilir.

- [ ] **Step 2: Split the mirror in ApplyTrayMirror**

`ApplyTrayMirror` başında, yalnız birincil pencerede:

```csharp
IReadOnlyList<string> snapshotKeys = items.Select(static i => i.Key).ToList();
_traySnapshotCount = snapshotKeys.Count;
TrayPromotedLayout layout = TrayPromotedSplit.Split(
    snapshotKeys,
    _config.Current.TopBar?.TrayPromoted);
```

Bar hücrelerini `layout.BarKeys` sırasıyla kur. Kapsül hücrelerini `layout.CapsuleKeys` sırasıyla `TrayCapsuleItems`’a kur. Hücre üretimi bugünkü `Border` + 16 DIP `Image` + `SetPlainToolTip` yolunun aynısıdır. `Tag` `TrayMirrorItem` kalır. Var olan hücreyi anahtarla bul, ikonunu tazele, sırayı bozma diye hedef koleksiyondan çıkıp doğru koleksiyona taşı.

```csharp
_trayMirrorActive = layout.BarKeys.Count > 0;
```

`_traySnapshotCount == 0` iken kapsül açıksa kapatma. Anlık görüntü yenilenince `TrayCapsulePopup.IsOpen` değiştirilmez. Boş kapsülde `TrayCapsuleEmpty.Visibility = Visible` ve `TrayCapsuleItems.Visibility = Collapsed`. Doluysa tersi.

Kapsül ölçüldükten sonra. Genişlik tavanı, adanın kullandığı aynı çağrılardır (`Display.GetDpi`, `Display.GetMonitorWorkArea`). Yeni monitör API’si yazma.

```csharp
const double stride = 30;
double dpi = Display.GetDpi(hwnd);
RECT work = Display.GetMonitorWorkArea(hwnd);
double workDip = dpi > 0 ? (work.Right - work.Left) / (dpi / 96.0) : stride;
double maxWidth = Math.Max(stride, workDip - 24);
int rows = TrayPromotedSplit.IconRowCount(layout.CapsuleKeys.Count, stride, maxWidth);
int perRow = Math.Max(1, (int)Math.Floor(maxWidth / stride));
int shown = Math.Max(layout.CapsuleKeys.Count, 1);
TrayCapsuleItems.Width = Math.Min(shown, perRow) * stride;
TrayCapsuleRoot.CornerRadius = new CornerRadius(rows <= 1 ? 14 : 12);
TrayCapsuleRoot.Background = FlyoutSurface.Create();
```

`rows <= 1` hap için `14` kullan. `ActualHeight / 2` ölçümden önce `NaN` olur. Tek satırda `Height` henüz yokken yarım yükseklik kapsülü kare yapar. `14`, 6+18+6 dolguyla hapı kapatır. Sarılı panelde `12`.

Hücre, bugünkü aynanın kopyasıdır. Hem bar hem kapsül bunu çağırır:

```csharp
private Border CreateTrayCell(TrayMirrorItem item)
{
    var image = new Image
    {
        Source = item.Icon,
        Width = 16,
        Height = 16,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center,
    };
    var host = new Border
    {
        Width = 30,
        Height = 28,
        Background = Brushes.Transparent,
        Child = image,
        Tag = item,
        ToolTip = item.Tooltip,
    };
    host.MouseLeftButtonUp += OnTrayCellLeftUp;
    host.PreviewMouseRightButtonUp += OnTrayMirrorItemRightUp;
    return host;
}
```

`SetPlainToolTip` zaten varsa `ToolTip = item.Tooltip` yerine onu çağır. İkinci bir tooltip yolu ekleme.

`OnTrayCellLeftUp` bu görevde de vardır. Task 3’ten sonra basışta iletim yoktur. Bırakmada ilet ve kapsülü kapat:

```csharp
private void OnTrayCellLeftUp(object sender, MouseButtonEventArgs e)
{
    if (sender is not Border { Tag: TrayMirrorItem item } host)
    {
        return;
    }

    if (!TrayGestureMap.TryForward(false, e.ClickCount, true, out TrayClick kind))
    {
        return;
    }

    e.Handled = true;
    ForwardTrayClick(item, kind, host);
    CloseTrayCapsule(animate: true);
}
```

`_suppressTrayClick` Task 6’dadır. Bu görevde bayrak yok.

Hizalama, ada ile aynı yardımcıdır. `pillWidthDip` okun `ActualWidth` değeridir. `panelWidthDip` `IslandPopupAlign.WidthForPlacement(TrayCapsuleRoot.ActualWidth)` sonucudur. Sonuç `TrayCapsulePopup.HorizontalOffset` olur.

- [ ] **Step 3: Chevron visibility and toggle**

`ApplyStatusItemLayout` içindeki `show` ifadesine:

```csharp
TopBarStatusItemIds.TrayOverflow => TrayPromotedSplit.ChevronVisible(
    Monitor.IsPrimary,
    inCatalog: true,
    _traySnapshotCount),
```

Katalogda olmayan öğe zaten `order` döngüsüne girmez. `inCatalog: true` bu yüzden doğrudur.

`OnTrayOverflowClick` gövdesini değiştir. `_engine.TrayOverflow.TryOpen()` kalkar.

```csharp
private void OnTrayOverflowClick(object sender, MouseButtonEventArgs e)
{
    e.Handled = true;
    if (!Monitor.IsPrimary)
    {
        return;
    }

    if (TrayCapsulePopup.IsOpen)
    {
        CloseTrayCapsule(animate: true);
        return;
    }

    CloseFlyouts();
    TrayCapsuleRoot.Background = FlyoutSurface.Create();
    TrayCapsulePopup.IsOpen = true;
    AnimateFlyoutOpen(TrayCapsuleRoot);
}
```

`CloseFlyouts` ilk satırına `CloseTrayCapsule(animate);` ekle.

```csharp
private void CloseTrayCapsule(bool animate)
{
    if (!TrayCapsulePopup.IsOpen)
    {
        return;
    }

    if (!animate)
    {
        TrayCapsulePopup.IsOpen = false;
        return;
    }

    AnimateFlyoutClose(TrayCapsuleRoot, () => TrayCapsulePopup.IsOpen = false);
}
```

`_traySnapshotCount` alanını `TopBarWindow` üzerinde `int` olarak ekle. İkincil barda `0` kalır.

- [ ] **Step 4: Build and check the click-plan tests still pass**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayClickPlanTests|FullyQualifiedName~TrayPromotedSplitTests`

Expected: PASS. Bu görev yeni bir saf test eklemez. Derleme hatası yoksa tamamdır.

Elle: birincil barda, kayıt boşken ikonlar barda değil kapsüldedir. Ok, en az bir ikon varken görünür. İkincil barda ok yoktur.

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Views/TopBarWindow.xaml src/WharfBar/Views/TopBarWindow.xaml.cs src/WharfBar/Views/TopBarWindow.Tray.cs
git commit -m "feat(tray): tutulmayan ikonlar okun kapsulunde dursun"
```

---

### Task 5: Menü ve tıklama

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` (sol tık iletimini bırakmaya taşı)
- Modify: `src/WharfBar/Views/TopBarWindow.Tray.cs`

**Interfaces:**
- Consumes: `TrayGestureMap.TryForward`, `ForwardTrayClick`, `ConfigService.Save`
- Produces: sağ tık menüsü. Sol ve çift tık iletimden sonra kapsül kapanır. Menüden gizlenen ikon kapsülü açmaz.

- [ ] **Step 1: Forward left clicks on release**

`OnTrayMirrorItemMouseDown` sol tıkta `ForwardTrayClick` çağırmaz. `TryForward(..., isRelease: false)` zaten `false` döner. Sağ tık hâlâ `OnTrayMirrorItemRightUp` içindedir.

Sol bırakmayı bağla. Hücre `MouseLeftButtonUp` olayında:

```csharp
private void OnTrayCellLeftUp(object sender, MouseButtonEventArgs e)
{
    if (_suppressTrayClick)
    {
        _suppressTrayClick = false;
        e.Handled = true;
        return;
    }

    if (sender is not Border { Tag: TrayMirrorItem item } host)
    {
        return;
    }

    if (!TrayGestureMap.TryForward(isRightButton: false, e.ClickCount, isRelease: true, out TrayClick kind))
    {
        return;
    }

    e.Handled = true;
    ForwardTrayClick(item, kind, host);
    CloseTrayCapsule(animate: true);
}
```

`_suppressTrayClick` bu görevde hep `false` kalır. Task 6 sürükleme bitince `true` yapar.

- [ ] **Step 2: Replace the immediate right-click forward**

`OnTrayMirrorItemRightUp` içinde `ForwardTrayClick(item, TrayClick.Right, host)` çağrısını kaldır. Yerine menüyü aç:

```csharp
private void ShowTrayMenu(Border host, TrayMirrorItem item, TrayIconPlace place)
{
    var menu = new ContextMenu();
    var pin = new MenuItem
    {
        Header = place == TrayIconPlace.Bar ? "Gizle" : "Üst barda tut",
    };
    pin.Click += (_, _) =>
    {
        IReadOnlyList<string> snapshot = CurrentSnapshotKeys();
        TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
            place,
            place == TrayIconPlace.Bar ? TrayDropTarget.Capsule : TrayDropTarget.TrayGap,
            item.Key,
            gapIndex: place == TrayIconPlace.Bar ? 0 : int.MaxValue,
            _config.Current.TopBar.TrayPromoted,
            snapshot);
        if (plan.ChangesList)
        {
            PersistTrayPromoted(plan.Next);
        }
    };
    var app = new MenuItem { Header = "Uygulama menüsü" };
    app.Click += (_, _) =>
    {
        CloseTrayCapsule(animate: false);
        ForwardTrayClick(item, TrayClick.Right, host);
    };
    menu.Items.Add(pin);
    menu.Items.Add(app);
    menu.PlacementTarget = host;
    menu.IsOpen = true;
}
```

**Üst barda tut** sona ekler: `gapIndex: int.MaxValue`. `PlanDrop` bunu `Insert` içinde `Math.Clamp` ile listenin sonuna alır. **Gizle** `TrayDropTarget.Capsule` kullanır. Menü kapsülü kapatmaz. `PersistTrayPromoted` listeyi belleğe yazar, sonra kaydeder:

```csharp
private void PersistTrayPromoted(IReadOnlyList<string> next)
{
    _config.Current.TopBar.TrayPromoted = next.ToList();
    ApplyTrayMirror(_engine.TrayMirror.Items);
    try
    {
        _config.Save(_config.Current);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        System.Diagnostics.Debug.WriteLine($"WharfBar: tray promoted save failed — {ex.Message}");
    }
}
```

`CurrentSnapshotKeys` son uygulanan anlık görüntünün anahtar listesidir. `ApplyTrayMirror` bu listeyi alanda tutar. Kayıt başarısız olunca bellek zaten yenidir. Sonraki başarılı `Save` onu yazar.

Hücreyi kuran kod, bar için `TrayIconPlace.Bar`, kapsül için `TrayIconPlace.Capsule` saklar. `Border.Tag` `TrayMirrorItem` kalır. Yer bilgisi hücrenin hangi koleksiyonda olduğundan okunur: `TrayMirrorItems.Items.Contains(host)` ise bar.

- [ ] **Step 3: Build**

Run: `dotnet build src/WharfBar/WharfBar.csproj --no-restore`

Restore gerekirse önce `dotnet build WharfBar.sln`. Expected: başarı, yeni uyarı yok.

Elle: kapsülde sağ tık **Üst barda tut** ikonu bara alır ve kaydeder. Barda **Gizle** ikonu kapsüle indirir, kapsülü açmaz. **Uygulama menüsü** kapsülü kapatır ve bugünkü sağ tıkı iletir. Sol tık iletimden sonra kapsül kapanır.

- [ ] **Step 4: Commit**

```bash
git add src/WharfBar/Views/TopBarWindow.xaml.cs src/WharfBar/Views/TopBarWindow.Tray.cs
git commit -m "feat(tray): tut ve gizle menusu, tik iletimi kapsulden de gitsin"
```

---

### Task 6: Sürükleme

**Files:**
- Modify: `src/WharfBar/Views/TopBarWindow.Tray.cs`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml` (`TrayMirrorHost` ve `TrayOverflowButton` üzerine `AllowDrop="True"`)

**Interfaces:**
- Consumes: `TrayPromotedSplit.PlanDrop`, `NearestGap`, `PersistTrayPromoted`, `_suppressTrayClick`
- Produces: sürükleme tıklama sayılmaz. Bırakma hedefleri spec’teki tablodur.

Veri biçimi: `WharfBar.TrayPromoted`. Yük `key + "\n" + place` (`Bar` veya `Capsule`).

- [ ] **Step 1: Start a drag and swallow the click**

Hücre `PreviewMouseMove` içinde sol düğme basılıyken, başlangıç noktasından `6` DIP sapınca:

```csharp
_suppressTrayClick = true;
DragDrop.DoDragDrop(host, payload, DragDropEffects.Move);
```

`DoDragDrop` dönünce bayrak `true` kalır. Sonraki sol bırakma `OnTrayCellLeftUp` içinde yutulur ve bayrak sıfırlanır. Eşik aşılmazsa bayrak `false` kalır, bırakma tıklamadır.

- [ ] **Step 2: Drop**

`TrayMirrorHost`, `TrayOverflowButton` ve `TrayCapsuleRoot` `AllowDrop="True"`. `Drop` olayında anlık görüntü anahtarlarını ve `gapIndex`’i topla.

Bar aralığı: imleç `TrayMirrorHost`’a göredir. Her bar hücresinin merkezi `NearestGap`’e gider. Ok ve kapsül `TrayDropTarget.Chevron` / `Capsule` olur. Başka yer `Outside` olur.

```csharp
TrayDropPlan plan = TrayPromotedSplit.PlanDrop(
    source, target, key, gapIndex,
    _config.Current.TopBar.TrayPromoted,
    CurrentSnapshotKeys());
if (plan.ChangesList)
{
    PersistTrayPromoted(plan.Next);
}
```

`CanCommit` false ise `PlanDrop` listeyi yazmaz. Bu, sürükleme sırasında ikonun anlık görüntüden düşmesini karşılar.

Tutma veya gizlemeden sonra kapsül, `layout.CapsuleKeys.Count > 0` ise açık kalır. Sıfırsa `CloseTrayCapsule(animate: true)`.

- [ ] **Step 3: Build and run the split tests**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayPromotedSplitTests`

Expected: PASS

Elle: kapsülden bar aralığına bırakmak sıraya oturtur. Bardaki ikonu başka aralığa bırakmak taşır. Okun veya kapsülün üstüne bırakmak gizler. Kapsüldeki ikonu okun üstüne bırakmak listeyi değiştirmez. Boşa bırakmak değiştirmez. Sürükleme uygulamayı açmaz.

- [ ] **Step 4: Commit**

```bash
git add src/WharfBar/Views/TopBarWindow.xaml src/WharfBar/Views/TopBarWindow.Tray.cs
git commit -m "feat(tray): ikonu bar ile kapsul arasinda surukle"
```

---

### Task 7: Eski overflow yolunu kaldır, dokümanı güncelle

**Files:**
- Delete: `src/WharfBar/Services/TrayOverflowInvoker.cs`
- Modify: `src/WharfBar/Services/TopBarEngine.cs` (`TrayOverflow` özelliği ve kurucu satırı)
- Modify: `src/WharfBar/Native/AppBarPlacement.cs` (yorum, sabit `45` sn kalır)
- Modify: `docs/decisions/009-tray-mirror-toolbar-enumeration.md`
- Modify: `docs/decisions/005-shell-replace-live-dock-tray-hybrid.md`
- Modify: `.agent/boundaries.md`
- Modify: `.agent/screens/topbar.md`
- Modify: `.agent/project.md`
- Modify: `docs/00-overview.md`
- Modify: `docs/01-architecture.md`
- Modify: `docs/02-win32-appbar-dwm.md`
- Modify: `docs/03-development-order.md`
- Modify: `docs/04-gotchas.md`

`TrayOverflowNative.cs` silinmez.

**Interfaces:**
- Consumes: Task 4’te düğmenin invoker’ı bırakmış olması
- Produces: `TopBarEngine.TrayOverflow` yoktur. Derleme, invoker adına referans bırakmaz.

- [ ] **Step 1: Remove the invoker**

`TopBarEngine` içinden özelliği ve `TrayOverflow = new TrayOverflowInvoker(taskbar);` satırını sil. Dosyayı sil. `AppBarPlacement.TrayPeekMaxDuration` değeri `TimeSpan.FromSeconds(45)` kalır. Yorumu şöyle değiştir:

```csharp
/// Max duration a tray peek protects Shell_TrayWnd from the keep-hidden watch.
/// Past this the watch force-ends an orphaned peek so the taskbar cannot stay visible.
```

- [ ] **Step 2: Update the living docs**

ADR-009 karar tablosundaki “Kapsam dışı” satırını değiştir:

`Windows’un kendi gizli ikonları okunmaz. Ayna ikonları topBar.trayPromoted ile bar ve kapsül diye bölünür. trayOverflow düğmesi kapsülü açar. TrayOverflowInvoker yoktur.`

ADR-009’un sonuna 2026-10-08 revizyon notu ekle: düğme artık yerel overflow açmaz. Ayrıntı `docs/superpowers/specs/2026-10-08-tray-overflow-capsule-design.md`.

ADR-005’te “tek tray düğmesi gerçek Windows overflow’u açar (`Win+B`)” cümlesini değiştir: düğme WharfBar kapsülünü açar. Windows’un gizli ikonları okunmaz.

`.agent/screens/topbar.md` `trayOverflow` satırı: kapsül, **Gizli simge yok**, sürükleme ve sağ tık menüsü.

`.agent/boundaries.md` ve `.agent/project.md`: ayna ikonları bölünür. Ok `Win+B` açmaz.

`docs/00-overview.md`, `docs/01-architecture.md`, `docs/02-win32-appbar-dwm.md`, `docs/03-development-order.md`, `docs/04-gotchas.md`: “ok Win+B açar” cümlelerini kapsül cümlesiyle değiştir. `TrayOverflowNative` satırını silme. Gotchas tablosundaki “Gizli simgeler tıklanınca hiçbir şey olmaması / BeginTrayPeek” satırı bu düğme için kalkar. Peek, ayna okumasında duruyorsa o satır aynanın peek’i olarak kalır. Düğmenin peek’i olarak yazma.

Tarihsel spec ve plan dosyalarına (`docs/superpowers/specs/2026-08-09-*`, `docs/superpowers/plans/2026-08-09-*`, `2026-08-19-tray-mirror-design.md`) dokunma.

- [ ] **Step 3: Build and test**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~TrayPromoted|FullyQualifiedName~TrayClickPlanTests`

Expected: PASS

Run: `dotnet build WharfBar.sln`

Expected: başarı. `TrayOverflowInvoker` referansı kalmadı.

- [ ] **Step 4: Commit**

```bash
git add src/WharfBar/Services/TopBarEngine.cs src/WharfBar/Native/AppBarPlacement.cs docs .agent
git add -u src/WharfBar/Services/TrayOverflowInvoker.cs
git commit -m "docs(tray): ok kapsul acar, yerel overflow invoker'i kalkar"
```

`git add docs .agent` yalnızca bu görevde değişen dosyaları al. Çalışma ağacındaki ilgisiz ada ve dock farklarını ekleme. `git status` ile doğrula.
