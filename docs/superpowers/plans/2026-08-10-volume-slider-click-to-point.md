# Volume Slider Click-to-Point Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** QS içindeki tüm ses `Slider` kontrollerinde track’e tıklanınca thumb tıklanan noktaya gitsin.

**Architecture:** WPF yerleşik `Slider.IsMoveToPointEnabled = true` kullanılır. Main master ve Mixer master XAML’de; oturum satırları `BuildSessionRow` code-behind’da set edilir. ValueChanged / drag / mute mantığına dokunulmaz. Opaklık menü slider’ı hariç.

**Tech Stack:** .NET 8 WPF; yeni paket yok.

**Spec:** [`docs/superpowers/specs/2026-08-10-volume-slider-click-to-point-design.md`](../specs/2026-08-10-volume-slider-click-to-point-design.md)

## Global Constraints

- Yalnızca ses slider’ları: `MasterVolumeSlider`, `MixerMasterSlider`, oturum `Slider`
- `TopBarWindow` opaklık menü slider’ına dokunma
- Özel track template / PreviewMouse handler yok
- `IsSnapToTickEnabled` / `TickFrequency=1` korunur
- Commit yalnızca kullanıcı isterse
- Derleme: `dotnet build WharfBar.sln` (`dotnet run` AppBar riski — manuel doğrulama kullanıcıda)

---

## File map

| File | Role |
|------|------|
| `src/WharfBar/Views/QuickSettingsFlyout.xaml` | Master + Mixer master: `IsMoveToPointEnabled="True"` |
| `src/WharfBar/Views/QuickSettingsFlyout.xaml.cs` | `BuildSessionRow` session slider: `IsMoveToPointEnabled = true` |
| `docs/superpowers/specs/2026-08-10-volume-slider-click-to-point-design.md` | Durum → plan linki |

---

### Task 1: Enable click-to-point on all volume sliders

**Files:**
- Modify: `src/WharfBar/Views/QuickSettingsFlyout.xaml` (`MasterVolumeSlider`, `MixerMasterSlider`)
- Modify: `src/WharfBar/Views/QuickSettingsFlyout.xaml.cs` (`BuildSessionRow` slider initializer)
- Modify: `docs/superpowers/specs/2026-08-10-volume-slider-click-to-point-design.md` (status → plan link)

**Interfaces:**
- Consumes: existing `Slider` ValueChanged / drag handlers (unchanged)
- Produces: three volume sliders with `IsMoveToPointEnabled == true`

- [ ] **Step 1: Set property on Main master slider (XAML)**

In `src/WharfBar/Views/QuickSettingsFlyout.xaml`, update `MasterVolumeSlider` to include `IsMoveToPointEnabled="True"`:

```xml
<Slider x:Name="MasterVolumeSlider"
        Minimum="0"
        Maximum="100"
        VerticalAlignment="Center"
        Margin="8,0"
        TickFrequency="1"
        IsSnapToTickEnabled="True"
        IsMoveToPointEnabled="True"
        ValueChanged="OnMasterVolumeChanged" />
```

- [ ] **Step 2: Set property on Mixer master slider (XAML)**

Same file, update `MixerMasterSlider`:

```xml
<Slider x:Name="MixerMasterSlider"
        Minimum="0"
        Maximum="100"
        VerticalAlignment="Center"
        Margin="8,0"
        TickFrequency="1"
        IsSnapToTickEnabled="True"
        IsMoveToPointEnabled="True"
        ValueChanged="OnMixerMasterVolumeChanged" />
```

- [ ] **Step 3: Set property on session sliders (code-behind)**

In `src/WharfBar/Views/QuickSettingsFlyout.xaml.cs`, inside `BuildSessionRow`, add `IsMoveToPointEnabled = true` to the session `Slider` object initializer:

```csharp
var slider = new Slider
{
    Minimum = 0,
    Maximum = 100,
    Value = session.Percent,
    VerticalAlignment = VerticalAlignment.Center,
    Margin = new Thickness(8, 0, 8, 0),
    TickFrequency = 1,
    IsSnapToTickEnabled = true,
    IsMoveToPointEnabled = true,
    IsEnabled = !session.IsMuted,
    Opacity = session.IsMuted ? 0.45 : 1,
};
```

Do **not** change `TopBarWindow.xaml.cs` opacity menu slider.

- [ ] **Step 4: Point spec at this plan**

Update the Durum line in `docs/superpowers/specs/2026-08-10-volume-slider-click-to-point-design.md` to:

```markdown
**Durum:** Onaylandı (kullanıcı 2026-08-10) — uygulama planı:
  [`../plans/2026-08-10-volume-slider-click-to-point.md`](../plans/2026-08-10-volume-slider-click-to-point.md).
```

- [ ] **Step 5: Build**

Run:

```bash
dotnet build WharfBar.sln
```

Expected: `Build succeeded` (0 errors).

- [ ] **Step 6: Sanity-check property presence**

Run (PowerShell, repo root):

```powershell
Select-String -Path src/WharfBar/Views/QuickSettingsFlyout.xaml,src/WharfBar/Views/QuickSettingsFlyout.xaml.cs -Pattern 'IsMoveToPointEnabled'
Select-String -Path src/WharfBar/Views/TopBarWindow.xaml.cs -Pattern 'IsMoveToPointEnabled'
```

Expected:
- `QuickSettingsFlyout.xaml`: two matches (`MasterVolumeSlider`, `MixerMasterSlider`)
- `QuickSettingsFlyout.xaml.cs`: one match (`BuildSessionRow`)
- `TopBarWindow.xaml.cs`: **no** matches

- [ ] **Step 7: Manual verification (user / local run)**

Close any previous WharfBar cleanly (so AppBar is removed), then:

```bash
dotnet run --project src/WharfBar
```

Checklist:
1. Volume → Main QS: master track’e tıkla → seviye tıklanan yüzdeye gider
2. Karıştırıcı: master track → aynı
3. Bir uygulama oturumu track → aynı
4. Tıklayıp sürükle → thumb tıklanan noktadan sürüklenir
5. Mute’lu / disabled session slider → tıklama etki etmez
6. Üst bar arka plan opaklık menüsü (varsa) davranışı değişmemiş

Close the window normally (do not `Stop-Process -Force`).

- [ ] **Step 8: Commit (only if user asks)**

```bash
git add src/WharfBar/Views/QuickSettingsFlyout.xaml src/WharfBar/Views/QuickSettingsFlyout.xaml.cs docs/superpowers/specs/2026-08-10-volume-slider-click-to-point-design.md docs/superpowers/plans/2026-08-10-volume-slider-click-to-point.md
git commit -m "feat(qs): jump volume sliders to click point on track."
```

---

## Spec coverage (self-review)

| Spec requirement | Task |
|------------------|------|
| Main master click-to-point | Task 1 Step 1 |
| Mixer master click-to-point | Task 1 Step 2 |
| Session sliders click-to-point | Task 1 Step 3 |
| Opacity slider excluded | Task 1 Step 3 note + Step 6 |
| No custom handler / template | Task 1 uses only `IsMoveToPointEnabled` |
| Snap-to-tick preserved | Existing attrs kept in Steps 1–3 |
| Manual validation checklist | Task 1 Step 7 |
