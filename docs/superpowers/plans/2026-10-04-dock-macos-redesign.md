# Dock macOS Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WharfBar'ın dock'unu Windows görev çubuğunu yansıtan bir ayna olmaktan çıkarıp, kendi kalıcı listesini tutan, hover'da komşularıyla birlikte büyüyen (macOS mantığı) bir dock'a dönüştürmek.

**Architecture:** Saf ve test edilebilir iki yardımcı (`DockMagnification` eğri/yumuşatma, `DockStripLayout` slot ofsetleri ve pencere boyut bütçesi) büyütme matematiğini WPF'den ayırır. Özel `DockStrip : Panel`, fare konumunu **kesirli** bir `CursorPosition` olarak tutar ve `ArrangeOverride` içinde slot genişliklerini yeniden hesaplar; büyüme `RenderTransform` ile görselleşir, yerleşim `InvalidateMeasure` ile akar. Pencere boyutu **bir kez** maksimum büyümeye göre sabitlenir — kare başına `SetWindowPos` çağrısı yapılmaz, dolayısıyla bugünkü stutter sorunu tasarımın içinde çözülür.

**Tech Stack:** .NET 8, WPF, WPF-UI 4.3, Win32 P/Invoke (`EnumWindows`, `IsIconic`, `SetWindowPos`), xUnit 2.9.3

**Spec:** `docs/superpowers/specs/2026-10-04-dock-macos-redesign-design.md`

> **Durum: uygulandı (2026-10-04).** Bu plan **uygulama öncesi** bir sözleşmedir; görev
> sırasındaki kod parçacıkları bir sonraki okuyucunun kopyalamaması için bırakılmıştır, ama
> **koda aykırı olan adımlar uygulama sırasında düzeltildi.** Tam sapma listesi ve gerekçeleri
> spec'in [§0 Uygulama sonrası sapmalar](2026-10-04-dock-macos-redesign-design.md) bölümünde.
> En kritiği: **Task 6'daki tıklama bastırma kodu (`_suppressClickFor`) uygulanmadı** — koda
> **anahtarsız** `_suppressNextClick` bayrağı girildi, çünkü bırakma sırayı değiştirdiği için
> bırakmanın düştüğü hücre o anda başka bir kutucuğu tutuyor olabilir ve anahtar karşılaştırması
> ıskalayıp uygulamayı açardı. `RunningAppsSource.WindowsFor(id)` de yazılmadı; menü
> `MinimizedWindowsSource.Windows` önbelleğinden beslenir. Test sayısı 425 → **566**.

---

## Global Constraints

- Hedef framework `net8.0-windows10.0.19041.0`. **`global.json` ile 8.x SDK'a sabitleme.**
- `AllowsTransparency="True"` dock'ta kalıcıdır. **DWM Mica/Acrylic backdrop ile birleştirme** (`docs/04-gotchas.md:7`).
- Dock bir AppBar **değildir** (`docs/decisions/001`). Yerleşim yalnızca fiziksel piksel `SetWindowPos` ile yapılır; WPF `Left/Top/Width/Height` kullanılmaz.
- **`WM_WINDOWPOSCHANGING` içinde `Measure()` çağrısı yasak** (`docs/04-gotchas.md:41`). `BuildEdgeProbe` yalnızca önbelleklenmiş son yerleşim geometrisini okur.
- **Kare başına `SetWindowPos` çağrılamaz.** Pencere boyutu `DockStripLayout.MaxExtentWidth` / `MaxExtentHeight` ile bir kez hesaplanır.
- Yeni Win32 çağrısı imzası `docs/02-win32-appbar-dwm.md`'ye eklenir.
- Test düzeni: dosya `tests/WharfBar.Tests/<Type>Tests.cs`, sınıf `public class`, adlandırma `Method_WhenCondition_IsOutcome`, `[Fact]`, `Assert.*`. Testler `namespace WharfBar.Tests;` kullanır ve `internal` türleri görebilir (`InternalsVisibleTo` mevcut).
- Tüm arayüz metinleri Türkçe; kod tanımlayıcıları İngilizce.
- Commit yalnızca kullanıcı istediğinde; bu plan commit adımlarını içerir ama **kullanıcı onayı olmadan çalıştırılmaz**.
- Baseline doğrulandı: `dotnet build WharfBar.sln` temiz, `dotnet test WharfBar.sln` → **425 geçen**.

---

## Dosya haritası

**Yeni:**
| Dosya | Tek sorumluluk |
|---|---|
| `src/WharfBar/Services/DockMagnification.cs` | Saf büyütme eğrisi + kare-hızından bağımsız yumuşatma |
| `src/WharfBar/Services/DockStripLayout.cs` | Saf slot ofsetleri + pencere boyut bütçesi |
| `src/WharfBar/Services/DockItemStore.cs` | Sıralı, tekilleştirilmiş, kalıcı sabit liste |
| `src/WharfBar/Services/MinimizedWindowsSource.cs` | Küçültülmüş üst düzey pencereler |
| `src/WharfBar/Services/DockStripBuilder.cs` | `DockAppItem` listesinden bölüm ayraçlı şerit satırları üretir |
| `src/WharfBar/Services/DockDropFormats.cs` | Bırakılan dosya uzantısı süzgeci + kısayol çözümü |
| `src/WharfBar/Views/DockItemViewModel.cs` | Slot başına render verisi |
| `src/WharfBar/Views/Controls/DockItemCell.xaml(.cs)` | Tek hücre: ikon + çalışan göstergesi (+ ayraç varyantı) |
| `src/WharfBar/Views/Controls/DockStrip.cs` | `Panel`: hücre sahipliği, slot yerleşimi, büyütme durumu |
| `src/WharfBar/Views/Pages/DockSettingsRow.cs` | Ayar sayfasındaki tek liste satırı |
| `tests/WharfBar.Tests/DockMagnificationTests.cs` | Eğri + yumuşatma testleri |
| `tests/WharfBar.Tests/DockStripLayoutTests.cs` | Ofset + boyut bütçesi testleri |
| `tests/WharfBar.Tests/DockItemStoreTests.cs` | Liste işlemleri + kalıcılık testleri |
| `tests/WharfBar.Tests/MinimizedWindowsSourceTests.cs` | Küçültülmüş pencere süzme testleri |
| `tests/WharfBar.Tests/DockAppsComposerTests.cs` | Bölüm sırası + eşleşme + ayraç testleri |
| `tests/WharfBar.Tests/ConfigServiceDockTests.cs` | Yumuşak göç + normalize testleri |
| `tests/WharfBar.Tests/DockDropFormatTests.cs` | Bırakma süzgeci testleri |

**Değişen:**
| Dosya | Değişiklik |
|---|---|
| `src/WharfBar/Models/AppConfig.cs` | `DockConfig.Items/Position/Magnification/ShowRunningApps`, `DockPositions` |
| `src/WharfBar/Models/DockAppItem.cs` | `DockSection` enum + kayıt imzası |
| `src/WharfBar/Services/ConfigService.cs` | Test edilebilir dizin, göç, `Dock` normalizasyonu |
| `src/WharfBar/Services/DockAppsComposer.cs` | Tam yeniden yazım |
| `src/WharfBar/Services/RunningAppsSource.cs` | ~~`WindowsFor(id)`~~ → **değişmedi**; menü `MinimizedWindowsSource` önbelleğini kullanıyor (aşağıdaki Task notu) |
| `src/WharfBar/Native/WindowEnumeration.cs` | Filtreyi paylaşılır hale getir, küçültülmüş taraması |
| `src/WharfBar/Views/DockWindow.xaml` | `DockStrip` + ayraç/panel isimlendirmesi |
| `src/WharfBar/Views/DockWindow.xaml.cs` | Panel bağlama, boyut bütçesi, konum, DPI, etkileşim |
| `src/WharfBar/Views/Pages/DockPage.xaml(.cs)` | İçerik kartı + büyütme/konum/çalışan ayarları |
| `docs/`, `.agent/` | ADR-005 revizyonu ve tüm doküman senkronu |

**Silinen:** `src/WharfBar/Services/TaskbarPinnedAppsSource.cs` — yalnızca Task 8'de, referans taraması temiz çıkarsa. *(Tarama temiz çıktı; dosya silindi.)*

---

## Task 1: `DockMagnification` — saf büyütme eğrisi

**Files:**
- Create: `src/WharfBar/Services/DockMagnification.cs`
- Test: `tests/WharfBar.Tests/DockMagnificationTests.cs`

**Interfaces:**
- Consumes: yok
- Produces:
  - `WharfBar.Services.DockMagnification`
  - `const double Default = 1.65d`, `const double Min = 1.0d`, `const double Max = 2.0d`
  - `static double Clamp(double value)`
  - `static double ScaleAtDistance(double distance, double maxScale)`
  - `static int RadiusFor(double maxScale)`
  - `static double Eased(double current, double target, double dtMs)`

> Spec notu: spec'in §7.1'inde listelenen `MaxRadius` sabiti **uygulanmıyor**. Sabit bir üst sınır `maxScale = 2.0` iken `ScaleAtDistance(3, 2.0)` = 1.103 ile `ScaleAtDistance(4, 2.0)` = 1.0 arasında süreksizlik yaratırdı. Bunun yerine yarıçap `RadiusFor(maxScale)` ile hesaplanır ve `1.0` taban kelepçesi düşüşü doğal olarak bitirir.

- [ ] **Step 1: Test dosyasını yaz**

```csharp
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockMagnificationTests
{
    [Fact]
    public void Clamp_WhenValueInRange_ReturnsUnchanged()
    {
        Assert.Equal(1.4d, DockMagnification.Clamp(1.4d), 6);
    }

    [Fact]
    public void Clamp_WhenAboveMax_ReturnsMax()
    {
        Assert.Equal(DockMagnification.Max, DockMagnification.Clamp(5d), 6);
    }

    [Fact]
    public void Clamp_WhenBelowMin_ReturnsMin()
    {
        Assert.Equal(DockMagnification.Min, DockMagnification.Clamp(0.2d), 6);
    }

    [Fact]
    public void Clamp_WhenZeroOrNegative_ReturnsDefault()
    {
        Assert.Equal(DockMagnification.Default, DockMagnification.Clamp(0d), 6);
        Assert.Equal(DockMagnification.Default, DockMagnification.Clamp(-3d), 6);
    }

    [Fact]
    public void ScaleAtDistance_WhenDistanceIsZero_ReturnsMaxScale()
    {
        Assert.Equal(1.65d, DockMagnification.ScaleAtDistance(0d, 1.65d), 6);
    }

    [Fact]
    public void ScaleAtDistance_WhenNegative_ReturnsMaxScale()
    {
        Assert.Equal(1.65d, DockMagnification.ScaleAtDistance(-2d, 1.65d), 6);
    }

    [Fact]
    public void ScaleAtDistance_WhenOneStepAway_DropsToFalloffCurve()
    {
        // 1.65 * 0.82 = 1.353
        Assert.Equal(1.353d, DockMagnification.ScaleAtDistance(1d, 1.65d), 3);
    }

    [Fact]
    public void ScaleAtDistance_WhenFar_IsClampedToRestingScale()
    {
        Assert.Equal(1.0d, DockMagnification.ScaleAtDistance(3d, 1.65d), 6);
        Assert.Equal(1.0d, DockMagnification.ScaleAtDistance(40d, 1.65d), 6);
    }

    [Fact]
    public void ScaleAtDistance_NeverReturnsBelowRestingScale()
    {
        for (double d = 0d; d <= 8d; d += 0.25d)
        {
            foreach (double max in new[] { 1.0d, 1.2d, 1.65d, 2.0d })
            {
                Assert.True(DockMagnification.ScaleAtDistance(d, max) >= 1.0d);
            }
        }
    }

    [Fact]
    public void RadiusFor_WhenMagnificationIsDefault_ReturnsThree()
    {
        Assert.Equal(3, DockMagnification.RadiusFor(1.65d));
    }

    [Fact]
    public void RadiusFor_WhenMagnificationIsOff_ReturnsZero()
    {
        Assert.Equal(0, DockMagnification.RadiusFor(1.0d));
    }

    [Fact]
    public void RadiusFor_IsTheSmallestRadiusThatReachesRestingScale()
    {
        foreach (double max in new[] { 1.05d, 1.3d, 1.65d, 2.0d })
        {
            int r = DockMagnification.RadiusFor(max);
            Assert.True(DockMagnification.ScaleAtDistance(r, max) >= 1.0d - 1e-9d);
            if (r > 0)
            {
                Assert.True(DockMagnification.ScaleAtDistance(r - 1, max) > 1.0d);
            }
        }
    }

    [Fact]
    public void Eased_WhenAlreadyAtTarget_StaysAtTarget()
    {
        Assert.Equal(1.25d, DockMagnification.Eased(1.25d, 1.25d, 16d), 6);
    }

    [Fact]
    public void Eased_WhenNoTimeElapsed_ReturnsCurrent()
    {
        Assert.Equal(1.0d, DockMagnification.Eased(1.0d, 2.0d, 0d), 6);
    }

    [Fact]
    public void Eased_MovesTowardTargetWithoutOvershooting()
    {
        double value = 1.0d;
        for (int i = 0; i < 20; i++)
        {
            value = DockMagnification.Eased(value, 2.0d, 16d);
            Assert.InRange(value, 1.0d, 2.0d);
        }
    }

    [Fact]
    public void Eased_IsFrameRateIndependent()
    {
        // One 48 ms step must land in the same place as three 16 ms steps.
        double single = DockMagnification.Eased(1.0d, 2.0d, 48d);
        double stepped = 1.0d;
        stepped = DockMagnification.Eased(stepped, 2.0d, 16d);
        stepped = DockMagnification.Eased(stepped, 2.0d, 16d);
        stepped = DockMagnification.Eased(stepped, 2.0d, 16d);
        Assert.Equal(single, stepped, 3);
    }

    [Fact]
    public void Eased_WhenTargetIsBelowCurrent_MovesDown()
    {
        Assert.True(DockMagnification.Eased(2.0d, 1.0d, 16d) < 2.0d);
        Assert.True(DockMagnification.Eased(2.0d, 1.0d, 16d) > 1.0d);
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~DockMagnificationTests
```
Beklenen: BUILD FAILED — `DockMagnification` bulunamadı.

- [ ] **Step 3: Uygulamayı yaz**

`src/WharfBar/Services/DockMagnification.cs`:

```csharp
using System;

namespace WharfBar.Services;

/// <summary>
/// Pure magnification math for the dock strip: the distance falloff curve, the
/// influence radius, and a frame-rate-independent easing step.
/// No WPF types — this is the part that must stay unit-testable.
/// </summary>
internal static class DockMagnification
{
    /// <summary>Hover ceiling at the cursor (1.0 disables magnification).</summary>
    public const double Default = 1.65d;

    public const double Min = 1.0d;

    public const double Max = 2.0d;

    /// <summary>Fraction of the previous step's scale kept by each neighbour step.</summary>
    internal const double Falloff = 0.82d;

    /// <summary>Easing time constant; larger = slower catch-up.</summary>
    internal const double EasingTauMs = 55d;

    private const double SettleEpsilon = 1e-4d;

    /// <summary>Clamps a stored/UI value into the supported range. Non-positive means "unset".</summary>
    public static double Clamp(double value) =>
        Math.Clamp(value <= 0d ? Default : value, Min, Max);

    /// <summary>
    /// Scale of the slot <paramref name="distance"/> icons away from the cursor.
    /// Never returns less than the resting scale, so icons never shrink.
    /// </summary>
    public static double ScaleAtDistance(double distance, double maxScale)
    {
        if (distance <= 0d)
        {
            return maxScale;
        }

        double scaled = maxScale * Math.Pow(Falloff, distance);
        return scaled < 1.0d ? 1.0d : scaled;
    }

    /// <summary>
    /// Smallest radius where the falloff reaches the resting scale — i.e. how many
    /// neighbours on each side can ever grow. Drives buffer sizing only.
    /// </summary>
    public static int RadiusFor(double maxScale)
    {
        if (maxScale <= 1.0d)
        {
            return 0;
        }

        double ratio = Math.Log(1.0d / maxScale) / Math.Log(Falloff);
        int radius = (int)Math.Ceiling(ratio - 1e-9d);
        return radius < 0 ? 0 : radius;
    }

    /// <summary>
    /// Exponential smoothing step. Frame-rate independent: one 48 ms step lands
    /// where three 16 ms steps land, so a slow frame does not overshoot.
    /// </summary>
    public static double Eased(double current, double target, double dtMs)
    {
        if (dtMs <= 0d || Math.Abs(target - current) < SettleEpsilon)
        {
            return Math.Abs(target - current) < SettleEpsilon ? target : current;
        }

        double factor = 1.0d - Math.Exp(-dtMs / EasingTauMs);
        return current + ((target - current) * factor);
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~DockMagnificationTests
```
Beklenen: 17 geçen.

- [ ] **Step 5: Tüm testleri çalıştır**

```
dotnet test WharfBar.sln
```
Beklenen: 425 + 17 = 442 geçen, 0 başarısız.

- [ ] **Step 6: Commit**

```
git add src/WharfBar/Services/DockMagnification.cs tests/WharfBar.Tests/DockMagnificationTests.cs
git commit -m "feat(dock): saf buyutme egri ve kare hizindan bagimsiz yumusatma"
```

---

## Task 2: `DockStripLayout` — slot ofsetleri ve pencere boyut bütçesi

**Files:**
- Create: `src/WharfBar/Services/DockStripLayout.cs`
- Test: `tests/WharfBar.Tests/DockStripLayoutTests.cs`

**Interfaces:**
- Consumes: `DockMagnification.ScaleAtDistance`, `DockMagnification.RadiusFor` (Task 1)
- Produces:
  - `WharfBar.Services.DockStripLayout`
  - `static void ComputeScales(int count, double cursorPosition, double phase, double maxScale, double[] into)`
  - `static void ComputeOffsets(int count, double[] baseWidths, double[] scales, double gap, double[] offsets, out double totalWidth)`
  - `static double MaxExtentWidth(int count, double widestSlot, double gap, double maxScale)`
  - `static double MaxExtentHeight(double icon, double indicatorGap, double indicatorSize, double verticalPadding, double maxScale)`

> Tüm dizi parametreleri **dışarıdan sağlanır**; metotlar kare başında ayırma (allocation) yapmaz. `MaxExtentWidth` panel genişliğini döndürür — yatay dolguyu `DockWindow` ekler.

- [ ] **Step 1: Test dosyasını yaz**

```csharp
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockStripLayoutTests
{
    [Fact]
    public void ComputeScales_WhenPhaseIsZero_AllSlotsResting()
    {
        var scales = new double[3];
        DockStripLayout.ComputeScales(3, cursorPosition: 1d, phase: 0d, maxScale: 1.65d, scales);
        Assert.Equal(1d, scales[0], 6);
        Assert.Equal(1d, scales[1], 6);
        Assert.Equal(1d, scales[2], 6);
    }

    [Fact]
    public void ComputeScales_WhenPhaseIsOne_CursorSlotHitsMax()
    {
        var scales = new double[5];
        DockStripLayout.ComputeScales(5, cursorPosition: 2d, phase: 1d, maxScale: 1.65d, scales);
        Assert.Equal(1.65d, scales[2], 6);
    }

    [Fact]
    public void ComputeScales_IsSymmetricAroundFractionalCursor()
    {
        var left = new double[5];
        var right = new double[5];
        DockStripLayout.ComputeScales(5, cursorPosition: 2.5d, phase: 1d, maxScale: 1.65d, left);
        DockStripLayout.ComputeScales(5, cursorPosition: 2.5d, phase: 1d, maxScale: 1.65d, right);
        Assert.Equal(left[1], right[3], 6);
        Assert.Equal(left[2], right[2], 6);
    }

    [Fact]
    public void ComputeScales_WhenCursorBetweenTwoSlots_SplitsTheDifference()
    {
        var scales = new double[2];
        DockStripLayout.ComputeScales(2, cursorPosition: 0.5d, phase: 1d, maxScale: 1.65d, scales);
        // distance 0.5 from both slots -> identical scale on each side
        Assert.Equal(scales[0], scales[1], 6);
        Assert.InRange(scales[0], 1.0d, 1.65d);
    }

    [Fact]
    public void ComputeScales_NeverDipsBelowRestingScale()
    {
        var scales = new double[9];
        for (double pos = -1d; pos <= 9d; pos += 0.37d)
        {
            DockStripLayout.ComputeScales(9, pos, phase: 1d, maxScale: 2.0d, scales);
            foreach (double s in scales)
            {
                Assert.True(s >= 1.0d);
            }
        }
    }

    [Fact]
    public void ComputeOffsets_PlacesSlotsLeftToRight()
    {
        double[] widths = { 28, 28, 28 };
        double[] scales = { 1.65, 1.353, 1.0 };
        var offsets = new double[3];

        DockStripLayout.ComputeOffsets(3, widths, scales, gap: 12d, offsets, out double total);

        Assert.Equal(0d, offsets[0], 6);
        Assert.Equal(28d * 1.65d + 12d, offsets[1], 6);
        Assert.Equal((28d * 1.65d + 12d) + (28d * 1.353d + 12d), offsets[2], 6);
        Assert.Equal(offsets[2] + (28d * 1.0d), total, 6);
    }

    [Fact]
    public void ComputeOffsets_ForEmptyStrip_IsZero()
    {
        var offsets = new double[1];
        DockStripLayout.ComputeOffsets(0, [], [], gap: 12d, offsets, out double total);
        Assert.Equal(0d, total, 6);
    }

    [Fact]
    public void ComputeOffsets_OrdersSlotsStrictlyIncreasing()
    {
        var widths = new double[6];
        var scales = new double[6];
        for (int i = 0; i < 6; i++)
        {
            widths[i] = i == 3 ? 1d : 28d; // slot 3 is a separator
        }

        DockStripLayout.ComputeScales(6, cursorPosition: 0d, phase: 1d, maxScale: 1.65d, scales);
        var offsets = new double[6];
        DockStripLayout.ComputeOffsets(6, widths, scales, gap: 12d, offsets, out double total);

        for (int i = 1; i < 6; i++)
        {
            Assert.True(offsets[i] > offsets[i - 1]);
        }

        Assert.True(total > offsets[5]);
    }

    [Fact]
    public void MaxExtentWidth_WhenEmpty_IsZero()
    {
        Assert.Equal(0d, DockStripLayout.MaxExtentWidth(0, 28d, 12d, 1.65d), 6);
    }

    [Fact]
    public void MaxExtentWidth_MatchesBasePlusBothSidedGrowth()
    {
        // base 6*(28+12)-12 = 228 ; growth 28*0.65 + 2*(28*0.353 + 28*0.1095) = 44.1
        double width = DockStripLayout.MaxExtentWidth(6, widestSlot: 28d, gap: 12d, maxScale: 1.65d);
        Assert.Equal(272.1d, width, 1);
    }

    [Fact]
    public void MaxExtentWidth_ExceedsAnyLiveStripWidth()
    {
        for (int count = 1; count <= 12; count++)
        {
            var widths = new double[count];
            var scales = new double[count];
            double budget = DockStripLayout.MaxExtentWidth(count, 28d, 12d, 1.65d);

            for (double pos = 0d; pos <= count - 1d; pos += 0.25d)
            {
                for (double phase = 0d; phase <= 1.0001d; phase += 0.25d)
                {
                    DockStripLayout.ComputeScales(count, pos, phase, 1.65d, scales);
                    DockStripLayout.ComputeOffsets(count, widths, scales, 12d, new double[count], out double live);
                    Assert.True(
                        live <= budget + 1e-6d,
                        $"count={count} pos={pos} phase={phase} live={live} budget={budget}");
                }
            }
        }
    }

    [Fact]
    public void MaxExtentWidth_WhenMagnificationOff_EqualsBaseWidth()
    {
        Assert.Equal(6d * (28d + 12d) - 12d, DockStripLayout.MaxExtentWidth(6, 28d, 12d, 1.0d), 6);
    }

    [Fact]
    public void MaxExtentHeight_IsPlatePlusIconRise()
    {
        // 2*6 padding + 28 icon + 2 gap + 4 indicator + 28*0.65 rise = 64.2
        double height = DockStripLayout.MaxExtentHeight(
            icon: 28d, indicatorGap: 2d, indicatorSize: 4d, verticalPadding: 6d, maxScale: 1.65d);
        Assert.Equal(64.2d, height, 6);
    }

    [Fact]
    public void MaxExtentHeight_WhenMagnificationOff_EqualsPlateHeight()
    {
        double height = DockStripLayout.MaxExtentHeight(
            icon: 28d, indicatorGap: 2d, indicatorSize: 4d, verticalPadding: 6d, maxScale: 1.0d);
        Assert.Equal(46d, height, 6);
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~DockStripLayoutTests
```
Beklenen: BUILD FAILED — `DockStripLayout` bulunamadı.

- [ ] **Step 3: Uygulamayı yaz**

`src/WharfBar/Services/DockStripLayout.cs`:

```csharp
using System;

namespace WharfBar.Services;

/// <summary>
/// Pure horizontal layout math for the dock strip. All arrays are supplied by the caller
/// so the per-frame path allocates nothing.
/// <para>
/// Two layers, deliberately separate:
/// <list type="bullet">
/// <item><description>slot <b>scale</b> — how much bigger a magnified icon looks (visual only)</description></item>
/// <item><description>slot <b>offset</b> — where neighbours end up once bigger icons push them (reflow)</description></item>
/// </list>
/// </para>
/// </summary>
internal static class DockStripLayout
{
    /// <summary>
    /// Writes the resting-to-magnified scale of every slot into <paramref name="into"/>.
    /// <paramref name="cursorPosition"/> is fractional, so moving between two icons
    /// transfers the peak smoothly instead of snapping.
    /// </summary>
    public static void ComputeScales(int count, double cursorPosition, double phase, double maxScale, double[] into)
    {
        ArgumentNullException.ThrowIfNull(into);

        for (int i = 0; i < count; i++)
        {
            double peak = DockMagnification.ScaleAtDistance(Math.Abs(i - cursorPosition), maxScale);
            into[i] = 1.0d + ((peak - 1.0d) * phase);
        }
    }

    /// <summary>
    /// Writes each slot's left edge into <paramref name="offsets"/> and reports the
    /// live strip width. Gaps sit between every pair of slots.
    /// </summary>
    public static void ComputeOffsets(
        int count,
        double[] baseWidths,
        double[] scales,
        double gap,
        double[] offsets,
        out double totalWidth)
    {
        ArgumentNullException.ThrowIfNull(baseWidths);
        ArgumentNullException.ThrowIfNull(scales);
        ArgumentNullException.ThrowIfNull(offsets);

        double x = 0d;
        for (int i = 0; i < count; i++)
        {
            offsets[i] = x;
            x += (baseWidths[i] * scales[i]) + gap;
        }

        totalWidth = count <= 0 ? 0d : x - gap;
    }

    /// <summary>
    /// Upper bound on the strip width for any cursor position: the resting width plus
    /// every slot that could ever grow. Sizing the window to this is what keeps
    /// <c>SetWindowPos</c> off the per-frame path.
    /// <para>
    /// <paramref name="widestSlot"/> is the icon width, so separator slots are charged
    /// the icon width too — a deliberate over-estimate, never an under-estimate.
    /// </para>
    /// </summary>
    public static double MaxExtentWidth(int count, double widestSlot, double gap, double maxScale)
    {
        if (count <= 0)
        {
            return 0d;
        }

        double resting = (count * (widestSlot + gap)) - gap;
        int radius = DockMagnification.RadiusFor(maxScale);
        double growth = 0d;

        for (int d = 0; d <= radius; d++)
        {
            double slot = widestSlot * (DockMagnification.ScaleAtDistance(d, maxScale) - 1.0d);

            // d == 0 has no mirror partner; every other step can grow both sides.
            growth += d == 0 ? slot : slot * 2.0d;
        }

        return resting + growth;
    }

    /// <summary>
    /// Plate height plus how far the tallest magnified icon rises above it.
    /// The plate itself stays at resting height — only the icon overflows upward.
    /// </summary>
    public static double MaxExtentHeight(
        double icon,
        double indicatorGap,
        double indicatorSize,
        double verticalPadding,
        double maxScale)
    {
        double plate = (2.0d * verticalPadding) + icon + indicatorGap + indicatorSize;
        return plate + (icon * (maxScale - 1.0d));
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~DockStripLayoutTests
```
Beklenen: 14 geçen.

- [ ] **Step 5: Commit**

```
git add src/WharfBar/Services/DockStripLayout.cs tests/WharfBar.Tests/DockStripLayoutTests.cs
git commit -m "feat(dock): saf slot ofsetleri ve pencere boyut butcesi"
```

---

## Task 3: Yapılandırma — bağımsız liste, konum, büyütme ve legacy göç

**Files:**
- Modify: `src/WharfBar/Models/AppConfig.cs` (`DockSurfaceStyles` sonrasına `DockPositions`; `DockConfig`'e 4 alan)
- Modify: `src/WharfBar/Services/ConfigService.cs` (`:31-38` ctor, `:125-148` `CreateDefault`, `:73-89` `LoadOrCreate`, `:253-282` `Normalize`)
- Test: `tests/WharfBar.Tests/ConfigServiceDockTests.cs`

**Interfaces:**
- Consumes: `DockMagnification.Clamp`, `DockMagnification.Default/Min/Max` (Task 1)
- Produces:
  - `WharfBar.Models.DockPositions` — `const string BottomLeft/BottomCenter/BottomRight`, `static string Normalize(string? position)`
  - `DockConfig.Items : List<PinnedApp>`, `DockConfig.Position : string`, `DockConfig.Magnification : double`, `DockConfig.ShowRunningApps : bool`
  - `ConfigService(string? configDirectory = null)` — test edilebilir dizin
  - `ConfigService.NeedsDockItemsMigration(AppConfig loaded) : bool`
  - `ConfigService.MigrateDockItems(AppConfig config) : AppConfig`
  - `ConfigService.NormalizePinnedList(List<PinnedApp>? items)` (eski `private` gövden `internal static` olur)

> **Göç kuralı:** `dock.items` boş **ve** `pinnedApps` doluysa `pinnedApps` → `dock.items` kopyalanır **ve `pinnedApps` boşaltılır**. Boşaltma şarttır: aksi hâlde kullanıcı dock'u bilerek boşalttığında bir sonraki açılışta demo pinleri geri gelir. `schemaVersion` **2'de kalır**.

- [ ] **Step 1: Test dosyasını yaz**

```csharp
using System;
using System.IO;
using System.Linq;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ConfigServiceDockTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-dock-config-tests", Guid.NewGuid().ToString("N"));

    private string ConfigFile => Path.Combine(_root, "WharfBar", "config.json");

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

    private ConfigService NewService() => new(_root);

    private static AppConfig LegacyConfig()
    {
        var config = ConfigService.CreateDefault();
        config.Dock.Items = [];
        config.PinnedApps =
        [
            new PinnedApp("C:\\Windows\\System32\\notepad.exe", string.Empty, "Not Defteri"),
        ];
        return config;
    }

    [Fact]
    public void NeedsDockItemsMigration_WhenDockEmptyAndLegacySeeded_IsTrue()
    {
        Assert.True(ConfigService.NeedsDockItemsMigration(LegacyConfig()));
    }

    [Fact]
    public void NeedsDockItemsMigration_WhenDockAlreadyPopulated_IsFalse()
    {
        AppConfig config = LegacyConfig();
        config.Dock.Items = [new PinnedApp("C:\\a.exe", string.Empty, "A")];
        Assert.False(ConfigService.NeedsDockItemsMigration(config));
    }

    [Fact]
    public void NeedsDockItemsMigration_WhenLegacyEmpty_IsFalse()
    {
        var config = ConfigService.CreateDefault();
        config.Dock.Items = [];
        Assert.False(ConfigService.NeedsDockItemsMigration(config));
    }

    [Fact]
    public void MigrateDockItems_CopiesLegacyEntriesIntoDock()
    {
        AppConfig config = ConfigService.MigrateDockItems(LegacyConfig());
        Assert.Single(config.Dock.Items);
        Assert.Equal("C:\\Windows\\System32\\notepad.exe", config.Dock.Items[0].Path);
    }

    [Fact]
    public void MigrateDockItems_ClearsLegacyListSoPinsDoNotResurrect()
    {
        AppConfig config = ConfigService.MigrateDockItems(LegacyConfig());
        Assert.Empty(config.PinnedApps);
    }

    [Fact]
    public void MigrateDockItems_WhenDockPopulated_LeavesLegacyUntouched()
    {
        AppConfig config = LegacyConfig();
        config.Dock.Items = [new PinnedApp("C:\\a.exe", string.Empty, "A")];
        ConfigService.MigrateDockItems(config);
        Assert.Single(config.PinnedApps);
    }

    [Fact]
    public void Normalize_ClampsMagnificationAboveMax()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Magnification = 9d;
        service.Save(service.Current);
        Assert.Equal(DockMagnification.Max, service.Current.Dock.Magnification, 6);
    }

    [Fact]
    public void Normalize_ClampsMagnificationBelowMin()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Magnification = 0.25d;
        service.Save(service.Current);
        Assert.Equal(DockMagnification.Min, service.Current.Dock.Magnification, 6);
    }

    [Fact]
    public void Normalize_TreatsNonPositiveMagnificationAsDefault()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Magnification = -1d;
        service.Save(service.Current);
        Assert.Equal(DockMagnification.Default, service.Current.Dock.Magnification, 6);
    }

    [Fact]
    public void Normalize_FallsBackToBottomCenterForUnknownPosition()
    {
        Assert.Equal(DockPositions.BottomCenter, DockPositions.Normalize("middle-ish"));
        Assert.Equal(DockPositions.BottomCenter, DockPositions.Normalize(null));
    }

    [Fact]
    public void Normalize_PreservesLeftAndRightPositions()
    {
        Assert.Equal(DockPositions.BottomLeft, DockPositions.Normalize("Bottom-Left"));
        Assert.Equal(DockPositions.BottomRight, DockPositions.Normalize("bottom-right"));
    }

    [Fact]
    public void Normalize_DropsDockItemsWithoutPath()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Items =
        [
            new PinnedApp("   ", string.Empty, "Boş"),
            new PinnedApp("C:\\a.exe", string.Empty, "A"),
        ];
        service.Save(service.Current);
        Assert.Single(service.Current.Dock.Items);
        Assert.Equal("C:\\a.exe", service.Current.Dock.Items[0].Path);
    }

    [Fact]
    public void Normalize_FillsMissingDockItemLabelFromFileName()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Items = [new PinnedApp("C:\\tools\\thing.exe", string.Empty, "   ")];
        service.Save(service.Current);
        Assert.Equal("thing", service.Current.Dock.Items[0].Label);
    }

    [Fact]
    public void Normalize_TrimsDockItemPathAndArgs()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Items = [new PinnedApp("  C:\\a.exe  ", "  --flag  ", "  A  ")];
        service.Save(service.Current);
        Assert.Equal("C:\\a.exe", service.Current.Dock.Items[0].Path);
        Assert.Equal("--flag", service.Current.Dock.Items[0].Args);
        Assert.Equal("A", service.Current.Dock.Items[0].Label);
    }

    [Fact]
    public void CreateDefault_SeedsDockItemsFromDemoPins()
    {
        AppConfig config = ConfigService.CreateDefault();
        Assert.NotEmpty(config.Dock.Items);
        Assert.Empty(config.PinnedApps);
    }

    [Fact]
    public void LoadOrCreate_MigratesLegacyListAndPersistsIt()
    {
        // Note: no File.Delete here. Deleting the file would turn the reopen into a fresh
        // install, which seeds dock.items from DemoPinnedApps and never exercises the migration.
        NewService().Save(LegacyConfig());

        ConfigService reopened = NewService();
        AppConfig loaded = reopened.LoadOrCreate();

        Assert.Single(loaded.Dock.Items);
        Assert.Empty(loaded.PinnedApps);

        // Migration is persisted, so a second open does not need to redo it.
        ConfigService third = NewService();
        Assert.Single(third.LoadOrCreate().Dock.Items);
    }

    [Fact]
    public void LoadOrCreate_DoesNotResurrectPinsAfterUserEmptiesDock()
    {
        NewService().Save(LegacyConfig());

        ConfigService first = NewService();
        first.LoadOrCreate();
        first.Current.Dock.Items = [];
        first.Save(first.Current);

        ConfigService second = NewService();
        Assert.Empty(second.LoadOrCreate().Dock.Items);
    }

    [Fact]
    public void LoadOrCreate_OnFreshInstallWritesDockItems()
    {
        ConfigService service = NewService();
        AppConfig loaded = service.LoadOrCreate();
        Assert.True(File.Exists(ConfigFile));
        Assert.Equal(loaded.Dock.Items.Count, service.Current.Dock.Items.Count);
        Assert.All(loaded.Dock.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Path)));
    }

    [Fact]
    public void DemoPins_AreAllUsablePaths()
    {
        // The seeded dock must not point at files that do not exist on a real machine.
        foreach (PinnedApp pin in DemoPinnedApps.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(pin.Path));
            Assert.True(pin.Label.Trim().Length > 0);
        }
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~ConfigServiceDockTests
```
Beklenen: BUILD FAILED — `DockPositions` bulunamadı.

- [ ] **Step 3: `DockPositions` ve `DockConfig` alanlarını ekle**

`src/WharfBar/Models/AppConfig.cs` — `DockSurfaceStyles` sınıfının kapanış süslüğünden hemen sonra ekle:

```csharp
/// <summary>Dock anchor along the monitor bottom edge (<see cref="DockConfig.Position"/>).</summary>
internal static class DockPositions
{
    internal const string BottomLeft = "bottom-left";

    internal const string BottomCenter = "bottom-center";

    internal const string BottomRight = "bottom-right";

    internal static string Normalize(string? position)
    {
        if (string.Equals(position, BottomLeft, StringComparison.OrdinalIgnoreCase))
        {
            return BottomLeft;
        }

        if (string.Equals(position, BottomRight, StringComparison.OrdinalIgnoreCase))
        {
            return BottomRight;
        }

        return BottomCenter;
    }
}
```

`DockConfig` içinde, `Surface` özelliğinden sonra:

```csharp
    /// <summary>
    /// The dock's own ordered, persisted item list. Display order equals list order.
    /// This is the single runtime source; the Windows taskbar is never read or written.
    /// </summary>
    public List<PinnedApp> Items { get; set; } = [];

    /// <summary>Bottom anchor: <c>bottom-left</c>, <c>bottom-center</c>, or <c>bottom-right</c>.</summary>
    public string Position { get; set; } = DockPositions.BottomCenter;

    /// <summary>Hover magnification ceiling; 1.0 disables magnification.</summary>
    public double Magnification { get; set; } = DockMagnification.Default;

    /// <summary>When false, running-but-unpinned apps and minimized windows are hidden.</summary>
    public bool ShowRunningApps { get; set; } = true;
```

`Models/AppConfig.cs` en üste `using WharfBar.Services;` ekle.

- [ ] **Step 4: `ConfigService`'i yeni alanlara uyarlamak için değiştir**

`ConfigService.cs:31-38` — ctor:

```csharp
    internal ConfigService(string? configDirectory = null)
    {
        ConfigDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WharfBar");
        ConfigPath = Path.Combine(ConfigDirectory, "config.json");
        Current = CreateDefault();
    }
```

`ConfigService.cs:125-148` — `CreateDefault` içinde `Dock` bloğunu ve `PinnedApps` satırını değiştir:

```csharp
            Dock = new DockConfig
            {
                AutoHide = true,
                IconSize = DockConfig.DefaultIconSize,
                Items = DemoPinnedApps.All
                    .Select(static a => new PinnedApp(a.Path, a.Args, a.Label))
                    .ToList(),
            },
            // Legacy seed list: emptied by MigrateDockItems, never read again.
            PinnedApps = [],
```

`ConfigService.cs:73` civarı — `LoadOrCreate` içinde, `double originalIconSize` satırından önce:

```csharp
            bool dockItemsMigrated = NeedsDockItemsMigration(loaded);
            if (dockItemsMigrated)
            {
                MigrateDockItems(loaded);
            }
```

`LoadOrCreate` içindeki kaydetme koşulunu güncelle:

```csharp
            if (iconChanged || statusChanged || catalogChanged || veilKeyMissing
                || islandKeyMissing || monitorsChanged || dockItemsMigrated)
```

`CreateDefault` metodunun hemen ardından ekle:

```csharp
    /// <summary>
    /// True when the legacy root <see cref="AppConfig.PinnedApps"/> list still holds entries
    /// and the dock list is empty — i.e. the one-time hand-off is pending.
    /// </summary>
    internal static bool NeedsDockItemsMigration(AppConfig loaded) =>
        loaded.Dock?.Items is null or { Count: 0 }
        && loaded.PinnedApps is { Count: > 0 };

    /// <summary>
    /// Moves the legacy root pin list into <see cref="DockConfig.Items"/> and clears the
    /// legacy list. Clearing is what stops a deliberately emptied dock from resurrecting
    /// the old pins on the next launch.
    /// </summary>
    internal static AppConfig MigrateDockItems(AppConfig config)
    {
        config.Dock ??= new DockConfig();
        config.Dock.Items ??= [];

        if (config.Dock.Items.Count == 0 && config.PinnedApps is { Count: > 0 } legacy)
        {
            foreach (PinnedApp? app in legacy)
            {
                if (app is not null && !string.IsNullOrWhiteSpace(app.Path))
                {
                    config.Dock.Items.Add(new PinnedApp(app.Path, app.Args ?? string.Empty, app.Label ?? string.Empty));
                }
            }
        }

        config.PinnedApps = [];
        return config;
    }
```

`ConfigService.cs:265-282` — `Normalize` içindeki `Surface` satırından sonrasını değiştir. `config.Dock.Surface = ...` satırını koru, ardından ekle:

```csharp
        config.Dock.Items ??= [];
        config.Dock.Position = DockPositions.Normalize(config.Dock.Position);
        config.Dock.Magnification = DockMagnification.Clamp(config.Dock.Magnification);
        config.PinnedApps ??= [];
```

ve mevcut `for` döngüsünü (`:269-282`) silip yerine:

```csharp
        NormalizePinnedList(config.PinnedApps);
        NormalizePinnedList(config.Dock.Items);
```

`ConfigService` sınıfının sonuna ekle:

```csharp
    /// <summary>
    /// Drops entries without a path, trims path/args, and derives a missing label from the
    /// file name. Shared by the legacy root list and <see cref="DockConfig.Items"/>.
    /// </summary>
    internal static void NormalizePinnedList(List<PinnedApp>? items)
    {
        if (items is null)
        {
            return;
        }

        for (int i = items.Count - 1; i >= 0; i--)
        {
            PinnedApp? app = items[i];
            if (app is null || string.IsNullOrWhiteSpace(app.Path))
            {
                items.RemoveAt(i);
                continue;
            }

            items[i] = new PinnedApp(
                app.Path.Trim(),
                app.Args ?? string.Empty,
                string.IsNullOrWhiteSpace(app.Label)
                    ? Path.GetFileNameWithoutExtension(app.Path)
                    : app.Label.Trim());
        }
    }
```

- [ ] **Step 5: Testlerin geçtiğini doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~ConfigServiceDockTests
```
Beklenen: 19 geçen.

- [ ] **Step 6: Tüm testler**

```
dotnet test WharfBar.sln
```
Beklenen: 475 geçen, 0 başarısız. (`DockAppsComposer` henüz `PinnedApps`'ı okumaya devam eder — `CreateDefault` artık onu boş bıraktığı için mevcut davranış "canlı yok → fallback boş" olur. Bu Task 6'da düzeltilir; ara adımda derleme ve testler yeşil olmalıdır.)

- [ ] **Step 7: Doküman**

`docs/01-architecture.md` şema tablosuna (`dock.*` satırlarının yanına) ekle:

```
| `dock.items` | macOS tarzı sıralı sabit liste (Path/Args/Label) |
| `dock.position` | `bottom-left` \| `bottom-center` \| `bottom-right` |
| `dock.magnification` | Hover tavanı 1.0–2.0 (varsayılan 1.65) |
| `dock.showRunningApps` | Sabitlenmemiş çalışanlar + küçültülmüş pencereler |
```

- [ ] **Step 8: Commit**

```
git add src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs tests/WharfBar.Tests/ConfigServiceDockTests.cs docs/01-architecture.md
git commit -m "feat(dock): bagimsiz liste, konum ve buyutme ayarlari + legacy pin gocu"
```

---

## Task 4: `DockItemStore` — sıralı, tekilleştirilmiş, kalıcı sabit liste

**Files:**
- Create: `src/WharfBar/Services/DockItemStore.cs`
- Test: `tests/WharfBar.Tests/DockItemStoreTests.cs`

**Interfaces:**
- Consumes: `ConfigService(string? configDirectory)`, `ConfigService.Save`, `PinnedApp` (Task 3)
- Produces:
  - `WharfBar.Services.DockItemStore(ConfigService config)`
  - `IReadOnlyList<PinnedApp> Items`, `event EventHandler? Changed`
  - `bool TryAdd(string path, string args, string label)`, `bool Remove(string id)`, `bool Move(string id, int toIndex)`, `void Reload()`
  - statik çekirdekler (test edilebilir): `static string Normalize(string path)`, `static string IdFor(PinnedApp app)`, `static bool ContainsById(IReadOnlyList<PinnedApp> items, string id)`, `static bool TryAddCore(List<PinnedApp> items, string path, string args, string label)`, `static bool RemoveCore(List<PinnedApp> items, string id)`, `static bool MoveCore(List<PinnedApp> items, string id, int toIndex)`

> Kalıcılık testleri `ConfigService`'in geçici dizin ctor'unu (Task 3) kullanır — `IslandStoreTests` ile aynı desen. Liste işlemlerinin statik çekirdekleri doğrudan test edilir.

- [ ] **Step 1: Test dosyasını yaz**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockItemStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-dock-item-store-tests", Guid.NewGuid().ToString("N"));

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

    private static DockItemStore NewStore(out ConfigService config)
    {
        config = new ConfigService(_root);
        config.LoadOrCreate();
        return new DockItemStore(config);
    }

    private static List<PinnedApp> Three() =>
    [
        new PinnedApp(@"C:\apps\one.exe", string.Empty, "One"),
        new PinnedApp(@"C:\apps\two.exe", string.Empty, "Two"),
        new PinnedApp(@"C:\apps\three.exe", string.Empty, "Three"),
    ];

    [Fact]
    public void Normalize_WhenPathIsAbsolute_TrimsAndKeepsFullPath()
    {
        Assert.Equal(@"C:\apps\one.exe", DockItemStore.Normalize(@"  C:\apps\one.exe  "));
    }

    [Fact]
    public void Normalize_WhenPathIsMalformed_FallsBackToTrimmedInput()
    {
        Assert.Equal("   ", DockItemStore.Normalize("   ").Trim());
        Assert.False(string.IsNullOrWhiteSpace(DockItemStore.Normalize("relative.exe")));
    }

    [Fact]
    public void IdFor_UsesNormalizedPath()
    {
        Assert.Equal(
            DockItemStore.Normalize(@"C:\apps\one.exe"),
            DockItemStore.IdFor(new PinnedApp(@"  C:\apps\one.exe", string.Empty, "One")));
    }

    [Fact]
    public void ContainsById_IgnoresCaseAndSurroundingSpace()
    {
        List<PinnedApp> items = Three();
        Assert.True(DockItemStore.ContainsById(items, "  c:\\APPS\\ONE.EXE "));
        Assert.False(DockItemStore.ContainsById(items, @"C:\apps\four.exe"));
    }

    [Fact]
    public void TryAddCore_WhenNew_AppendsWithNormalizedPath()
    {
        List<PinnedApp> items = [];
        Assert.True(DockItemStore.TryAddCore(items, @"  C:\apps\one.exe ", string.Empty, "One"));
        Assert.Equal(@"C:\apps\one.exe", Assert.Single(items).Path);
    }

    [Fact]
    public void TryAddCore_WhenDuplicatePath_IsRejected()
    {
        List<PinnedApp> items = Three();
        Assert.False(DockItemStore.TryAddCore(items, @"C:\apps\two.exe", string.Empty, "Two"));
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void TryAddCore_WhenDuplicateDiffersOnlyByCase_IsRejected()
    {
        List<PinnedApp> items = Three();
        Assert.False(DockItemStore.TryAddCore(items, @"C:\APPS\TWO.EXE", string.Empty, "Two"));
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void TryAddCore_WhenLabelBlank_DerivesLabelFromFileName()
    {
        List<PinnedApp> items = [];
        DockItemStore.TryAddCore(items, @"C:\tools\thing.exe", string.Empty, "   ");
        Assert.Equal("thing", Assert.Single(items).Label);
    }

    [Fact]
    public void TryAddCore_WhenPathBlank_IsRejected()
    {
        List<PinnedApp> items = [];
        Assert.False(DockItemStore.TryAddCore(items, "   ", string.Empty, "Yok"));
        Assert.Empty(items);
    }

    [Fact]
    public void RemoveCore_WhenPresent_Removes()
    {
        List<PinnedApp> items = Three();
        Assert.True(DockItemStore.RemoveCore(items, DockItemStore.IdFor(items[1])));
        Assert.Equal(["One", "Three"], items.Select(i => i.Label));
    }

    [Fact]
    public void RemoveCore_WhenAbsent_ReturnsFalse()
    {
        List<PinnedApp> items = Three();
        Assert.False(DockItemStore.RemoveCore(items, @"C:\apps\nope.exe"));
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void MoveCore_MovesForwardPastTheEnd_AppendsLast()
    {
        List<PinnedApp> items = Three();
        Assert.True(DockItemStore.MoveCore(items, DockItemStore.IdFor(items[0]), 99));
        Assert.Equal(["Two", "Three", "One"], items.Select(i => i.Label));
    }

    [Fact]
    public void MoveCore_MovesBackwardToStart()
    {
        List<PinnedApp> items = Three();
        Assert.True(DockItemStore.MoveCore(items, DockItemStore.IdFor(items[2]), 0));
        Assert.Equal(["Three", "One", "Two"], items.Select(i => i.Label));
    }

    [Fact]
    public void MoveCore_WhenTargetIndexNegative_PutsFirst()
    {
        List<PinnedApp> items = Three();
        DockItemStore.MoveCore(items, DockItemStore.IdFor(items[1]), -5);
        Assert.Equal("Two", items[0].Label);
    }

    [Fact]
    public void MoveCore_WhenIdAbsent_ReturnsFalseAndKeepsOrder()
    {
        List<PinnedApp> items = Three();
        Assert.False(DockItemStore.MoveCore(items, @"C:\apps\nope.exe", 0));
        Assert.Equal(["One", "Two", "Three"], items.Select(i => i.Label));
    }

    [Fact]
    public void TryAdd_PersistsAndRaisesChanged()
    {
        DockItemStore store = NewStore(out ConfigService config);
        int changes = 0;
        store.Changed += (_, _) => changes++;
        int before = store.Items.Count;

        Assert.True(store.TryAdd(@"C:\apps\added.exe", "--x", "Added"));

        Assert.Equal(before + 1, store.Items.Count);
        Assert.Equal(1, changes);
        Assert.Equal(before + 1, new DockItemStore(config).Items.Count);
    }

    [Fact]
    public void TryAdd_WhenDuplicate_DoesNotRaiseChanged()
    {
        DockItemStore store = NewStore(out _);
        string existing = store.Items[0].Path;
        int changes = 0;
        store.Changed += (_, _) => changes++;

        Assert.False(store.TryAdd(existing, string.Empty, "Yine"));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Remove_PersistsRemoval()
    {
        DockItemStore store = NewStore(out ConfigService config);
        string target = store.Items[1].Path;
        int before = store.Items.Count;

        Assert.True(store.Remove(DockItemStore.IdFor(store.Items[1])));

        Assert.Equal(before - 1, store.Items.Count);
        Assert.DoesNotContain(new DockItemStore(config).Items, i => i.Path == target);
    }

    [Fact]
    public void Move_PersistsNewOrder()
    {
        DockItemStore store = NewStore(out ConfigService config);
        string lastPath = store.Items[^1].Path;

        Assert.True(store.Move(DockItemStore.IdFor(store.Items[^1]), 0));

        Assert.Equal(lastPath, store.Items[0].Path);
        Assert.Equal(lastPath, new DockItemStore(config).Items[0].Path);
    }

    [Fact]
    public void Reload_PicksUpExternalChange()
    {
        DockItemStore store = NewStore(out ConfigService config);
        config.Current.Dock.Items = [];
        config.Save(config.Current);

        Assert.NotEmpty(store.Items);
        store.Reload();
        Assert.Empty(store.Items);
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~DockItemStoreTests
```
Beklenen: BUILD FAILED — `DockItemStore` bulunamadı.

- [ ] **Step 3: Uygulamayı yaz**

`src/WharfBar/Services/DockItemStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Owns the dock's ordered, de-duplicated, persisted pinned list.
/// <para>
/// List operations are pure statics (<c>*Core</c>) so they can be unit-tested without
/// touching disk; the instance methods add persistence and change notification on top.
/// </para>
/// </summary>
internal sealed class DockItemStore
{
    private readonly ConfigService _config;
    private List<PinnedApp> _items;

    internal DockItemStore(ConfigService config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _items = Snapshot(config.Current.Dock.Items);
    }

    internal IReadOnlyList<PinnedApp> Items => _items;

    internal event EventHandler? Changed;

    /// <summary>Canonical comparison key for an item's target path.</summary>
    internal static string Normalize(string path)
    {
        string trimmed = path?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or IOException
            or System.Security.SecurityException)
        {
            return trimmed;
        }
    }

    internal static string IdFor(PinnedApp app) => Normalize(app.Path);

    internal static bool ContainsById(IReadOnlyList<PinnedApp> items, string id)
    {
        string key = Normalize(id);
        foreach (PinnedApp item in items)
        {
            if (string.Equals(IdFor(item), key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryAddCore(List<PinnedApp> items, string path, string args, string label)
    {
        string normalized = Normalize(path);
        if (normalized.Length == 0 || ContainsById(items, normalized))
        {
            return false;
        }

        string resolvedLabel = string.IsNullOrWhiteSpace(label)
            ? Path.GetFileNameWithoutExtension(normalized)
            : label.Trim();

        items.Add(new PinnedApp(normalized, args?.Trim() ?? string.Empty, resolvedLabel));
        return true;
    }

    internal static bool RemoveCore(List<PinnedApp> items, string id)
    {
        int index = IndexOfId(items, id);
        if (index < 0)
        {
            return false;
        }

        items.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Moves the item to <paramref name="toIndex"/> in the resulting list. Indices past
    /// either end clamp, so a drop beyond the last icon appends instead of failing.
    /// </summary>
    internal static bool MoveCore(List<PinnedApp> items, string id, int toIndex)
    {
        int index = IndexOfId(items, id);
        if (index < 0)
        {
            return false;
        }

        PinnedApp moved = items[index];
        items.RemoveAt(index);
        items.Insert(Math.Clamp(toIndex, 0, items.Count), moved);
        return true;
    }

    internal bool TryAdd(string path, string args, string label)
    {
        if (!TryAddCore(_items, path, args, label))
        {
            return false;
        }

        Persist();
        return true;
    }

    internal bool Remove(string id)
    {
        if (!RemoveCore(_items, id))
        {
            return false;
        }

        Persist();
        return true;
    }

    internal bool Move(string id, int toIndex)
    {
        if (!MoveCore(_items, id, toIndex))
        {
            return false;
        }

        Persist();
        return true;
    }

    internal void Reload() => _items = Snapshot(_config.Current.Dock.Items);

    private static List<PinnedApp> Snapshot(IReadOnlyList<PinnedApp> source)
    {
        var copy = new List<PinnedApp>(source?.Count ?? 0);
        if (source is null)
        {
            return copy;
        }

        foreach (PinnedApp app in source)
        {
            if (app is not null && !string.IsNullOrWhiteSpace(app.Path))
            {
                copy.Add(new PinnedApp(app.Path, app.Args ?? string.Empty, app.Label ?? string.Empty));
            }
        }

        return copy;
    }

    private static int IndexOfId(IReadOnlyList<PinnedApp> items, string id)
    {
        string key = Normalize(id);
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(IdFor(items[i]), key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void Persist()
    {
        _config.Current.Dock.Items = new List<PinnedApp>(_items);
        _config.Save(_config.Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 4: Testlerin geçtiğini doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~DockItemStoreTests
```
Beklenen: 20 geçen.

- [ ] **Step 5: Commit**

```
git add src/WharfBar/Services/DockItemStore.cs tests/WharfBar.Tests/DockItemStoreTests.cs
git commit -m "feat(dock): sirali, tekillestirilmis kalici sabit liste"
```

---

## Task 5: Küçültülmüş pencereler — `MinimizedWindowsSource` ve paylaşılan pencere taraması

**Files:**
- Create: `src/WharfBar/Services/MinimizedWindowsSource.cs`
- Create: `tests/WharfBar.Tests/MinimizedWindowsSourceTests.cs`
- Modify: `src/WharfBar/Native/WindowEnumeration.cs` (`:22-37` yeni `GetMinimizedCandidates`, `IsCandidate` paylaşımlı kalır)

**Interfaces:**
- Consumes: `WindowEnumeration.GetTopLevelCandidates()`, `NativeMethods.IsIconic`
- Produces:
  - `internal sealed record AppWindowInfo(IntPtr Handle, string Title, uint Pid, bool IsMinimized)`
  - `WharfBar.Services.MinimizedWindowsSource(Dispatcher dispatcher)`
  - `IReadOnlyList<AppWindowInfo> Windows`, `IReadOnlyList<AppWindowInfo> Minimized`, `event EventHandler? Changed`, `void Refresh()`, `void Dispose()`
  - statik saf yardımcılar: `static IReadOnlyList<AppWindowInfo> SelectMinimized(IReadOnlyList<AppWindowInfo> windows)`, `static IReadOnlyList<AppWindowInfo> ForProcess(IReadOnlyList<AppWindowInfo> windows, uint pid)`
  - `WharfBar.Native.WindowEnumeration.GetMinimizedCandidates()`

> **Spec sapması (gerekçeli):** spec §6.1 `RunningAppsSource.WindowsFor(id)` diyordu. Bu **uygulanmıyor**; `WindowsFor` bir bağlam menüsü açılışında, yani **UI iş parçacığında** çağrılacak ve `EnumWindows` donmuş HWND'de UI'yi kilitler (`WindowEnumeration.cs:17-21` uyarısı). Bunun yerine `MinimizedWindowsSource` zaten 800 ms'de bir kez tüm pencere tablosunu **arka iş parçacığında** tarar; menü bu önbellekten filtreler. `RunningAppsSource` **değişmez**.

- [ ] **Step 1: Test dosyasını yaz**

```csharp
using System.Collections.Generic;
using System.Linq;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MinimizedWindowsSourceTests
{
    private static AppWindowInfo W(int id, uint pid, bool minimized, string title) =>
        new((IntPtr)id, title, pid, minimized);

    [Fact]
    public void SelectMinimized_ReturnsOnlyIconicWindows()
    {
        List<AppWindowInfo> windows = [W(1, 10, true, "A"), W(2, 20, false, "B")];
        AppWindowInfo[] result = [.. MinimizedWindowsSource.SelectMinimized(windows)];
        Assert.Equal([(IntPtr)1], result.Select(w => w.Handle));
    }

    [Fact]
    public void SelectMinimized_WhenNoneMinimized_IsEmpty()
    {
        List<AppWindowInfo> windows = [W(1, 10, false, "A")];
        Assert.Empty(MinimizedWindowsSource.SelectMinimized(windows));
    }

    [Fact]
    public void SelectMinimized_PreservesScanOrder()
    {
        List<AppWindowInfo> windows =
        [
            W(5, 10, true, "Z"),
            W(6, 20, false, "Y"),
            W(7, 30, true, "X"),
        ];
        Assert.Equal(
            [(IntPtr)5, (IntPtr)7],
            MinimizedWindowsSource.SelectMinimized(windows).Select(w => w.Handle));
    }

    [Fact]
    public void ForProcess_ReturnsEveryWindowOfThatProcessIncludingMinimized()
    {
        List<AppWindowInfo> windows =
        [
            W(1, 10, false, "Main"),
            W(2, 10, true, "Child"),
            W(3, 20, false, "Other"),
        ];
        IntPtr[] result = [.. MinimizedWindowsSource.ForProcess(windows, 10).Select(w => w.Handle)];
        Assert.Equal([(IntPtr)1, (IntPtr)2], result);
    }

    [Fact]
    public void ForProcess_WhenPidIsZero_ReturnsEmpty()
    {
        List<AppWindowInfo> windows = [W(1, 10, false, "Main")];
        Assert.Empty(MinimizedWindowsSource.ForProcess(windows, 0));
    }

    [Fact]
    public void ForProcess_WhenUnknownPid_ReturnsEmpty()
    {
        List<AppWindowInfo> windows = [W(1, 10, false, "Main")];
        Assert.Empty(MinimizedWindowsSource.ForProcess(windows, 999));
    }

    [Fact]
    public void ForProcess_OrdersMinimizedLastForStableDockOrder()
    {
        List<AppWindowInfo> windows =
        [
            W(1, 10, true, "Min"),
            W(2, 10, false, "Main"),
        ];
        Assert.Equal(
            [(IntPtr)2, (IntPtr)1],
            MinimizedWindowsSource.ForProcess(windows, 10).Select(w => w.Handle));
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~MinimizedWindowsSourceTests
```
Beklenen: BUILD FAILED — `MinimizedWindowsSource` bulunamadı.

- [ ] **Step 3: `WindowEnumeration`'a küçültülmüş taramayı ekle**

`src/WharfBar/Native/WindowEnumeration.cs` — `GetTopLevelCandidates` metodundan sonra ekle:

```csharp
    /// <summary>
    /// Iconic top-level windows. Minimized windows keep WS_VISIBLE, so they also pass
    /// <see cref="IsCandidate"/> — the extra check is what makes this list minimal.
    /// </summary>
    internal static IReadOnlyList<(IntPtr Hwnd, string Title, uint Pid)> GetMinimizedCandidates()
    {
        var results = new List<(IntPtr Hwnd, string Title, uint Pid)>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (NativeMethods.IsIconic(hwnd) && IsCandidate(hwnd, out string title, out uint pid))
            {
                results.Add((hwnd, title, pid));
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }
```

> `IsCandidate` `private static` kalır — aynı dosyada kullanılıyor, dışarı açmaya gerek yok. `docs/02-win32-appbar-dwm.md`'ye `IsIconic` imzası için bir not ekle.

- [ ] **Step 4: `MinimizedWindowsSource` uygulamasını yaz**

`src/WharfBar/Services/MinimizedWindowsSource.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>One top-level window, captured during the off-thread scan.</summary>
internal sealed record AppWindowInfo(IntPtr Handle, string Title, uint Pid, bool IsMinimized);

/// <summary>
/// Maintains the full top-level window table off the UI thread and derives the
/// minimized subset the dock shows as its own section.
/// <para>
/// The whole table is cached so the dock's "Pencereler" submenu can filter it on the UI
/// thread without calling <c>EnumWindows</c> (which stalls on a hung HWND).
/// </para>
/// </summary>
internal sealed class MinimizedWindowsSource : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(800);

    private readonly Dispatcher _dispatcher;
    private List<AppWindowInfo> _windows = [];
    private List<AppWindowInfo> _minimized = [];
    private DispatcherTimer? _poll;
    private bool _disposed;
    private int _refreshBusy;

    internal MinimizedWindowsSource(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _poll = new DispatcherTimer { Interval = PollInterval };
        _poll.Tick += (_, _) => RequestRefresh();
        _poll.Start();
        RequestRefresh();
    }

    internal IReadOnlyList<AppWindowInfo> Windows => _windows;

    internal IReadOnlyList<AppWindowInfo> Minimized => _minimized;

    internal event EventHandler? Changed;

    internal void Refresh() => RequestRefresh();

    internal static IReadOnlyList<AppWindowInfo> SelectMinimized(IReadOnlyList<AppWindowInfo> windows) =>
        windows is null ? [] : [.. windows.Where(static w => w.IsMinimized)];

    /// <summary>
    /// Every window owned by <paramref name="pid"/>, visible first so the dock's menu
    /// shows the main window above the minimized ones.
    /// </summary>
    internal static IReadOnlyList<AppWindowInfo> ForProcess(IReadOnlyList<AppWindowInfo> windows, uint pid)
    {
        if (windows is null || pid == 0)
        {
            return [];
        }

        return [.. windows.Where(w => w.Pid == pid).OrderBy(static w => w.IsMinimized)];
    }

    private void RequestRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshBusy, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                List<AppWindowInfo> next = Scan();
                if (_disposed)
                {
                    return;
                }

                _ = _dispatcher.BeginInvoke(() => ApplyScan(next));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar minimized-windows scan failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshBusy, 0);
            }
        });
    }

    private static List<AppWindowInfo> Scan()
    {
        var all = new List<AppWindowInfo>();

        foreach (var (hwnd, title, pid) in WindowEnumeration.GetTopLevelCandidates())
        {
            all.Add(new AppWindowInfo(hwnd, title, pid, NativeMethods.IsIconic(hwnd)));
        }

        return all;
    }

    private void ApplyScan(List<AppWindowInfo> next)
    {
        if (_disposed)
        {
            return;
        }

        List<AppWindowInfo> minimized = [.. SelectMinimized(next)];
        if (WindowsEqual(_windows, next))
        {
            return;
        }

        _windows = next;
        _minimized = minimized;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool WindowsEqual(List<AppWindowInfo> a, List<AppWindowInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].Handle != b[i].Handle
                || a[i].Pid != b[i].Pid
                || a[i].IsMinimized != b[i].IsMinimized
                || !string.Equals(a[i].Title, b[i].Title, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _poll?.Stop();
        _poll = null;
    }
}
```

- [ ] **Step 5: Testlerin geçtiğini doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~MinimizedWindowsSourceTests
```
Beklenen: 7 geçen.

- [ ] **Step 6: Tüm testler**

```
dotnet test WharfBar.sln
```
Beklenen: 502 geçen, 0 başarısız.

- [ ] **Step 7: Commit**

```
git add src/WharfBar/Services/MinimizedWindowsSource.cs src/WharfBar/Native/WindowEnumeration.cs tests/WharfBar.Tests/MinimizedWindowsSourceTests.cs
git commit -m "feat(dock): kucultulmus pencere tablosu, arka is parcaciginda tarama"
```

---

## Task 6: `DockAppsComposer` yeniden yazımı ve bölüm/ayraç üretimi

**Files:**
- Modify: `src/WharfBar/Models/DockAppItem.cs` (`DockSection` + yeni kayıt imzası)
- Modify: `src/WharfBar/Services/DockAppsComposer.cs` (tam yeniden yazım)
- Create: `src/WharfBar/Services/DockStripBuilder.cs`
- Test: `tests/WharfBar.Tests/DockAppsComposerTests.cs`

**Interfaces:**
- Consumes: `DockItemStore`, `DockConfig.ShowRunningApps` (Task 3), `RunningAppInfo` (mevcut), `AppWindowInfo`/`MinimizedWindowsSource` (Task 5)
- Produces:
  - `WharfBar.Models.DockSection { Pinned, Running, Minimized }`
  - `DockAppItem(string Id, string Path, string Args, string Label, bool IsPinned, bool IsRunning, IntPtr MainWindowHandle, DockSection Section, string WindowTitle)`
  - `DockAppsComposer(DockItemStore store, RunningAppsSource running, MinimizedWindowsSource minimized, DockConfig config)`
  - `static IReadOnlyList<DockAppItem> Compose(IReadOnlyList<PinnedApp> pins, IReadOnlyList<RunningAppInfo> running, IReadOnlyList<AppWindowInfo> minimized, bool showRunning)`
  - `static bool ItemsEqual(IReadOnlyList<DockAppItem> a, IReadOnlyList<DockAppItem> b)`
  - `WharfBar.Services.DockStripRow(DockAppItem? Item)` + `bool IsSeparator` + `static DockStripRow Separator()`
  - `WharfBar.Services.DockStripBuilder.SeparatorIndices(...)`, `DockStripBuilder.Build(...)`, `const double SeparatorSlotDip = 1d`

- [ ] **Step 1: Test dosyasını yaz**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockAppsComposerTests
{
    private const string Notepad = @"C:\Windows\System32\notepad.exe";
    private const string Calc = @"C:\Windows\System32\calc.exe";

    private static List<PinnedApp> Pins(params string[] paths) =>
        [.. paths.Select(p => new PinnedApp(p, string.Empty, System.IO.Path.GetFileNameWithoutExtension(p)))];

    private static RunningAppInfo Running(string id, string path, string label, IntPtr hwnd = default) =>
        new(id, path, label, hwnd);

    private static AppWindowInfo Minimized(IntPtr hwnd, uint pid, string title) =>
        new(hwnd, title, pid, IsMinimized: true);

    private static IReadOnlyList<DockAppItem> Compose(
        IReadOnlyList<PinnedApp>? pins = null,
        IReadOnlyList<RunningAppInfo>? running = null,
        IReadOnlyList<AppWindowInfo>? minimized = null,
        bool showRunning = true) =>
        DockAppsComposer.Compose(pins ?? [], running ?? [], minimized ?? [], showRunning);

    [Fact]
    public void Compose_KeepsConfiguredPinOrder()
    {
        IReadOnlyList<DockAppItem> items = Compose(Pins(Notepad, Calc));
        Assert.Equal([@"C:\Windows\System32\notepad", @"C:\Windows\System32\calc"],
            items.Select(i => i.Label));
        Assert.All(items, i => Assert.Equal(DockSection.Pinned, i.Section));
    }

    [Fact]
    public void Compose_MarksPinnedItemRunningWhenPathMatches()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad), [Running("Microsoft.Notepad", Notepad, "Not Defteri", (IntPtr)7)]);
        DockAppItem item = Assert.Single(items);
        Assert.True(item.IsRunning);
        Assert.Equal((IntPtr)7, item.MainWindowHandle);
        Assert.Equal("Microsoft.Notepad", item.Id);
    }

    [Fact]
    public void Compose_MatchesRunningAppToAtMostOnePin()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad, Notepad.ToUpperInvariant()),
            [Running("A", Notepad, "Not Defteri", (IntPtr)7)]);

        Assert.Equal(2, items.Count);
        Assert.Single(items.Where(i => i.IsRunning));
        Assert.Equal(2, items.Select(i => i.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Compose_UsesPinPathAsIdWhenNothingIsRunning()
    {
        DockAppItem item = Assert.Single(Compose(Pins(Notepad)));
        Assert.Equal(Notepad, item.Id);
        Assert.False(item.IsRunning);
        Assert.Equal(IntPtr.Zero, item.MainWindowHandle);
    }

    [Fact]
    public void Compose_AppendsUnpinnedRunningAppsSortedByLabel()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [Running("c", Calc, "Zebra"), Running("b", @"C:\x.exe", "Alpha"), Running("a", Notepad, "Not Defteri")]);

        Assert.Equal(["Not Defteri", "Alpha", "Zebra"], items.Select(i => i.Label));
        Assert.Equal(DockSection.Running, items[1].Section);
        Assert.False(items[1].IsPinned);
    }

    [Fact]
    public void Compose_WhenShowRunningAppsFalse_OmitsRunningAndMinimizedSections()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            Pins(Notepad),
            [Running("c", Calc, "Calc")],
            [Minimized((IntPtr)9, 42, "Belge")],
            showRunning: false);

        Assert.Single(items);
        Assert.Equal(DockSection.Pinned, Assert.Single(items).Section);
    }

    [Fact]
    public void Compose_CreatesOneTilePerMinimizedWindow()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [Running("np", Notepad, "Not Defteri")],
            [Minimized((IntPtr)11, 100, "Bir"), Minimized((IntPtr)12, 100, "İki")]);

        DockAppItem[] minimized = [.. items.Where(i => i.Section == DockSection.Minimized)];
        Assert.Equal(2, minimized.Length);
        Assert.Equal(["Bir", "İki"], minimized.Select(i => i.WindowTitle));
        Assert.Equal("hwnd:11", minimized[0].Id);
        Assert.Equal("hwnd:12", minimized[1].Id);
    }

    [Fact]
    public void Compose_MapsMinimizedWindowToAppPathForIconAndLabel()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [Running("np", Notepad, "Not Defteri")],
            [Minimized((IntPtr)11, 100, "Belge")]);

        DockAppItem minimized = Assert.Single(items.Where(i => i.Section == DockSection.Minimized));
        Assert.Equal(Notepad, minimized.Path);
        Assert.Equal("Not Defteri", minimized.Label);
        Assert.Equal("Belge", minimized.WindowTitle);
    }

    [Fact]
    public void Compose_UnmappedMinimizedWindowFallsBackToWindowTitle()
    {
        IReadOnlyList<DockAppItem> items = Compose([], [], [Minimized((IntPtr)13, 999, "Yalnız")]);
        DockAppItem minimized = Assert.Single(items);
        Assert.Equal(string.Empty, minimized.Path);
        Assert.Equal("Yalnız", minimized.Label);
    }

    [Fact]
    public void Compose_GroupsMinimizedWindowsOfSameAppTogether()
    {
        IReadOnlyList<DockAppItem> items = Compose(
            [],
            [Running("np", Notepad, "Not Defteri"), Running("c", Calc, "Hesap Makinesi")],
            [Minimized((IntPtr)21, 100, "A"), Minimized((IntPtr)22, 200, "B"), Minimized((IntPtr)23, 100, "C")]);

        string[] labels = [.. items.Where(i => i.Section == DockSection.Minimized).Select(i => i.Label)];
        Assert.Equal(["Not Defteri", "Not Defteri", "Hesap Makinesi"], labels);
    }

    [Fact]
    public void Compose_WhenAllInputsEmpty_YieldsNoItems()
    {
        Assert.Empty(Compose());
    }

    [Fact]
    public void ItemsEqual_WhenSectionDiffers_IsFalse()
    {
        List<DockAppItem> a = [new("x", "p", string.Empty, "L", true, true, IntPtr.Zero, DockSection.Pinned, string.Empty)];
        List<DockAppItem> b = [a[0] with { Section = DockSection.Running }];
        Assert.False(DockAppsComposer.ItemsEqual(a, b));
    }

    [Fact]
    public void ItemsEqual_WhenWindowTitleDiffers_IsFalse()
    {
        List<DockAppItem> a = [new("x", "p", string.Empty, "L", true, true, IntPtr.Zero, DockSection.Minimized, "A")];
        List<DockAppItem> b = [a[0] with { WindowTitle = "B" }];
        Assert.False(DockAppsComposer.ItemsEqual(a, b));
    }

    [Fact]
    public void ItemsEqual_WhenEverythingMatches_IsTrue()
    {
        List<DockAppItem> a = [new("x", "p", string.Empty, "L", true, true, IntPtr.Zero, DockSection.Pinned, string.Empty)];
        List<DockAppItem> b = [new("X", "P", string.Empty, "L", true, true, IntPtr.Zero, DockSection.Pinned, string.Empty)];
        Assert.True(DockAppsComposer.ItemsEqual(a, b));
    }

    private static IReadOnlyList<DockAppItem> ThreeSections() =>
    [
        new("a", "p1", string.Empty, "P1", true, true, (IntPtr)1, DockSection.Pinned, string.Empty),
        new("b", "p2", string.Empty, "P2", true, false, IntPtr.Zero, DockSection.Pinned, string.Empty),
        new("c", "r1", string.Empty, "R1", false, true, (IntPtr)3, DockSection.Running, string.Empty),
        new("d", "m1", string.Empty, "M1", false, true, (IntPtr)4, DockSection.Minimized, "W1"),
    ];

    [Fact]
    public void SeparatorIndices_InsertsAtEverySectionBoundary()
    {
        Assert.Equal([2, 3], DockStripBuilder.SeparatorIndices(ThreeSections()));
    }

    [Fact]
    public void SeparatorIndices_ForSingleSection_IsEmpty()
    {
        IReadOnlyList<DockAppItem> items = ThreeSections().Where(i => i.Section == DockSection.Pinned).ToList();
        Assert.Empty(DockStripBuilder.SeparatorIndices(items));
    }

    [Fact]
    public void SeparatorIndices_ForEmptyList_IsEmpty()
    {
        Assert.Empty(DockStripBuilder.SeparatorIndices([]));
    }

    [Fact]
    public void Build_RowCountIsItemsPlusSeparators()
    {
        IReadOnlyList<DockStripRow> rows = DockStripBuilder.Build(ThreeSections());
        Assert.Equal(6, rows.Count);
        Assert.Equal(2, rows.Count(r => r.IsSeparator));
    }

    [Fact]
    public void Build_PlacesSeparatorBeforeFirstItemOfNewSection()
    {
        IReadOnlyList<DockStripRow> rows = DockStripBuilder.Build(ThreeSections());
        Assert.False(rows[0].IsSeparator);
        Assert.True(rows[2].IsSeparator);
        Assert.Equal("R1", rows[3].Item!.Label);
        Assert.True(rows[4].IsSeparator);
        Assert.Equal(DockSection.Minimized, rows[5].Item!.Section);
    }

    [Fact]
    public void Build_ForEmptyList_YieldsNoRows()
    {
        Assert.Empty(DockStripBuilder.Build([]));
    }
}
```

- [ ] **Step 2: Testlerin başarısız olduğunu doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~DockAppsComposerTests
```
Beklenen: BUILD FAILED — `DockSection` bulunamadı.

- [ ] **Step 3: `DockAppItem`'ı genişlet**

`src/WharfBar/Models/DockAppItem.cs` tamamen:

```csharp
using System;

namespace WharfBar.Models;

/// <summary>Which dock section an item belongs to. Sections render in enum order.</summary>
internal enum DockSection
{
    Pinned,
    Running,
    Minimized,
}

internal sealed record DockAppItem(
    string Id,
    string Path,
    string Args,
    string Label,
    bool IsPinned,
    bool IsRunning,
    IntPtr MainWindowHandle,
    DockSection Section,
    string WindowTitle)
{
    /// <summary>Tooltip copy: minimized tiles name the window, everything else the app.</summary>
    internal string Tooltip =>
        Section != DockSection.Minimized || string.IsNullOrEmpty(WindowTitle) || WindowTitle == Label
            ? Label
            : $"{Label} — {WindowTitle}";
}
```

- [ ] **Step 4: `DockAppsComposer`'u yeniden yaz**

`src/WharfBar/Services/DockAppsComposer.cs` tamamen:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Merges the dock's own pinned list, the live running-app list, and minimized windows
/// into one ordered strip of <see cref="DockSection"/>s.
/// </summary>
internal sealed class DockAppsComposer : IDisposable
{
    private readonly DockItemStore _store;
    private readonly RunningAppsSource _running;
    private readonly MinimizedWindowsSource _minimized;
    private readonly DockConfig _config;
    private List<DockAppItem> _items = [];

    internal DockAppsComposer(
        DockItemStore store,
        RunningAppsSource running,
        MinimizedWindowsSource minimized,
        DockConfig config)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(minimized);
        ArgumentNullException.ThrowIfNull(config);

        _store = store;
        _running = running;
        _minimized = minimized;
        _config = config;

        _store.Changed += OnSourceChanged;
        _running.Changed += OnSourceChanged;
        _minimized.Changed += OnSourceChanged;
        Rebuild();
    }

    internal IReadOnlyList<DockAppItem> Items => _items;

    internal event EventHandler? Changed;

    /// <summary>
    /// Pure merge. Pinned order is authoritative; a running app binds to the first pin
    /// with a matching path; leftovers become the Running section; every minimized
    /// window becomes its own Minimized tile.
    /// </summary>
    internal static IReadOnlyList<DockAppItem> Compose(
        IReadOnlyList<PinnedApp> pins,
        IReadOnlyList<RunningAppInfo> running,
        IReadOnlyList<AppWindowInfo> minimized,
        bool showRunning)
    {
        var next = new List<DockAppItem>(pins.Count + running.Count + minimized.Count);
        var matched = new bool[running.Count];

        foreach (PinnedApp pin in pins)
        {
            int hit = -1;
            for (int i = 0; i < running.Count; i++)
            {
                if (!matched[i] && PathsMatch(pin.Path, running[i].Path))
                {
                    hit = i;
                    break;
                }
            }

            if (hit < 0)
            {
                next.Add(new DockAppItem(
                    pin.Path, pin.Path, pin.Args, pin.Label,
                    IsPinned: true, IsRunning: false, IntPtr.Zero,
                    DockSection.Pinned, string.Empty));
                continue;
            }

            matched[hit] = true;
            RunningAppInfo app = running[hit];
            next.Add(new DockAppItem(
                app.Id, pin.Path, pin.Args, pin.Label,
                IsPinned: true, IsRunning: true, app.MainWindowHandle,
                DockSection.Pinned, string.Empty));
        }

        if (!showRunning)
        {
            return next;
        }

        var unpinned = new List<RunningAppInfo>();
        for (int i = 0; i < running.Count; i++)
        {
            if (!matched[i])
            {
                unpinned.Add(running[i]);
            }
        }

        unpinned.Sort(static (a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        foreach (RunningAppInfo app in unpinned)
        {
            next.Add(new DockAppItem(
                app.Id, app.Path, string.Empty, app.Label,
                IsPinned: false, IsRunning: true, app.MainWindowHandle,
                DockSection.Running, string.Empty));
        }

        var byPid = new Dictionary<uint, RunningAppInfo>();
        foreach (RunningAppInfo app in running)
        {
            uint pid = NativeMethods.GetWindowThreadProcessId(app.MainWindowHandle, out uint resolved);
            if (pid != 0)
            {
                byPid.TryAdd(resolved, app);
            }
        }

        foreach (AppWindowInfo window in minimized
            .OrderBy(static w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static w => w.Handle))
        {
            byPid.TryGetValue(window.Pid, out RunningAppInfo? owner);
            next.Add(new DockAppItem(
                $"hwnd:{window.Handle}",
                owner?.Path ?? string.Empty,
                string.Empty,
                string.IsNullOrEmpty(owner?.Label) ? window.Title : owner.Label,
                IsPinned: false,
                IsRunning: true,
                window.Handle,
                DockSection.Minimized,
                window.Title));
        }

        return next;
    }

    internal static bool ItemsEqual(IReadOnlyList<DockAppItem> a, IReadOnlyList<DockAppItem> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i].Id, b[i].Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Path, b[i].Path, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a[i].Args, b[i].Args, StringComparison.Ordinal)
                || !string.Equals(a[i].Label, b[i].Label, StringComparison.Ordinal)
                || !string.Equals(a[i].WindowTitle, b[i].WindowTitle, StringComparison.Ordinal)
                || a[i].IsPinned != b[i].IsPinned
                || a[i].IsRunning != b[i].IsRunning
                || a[i].MainWindowHandle != b[i].MainWindowHandle
                || a[i].Section != b[i].Section)
            {
                return false;
            }
        }

        return true;
    }

    private static bool PathsMatch(string a, string b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void OnSourceChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        IReadOnlyList<DockAppItem> next = Compose(
            _store.Items, _running.Apps, _minimized.Minimized, _config.ShowRunningApps);

        if (ItemsEqual(_items, next))
        {
            return;
        }

        _items = [.. next];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _store.Changed -= OnSourceChanged;
        _running.Changed -= OnSourceChanged;
        _minimized.Changed -= OnSourceChanged;
    }
}
```

> `DockAppsComposer.cs` dosyasının `using WharfBar.Native;` eklemesi gerekir (`NativeMethods.GetWindowThreadProcessId` için).

- [ ] **Step 5: `DockStripBuilder` uygulamasını yaz**

`src/WharfBar/Services/DockStripBuilder.cs`:

```csharp
using System.Collections.Generic;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>One rendered slot: either a dock item or a section separator.</summary>
internal sealed record DockStripRow(DockAppItem? Item)
{
    internal static DockStripRow Separator() => new(null);

    internal bool IsSeparator => Item is null;
}

/// <summary>Turns composed items into renderable rows, inserting one separator per section change.</summary>
internal static class DockStripBuilder
{
    /// <summary>Resting width of the 1 px divider bar (DIP).</summary>
    internal const double SeparatorSlotDip = 1d;

    /// <summary>Indices (into the item list) where a separator must precede that item.</summary>
    internal static IReadOnlyList<int> SeparatorIndices(IReadOnlyList<DockAppItem> items)
    {
        var indices = new List<int>();
        DockSection? previous = null;

        for (int i = 0; i < items.Count; i++)
        {
            DockSection section = items[i].Section;
            if (previous is not null && previous != section)
            {
                indices.Add(i);
            }

            previous = section;
        }

        return indices;
    }

    internal static IReadOnlyList<DockStripRow> Build(IReadOnlyList<DockAppItem> items)
    {
        var separators = new HashSet<int>(SeparatorIndices(items));
        var rows = new List<DockStripRow>(items.Count + separators.Count);

        for (int i = 0; i < items.Count; i++)
        {
            if (separators.Contains(i))
            {
                rows.Add(DockStripRow.Separator());
            }

            rows.Add(new DockStripRow(items[i]));
        }

        return rows;
    }
}
```

- [ ] **Step 6: Testlerin geçtiğini doğrula**

```
dotnet test WharfBar.sln --filter FullyQualifiedName~DockAppsComposerTests
```
Beklenen: 20 geçen.

- [ ] **Step 7: `DockWindow`'un derlenmesini sağla**

`src/WharfBar/Views/DockWindow.xaml.cs:249-253` — eski kaynak kurulumunu değiştir:

```csharp
        _itemStore = new DockItemStore(WharfBarConfigService());
        _runningSource = new RunningAppsSource(Dispatcher);
        _minimizedSource = new MinimizedWindowsSource(Dispatcher);
        _composer = new DockAppsComposer(_itemStore, _runningSource, _minimizedSource, _config.Dock);
        _activation = new WindowActivationService();
```

ve alan bildirimleri:

```csharp
    private ConfigService _configService = null!;
    private DockItemStore? _itemStore;
    private MinimizedWindowsSource? _minimizedSource;
```

`DockWindow` içine ekle:

```csharp
    private ConfigService WharfBarConfigService
    {
        get
        {
            if (_configService is null)
            {
                _configService = (ConfigService)((App)Application.Current).Config!;
            }

            return _configService;
        }
    }
```

> Bu adım yalnızca **derinlik** sağlar: Task 6'nın görsel/etkileşim kısmı henüz değişmedi, ancak kaynak türleri artık `DockItemStore`/`MinimizedWindowsSource` tabanlı. `RebuildStrip` eski `CreateIconVisual` yolunu kullanmaya devam eder ve `new DockAppItem(...)` çağrısı yapmadığı için derlenir.

- [ ] **Step 8: Tüm testler ve derleme**

```
dotnet build WharfBar.sln && dotnet test WharfBar.sln
```
Beklenen: derleme temiz, **522 geçen**, 0 başarısız.

- [ ] **Step 9: Commit**

```
git add src/WharfBar/Models/DockAppItem.cs src/WharfBar/Services/DockAppsComposer.cs src/WharfBar/Services/DockStripBuilder.cs src/WharfBar/Views/DockWindow.xaml.cs tests/WharfBar.Tests/DockAppsComposerTests.cs
git commit -m "refactor(dock): bairesiz birlestirici, Pinned/Running/Minimized bolumleri"
```

---

## Task 7: Görsel katman — `DockItemCell`, `DockStrip`, büyütme yeniden düğüme

**Files:**
- Create: `src/WharfBar/Views/DockItemViewModel.cs`
- Create: `src/WharfBar/Views/Controls/DockItemCell.xaml`
- Create: `src/WharfBar/Views/Controls/DockItemCell.xaml.cs`
- Create: `src/WharfBar/Views/Controls/DockStrip.cs`
- Modify: `src/WharfBar/Views/DockWindow.xaml:22-40`
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs` (`:33-55` sabitler, `:97-124` ctor, `:249-254` kaynaklar, `:325-409` `RebuildStrip`/`CreateIconVisual`, `:651-668` ikon çözünürlüğü, `:908-922` geometri)

**Interfaces:**
- Consumes: `DockStripLayout`, `DockMagnification` (Task 1-2), `DockStripRow`/`DockStripBuilder` (Task 6), `AppIconService`
- Produces:
  - `WharfBar.Views.DockItemViewModel` (`public sealed`; `Item` özelliği `internal` kalır) — `static ForItem(DockAppItem, ImageSource)`, `static ForSeparator()`, `DockAppItem? Item`, `bool IsSeparator`, `string Id`, `string Label`, `string Tooltip`, `ImageSource Icon`, `bool IsRunning`, `bool IsMinimized`, `IntPtr Handle`
  - `public sealed class DockItemCell : UserControl` — `DockItemViewModel? Row` (DP), `ApplyMetrics(double iconSize, double indicatorGap, double indicatorSize, double height)`, `SetMagnification(double scale)`
  - `public sealed class DockStrip : Panel` — `IconSize`, `IconGap`, `IndicatorGap`, `IndicatorSize`, `MaxScale`, `Phase` (DP), `CursorPosition` (DP), `IReadOnlyList<DockItemCell> Cells`, `int SlotCount`, `double RestingHeight`, `void SetRows(IReadOnlyList<DockItemViewModel> rows)`, `double HitTestCursor(double x)`

> **Spec sapması (gerekçeli):** spec §6.3 "`ItemsControl` + `ItemTemplate`" diyordu. Uygulanmıyor. Dock'un her hücresine **imperatif** tooltip yerleşimi, bağlam menüsü ve sürükle/bırak bağlanması gerekiyor; `ItemsControl` hücrelere erişim vermediği için ek bir `VisualTreeHelper` yürüyüşü ya da hücre-içi olay köprüsü gerekirdi. Bunun yerine `DockStrip : Panel` hücreleri **kendisi** sahiplenir ve `DockWindow`'a `Cells` listesi olarak açar. Deklaratif olan kısır korunur: `DockItemCell` bir `UserControl`'dür ve XAML ile tanımlıdır.

- [ ] **Step 1: `DockItemViewModel` yaz**

`src/WharfBar/Views/DockItemViewModel.cs`:

```csharp
using System;
using System.Windows.Media;
using WharfBar.Models;

namespace WharfBar.Views;

/// <summary>
/// Render-ready snapshot of one strip slot: a dock item or a section separator.
/// Public because <see cref="Controls.DockItemCell.Row"/> is a public dependency property;
/// <see cref="Item"/> stays internal so the internal <see cref="DockAppItem"/> never leaks
/// past the assembly boundary.
/// </summary>
public sealed class DockItemViewModel
{
    private DockItemViewModel(DockAppItem? item, ImageSource icon, bool isSeparator)
    {
        Item = item;
        Icon = icon;
        IsSeparator = isSeparator;
    }

    internal DockAppItem? Item { get; }

    public ImageSource Icon { get; }

    public bool IsSeparator { get; }

    public string Id => Item?.Id ?? string.Empty;

    public string Label => Item?.Label ?? string.Empty;

    public string Tooltip => Item?.Tooltip ?? string.Empty;

    public bool IsRunning => Item?.IsRunning == true;

    public bool IsMinimized => Item?.Section == DockSection.Minimized;

    public IntPtr Handle => Item?.MainWindowHandle ?? IntPtr.Zero;

    internal static DockItemViewModel ForItem(DockAppItem item, ImageSource icon)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DockItemViewModel(item, icon, isSeparator: false);
    }

    internal static DockItemViewModel ForSeparator() => new(null, null!, isSeparator: true);
}
```

- [ ] **Step 2: `DockItemCell` XAML ve code-behind yaz**

`src/WharfBar/Views/Controls/DockItemCell.xaml`:

```xml
<UserControl x:Class="WharfBar.Views.Controls.DockItemCell"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d"
             Background="Transparent"
             Focusable="False"
             UseLayoutRounding="True">

    <!-- Geometry is pushed in by DockStrip so the panel stays the single source of
         measurement truth. Icons grow upward: RenderTransformOrigin is bottom-centre. -->
    <Grid x:Name="Root" Background="Transparent">

        <Image x:Name="IconImage"
               Stretch="Uniform"
               SnapsToDevicePixels="True"
               HorizontalAlignment="Center"
               VerticalAlignment="Bottom"
               RenderTransformOrigin="0.5,1">
            <Image.RenderTransform>
                <ScaleTransform x:Name="IconScale" ScaleX="1" ScaleY="1" />
            </Image.RenderTransform>
        </Image>

        <Border x:Name="RunningPill"
                HorizontalAlignment="Center"
                VerticalAlignment="Bottom"
                CornerRadius="2"
                Background="{DynamicResource TextFillColorPrimaryBrush}"
                Visibility="Collapsed" />

        <Border x:Name="SeparatorBar"
                Width="1"
                HorizontalAlignment="Center"
                VerticalAlignment="Stretch"
                Margin="0,2,0,2"
                Opacity="0.3"
                Background="{DynamicResource TextFillColorPrimaryBrush}"
                Visibility="Collapsed" />

    </Grid>
</UserControl>
```

`src/WharfBar/Views/Controls/DockItemCell.xaml.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WharfBar.Views.Controls;

/// <summary>
/// One dock slot: an app icon with its running pill, or a section separator.
/// The cell never decides its own size — <see cref="DockStrip"/> pushes metrics in and
/// scales the icon from its bottom edge so growth reads as "rising off the plate".
/// </summary>
public sealed partial class DockItemCell : UserControl
{
    private const double MagnificationEpsilon = 5e-4d;

    private double _appliedScale = 1d;
    private double _iconSize;

    public DockItemCell()
    {
        InitializeComponent();
        RenderOptions.SetBitmapScalingMode(IconImage, BitmapScalingMode.HighQuality);
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public static readonly DependencyProperty RowProperty =
        DependencyProperty.Register(
            nameof(Row),
            typeof(DockItemViewModel),
            typeof(DockItemCell),
            new PropertyMetadata(null, OnRowChanged));

    /// <summary>Slot content. Setting it re-applies icon, pill, and separator visibility.</summary>
    public DockItemViewModel? Row
    {
        get => (DockItemViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>1 px divider rather than an app icon.</summary>
    public bool IsSeparator => Row?.IsSeparator != false;

    internal void ApplyMetrics(double iconSize, double indicatorGap, double indicatorSize, double height)
    {
        Root.Height = height;
        SeparatorBar.Visibility = IsSeparator ? Visibility.Visible : Visibility.Collapsed;

        if (Math.Abs(iconSize - _iconSize) < 0.01d)
        {
            return;
        }

        _iconSize = iconSize;
        IconImage.Width = iconSize;
        IconImage.Height = iconSize;
        RunningPill.Width = indicatorSize * 3.5d;
        RunningPill.Height = indicatorSize;
        RunningPill.Margin = new Thickness(0, 0, 0, indicatorGap);
        IconImage.Margin = new Thickness(0, 0, 0, indicatorGap + indicatorSize);
        IconScale.ScaleX = _appliedScale;
        IconScale.ScaleY = _appliedScale;
    }

    internal void SetMagnification(double scale)
    {
        if (Math.Abs(scale - _appliedScale) < MagnificationEpsilon)
        {
            return;
        }

        _appliedScale = scale;
        IconScale.ScaleX = scale;
        IconScale.ScaleY = scale;
    }

    private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DockItemCell)d).ApplyRow();

    private void ApplyRow()
    {
        DockItemViewModel? row = Row;
        if (row is null)
        {
            IconImage.Source = null;
            RunningPill.Visibility = Visibility.Collapsed;
            SeparatorBar.Visibility = Visibility.Collapsed;
            return;
        }

        bool separator = row.IsSeparator;
        IconImage.Visibility = separator ? Visibility.Collapsed : Visibility.Visible;
        SeparatorBar.Visibility = separator ? Visibility.Visible : Visibility.Collapsed;
        RunningPill.Visibility = !separator && row.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        IconImage.Source = separator ? null : row.Icon;
        ToolTip = null;
        ContextMenu = null;
    }
}
```

> `ApplyRow` bağlam menüsü ve tooltip'i **temizler**; `DockWindow.RebuildStrip` hücreleri `SetRows` sonrasında yeniden bağlar. Hücre yeniden kullanıldığında eski menü sızmasın diye.

- [ ] **Step 3: `DockStrip` panelini yaz**

`src/WharfBar/Views/Controls/DockStrip.cs`:

```csharp
using System;
using System.Collections.Generic;
using WharfBar.Services;

namespace WharfBar.Views.Controls;

/// <summary>
/// The dock's horizontal strip: owns one <see cref="DockItemCell"/> per slot, arranges them
/// with magnified slot widths, and maps the cursor to a fractional slot index so the peak
/// icon slides between neighbours instead of snapping.
/// <para>
/// The panel reports its live width as <c>DesiredSize</c> so the plate hugs the icons, while
/// the window itself stays sized for the worst case — nothing here calls into Win32.
/// </para>
/// </summary>
public sealed class DockStrip : Panel
{
    private readonly List<DockItemCell> _cells = [];
    private double[] _scales = [];
    private double[] _offsets = [];
    private double[] _baseWidths = [];
    private double[] _centers = [];
    private double _restingWidth;

    public DockStrip()
    {
        ClipToBounds = false;
    }

    public double IconSize { get; set; } = 28d;

    public double IconGap { get; set; } = 12d;

    public double IndicatorGap { get; set; } = 2d;

    public double IndicatorSize { get; set; } = 4d;

    public double MaxScale { get; set; } = DockMagnification.Default;

    /// <summary>0 at rest, 1 fully magnified. Driven by an animation on enter/leave.</summary>
    public static readonly DependencyProperty PhaseProperty =
        DependencyProperty.Register(
            nameof(Phase),
            typeof(double),
            typeof(DockStrip),
            new PropertyMetadata(0d, OnMagnificationStateChanged));

    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Cursor position in slot units; fractional values interpolate between slots.</summary>
    public static readonly DependencyProperty CursorPositionProperty =
        DependencyProperty.Register(
            nameof(CursorPosition),
            typeof(double),
            typeof(DockStrip),
            new PropertyMetadata(0d, OnMagnificationStateChanged));

    public double CursorPosition
    {
        get => (double)GetValue(CursorPositionProperty);
        set => SetValue(CursorPositionProperty, value);
    }

    public IReadOnlyList<DockItemCell> Cells => _cells;

    public int SlotCount => _cells.Count;

    /// <summary>Plate content height at rest: icon + indicator gap + indicator.</summary>
    public double RestingHeight => IconSize + IndicatorGap + IndicatorSize;

    /// <summary>
    /// Replaces the slot list, reusing existing cells so hover state, bitmaps, and menus
    /// survive the composer's 2-second poll.
    /// </summary>
    public void SetRows(IReadOnlyList<DockItemViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        while (_cells.Count > rows.Count)
        {
            DockItemCell extra = _cells[^1];
            _cells.RemoveAt(_cells.Count - 1);
            RemoveVisualChild(extra);
        }

        while (_cells.Count < rows.Count)
        {
            var cell = new DockItemCell();
            _cells.Add(cell);
            AddVisualChild(cell);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            _cells[i].Row = rows[i];
        }

        RebuildRestingGeometry();
        InvalidateMeasure();
    }

    /// <summary>
    /// Fractional slot index for a resting-layout x position. Uses the resting centers so
    /// the mapping cannot feed back on the magnified layout it produces.
    /// </summary>
    public double HitTestCursor(double x)
    {
        int count = _cells.Count;
        if (count == 0)
        {
            return 0d;
        }

        if (x <= _centers[0])
        {
            return 0d;
        }

        for (int i = 1; i < count; i++)
        {
            if (x < _centers[i])
            {
                double span = _centers[i] - _centers[i - 1];
                return span <= 0d ? i - 1 : i - 1 + ((x - _centers[i - 1]) / span);
            }
        }

        return count - 1;
    }

    /// <summary>Live slot widths must fit inside the budget <c>DockWindow</c> sized the window to.</summary>
    public double MaxLiveWidth() => DockStripLayout.MaxExtentWidth(
        Math.Max(SlotCount, 1), IconSize, IconGap, MaxScale);

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = _cells.Count;
        EnsureBuffers(count);

        double height = RestingHeight;
        for (int i = 0; i < count; i++)
        {
            _cells[i].ApplyMetrics(IconSize, IndicatorGap, IndicatorSize, height);
            _cells[i].Measure(new Size(_baseWidths[i], height));
        }

        DockStripLayout.ComputeScales(count, CursorPosition, Phase, MaxScale, _scales);
        DockStripLayout.ComputeOffsets(count, _baseWidths, _scales, IconGap, _offsets, out double live);
        return new Size(live, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = _cells.Count;
        EnsureBuffers(count);
        double height = RestingHeight;

        DockStripLayout.ComputeScales(count, CursorPosition, Phase, MaxScale, _scales);
        DockStripLayout.ComputeOffsets(count, _baseWidths, _scales, IconGap, _offsets, out _);

        for (int i = 0; i < count; i++)
        {
            DockItemCell cell = _cells[i];
            cell.SetMagnification(_scales[i]);
            cell.Arrange(new Rect(_offsets[i], 0, _baseWidths[i] * _scales[i], height));
        }

        return finalSize;
    }

    protected override int VisualChildrenCount => _cells.Count;

    protected override Visual GetVisualChild(int index) => _cells[index];

    private static void OnMagnificationStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DockStrip)d).InvalidateMeasure();

    private static double BaseWidthFor(DockItemCell cell, double iconSize) =>
        cell.IsSeparator ? DockStripBuilder.SeparatorSlotDip : iconSize;

    private void RebuildRestingGeometry()
    {
        int count = _cells.Count;
        EnsureBuffers(count);

        for (int i = 0; i < count; i++)
        {
            _baseWidths[i] = BaseWidthFor(_cells[i], IconSize);
        }

        Array.Fill(_scales, 1.0d);
        DockStripLayout.ComputeOffsets(count, _baseWidths, _scales, IconGap, _offsets, out _restingWidth);

        for (int i = 0; i < count; i++)
        {
            _centers[i] = _offsets[i] + (_baseWidths[i] / 2.0d);
        }
    }

    private void EnsureBuffers(int count)
    {
        if (_scales.Length >= count)
        {
            return;
        }

        int size = Math.Max(count, 8);
        _scales = new double[size];
        _offsets = new double[size];
        _baseWidths = new double[size];
        _centers = new double[size];
    }
}
```

`DockStrip.cs` dosyasının başına `using System.Windows.Controls;` ve `using System.Windows.Media;` ekle.

- [ ] **Step 4: `DockWindow.xaml`'ı yeni şeride bağla**

`src/WharfBar/Views/DockWindow.xaml` içeriğini tamamen:

```xml
<Window x:Class="WharfBar.Views.DockWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:controls="clr-namespace:WharfBar.Views.Controls"
        Title="WharfBar Dock"
        WindowStyle="None"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Topmost="True"
        WindowStartupLocation="Manual"
        SizeToContent="Manual"
        Background="Transparent"
        Foreground="{DynamicResource TextFillColorPrimaryBrush}"
        UseLayoutRounding="True"
        AllowsTransparency="True">

    <!-- ContextMenu / MenuItem styles live in App.xaml (BasedOn WPF-UI) so Fluent
         templates are not replaced by Aero2's light icon gutter. -->

    <!-- Layered window: the strip is a WPF-drawn rounded surface (theme-aware brush set from
         code), everything outside it is fully transparent AND click-through. The window is
         fixed at the maximum magnified extent (never resized during hover — SetWindowPos per
         frame stutters); the Surface hugs the DockStrip's live width and stays centred. -->
    <Grid>
        <Border x:Name="Surface"
                Padding="12,6"
                Background="Transparent"
                CornerRadius="12"
                BorderThickness="1"
                BorderBrush="#1AFFFFFF"
                HorizontalAlignment="Center"
                VerticalAlignment="Bottom">
            <controls:DockStrip x:Name="Strip" />
        </Border>
    </Grid>
</Window>
```

- [ ] **Step 5: `DockWindow` sabitlerini ve geometriyi güncelle**

`src/WharfBar/Views/DockWindow.xaml.cs`:

`:33-55` bloğunu değiştir:

```csharp
    /// <summary>Gap between adjacent icons (DIP). Wider than early 4 DIP — less cramped.</summary>
    private const double IconGapDip = 12d;

    /// <summary>Height of the W11-style running pill (DIP).</summary>
    private const double RunningIndicatorSizeDip = 4d;

    /// <summary>Gap between icon bottom and the running pill (DIP).</summary>
    private const double RunningIndicatorGapDip = 2d;

    private const double BottomGapDip = 0d;

    private const int AutoHideAnimMs = 200;
    private const double MinWidthDip = 180d;

    /// <summary>Magnification enter/exit durations (ms).</summary>
    private const int PhaseInMs = 130;

    private const int PhaseOutMs = 180;

    /// <summary>Cursor catch-up frame interval (ms).</summary>
    private const int CursorFrameMs = 16;

    /// <summary>Acrylic surface translucency (0–255 alpha over the theme color).</summary>
    private const byte AcrylicAlpha = 0xCC;
```

`:97-124` ctor'un gövdesini değiştir:

```csharp
        _iconSizeDip = Math.Clamp(
            config.Dock.IconSize <= 0 ? DockConfig.DefaultIconSize : config.Dock.IconSize,
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);
        _autoHide = new DockAutoHideController(config.Dock.AutoHide, Dispatcher);

        _cursorTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(CursorFrameMs),
        };
        _cursorTimer.Tick += OnCursorFrame;

        ApplyStripGeometry();

        // Keep XAML padding in sync with the height budget used by SetWindowPos.
        Surface.Padding = new Thickness(
            HorizontalPaddingDip,
            VerticalPaddingDip,
            HorizontalPaddingDip,
            VerticalPaddingDip);
```

`:908-922` bloğunu değiştir:

```csharp
    private int ComputeMaxWidthPx(uint dpi)
    {
        // Budget the window for the worst magnified layout. Hover only ever re-runs WPF
        // measure/arrange — no SetWindowPos per frame, which is what used to stutter.
        double maxScale = DockMagnification.Clamp(_config.Dock.Magnification);
        double panel = _strip.MaxLiveWidth();
        double width = panel + (2d * HorizontalPaddingDip);
        return Display.DipToPixels(width, dpi);
    }

    private double MaxHeightDip =>
        DockStripLayout.MaxExtentHeight(
            _iconSizeDip,
            RunningIndicatorGapDip,
            RunningIndicatorSizeDip,
            VerticalPaddingDip,
            DockMagnification.Clamp(_config.Dock.Magnification));

    /// <summary>Pushes icon metrics and the magnification ceiling into the strip.</summary>
    private void ApplyStripGeometry()
    {
        _strip.IconSize = _iconSizeDip;
        _strip.IconGap = IconGapDip;
        _strip.IndicatorGap = RunningIndicatorGapDip;
        _strip.IndicatorSize = RunningIndicatorSizeDip;
        _strip.MaxScale = DockMagnification.Clamp(_config.Dock.Magnification);
    }
```

- [ ] **Step 6: `RebuildStrip`'i yeniden yaz ve eski görsel üretimini sil**

`src/WharfBar/Views/DockWindow.xaml.cs:325-409` — `RebuildStrip`, `CreateSeparator`, `CreateIconVisual` bloklarının yerine:

```csharp
    private void RebuildStrip()
    {
        IReadOnlyList<DockAppItem> items = _composer!.Items;
        IReadOnlyList<DockStripRow> rows = DockStripBuilder.Build(items);
        int iconPx = ResolveIconPixelSize();

        var viewModels = new List<DockItemViewModel>(rows.Count);
        foreach (DockStripRow row in rows)
        {
            viewModels.Add(row.IsSeparator
                ? DockItemViewModel.ForSeparator()
                : DockItemViewModel.ForItem(row.Item!, _icons.GetIcon(row.Item!.Path, iconPx)));
        }

        _strip.SetRows(viewModels);

        // One block, on purpose: a cell reused from the previous rebuild must always get a
        // fresh tooltip and menu, otherwise a stale menu leaks onto a different app.
        foreach (DockItemCell cell in _strip.Cells)
        {
            DockItemViewModel? row = cell.Row;
            if (row is null || row.IsSeparator)
            {
                continue;
            }

            cell.ToolTip = CreateDockTip(row.Tooltip, cell);
            cell.ContextMenu = BuildContextMenuForItem(row.Item!);
        }

        ApplyPlacement();
    }
```

`CreateDockTip(string, UIElement)` mevcut tooltip kurucusunu alır ve ilk parametresi `item.Label` yerine verilen metni kullanır.

`RebuildStrip` artık bağlam menüsünün **başlık kartını** `item.Label`'dan okuduğu için `BuildContextMenuForApp` **adını** `BuildContextMenuForItem` yap ve içindeki `item` referanslarını koru.

`ApplyIconSizeFromConfig` metodunun sonuna `ApplyStripGeometry();` ekle — ikon boyutu değişince hem
ikon ölçüleri hem büyütme tavanı yeniden itilmeli.

- [ ] **Step 7: Fare takibi ve yumuşatma döngüsünü ekle**

`src/WharfBar/Views/DockWindow.xaml.cs` — alan bildirimlerine ekle:

```csharp
    private readonly DispatcherTimer _cursorTimer;
    private double _cursorTarget;
    private long _lastCursorFrameMs;
```

`OnSourceInitialized` içinde, `_composer.Changed += OnComposerChanged;` satırından sonra:

```csharp
        ApplyStripGeometry();
        Strip.MouseMove += OnStripMouseMove;
        Strip.MouseLeave += OnStripMouseLeave;
```

`OnClosed` içinde, `_source?.RemoveHook(WndProc);` satırından **önce**:

```csharp
        Strip.MouseMove -= OnStripMouseMove;
        Strip.MouseLeave -= OnStripMouseLeave;
        _cursorTimer.Tick -= OnCursorFrame;
        _cursorTimer.Stop();
```

Metotları ekle:

```csharp
    private void OnStripMouseMove(object sender, MouseEventArgs e)
    {
        _cursorTarget = Strip.HitTestCursor(e.GetPosition(Strip).X);

        if (!_cursorTimer.IsEnabled)
        {
            _lastCursorFrameMs = Environment.TickCount64;
            _cursorTimer.Start();
        }

        AnimatePhase(1d, PhaseInMs);
    }

    private void OnStripMouseLeave(object sender, MouseEventArgs e)
    {
        _cursorTimer.Stop();
        AnimatePhase(0d, PhaseOutMs);
    }

    private void OnCursorFrame(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = now - _lastCursorFrameMs;
        _lastCursorFrameMs = now;

        double next = DockMagnification.Eased(Strip.CursorPosition, _cursorTarget, dt);
        if (Math.Abs(next - _cursorTarget) < 0.001d)
        {
            Strip.CursorPosition = _cursorTarget;
            _cursorTimer.Stop();
            return;
        }

        Strip.CursorPosition = next;
    }

    private void AnimatePhase(double to, int milliseconds)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Strip.BeginAnimation(DockStrip.PhaseProperty, animation);
    }
```

- [ ] **Step 8: Plaka dolgusunu ve üst ışık çizgisini spec'e göre ayarla**

`src/WharfBar/Views/DockWindow.xaml.cs:37-39` sabitlerini değiştir:

```csharp
    /// <summary>Mica translucency over the theme color (0–255 alpha).</summary>
    private const byte MicaDarkAlpha = 0x99;

    private const byte MicaLightAlpha = 0xD9;

    /// <summary>Acrylic surface translucency (0–255 alpha over the theme color).</summary>
    private const byte AcrylicDarkAlpha = 0xCC;

    private const byte AcrylicLightAlpha = 0xE6;
```

`ApplySurfaceFromConfig` (`:213-223`) gövdesini değiştir:

```csharp
    /// <summary>
    /// The dock surface is drawn by WPF (layered window), so DWM backdrops are off the table
    /// (docs/04-gotchas.md:7). Mica is a translucent theme tint; Acrylic is denser. Both follow
    /// the system light/dark preference.
    /// </summary>
    private void ApplySurfaceFromConfig()
    {
        bool acrylic = string.Equals(_config.Dock.Surface, DockSurfaceStyles.Acrylic, StringComparison.OrdinalIgnoreCase);
        bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        byte baseRgb = dark ? (byte)0x20 : (byte)0xFA;
        byte alpha = acrylic
            ? (dark ? AcrylicDarkAlpha : AcrylicLightAlpha)
            : (dark ? MicaDarkAlpha : MicaLightAlpha);

        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseRgb, baseRgb, baseRgb));
        brush.Freeze();
        Surface.Background = brush;
    }
```

`src/WharfBar/Views/DockWindow.xaml` içinde `Surface` `Border`'ının içine, `DockStrip`'ten hemen önce iç üst ışık çizgisini ekle:

```xml
            <Border BorderThickness="0,1,0,0" BorderBrush="#24FFFFFF"
                    Margin="0,0,0,-1" IsHitTestVisible="False" />
```

Bu, tek piksellik `#1AFFFFFF` kenarın üstünde ince bir highlight verir (mockupta seçilen "üst ışık çizgisi"). **Gölge eklenmiyor** — `DropShadowEffect` kare başına yazılım maliyeti getirir.

- [ ] **Step 9: İkon çözünürlüğünü büyütme tavanına bağla**
`src/WharfBar/Views/DockWindow.xaml.cs:651-658` — `ResolveIconPixelSize`:

```csharp
    private int ResolveIconPixelSize()
    {
        uint dpi = _source is null ? Monitor.Dpi : Display.GetDpi(_source.Handle);
        // Request the bitmap at the magnification ceiling so a magnified icon stays sharp —
        // upscaling a resting-size bitmap looks blurry (docs/04-gotchas.md:31).
        double maxScale = DockMagnification.Clamp(_config.Dock.Magnification);
        int px = Display.DipToPixels(_iconSizeDip * maxScale, dpi);
        return Math.Clamp(px <= 0 ? 24 : px, 16, 256);
    }
```

Artık kullanılmayanları sil: `HoverScale`, `HoverDurationMs`, `AnimateScale`, `CreateSeparator`, `CreateIconVisual`, `_dockHeightDip` alanı.

- [ ] **Step 10: Derle**

```
dotnet build WharfBar.sln
```
Beklenen: 0 hata. Kalan uyarıları düzelt — özellikle `DockStrip`'te eksik `using` satırları.

- [ ] **Step 11: Tüm testler**

```
dotnet test WharfBar.sln
```
Beklenen: **522 geçen**, 0 başarısız. (Yeni saf mantık yok; mevcut davranış korunuyor.)

- [ ] **Step 12: Elle doğrulama**

Uygulamayı **normal yoldan** çalıştır (`dotnet run --project src/WharfBar`), sonra pencereyi normal kapat. Kontrol listesi:
1. Dock görünür; simgeler temiz (bulut değil).
2. Fare ikonların üstüne girince ikon büyür, **komşuları da** büyür ve yatayda kayar.
3. Fare çıktığında ikonlar yumuşakça eski boyutuna döner.
4. Pencere **kaymıyor**, şerit ortalanıyor, büyüyen ikon **plakanın üstüne taşıyor**.
5. Çalışan uygulama noktası yerine **W11 çizgisi** görünüyor.
6. Sağ tık menüsü çalışıyor; "Dock'tan kaldır" **görev çubuğuna dokunmadan** dock'tan siliyor.
7. Simge boyutu 16 ve 36 değerlerinde şerit taşmıyor.

- [ ] **Step 13: Commit**

```
git add src/WharfBar/Views/DockItemViewModel.cs src/WharfBar/Views/Controls/DockItemCell.xaml src/WharfBar/Views/Controls/DockItemCell.xaml.cs src/WharfBar/Views/Controls/DockStrip.cs src/WharfBar/Views/DockWindow.xaml src/WharfBar/Views/DockWindow.xaml.cs
git commit -m "feat(dock): ozel panel, komsu buyumesi ve yeniden kurgulanmis hucre"
```

---

## Task 8: Konum, DPI tazeleme, görev çubuğu bağımlılığının temizlenmesi, "Pencereler" menüsü

**Files:**
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs` (`:97-124` ctor DPI alanı, `:723-761` `TryGetPlacementGeometry`, `:972-1005` `WndProc`, `:430-529` `BuildContextMenuForApp`)
- Delete: `src/WharfBar/Services/TaskbarPinnedAppsSource.cs` — **yalnızca** referans taraması temizse
- Modify: `docs/02-win32-appbar-dwm.md` (`IsIconic` imzası)

**Interfaces:**
- Consumes: `DockPositions` (Task 3), `MinimizedWindowsSource.Windows` / `ForProcess` (Task 5), `WindowActivationService.TryActivate` (mevcut)
- Produces: konum çözümü `bottom-left|center|right`, `WM_DPICHANGED` ile `_monitorDpi` tazeleme, "Pencereler" alt menüsü

- [ ] **Step 1: Görev çubuğu pin kaynağı referansını tara**

```
rg -n "TaskbarPinnedAppsSource|GetTaskBarPinnedDirectory|ShortcutResolver.TryCreateShortcut" src tests
```
Beklenen: yalnızca `Services/TaskbarPinnedAppsSource.cs` içinde eşleşme. Başka bir yerde eşleşme varsa **dosyayı silme** — bu durumda `DockAppsComposer`'a bağlı kalmayan tüketiciler not edilir ve dosya bırakılır.

- [ ] **Step 2: Konum çözümünü ekle**

`src/WharfBar/Views/DockWindow.xaml.cs` — `TryGetPlacementGeometry` içinde (`:751`):

```csharp
        int x = ResolvePlacementX(monitor, widthPx, dpi);
```

ve metodu ekle:

```csharp
    /// <summary>
    /// Horizontal anchor. Only bottom-left/center/right: the work-area reservation and the
    /// downward auto-hide slide stay untouched, so a side-docked variant would be a
    /// different feature (spec §4.2).
    /// </summary>
    private int ResolvePlacementX(RECT monitor, int widthPx, uint dpi)
    {
        int marginPx = Display.DipToPixels(12d, dpi);

        return DockPositions.Normalize(_config.Dock.Position) switch
        {
            DockPositions.BottomLeft => monitor.Left + marginPx,
            DockPositions.BottomRight => monitor.Right - widthPx - marginPx,
            _ => monitor.Left + ((monitor.Width - widthPx) / 2),
        };
    }
```

- [ ] **Step 3: DPI'yi `WM_DPICHANGED`'den tazele**

`src/WharfBar/Views/DockWindow.xaml.cs` — `Monitor.Dpi` okuyan **tüm** yerler `_monitorDpi` kullanacak şekilde değiştirilir (`TryGetPlacementGeometry`, `BuildEdgeProbe`, `ResolveIconPixelSize`).

Alan bildirimi ekle:

```csharp
    /// <summary>
    /// Live DPI for this window. Starts at <see cref="MonitorInfo.Dpi"/> and is refreshed
    /// from <c>WM_DPICHANGED</c> so a runtime scale change does not leave the strip sized
    /// for the old DPI.
    /// </summary>
    private uint _monitorDpi;
```

Ctor'da `Monitor = monitor;` satırından sonra:

```csharp
        _monitorDpi = monitor.Dpi;
```

`WndProc` içindeki `WM_DPICHANGED`/`WM_DISPLAYCHANGE` bloğunu ayır:

```csharp
            case NativeMethods.WM_DPICHANGED:
                // wParam high word carries the new DPI; MonitorInfo.Dpi was captured at
                // construction and goes stale when the user changes display scale.
                _monitorDpi = (uint)(wParam >> 16);
                if (_monitorDpi == 0)
                {
                    _monitorDpi = Monitor.Dpi;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CancelYAnimation();
                    ApplyPlacement();
                }));
                break;

            case NativeMethods.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CancelYAnimation();
                    ApplyPlacement();
                }));
                break;
```

- [ ] **Step 4: "Pencereler" alt menüsünü ekle**

`src/WharfBar/Views/DockWindow.xaml.cs` — `BuildContextMenuForApp` metodunu `BuildContextMenuForItem` olarak yeniden adlandır ve görev sonlandırma/dosya konumu bloklarından sonuna ekle:

```csharp
        if (item.MainWindowHandle != IntPtr.Zero
            && item.Section is DockSection.Running or DockSection.Minimized)
        {
            uint pid = NativeMethods.GetWindowThreadProcessId(item.MainWindowHandle, out uint resolved);
            IReadOnlyList<AppWindowInfo> windows = pid == 0
                ? []
                : MinimizedWindowsSource.ForProcess(_minimizedSource?.Windows ?? [], pid);

            if (windows.Count > 0)
            {
                menu.Items.Add(new Separator());

                var windowsMenu = new MenuItem { Header = "Pencereler" };
                foreach (AppWindowInfo window in windows)
                {
                    var entry = new MenuItem
                    {
                        Header = string.IsNullOrWhiteSpace(window.Title)
                            ? "Başlıksız pencere"
                            : window.Title,
                    };
                    entry.Click += (_, _) => _activation?.TryActivate(window.Handle);
                    windowsMenu.Items.Add(entry);
                }

                menu.Items.Add(windowsMenu);
            }
        }
```

> `MenuItem` bu dosyada `Wpf.Ui.Controls.MenuItem` olarak zaten alias'lı; `Separator` de aynı ad alanından gelir. `DockSection` için `using WharfBar.Models;` zaten mevcut.

- [ ] **Step 5: Görev çubuğu pin yazma yolunu kaldır**

`src/WharfBar/Views/DockWindow.xaml.cs` — `TogglePin` metodunun gövdesini değiştir:

```csharp
    /// <summary>
    /// Adds or removes the item from WharfBar's own dock list. The Windows taskbar is
    /// never written — the dock stopped mirroring it in this redesign.
    /// </summary>
    private void TogglePin(DockAppItem item, bool pin)
    {
        if (_itemStore is null)
        {
            return;
        }

        bool changed = pin
            ? _itemStore.TryAdd(item.Path, item.Args, item.Label)
            : _itemStore.Remove(item.Id);

        if (!changed)
        {
            return;
        }

        // DockItemStore raises Changed, which reaches the composer and rebuilds the strip.
        ApplyPlacement();
    }
```

`SanitizeFileName` artık kullanılmıyor — sil. `ProcessLauncherService.LaunchAsAdmin`, `OpenFileLocation` ve "Yeni pencere aç" korunur.

- [ ] **Step 6: Referans taraması temizse `TaskbarPinnedAppsSource`'ı sil**

```
Remove-Item -LiteralPath src\WharfBar\Services\TaskbarPinnedAppsSource.cs
dotnet build WharfBar.sln
```
Beklenen: 0 hata. Adı geçen başka dosya yoksa `ShortcutResolver.TryCreateShortcut` de kullanılmıyor olabilir — o durumda `ShortcutResolver` yalnızca `TryResolve` içeren tek metoda düşürülür.

- [ ] **Step 7: Doküman**

`docs/02-win32-appbar-dwm.md` §e (dock ikon) bölümüne ekle:

```
GetMinimizedCandidates(): EnumWindows + IsIconic(hwnd) + mevcut IsCandidate filtresi.
Küçültülmüş pencereler WS_VISIBLE korur, bu yüzden ayrı IsIconic filtresi şarttır.
UI iş parçacığında EnumWindows çağrırma (donmuş HWND'de kilitler) — pencere tablosu
MinimizedWindowsSource'da arka iş parçacığında 800 ms'de bir kez taranır.
```

- [ ] **Step 8: Derle, test et, elle doğrula**

```
dotnet build WharfBar.sln && dotnet test WharfBar.sln
```
Beklenen: 0 hata, **522 geçen**.

Elle: `dotnet run --project src/WharfBar`, normal kapat. Ayarlar'da `dock.position` değerini `config.json` içinde elle `bottom-left` yapıp yeniden başlat → dock sola yaslanıyor, pencereden taşmıyor. Bir çalışan uygulamaya sağ tık → "Pencereler" listesi var, bir pencereye tıklamak öne getiriyor.

- [ ] **Step 9: Commit**

```
git add src/WharfBar/Views/DockWindow.xaml.cs docs/02-win32-appbar-dwm.md
git add -A src/WharfBar/Services/TaskbarPinnedAppsSource.cs
git commit -m "feat(dock): konum secimi, canli DPI ve pencere listesi menusu"
```

---

## Task 9: Sürükleyerek sıralama, dosya bırakma, tıklama davranışı

**Files:**
- Create: `src/WharfBar/Services/DockDropFormats.cs`
- Create: `tests/WharfBar.Tests/DockDropFormatTests.cs`
- Modify: `src/WharfBar/Views/Controls/DockStrip.cs` (`HitTestCursor` yanına `ResolveInsertIndex`, `RestingLeftOf`)
- Modify: `src/WharfBar/Views/DockWindow.xaml` (`Surface` olayları + `DropIndicator`)
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs` (hücre olay bağlama, tıklama, sürükleme)

**Interfaces:**
- Consumes: `DockItemStore.TryAdd/Move` (Task 4), `ShortcutResolver.TryResolve` (mevcut), `WindowActivationService` (mevcut), `DockStripLayout` (Task 2)
- Produces:
  - `WharfBar.Services.DockDropFormats` — `static bool IsSupported(string path)`, `static (string Path, string Args) Resolve(string path)`
  - `DockStrip.ResolveInsertIndex(double x) : int`, `DockStrip.RestingLeftOf(int index) : double`

> **OLE riski (spec R1):** katmanlı `Topmost` pencerenin OLE bırakma hedefi kabulü hiç ölçülmedi (`docs/04-gotchas.md:65`). `DragOver` bırakma teklif edilmediğinde `Effects = None` döner; ayarlar listesindeki "ekle" akışı (Task 10) yedek yol olarak zaten var. Bırakma çalışmasa bile uygulama kullanılabilir kalır.

- [ ] **Step 1: `DockDropFormats` testlerini yaz**

`tests/WharfBar.Tests/DockDropFormatTests.cs`:

```csharp
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockDropFormatTests
{
    [Theory]
    [InlineData(@"C:\apps\thing.exe", true)]
    [InlineData(@"C:\apps\thing.EXE", true)]
    [InlineData(@"C:\apps\thing.lnk", true)]
    [InlineData(@"C:\apps\thing.bat", true)]
    [InlineData(@"C:\apps\thing.cmd", true)]
    [InlineData(@"C:\apps\notes.txt", false)]
    [InlineData(@"C:\apps\folder", false)]
    [InlineData("", false)]
    public void IsSupported_FollowsTheExtensionAllowList(string path, bool expected)
    {
        Assert.Equal(expected, DockDropFormats.IsSupported(path));
    }

    [Fact]
    public void Resolve_WhenExtensionIsUnsupported_ReturnsEmpty()
    {
        (string path, string args) = DockDropFormats.Resolve(@"C:\apps\notes.txt");
        Assert.Equal(string.Empty, path);
        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void Resolve_WhenPlainExecutable_ReturnsPathUnchanged()
    {
        (string path, string args) = DockDropFormats.Resolve(@"C:\apps\thing.exe");
        Assert.Equal(@"C:\apps\thing.exe", path);
        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void Resolve_WhenShortcutIsUnreadable_KeepsShortcutPath()
    {
        // No such file on disk: the resolver cannot read it, so the .lnk path is used and
        // the store derives a label from the file name.
        (string path, _) = DockDropFormats.Resolve(@"C:\nope\missing.lnk");
        Assert.Equal(@"C:\nope\missing.lnk", path);
    }
}
```

- [ ] **Step 2: `DockDropFormats` uygulamasını yaz**

`src/WharfBar/Services/DockDropFormats.cs`:

```csharp
using System;
using System.IO;

namespace WharfBar.Services;

/// <summary>Which dropped files the dock accepts, and how to turn one into a list entry.</summary>
internal static class DockDropFormats
{
    internal static readonly string[] Extensions = [".lnk", ".exe", ".bat", ".cmd"];

    internal static bool IsSupported(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        foreach (string candidate in Extensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a dropped file into a dock list entry. Labels are left empty on purpose so
    /// <see cref="DockItemStore.TryAddCore"/> derives them from the file name in one place.
    /// </summary>
    internal static (string Path, string Args) Resolve(string path)
    {
        if (!IsSupported(path))
        {
            return (string.Empty, string.Empty);
        }

        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            && ShortcutResolver.TryResolve(path, out string target, out string args, out _))
        {
            return (target, args);
        }

        return (path, string.Empty);
    }
}
```

- [ ] **Step 3: `DockStrip`'a bırakma çözümlemesi ekle**

`src/WharfBar/Views/Controls/DockStrip.cs` — `HitTestCursor` metodundan sonra ekle:

```csharp
    /// <summary>
    /// Insertion index for a drag, using the resting layout so the target does not drift
    /// while magnified. Returns <c>SlotCount</c> to mean "append at the end".
    /// </summary>
    public int ResolveInsertIndex(double x)
    {
        int count = _cells.Count;
        if (count == 0)
        {
            return 0;
        }

        if (x > _centers[count - 1])
        {
            return count;
        }

        double slot = HitTestCursor(x);
        return Math.Clamp((int)Math.Floor(slot + 0.5d), 0, count);
    }

    /// <summary>Resting left edge of a slot; the resting right edge for <c>SlotCount</c>.</summary>
    public double RestingLeftOf(int index) =>
        index < 0 || index >= _offsets.Length || index >= _cells.Count
            ? _restingWidth
            : _offsets[index];
```

`DockStrip.cs` başına `using System;` ekle.

- [ ] **Step 4: `DockWindow.xaml`'a bırakma kancalarını ekle**

`src/WharfBar/Views/DockWindow.xaml` içinde `<Grid>` bloğunu değiştir:

```xml
    <Grid AllowDrop="True" DragOver="OnSurfaceDragOver" Drop="OnSurfaceDrop">
        <Border x:Name="Surface"
                Padding="12,6"
                Background="Transparent"
                CornerRadius="12"
                BorderThickness="1"
                BorderBrush="#1AFFFFFF"
                HorizontalAlignment="Center"
                VerticalAlignment="Bottom">
            <controls:DockStrip x:Name="Strip" />
        </Border>

        <!-- Reorder affordance. Anchored to the resting layout, so drags run with
             magnification switched off (see OnCellMouseMove). -->
        <Border x:Name="DropIndicator"
                Width="2"
                Height="30"
                CornerRadius="1"
                HorizontalAlignment="Left"
                VerticalAlignment="Bottom"
                Background="{DynamicResource AccentFillColorDefaultBrush}"
                Visibility="Collapsed" />
    </Grid>
```

- [ ] **Step 5: Hücre olaylarını tek blokta bağla**

`src/WharfBar/Views/DockWindow.xaml.cs` — `RebuildStrip` içindeki `foreach (DockItemCell cell in _strip.Cells)` döngüsünü şu hale getir:

```csharp
        // One block, on purpose: a cell reused from the previous rebuild must always get a
        // fresh tooltip and menu, and its handlers must be wired exactly once. Binding a
        // subset silently leaves an element dead (see TopBarWindow.xaml:156-157).
        foreach (DockItemCell cell in _strip.Cells)
        {
            DockItemViewModel? row = cell.Row;
            if (row is null || row.IsSeparator)
            {
                continue;
            }

            cell.ToolTip = CreateDockTip(row.Tooltip, cell);
            cell.ContextMenu = BuildContextMenuForItem(row.Item!);
            cell.AllowDrop = true;
            cell.PreviewMouseLeftButtonDown += OnCellPreviewMouseLeftButtonDown;
            cell.MouseLeftButtonUp += OnCellMouseLeftButtonUp;
            cell.PreviewMouseMove += OnCellMouseMove;
            cell.DragOver += OnCellDragOver;
            cell.Drop += OnCellDrop;
        }
```

> `DockItemCell` örnekleri yeniden kullanıldığı için `SetRows` bunları `DockWindow`'dan gizler; bu döngü her yeniden kurulumda **bir kez** çalışır, `+=` çoğul bağlama oluşturmaz.

- [ ] **Step 6: Tıklama, sürükleme ve bırakma işleyicilerini ekle**

> ⚠️ **Bu adım koda birebir uygulanmadı.** Aşağıdaki `_suppressClickFor` **anahtar tabanlı**
> tasarım yerine **anahtarsız `bool _suppressNextClick`** uygulandı (spec §0 #1): bırakma sırayı
> değiştirir → composer `Changed` → `RebuildStrip` → `SetRows` hücreleri indikse göre yeniden
> eşler, yani bırakmanın düştüğü hücre o anda başka bir kutucuğu tutuyor olabilir; `Id`
> karşılaştırması ıskalar ve sürükleme uygulamayı açar. Aynı şekilde `_dragItemId` `row.Id`
> değil `DockItemStore.Normalize(item.Path)` (`ReorderKeyFor`) ile doldurulur. Kullanılan hâli:
> `DockWindow.xaml.cs` → `_suppressNextClick`, `ReorderKeyFor`, `OnCellMouseLeftButtonUp`.

`src/WharfBar/Views/DockWindow.xaml.cs` — alan bildirimlerine ekle:

```csharp
    private Point _dragOrigin;
    private string? _dragItemId;

    /// <summary>
    /// Item id whose next <c>MouseLeftButtonUp</c> must be swallowed. Without this a
    /// reorder drag would also launch the app.
    /// </summary>
    private string? _suppressClickFor;
```

Metodları ekle:

```csharp
    private void OnCellPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DockItemCell { Row: { IsSeparator: false } row })
        {
            return;
        }

        // Foreground must be sampled before anything else touches focus (docs/04-gotchas.md:30).
        _activation?.CaptureForegroundHint();
        _dragOrigin = e.GetPosition(null);
        _dragItemId = row.IsPinned ? row.Id : null;
    }

    private void OnCellMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DockItemCell { Row: { IsSeparator: false } row })
        {
            return;
        }

        if (string.Equals(_suppressClickFor, row.Id, StringComparison.OrdinalIgnoreCase))
        {
            _suppressClickFor = null;
            return;
        }

        _activation?.LaunchOrActivate(row.Item!, _launcher);
    }

    private void OnCellMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton is not MouseButtonState.Pressed || _dragItemId is null)
        {
            return;
        }

        Point position = e.GetPosition(null);
        double dx = position.X - _dragOrigin.X;
        double dy = position.Y - _dragOrigin.Y;
        if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        string dragged = _dragItemId;
        _dragItemId = null;

        // Rest the strip while reordering: the drop indicator is anchored to the resting
        // layout, and a magnified strip under the cursor is just noise mid-drag.
        AnimatePhase(0d, PhaseOutMs);

        if (DragDrop.DoDragDrop((DependencyObject)sender, dragged, DragDropEffects.Move)
            == DragDropEffects.Move)
        {
            _suppressClickFor = dragged;
        }
    }

    private void OnCellDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.StringFormat) is not string dragged || dragged.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            HideDropIndicator();
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        ShowDropIndicator(Strip.ResolveInsertIndex(e.GetPosition(Strip).X));
    }

    private void OnCellDrop(object sender, DragEventArgs e)
    {
        HideDropIndicator();

        if (e.Data.GetData(DataFormats.StringFormat) is not string dragged || dragged.Length == 0)
        {
            return;
        }

        _itemStore?.Move(dragged, Strip.ResolveInsertIndex(e.GetPosition(Strip).X));
        e.Handled = true;
    }

    private void OnSurfaceDragOver(object sender, DragEventArgs e)
    {
        // A layered Topmost window may not be offered OLE drops at all; then Effects stays
        // None and the drag simply is not accepted (spec risk R1).
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSurfaceDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) is false
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        int added = 0;
        foreach (string path in paths)
        {
            (string resolvedPath, string args) = DockDropFormats.Resolve(path);
            if (_itemStore?.TryAdd(resolvedPath, args, string.Empty) == true)
            {
                added++;
            }
        }

        Debug.WriteLine($"[WharfBar] dock drop: {added}/{paths.Length} added");
        e.Handled = true;
    }

    private void ShowDropIndicator(int index)
    {
        if (index < 0 || index > Strip.SlotCount || VisualTreeHelper.GetParent(DropIndicator) is not DependencyObject host)
        {
            HideDropIndicator();
            return;
        }

        Point left = Strip.TranslatePoint(new Point(Strip.RestingLeftOf(index), 0), host);
        DropIndicator.Height = Strip.RestingHeight;
        DropIndicator.Margin = new Thickness(left.X, 0, 0, 0);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;
```

`src/WharfBar/Views/DockWindow.xaml.cs` başına `using System.Windows;` (zaten var) ve `using System.Windows.Media;` (zaten var) doğrula; `DragDrop` için `System.Windows` yeterlidir.

- [ ] **Step 7: Testler ve derleme**

```
dotnet build WharfBar.sln && dotnet test WharfBar.sln
```
Beklenen: 0 hata, **532 geçen** (522 + 10 `DockDropFormatTests`) — *nihai sayı Task 10 sonrası **566** oldu.*

- [ ] **Step 8: Elle doğrulama**

1. Bir simgeyi tutup sağa sürükle → accent çizgi hedefi gösteriyor, bırakınca sıra değişiyor, **uygulama açılmıyor**.
2. Sadece tıkla → uygulama açılıyor/kisayola geliyor.
3. Explorer'dan bir `.exe` dock'a sürükle → ekleniyor. Eklenmezse `Effects` None'dur; ayarlar listesindeki `+` yolunu kullan (Task 10). Sonucu not et.
4. Aynı dosyayı ikinci kez bırak → tekrarlanmıyor.
5. Fare dock'tan çıkınca bırakma çizgisi kayboluyor.

- [ ] **Step 9: Commit**

```
git add src/WharfBar/Services/DockDropFormats.cs tests/WharfBar.Tests/DockDropFormatTests.cs src/WharfBar/Views/Controls/DockStrip.cs src/WharfBar/Views/DockWindow.xaml src/WharfBar/Views/DockWindow.xaml.cs
git commit -m "feat(dock): siralamak icin surukle-birak, dosya birakma, tik catismasi cozumu"
```

---

## Task 10: Tek mağazalı `DockItemStore` ve `DockPage` ayarları

**Files:**
- Modify: `src/WharfBar/App.xaml.cs` (`DockItems` özelliği)
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs` (store'u `App`'ten al)
- Modify: `src/WharfBar/Views/Pages/DockPage.xaml`
- Modify: `src/WharfBar/Views/Pages/DockPage.xaml.cs`

**Interfaces:**
- Consumes: `DockItemStore` (Task 4), `DockPositions` (Task 3), `DockMagnification` (Task 1), `DockDropFormats.Resolve` (Task 9)
- Produces: `App.DockItems : DockItemStore` (tek örnek), `DockPage` yeni kartlar

> **Neden tek mağaza:** `DockWindow.OnSourceInitialized` her pencere için kendi `DockItemStore`'unu kuruyor. Çok ekranlı kurulumda ve ayar sayfası için bu yanlış: ayar sayfasındaki bir değişiklik dock pencerelerine ulaşamaz. Store `App` tarafından bir kez kurulur ve her yerde aynı örnek kullanılır.

- [ ] **Step 1: `App` store'u sahiplensin**

`src/WharfBar/App.xaml.cs` — `Config` özelliğinin yanına:

```csharp
    /// <summary>
    /// The single dock item store. Shared by every per-monitor dock window and the settings
    /// page so list edits propagate live instead of only through disk.
    /// </summary>
    internal DockItemStore? DockItems => _dockItems;

    private DockItemStore? _dockItems;
```

`OnStartup` içinde, `_config` atandıktan hemen sonra (uygulama `LoadOrCreate` çağrısının hemen ardından):

```csharp
        _dockItems = new DockItemStore(_config);
```

- [ ] **Step 2: `DockWindow` store'u `App`'ten alsın**

`src/WharfBar/Views/DockWindow.xaml.cs`:

```csharp
    private DockItemStore? _itemStore => ((App)Application.Current).DockItems;
```

`OnSourceInitialized` içindeki `_itemStore = new DockItemStore(WharfBarConfigService());` satırını **sil**; `WharfBarConfigService` yardımcı özelliği de kullanılmıyorsa sil.

`OnClosed` içindeki `_itemStore` ile ilgili hiçbir işlem yok — store `App` ömrüne bağlıdır.

- [ ] **Step 3: `DockPage.xaml`'a içerik kartını ve yeni ayarları ekle**

`src/WharfBar/Views/Pages/DockPage.xaml` — `<StackPanel MaxWidth="850" ...>` içine, mevcut "Davranış" kartından **önce** "İçerik" bölümünü ekle:

```xml
            <!-- İçerik Section -->
            <TextBlock Text="İçerik" FontSize="14" FontWeight="SemiBold"
                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,0,0,8" />

            <Border Style="{StaticResource PowerToysCard}">
                <StackPanel>
                    <Grid Margin="0,0,0,12">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <TextBlock Grid.Column="0" Text="Dock Öğeleri" FontSize="14" FontWeight="Medium"
                                   VerticalAlignment="Center" />

                        <ui:Button x:Name="AddItemButton" Grid.Column="1"
                                   Content="Ekle" Height="30"
                                   Click="OnAddItemClick" />
                    </Grid>

                    <ItemsControl x:Name="ItemsPanel">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border x:Name="ItemCard"
                                        Background="{DynamicResource ControlFillColorDefaultBrush}"
                                        BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                                        BorderThickness="1"
                                        CornerRadius="8"
                                        Padding="12,8"
                                        Margin="0,0,0,6"
                                        AllowDrop="True">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="Auto" />
                                            <ColumnDefinition Width="*" />
                                            <ColumnDefinition Width="Auto" />
                                            <ColumnDefinition Width="Auto" />
                                        </Grid.ColumnDefinitions>

                                        <ui:SymbolIcon Grid.Column="0" Symbol="ReorderDotsVertical24"
                                                       FontSize="16" VerticalAlignment="Center"
                                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                                       ToolTip="Sürükleyerek sırayı değiştirin" />

                                        <TextBlock Grid.Column="1" Text="{Binding Label}"
                                                   FontSize="13" VerticalAlignment="Center"
                                                   Margin="10,0,10,0" TextTrimming="CharacterEllipsis" />

                                        <TextBlock Grid.Column="2" Text="{Binding Path}"
                                                   FontSize="11" VerticalAlignment="Center"
                                                   Opacity="0.55" Margin="0,0,10,0"
                                                   TextTrimming="CharacterEllipsis" />

                                        <ui:SymbolIcon Grid.Column="3" Symbol="Dismiss24"
                                                       FontSize="16" VerticalAlignment="Center"
                                                       Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <TextBlock x:Name="EmptyHint" Text="Dock boş. Bir uygulamayı Explorer'dan
                               doğrudan dock'a sürükleyebilir veya yukarıdan ekleyebilirsin."
                               FontSize="11" Opacity="0.55" TextWrapping="Wrap"
                               Visibility="Collapsed" />
                </StackPanel>
            </Border>
```

Mevcut "Görünüm" kartının `<Grid.RowDefinitions>` bloğuna iki satır daha ekle (`RowDefinition Height="Auto"` × 2) ve kartın içine şu iki bloğu ekle (mevcut `Yüzey` satırından sonra):

```xml
                    <Grid Grid.Row="3" Margin="0,12,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <ui:SymbolIcon Grid.Column="0" Symbol="ZoomIn24"
                                       FontSize="20"
                                       Foreground="{DynamicResource AccentFillColorDefaultBrush}"
                                       VerticalAlignment="Center" Margin="0,0,16,0" />

                        <TextBlock Grid.Column="1" Text="Büyütme" FontSize="14" FontWeight="Medium" VerticalAlignment="Center" />

                        <TextBlock x:Name="MagnificationLabel" Grid.Column="2" Text="1.65×"
                                   HorizontalAlignment="Right" VerticalAlignment="Center"
                                   FontSize="13" FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                    </Grid>

                    <Slider x:Name="MagnificationSlider" Grid.Row="4"
                            Minimum="1" Maximum="2"
                            IsSnapToTickEnabled="True" TickFrequency="0.05"
                            Margin="36,12,0,4"
                            ValueChanged="OnMagnificationChanged" />

                    <Grid Grid.Row="5" Margin="0,12,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <ui:SymbolIcon Grid.Column="0" Symbol="DockRight24"
                                       FontSize="20"
                                       Foreground="{DynamicResource AccentFillColorDefaultBrush}"
                                       VerticalAlignment="Center" Margin="0,0,16,0" />

                        <TextBlock Grid.Column="1" Text="Konum" FontSize="14" FontWeight="Medium" VerticalAlignment="Center" />

                        <ComboBox x:Name="PositionCombo" Grid.Column="2" Width="180"
                                  HorizontalAlignment="Right" VerticalAlignment="Center">
                            <ComboBoxItem Content="Orta (varsayılan)" />
                            <ComboBoxItem Content="Sol" />
                            <ComboBoxItem Content="Sağ" />
                        </ComboBox>
                    </Grid>

                    <Grid Grid.Row="6" Margin="0,12,0,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <ui:SymbolIcon Grid.Column="0" Symbol="Apps24"
                                       FontSize="20"
                                       Foreground="{DynamicResource AccentFillColorDefaultBrush}"
                                       VerticalAlignment="Center" Margin="0,0,16,0" />

                        <StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="0,0,16,0">
                            <TextBlock Text="Çalışan Uygulamaları Göster" FontSize="14" FontWeight="Medium" />
                            <TextBlock Text="Sabitlenmemiş çalışan uygulamaları ve küçültülmüş pencereleri dock'ta göster"
                                       FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                       TextWrapping="Wrap" Margin="0,2,0,0" />
                        </StackPanel>

                        <ui:ToggleSwitch x:Name="ShowRunningToggle" Grid.Column="2"
                                         HorizontalAlignment="Right" VerticalAlignment="Center" />
                    </Grid>
```

> `SymbolRegular.ZoomIn24`, `DockRight24`, `Dismiss24`, `ReorderDotsVertical24` WPF-UI 4.3'te mevcuttur. Derleme hatası verirse sembol adını `XxxSymbol` ile değiştir.

- [ ] **Step 4: `DockPage.xaml.cs`'i yeni ayarlarla güncelle**

`src/WharfBar/Views/Pages/DockPage.xaml.cs` içeriğini tamamen:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WharfBar.Models;
using WharfBar.Services;
using WharfBar.Views.Controls;

namespace WharfBar.Views.Pages;

/// <summary>
/// Dock settings page: content list, auto-hide, icon size, surface, magnification,
/// position, running-apps visibility.
/// </summary>
public partial class DockPage : Page
{
    private const double MinimumDrag = 4d;

    private bool _isLoading = true;
    private Point _dragOrigin;
    private string? _draggingId;

    public DockPage()
    {
        InitializeComponent();
        LoadSettings();

        AutoHideToggle.Checked += OnAutoHideChanged;
        AutoHideToggle.Unchecked += OnAutoHideChanged;
        ShowRunningToggle.Checked += OnShowRunningChanged;
        ShowRunningToggle.Unchecked += OnShowRunningChanged;
        PositionCombo.SelectionChanged += OnPositionChanged;
        AddItemButton.Click += OnAddItemClick;

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;

    private ConfigService Config => WharfApp.Config!;

    private AppConfig Cfg => Config.Current;

    private DockItemStore Store => WharfApp.DockItems!;

    private void LoadSettings()
    {
        AutoHideToggle.IsChecked = Cfg.Dock.AutoHide;
        IconSizeSlider.Value = Cfg.Dock.IconSize;
        IconSizeLabel.Text = $"{(int)Cfg.Dock.IconSize} DIP";
        MagnificationSlider.Value = DockMagnification.Clamp(Cfg.Dock.Magnification);
        MagnificationLabel.Text = $"{DockMagnification.Clamp(Cfg.Dock.Magnification):0.00}×";
        ShowRunningToggle.IsChecked = Cfg.Dock.ShowRunningApps;
        PositionCombo.SelectedIndex = DockPositions.Normalize(Cfg.Dock.Position) switch
        {
            DockPositions.BottomLeft => 1,
            DockPositions.BottomRight => 2,
            _ => 0,
        };
        SurfaceCombo.SelectedIndex = string.Equals(
            Cfg.Dock.Surface,
            DockSurfaceStyles.Acrylic,
            StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        RebuildItems();
    }

    private void RebuildItems()
    {
        ItemsPanel.Items.Clear();
        foreach (PinnedApp item in Store.Items)
        {
            var card = new DockSettingsRow(item);
            ItemsPanel.Items.Add(card);
            HookRow(card);
        }

        EmptyHint.Visibility = Store.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Bind order/label path in one block: a row missing one handler is silently dead
    // (see TopBarWindow.xaml:156-157).
    private void HookRow(DockSettingsRow row)
    {
        row.RemoveClicked += (_, _) =>
        {
            Store.Remove(row.Id);
            RebuildItems();
        };
        row.PreviewMouseLeftButtonDown += OnRowMouseLeftButtonDown;
        row.PreviewMouseMove += OnRowMouseMove;
        row.DragOver += OnRowDragOver;
        row.Drop += OnRowDrop;
    }

    private void OnAddItemClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Dock'a ekle",
            Filter = "Uygulamalar (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (string path in dialog.FileNames)
        {
            (string resolvedPath, string args) = DockDropFormats.Resolve(path);
            Store.TryAdd(resolvedPath, args, string.Empty);
        }

        RebuildItems();
    }

    private void OnRowMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DockSettingsRow row)
        {
            _dragOrigin = e.GetPosition(null);
            _draggingId = row.Id;
        }
    }

    private void OnRowMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton is not MouseButtonState.Pressed
            || sender is not DockSettingsRow row
            || _draggingId is null)
        {
            return;
        }

        Point position = e.GetPosition(null);
        double dx = position.X - _dragOrigin.X;
        double dy = position.Y - _dragOrigin.Y;
        if (Math.Abs(dx) < MinimumDrag && Math.Abs(dy) < MinimumDrag)
        {
            return;
        }

        string dragged = _draggingId;
        _draggingId = null;
        _ = DragDrop.DoDragDrop(row, dragged, DragDropEffects.Move);
    }

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.StringFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.StringFormat) is not string dragged
            || sender is not DockSettingsRow target
            || string.Equals(dragged, target.Id, StringComparison.Ordinal))
        {
            return;
        }

        int from = IndexOf(dragged);
        int to = IndexOf(target.Id);
        if (from < 0 || to < 0)
        {
            return;
        }

        Store.Move(dragged, to);
        RebuildItems();
    }

    private int IndexOf(string id)
    {
        for (int i = 0; i < Store.Items.Count; i++)
        {
            if (string.Equals(DockItemStore.IdFor(Store.Items[i]), id, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnAutoHideChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Dock.AutoHide = AutoHideToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnShowRunningChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Dock.ShowRunningApps = ShowRunningToggle.IsChecked == true;
        SaveAndApply();
    }

    private void OnPositionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        Cfg.Dock.Position = PositionCombo.SelectedIndex switch
        {
            1 => DockPositions.BottomLeft,
            2 => DockPositions.BottomRight,
            _ => DockPositions.BottomCenter,
        };
        SaveAndApply();
    }

    private void OnIconSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        IconSizeLabel.Text = $"{v} DIP";

        if (_isLoading) return;

        Cfg.Dock.IconSize = Math.Clamp(v, DockConfig.MinIconSize, DockConfig.MaxIconSize);
        SaveAndApply();
    }

    private void OnMagnificationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double clamped = DockMagnification.Clamp(e.NewValue);
        MagnificationLabel.Text = $"{clamped:0.00}×";

        if (_isLoading) return;

        Cfg.Dock.Magnification = clamped;
        SaveAndApply();
    }

    private void OnSurfaceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        Cfg.Dock.Surface = SurfaceCombo.SelectedIndex == 1
            ? DockSurfaceStyles.Acrylic
            : DockSurfaceStyles.Mica;
        SaveAndApply();
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.ApplyDockChanges();
    }
}
```

> `SurfaceCombo.SelectionChanged` artık **yalnızca** code-behind'de bağlanıyor; XAML'deki `SelectionChanged="OnSurfaceChanged"` özniteliğini kaldır (mevcut kod iki kez abone oluyor).

- [ ] **Step 5: `DockSettingsRow` kontrolünü yaz**

`src/WharfBar/Views/Pages/DockSettingsRow.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace WharfBar.Views.Pages;

/// <summary>One row in the dock settings list: label, path, drag handle, remove button.</summary>
internal sealed class DockSettingsRow : Border
{
    internal DockSettingsRow(Models.PinnedApp item)
    {
        Id = Services.DockItemStore.IdFor(item);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        BorderBrush = (System.Windows.Media.Brush)Application.Current
            .TryFindResource("ControlStrokeColorDefaultBrush");
        Background = (System.Windows.Media.Brush)Application.Current
            .TryFindResource("ControlFillColorDefaultBrush");
        Padding = new Thickness(12, 8, 12, 8);
        Margin = new Thickness(0, 0, 0, 6);
        AllowDrop = true;
        Cursor = Cursors.Hand;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = new SymbolIcon(SymbolRegular.ReorderDotsVertical24)
        {
            FontSize = 16,
            Foreground = (System.Windows.Media.Brush)Application.Current
                .TryFindResource("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Sürükleyerek sırayı değiştirin",
        };
        Grid.SetColumn(handle, 0);
        grid.Children.Add(handle);

        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var path = new TextBlock
        {
            Text = item.Path,
            FontSize = 11,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 260,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(path, 2);
        grid.Children.Add(path);

        var remove = new SymbolIcon(SymbolRegular.Dismiss24)
        {
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = (System.Windows.Media.Brush)Application.Current
                .TryFindResource("TextFillColorSecondaryBrush"),
        };
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        remove.MouseLeftButtonUp += (_, _) => RemoveClicked?.Invoke(this, EventArgs.Empty);
        Child = grid;
    }

    internal string Id { get; }

    internal event EventHandler? RemoveClicked;
}
```

> `DockSettingsRow` koddan kurulduğu için Task 3'teki XAML `DataTemplate` bloğuna gerek yoktur — `DockPage.xaml`'daki `<ItemsControl>` yalnızca **kap** görevi görür, `DockPage.RebuildItems` satırları elle ekler. XAML'deki `ItemTemplate`'i sil.

- [ ] **Step 6: Derle ve düzelt**

```
dotnet build WharfBar.sln
```
Beklenen: 0 hata. Olası sorunlar: `DockSettingsRow` içinde `SymbolIcon` tek parametreli ctor'su
`SymbolRegular` bekler (`Symbol.Regular.*` değil) ve `DockSettingsRow` `internal` olduğu için
`ItemsControl.Items.Add` sorun çıkarmaz. `using Wpf.Ui.Controls;` sembol enum'ları için gerekli.

- [ ] **Step 7: Testler**

```
dotnet test WharfBar.sln
```
Beklenen: **532 geçen**, 0 başarısız — *nihai sayı **566**.*

- [ ] **Step 8: Elle doğrulama**

1. Ayarlar → Dock: "Dock Öğeleri" demo pinlerini listeliyor.
2. `+` ile bir exe ekle → listeye giriyor, **dock'a anında** düşüyor.
3. Satırı sürükle → sıra değişiyor, dock'taki sıra da değişiyor.
4. `×` ile sil → dock'tan düşüyor.
5. Büyütme kaydırıcısı 1.00× → büyütme tamamen kapanıyor; 2.00× → belirgin.
6. Konum Sol/Sağ → dock yaslanıyor.
7. "Çalışan Uygulamaları Göster" kapatılınca çalışan/küçültülmüş bölümleri kayboluyor.
8. Ayarları kapat, yeniden aç → liste korunuyor (dolgu boşaltma regresyonu yok).

- [ ] **Step 9: Commit**

```
git add src/WharfBar/App.xaml.cs src/WharfBar/Views/DockWindow.xaml.cs src/WharfBar/Views/Pages/DockPage.xaml src/WharfBar/Views/Pages/DockPage.xaml.cs src/WharfBar/Views/Pages/DockSettingsRow.cs
git commit -m "feat(dock): ayar sayfasinda icerik listesi, buyutme, konum, calisan gostergesi"
```

---

## Task 11: Doküman senkronu ve ADR-005 revizyonu

**Files:**
- Modify: `docs/decisions/005-shell-replace-live-dock-tray-hybrid.md`
- Modify: `docs/01-architecture.md`
- Modify: `docs/02-win32-appbar-dwm.md`
- Modify: `docs/04-gotchas.md`
- Modify: `.agent/screens/dock.md`
- Modify: `.agent/context/roadmap.md`
- Modify: `.agent/context/stack.md`
- Modify: `.agent/boundaries.md`
- Modify: `.agent/conventions.md`

> Bu görev ayrı bir çalıştırılabilir adımdır: `boundaries.md:110` ve `conventions.md:11` doküman güncellemesini **aynı değişiklikte** zorunlu kılar ve revize edilen mimari karar ADR-005'i etkiler.

> **Uygulama notu:** aşağıdaki metinler planın **yönergeleridir**, kopyalanacak kaynak metin
> değildir — hepsi uygulanırken koda göre düzeltildi. Sapmaların kaydı spec §0'dadır. Kısacası:
> Step 1'deki ADR notunda "§2" değil **§1'in `EnumWindows` satırı** deniyor (çalışan uygulama
> tespiti ADR'nin §1 tablosunda); Step 2'deki `ItemsPanel` adı yerine **`DockStrip`**; Step 4'teki
> beş tuzaktan **ikisi zaten Tasks 7–9'da yazılmıştı** ve daha doğruydu (OLE pencere düzeyinde +
> anahtarsız tıklama bastırma) — yalnız üçü eklendi. Step 9'un "532 geçen" sayısı **566**'dır.

- [ ] **Step 1: ADR-005'i revize et**

`docs/decisions/005-shell-replace-live-dock-tray-hybrid.md` — başlığın hemen altına durum satırı ekle:

```markdown
> **Revizyon (2026-10-04, dock yeniden tasarımı):** §1'in "birincil pin kaynağı =
> `Quick Launch\User Pinned\TaskBar\*.lnk`" kararı **tersine çevrildi**. Dock'un tek kaynağı
> artık `dock.items` (WharfBar'ın kendi kalıcı listesidir) ve görev çubuğu **hiç okunmaz,
> hiç yazılmaz**. §2'nin çalışan uygulama tespiti (`EnumWindows` + AUMID) ve §3-5'in görev
> çubuğu gizleme/restore disiplini **değişmemiştir**.
```

§1'in kaynak tablosunu güncelle:

```markdown
| Kaynak | Kullanım |
|---|---|
| `dock.items` (config) | **Tek** sabit listesi; sıra = görüntü sırası |
| `EnumWindows` + AUMID | Çalışan uygulama tespiti ve bölümleme (değişmedi) |
| `config.pinnedApps` | Eski şema alanı; yalnızca `dock.items`'e bir kez taşınır, sonra boşaltılır |
```

- [ ] **Step 2: `docs/01-architecture.md` sorumluluklarını güncelle**

`:185` `DockWindow` satırını değiştir:

```
DockWindow | Alt overlay; **kendi bağımsız listesi** (pin + running + küçültülmüş, bölüm ayraçlı);
            hover **büyütme + reflow** (özel ItemsPanel, imlecin üstü + 3 komşu, Phase/CursorPosition);
            sürükle-sırala, dosya bırakma, konum (alt-sol/orta/sağ), "Pencereler" alt menüsü;
            katmanlı pencere + tema renkli yüzey (Mica/Acrylic WPF ile çizilir); AppBar yok
```

`:190` `DockAppsComposer` satırını değiştir:

```
DockAppsComposer | Pinned | Running | Minimized bölümlerini birleştirir; çalışan uygulamayı
                 yolla eşleşen ilk sabitlenen öğeye bağlar; saf `Compose` çekirdeği test edilir
```

Kod haritasına yeni türleri ekle:

```
- `Services/DockMagnification.cs`, `Services/DockStripLayout.cs` — saf büyütme matematiği (test edilir)
- `Services/DockItemStore.cs` — dock'un sıralı, tekilleştirilmiş, kalıcı sabit listesi
- `Services/MinimizedWindowsSource.cs` — küçültülmüş pencere tablosu (arka iş parçacığı)
- `Services/DockStripBuilder.cs` — bölüm ayraçlarını üretir
- `Views/Controls/DockStrip.cs`, `Views/Controls/DockItemCell.xaml` — şerit paneli ve tek hücre
```

- [ ] **Step 3: `docs/02-win32-appbar-dwm.md` §g'yi güncelle**

`:310-314` bloğundaki *"macOS tipi büyütme yok"* paragrafını değiştir:

```
**Hover ve pencere boyutu:** macOS tipi büyütme **vardır**. Özel `DockStrip : Panel`,
`CursorPosition` (kesirli, ikon biriminde) + `Phase` (0→1) bağımlılık özelliklerinden ölçek ve
slot ofseti hesaplar; komşular yatayda **kayar** (gerçek reflow). Eğri ve yerleşim saf
`DockMagnification` / `DockStripLayout` içindedir ve birim testine tabidir.

Pencere **baştan** maksimum büyümeye göre boyutlandırılır (`MaxExtentWidth` /
`MaxExtentHeight`); kare başına `SetWindowPos` **çağrılmaz** — yalnızca WPF ölçüm/yerleşim
çalışır. `DesiredSize` canlı toplam genişliği döndürür, plaka onu sarar ve ortalanır.

ScrollViewer yoktur; çok sayıda ikon ekran genişliğine kırpılır.
```

- [ ] **Step 4: `docs/04-gotchas.md`'ye yeni tuzakları ekle`

```
| OLE bırakma katmanlı pencerede | `AllowsTransparency=true` + `Topmost` dock, Explorer'dan dosya
bırakma hedefi kabul edebilir ya da edemez — **ölçülmedi**. Kabul edilmezse `DragOver`
`Effects=None` döner ve ayarlar listesindeki "Ekle" akışı yedek yoldur. |

| Dock'ta sürükleme tıklama çakışması | Sol tık uygulamayı açar; sürükleme de aynı butonla başlar.
Eşik (`MinimumHorizontalDragDistance`) + bırakma sonrası `_suppressClickFor` ile tıklama
bastırılır. Bu ikisi olmadan sürükleme uygulamayı açar. |

| Dock'ta kare başına SetWindowPos | Büyüme animasyonunda pencereyi yeniden boyutlandırmak şeridi
takılı bırakır (eski `ComputeMaxWidthPx` yorumunun çıkardığı sonuç). Boyut **bir kez**
max büyümeye göre hesaplanır; animasyon yalnızca WPF yerleşimini değiştirir. |

| Dock DPI bayatlığı | `MonitorInfo.Dpi` yapılandırmada sabitlenir; `WM_DPICHANGED` wParam'ının
yüksek kelimesi yeni DPI'yi taşır. Okunmazsa ekran ölçeği değişiminde şerit yanlış boyutlanır. |

| Dock büyütmede ayırıcı | Ayraç slotu 1 px genişliğindedir ve komşularıyla birlikte ölçeklenmez;
`BaseWidthFor` ayrımını bozmak şeridi kaydırır. |
```

- [ ] **Step 5: `.agent/screens/dock.md`'yi yeniden yaz**

`.agent/screens/dock.md` içeriğini şu başlıklarla tamamen değiştir:

```markdown
# Dock

macOS tarzı alt şerit. AppBar **değil** (ADR-001); `Topmost` overlay, fiziksel piksel
`SetWindowPos` ile yerleşir, WPF `Left/Top/Width` kullanılmaz.

## İçerik

- **Pinned** — kullanıcının kendi listesi (`dock.items`), sıra config sırası.
- **Running** — sabitlenmemiş çalışan uygulamalar, ada göre sıralı.
- **Minimized** — küçültülmüş her pencere için ayrı tik, uygulama ikonuyla (thumbnail yok).

Bölümler 1 px ayraçla ayrılır. Windows görev çubuğu **okunmaz, yazılmaz** (ADR-005 revizyonu).

## Büyütme

Özel `DockStrip : Panel`. Fare konumu kesirli `CursorPosition` olarak okunur; ölçek
`DockMagnification.ScaleAtDistance`, slot ofseti `DockStripLayout.ComputeOffsets` ile hesaplanır.
Varsayılan tavan **1.65×**; düşüş katsayısı 0.82, taban 1.0'a kelepçeli → etki yarıçapı 3
(aynı anda en fazla 7 ikon). `Phase` girişte 130 ms, çıkışta 180 ms.

İkonlar alt-ortadan büyür (`RenderTransformOrigin="0.5,1"`); plaka dikeyde taban yüksekliğinde
sabit kalır, ikon üst kenarın üstüne taşar. Yatayda plaka iki yana büyüyüp ortalanır.

Pencere baştan max büyümeye göre boyutlanır; **hover sırasında `SetWindowPos` çalışmaz**.

## Etkileşim

| Aksiyon | Davranış |
|---|---|
| Sol tık | Öne getir / çalıştır; ikinci tık küçült (`WindowActivationService`) |
| Sağ tık | Başlık kartı, Dock'tan kaldır, Görevi sonlandır, Pencereyi kapat, Yönetici, Dosya konumu, Yeni pencere, **Pencereler** |
| Sürükle | Sabit öğeleri sırala; accent çizgi hedefi gösterir; tıklama bastırılır |
| Bırakma | `.lnk/.exe/.bat/.cmd` ekler; yola göre tekilleştirir |

## Ayarlar

`autoHide`, `iconSize` (16–36), `surface` (Mica/Acrylic), `magnification` (1.0–2.0),
`position` (alt-sol/orta/sağ), `showRunningApps`, içerik listesi (ekle/sil/sırala).

## Tuzaklar

`docs/04-gotchas.md` → "Dock'ta ..." girdileri. Ayrıca: büyütme açıkken pencerenin saydam
kenarı plakadan geniş olduğu için tıklamayı yutar (mevcut davranış).
```

- [ ] **Step 6: Bayat yol satırlarını düzelt**

`.agent/context/roadmap.md:34` — "ScrollViewer + PreviewMouseWheel yatay kaydırma" ifadesini
`ScrollViewer` ile ilgili her şeyi silerek yeniden yaz:

```
| Dock bağlam menüsü ve hover ölçek | ✓ (2026-08-15) — bağlam menüsü + **macOS tipi büyütme ve reflow** (2026-10-04)
```

`docs/03-development-order.md` Faz 4 / Faz 9b satırlarına ekle:

```
| Faz 4 (2026-10-04) | Dock yeniden tasarımı: bağımsız liste, büyütme motoru, sürükle-sırala, ayar sayfası |
```

- [ ] **Step 7: `.agent` bağlam dosyalarını güncelle**

`.agent/context/stack.md` — kod haritasına ekle:

```
- `Services/DockMagnification.cs` + `Services/DockStripLayout.cs` — saf büyütme/yerleşim matematiği
- `Services/DockItemStore.cs` — bağımsız sabit liste (App tek örneği)
- `Services/MinimizedWindowsSource.cs` — küçültülmüş pencere tablosu
- `Views/Controls/DockStrip.cs` — şerit `Panel` (büyütme + reflow)
- `Views/Controls/DockItemCell.xaml` — tek hücre (ikon + çalışan çizgisi | ayraç)
```

`.agent/boundaries.md` — "Yap" listesine ekle:

```
| Dock'un kendi listesini tutması, görev çubuğu pinlerine **dokunmaması** (ADR-005 revizyonu) | ✓ |
```

ve "Asla"ya ekle:

```
| Dock'u görev çubuğu `User Pinned` klasörüne yazmak (`.lnk` oluşturmak/silmek) | ✗ |
```

- [ ] **Step 8: Elle doğrulama listesini genişlet**

`.agent/conventions.md` §"Test / doğrulama (v1 manuel)" listesine ekle:

```
- Dock: fare ile komşu büyümesi var mı, pencere yerinden oynamıyor mu?
- Dock: sürükleyerek sıralama çalışıyor mu ve uygulama açılmıyor mu?
- Dock: Explorer'dan bırakma kabul ediliyor mu (değilse ayar listesinden ekleniyor mu)?
- Dock: simge boyutu 16/36 ve büyütme 1.0/2.0 uçlarında şerit taşmıyor mu?
- Dock: görev çubuğu pinleri WharfBar'yi etkilemiyor, WharfBar da görev çubuğunu etkilemiyor mu?
```

- [ ] **Step 9: Doğrula**

```
dotnet build WharfBar.sln && dotnet test WharfBar.sln
```
Beklenen: 0 hata, 0 uyarı, **566 geçen** (plan yazılırken 425 idi; Tasks 1–10'un testleri eklendi).

Ardından belgeleri gözden geçir: hiçbir yerde "macOS tipi büyütme yok", "TaskBar pinleri birincil
kaynak" veya "yatay kaydırma" ifadesi kalmamalı.

```
rg -n "büyütme yok|yatay kaydırma|User Pinned" docs .agent
```

> **Bu arama boş çıkmaz ve çıkmaması da gerekmez.** Kalan eşleşmeler üç çeşittir ve hepsi
> kasıtlıdır: (a) tersine çevrilen kararı **alıntılayan** ADR-005 revizyon notu ve yeni yasak
> cümleleri (`.agent/boundaries.md`, `.agent/project.md`); (b) **tarihsel** plan/spec'lerin
> gövdesi — bunların başına "tarihsel belge" notu eklendi, gövde **kasıtlı olarak
> yeniden yazılmadı**; (c) bu planın kendi Step 11 yönergeleri, aramanın kendi metni dâhil.
> Arama **güncel bir iddia** bulmamalıdır.

- [ ] **Step 10: Commit**

```
git add docs .agent
git commit -m "docs(dock): ADR-005 revizyonu ve dock yeniden tasarim dokumanlari"
```

---

## Bitiş: tüm testler ve elle doğrulama

```
dotnet build WharfBar.sln
dotnet test WharfBar.sln
```
Beklenen: 0 hata, 0 uyarı, **566 geçen**.

Sonra `dotnet run --project src/WharfBar` ile uygulamayı aç, Task 7/9/10 adımlarındaki elle
listeleri baştan sona çalıştır ve pencereyi **normal yoldan** kapat. Zorla sonlandırma
(`Stop-Process -Force`) `ABM_REMOVE` yolunu atlar ve rezerve AppBar şeridini asılı bırakır.
