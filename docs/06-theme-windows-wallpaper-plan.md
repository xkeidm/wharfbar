# Theme ↔ Windows + Wallpaper Pair Sync Implementation Plan

> **For implementers:** work through this plan one task at a time. Steps use checkbox (`- [ ]`) syntax for tracking; tick each box as the task lands.

**Goal:** Appearance light/dark also writes Windows Personalization theme; hub-applied `-light`/`-dark` wallpaper pairs follow the resolved theme.

**Architecture:** Thin `SystemThemeWriter` (HKCU Personalize + ImmersiveColorSet) beside the existing accent writer; pure `WallpaperPair` + `WallpaperThemeSyncPolicy`; `WallpaperThemeSync` remembers `island.lastHubWallpaper` on hub `TryApply` and applies the partner when the resolved theme changes. Fixed light/dark ignore external Windows theme for wallpaper; `system` mode follows `ImmersiveColorSet`.

**Tech Stack:** .NET 8 WPF, WPF-UI `ApplicationTheme`, Win32 registry + `WM_SETTINGCHANGE`, existing `WallpaperService` SPI path, xUnit.

**Spec:** [`docs/05-theme-windows-wallpaper.md`](05-theme-windows-wallpaper.md)

## Global Constraints

- No new NuGet packages.
- Soft config field only — do not bump `schemaVersion`.
- Light/Dark write Windows; System does not write Windows theme keys.
- Wallpaper auto-switch only for hub-applied paths with a `-light`/`-dark` sibling.
- No Windows theme restore on exit (same as accent).
- Gallery UI unchanged (two thumbnails).
- HighContrast resolved theme → skip wallpaper sync.
- Canonical docs English; UI strings EN + TR `.resx`.
- Do not add `global.json`.
- Never leave AppBar registered / taskbar unrestored (unchanged by this work).

## File map

| File | Role |
|------|------|
| `src/WharfBar/Services/WallpaperPair.cs` | Pure: resolve light/dark sibling path |
| `src/WharfBar/Services/WallpaperThemeSyncPolicy.cs` | Pure: when external Windows changes may drive sync; HighContrast skip |
| `src/WharfBar/Native/SystemThemeWriter.cs` | Best-effort AppsUseLightTheme / SystemUsesLightTheme + broadcast |
| `src/WharfBar/Native/SystemAccentWriter.cs` | Extract shared ImmersiveColorSet broadcast helper used by theme writer |
| `src/WharfBar/Native/ImmersiveColorNotify.cs` | Shared `Broadcast()` for accent + theme writers |
| `src/WharfBar/Models/AppConfig.cs` | `IslandConfig.LastHubWallpaper` + normalizer |
| `src/WharfBar/Services/ConfigService.cs` | Normalize `lastHubWallpaper` on load |
| `src/WharfBar/Services/WallpaperService.cs` | Optional `HubApplied` callback after successful TryApply |
| `src/WharfBar/Services/WallpaperThemeSync.cs` | Remember path; follow resolved theme |
| `src/WharfBar/App.xaml.cs` | Hold `WallpaperThemeSync?`; expose follow helper |
| `src/WharfBar/Views/TopBarWindow.Island.cs` | Wire sync when attaching wallpaper |
| `src/WharfBar/Views/TopBarWindow.xaml.cs` | ImmersiveColorSet → follow when `theme=system` |
| `src/WharfBar/Views/DockWindow.xaml.cs` | Same ImmersiveColorSet hook (idempotent) |
| `src/WharfBar/Views/Pages/AppearancePage.xaml.cs` | SystemThemeWriter + Follow after theme change |
| `src/WharfBar/Resources/Strings.resx` / `Strings.tr.resx` | Theme description copy |
| `docs/01-architecture.md`, `docs/02-win32-appbar-dwm.md`, `.agent/project.md`, `Wallpapers/README.md` | Docs |
| `tests/WharfBar.Tests/WallpaperPairTests.cs` | Pair unit tests |
| `tests/WharfBar.Tests/WallpaperThemeSyncPolicyTests.cs` | Policy unit tests |
| `tests/WharfBar.Tests/IslandNormalizerLastHubWallpaperTests.cs` | Normalize empty/whitespace |

---

### Task 1: WallpaperPair (pure)

**Files:**
- Create: `src/WharfBar/Services/WallpaperPair.cs`
- Test: `tests/WharfBar.Tests/WallpaperPairTests.cs`

**Interfaces:**
- Consumes: none
- Produces:
  - `internal static string? TryResolvePartner(string path, bool wantDark)` — full path of sibling when it exists and path is the other variant; `null` if unpaired, already correct, or sibling missing
  - `internal static bool IsDarkVariant(string path)` / `IsLightVariant(string path)` — suffix helpers (optional but useful in tests)

- [ ] **Step 1: Write the failing tests**

```csharp
using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperPairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wharfbar-pair-" + Guid.NewGuid().ToString("N"));

    public WallpaperPairTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [0]);
        return Path.GetFullPath(path);
    }

    [Fact]
    public void TryResolvePartner_LightToDark_WhenSiblingExists()
    {
        string light = Touch("pier-horizon-light.jpg");
        string dark = Touch("pier-horizon-dark.jpg");
        Assert.Equal(dark, WallpaperPair.TryResolvePartner(light, wantDark: true), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolvePartner_DarkToLight_WhenSiblingExists()
    {
        string light = Touch("pier-horizon-light.jpg");
        string dark = Touch("pier-horizon-dark.jpg");
        Assert.Equal(light, WallpaperPair.TryResolvePartner(dark, wantDark: false), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolvePartner_AlreadyDark_WantDark_IsNull()
    {
        string dark = Touch("pier-horizon-dark.jpg");
        Touch("pier-horizon-light.jpg");
        Assert.Null(WallpaperPair.TryResolvePartner(dark, wantDark: true));
    }

    [Fact]
    public void TryResolvePartner_MissingSibling_IsNull()
    {
        string light = Touch("alpine-lake-light.jpg");
        Assert.Null(WallpaperPair.TryResolvePartner(light, wantDark: true));
    }

    [Fact]
    public void TryResolvePartner_UnpairedSolid_IsNull()
    {
        string solid = Touch("solid-navy.jpg");
        Assert.Null(WallpaperPair.TryResolvePartner(solid, wantDark: true));
    }

    [Fact]
    public void TryResolvePartner_PreservesExtension()
    {
        string light = Touch("glass-rose-light.png");
        string dark = Touch("glass-rose-dark.png");
        Assert.Equal(dark, WallpaperPair.TryResolvePartner(light, wantDark: true), StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run tests — expect fail**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~WallpaperPairTests`

Expected: FAIL (type/method missing)

- [ ] **Step 3: Implement**

```csharp
using System.IO;

namespace WharfBar.Services;

/// <summary>
/// Resolves <c>name-light.ext</c> ↔ <c>name-dark.ext</c> siblings in the same folder.
/// </summary>
internal static class WallpaperPair
{
    private const string LightSuffix = "-light";
    private const string DarkSuffix = "-dark";

    internal static bool IsLightVariant(string path) => Variant(path) == ThemeVariant.Light;

    internal static bool IsDarkVariant(string path) => Variant(path) == ThemeVariant.Dark;

    /// <summary>
    /// Partner full path when <paramref name="path"/> is the opposite variant and the sibling file exists.
    /// Null when unpaired, already the desired variant, or sibling missing.
    /// </summary>
    internal static string? TryResolvePartner(string path, bool wantDark)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        ThemeVariant variant = Variant(full);
        if (variant == ThemeVariant.None)
        {
            return null;
        }

        bool isDark = variant == ThemeVariant.Dark;
        if (isDark == wantDark)
        {
            return null; // already correct
        }

        string? partner = BuildPartnerPath(full, wantDark);
        if (partner is null || !File.Exists(partner))
        {
            return null;
        }

        return partner;
    }

    private static string? BuildPartnerPath(string full, bool wantDark)
    {
        string? dir = Path.GetDirectoryName(full);
        string name = Path.GetFileNameWithoutExtension(full);
        string ext = Path.GetExtension(full);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
        {
            return null;
        }

        string stem;
        if (name.EndsWith(LightSuffix, StringComparison.OrdinalIgnoreCase))
        {
            stem = name[..^LightSuffix.Length];
        }
        else if (name.EndsWith(DarkSuffix, StringComparison.OrdinalIgnoreCase))
        {
            stem = name[..^DarkSuffix.Length];
        }
        else
        {
            return null;
        }

        string suffix = wantDark ? DarkSuffix : LightSuffix;
        return Path.Combine(dir, stem + suffix + ext);
    }

    private static ThemeVariant Variant(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        if (name.EndsWith(DarkSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeVariant.Dark;
        }

        if (name.EndsWith(LightSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeVariant.Light;
        }

        return ThemeVariant.None;
    }

    private enum ThemeVariant { None, Light, Dark }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~WallpaperPairTests`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Services/WallpaperPair.cs tests/WharfBar.Tests/WallpaperPairTests.cs
git commit -m "feat(wallpaper): resolve -light/-dark sibling pairs"
```

---

### Task 2: WallpaperThemeSyncPolicy (pure)

**Files:**
- Create: `src/WharfBar/Services/WallpaperThemeSyncPolicy.cs`
- Test: `tests/WharfBar.Tests/WallpaperThemeSyncPolicyTests.cs`

**Interfaces:**
- Consumes: `AppConfig.ThemeSystem` / `ThemeLight` / `ThemeDark`; `Wpf.Ui.Appearance.ApplicationTheme`
- Produces:
  - `internal static bool ShouldFollowExternalWindows(string themeMode)` — true only for `system`
  - `internal static bool ShouldSyncForResolved(ApplicationTheme resolved)` — false for HighContrast; true for Light/Dark

- [ ] **Step 1: Write the failing tests**

```csharp
using WharfBar.Models;
using WharfBar.Services;
using Wpf.Ui.Appearance;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperThemeSyncPolicyTests
{
    [Theory]
    [InlineData(AppConfig.ThemeSystem, true)]
    [InlineData(AppConfig.ThemeLight, false)]
    [InlineData(AppConfig.ThemeDark, false)]
    public void ShouldFollowExternalWindows_OnlySystem(string mode, bool expected)
        => Assert.Equal(expected, WallpaperThemeSyncPolicy.ShouldFollowExternalWindows(mode));

    [Theory]
    [InlineData(ApplicationTheme.Light, true)]
    [InlineData(ApplicationTheme.Dark, true)]
    [InlineData(ApplicationTheme.HighContrast, false)]
    public void ShouldSyncForResolved_SkipsHighContrast(ApplicationTheme theme, bool expected)
        => Assert.Equal(expected, WallpaperThemeSyncPolicy.ShouldSyncForResolved(theme));
}
```

- [ ] **Step 2: Run — expect fail**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter FullyQualifiedName~WallpaperThemeSyncPolicyTests`

- [ ] **Step 3: Implement**

```csharp
using WharfBar.Models;
using Wpf.Ui.Appearance;

namespace WharfBar.Services;

internal static class WallpaperThemeSyncPolicy
{
    internal static bool ShouldFollowExternalWindows(string? themeMode)
        => string.Equals(themeMode, AppConfig.ThemeSystem, StringComparison.OrdinalIgnoreCase);

    internal static bool ShouldSyncForResolved(ApplicationTheme resolved)
        => resolved is ApplicationTheme.Light or ApplicationTheme.Dark;
}
```

- [ ] **Step 4: Run — expect pass**

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Services/WallpaperThemeSyncPolicy.cs tests/WharfBar.Tests/WallpaperThemeSyncPolicyTests.cs
git commit -m "feat(wallpaper): theme sync policy for system vs fixed"
```

---

### Task 3: ImmersiveColorNotify + SystemThemeWriter

**Files:**
- Create: `src/WharfBar/Native/ImmersiveColorNotify.cs`
- Create: `src/WharfBar/Native/SystemThemeWriter.cs`
- Modify: `src/WharfBar/Native/SystemAccentWriter.cs` — replace private broadcast with `ImmersiveColorNotify.Broadcast()`

**Interfaces:**
- Consumes: `NativeMethods.SendMessageTimeout`, `HWND_BROADCAST`, `WM_SETTINGCHANGE`, `SMTO_ABORTIFHUNG`
- Produces:
  - `ImmersiveColorNotify.Broadcast()`
  - `SystemThemeWriter.TryApply(bool light)` — writes both Personalize DWORDs; returns false on failure

- [ ] **Step 1: Extract broadcast helper**

Create `ImmersiveColorNotify.cs` by moving the body of `SystemAccentWriter.BroadcastImmersiveColorSet` into:

```csharp
using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class ImmersiveColorNotify
{
    internal static void Broadcast()
    {
        IntPtr lParam = Marshal.StringToHGlobalUni("ImmersiveColorSet");
        try
        {
            _ = NativeMethods.SendMessageTimeout(
                NativeMethods.HWND_BROADCAST,
                (uint)NativeMethods.WM_SETTINGCHANGE,
                IntPtr.Zero,
                lParam,
                NativeMethods.SMTO_ABORTIFHUNG,
                1000,
                out _);
        }
        finally
        {
            Marshal.FreeHGlobal(lParam);
        }
    }
}
```

In `SystemAccentWriter`, replace `BroadcastImmersiveColorSet();` with `ImmersiveColorNotify.Broadcast();` and delete the private method.

- [ ] **Step 2: Implement SystemThemeWriter**

```csharp
using Microsoft.Win32;

namespace WharfBar.Native;

/// <summary>
/// Best-effort Windows Personalization light/dark write (HKCU Themes\Personalize).
/// Failure is silent — callers must not depend on success. No exit restore.
/// </summary>
internal static class SystemThemeWriter
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <param name="light">true = light mode (DWORD 1), false = dark (DWORD 0).</param>
    internal static bool TryApply(bool light)
    {
        try
        {
            int dword = light ? 1 : 0;
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(PersonalizeKey, writable: true);
            if (key is null)
            {
                return false;
            }

            key.SetValue("AppsUseLightTheme", dword, RegistryValueKind.DWord);
            key.SetValue("SystemUsesLightTheme", dword, RegistryValueKind.DWord);
            ImmersiveColorNotify.Broadcast();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`

Expected: 0 errors. Existing `SystemAccentPackTests` still pass.

- [ ] **Step 4: Commit**

```bash
git add src/WharfBar/Native/ImmersiveColorNotify.cs src/WharfBar/Native/SystemThemeWriter.cs src/WharfBar/Native/SystemAccentWriter.cs
git commit -m "feat(theme): write Windows Personalize light/dark"
```

---

### Task 4: `lastHubWallpaper` config field

**Files:**
- Modify: `src/WharfBar/Models/AppConfig.cs` — property + `IslandNormalizer.NormalizeLastHubWallpaper`
- Modify: `src/WharfBar/Services/ConfigService.cs` — call normalizer with other Island fields
- Test: `tests/WharfBar.Tests/IslandNormalizerLastHubWallpaperTests.cs`

**Interfaces:**
- Consumes: none
- Produces: `IslandConfig.LastHubWallpaper` (JSON `lastHubWallpaper`), `IslandNormalizer.NormalizeLastHubWallpaper(string?)` → `""` if null/whitespace else trimmed string (no Path validation)

- [ ] **Step 1: Failing tests**

```csharp
using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class IslandNormalizerLastHubWallpaperTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(" C:\\x\\a.jpg ", "C:\\x\\a.jpg")]
    public void NormalizeLastHubWallpaper(string? input, string expected)
        => Assert.Equal(expected, IslandNormalizer.NormalizeLastHubWallpaper(input));
}
```

Note: on Windows the trim test uses a path with spaces around it; expected is trimmed only, not `GetFullPath`.

- [ ] **Step 2: Run — expect fail**

- [ ] **Step 3: Add to `IslandConfig` (after `WallpaperAccent`):**

```csharp
/// <summary>
/// Full path of the last wallpaper applied from the island hub (for light/dark pair sync).
/// Soft field — schema unchanged. Empty means no hub wallpaper remembered.
/// </summary>
public string LastHubWallpaper { get; set; } = "";
```

Add to `IslandNormalizer`:

```csharp
internal static string NormalizeLastHubWallpaper(string? value)
    => string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
```

In `ConfigService` normalize block (with other Island lines):

```csharp
config.Island.LastHubWallpaper =
    IslandNormalizer.NormalizeLastHubWallpaper(config.Island.LastHubWallpaper);
```

- [ ] **Step 4: Run tests — pass**

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs tests/WharfBar.Tests/IslandNormalizerLastHubWallpaperTests.cs
git commit -m "feat(config): soft island.lastHubWallpaper for pair sync"
```

---

### Task 5: WallpaperThemeSync + remember on TryApply

**Files:**
- Create: `src/WharfBar/Services/WallpaperThemeSync.cs`
- Modify: `src/WharfBar/Services/WallpaperService.cs` — `Action<string>? HubApplied` invoked with full path when `TryApply` returns true (both instant and transition-started paths)
- Modify: `src/WharfBar/App.xaml.cs` — `internal WallpaperThemeSync? WallpaperSync { get; private set; }` + `internal void AttachWallpaperSync(...)` / `ClearWallpaperSync()` / `FollowWallpaperTheme(bool fromExternalWindows)`
- Modify: `src/WharfBar/Views/TopBarWindow.Island.cs` — after creating `WallpaperService`, construct sync and set `HubApplied`

**Interfaces:**
- Consumes: `WallpaperPair`, `WallpaperThemeSyncPolicy`, `AppThemePreference`, `ConfigService`, `WallpaperService`
- Produces:
  - `WallpaperThemeSync.Remember(string path)`
  - `WallpaperThemeSync.FollowResolvedTheme(bool fromExternalWindows)`
  - `WallpaperService.HubApplied` — `Action<string>?`

- [ ] **Step 1: Add callback to WallpaperService**

Near other properties:

```csharp
/// <summary>Invoked with the applied full path when <see cref="TryApply"/> succeeds (hub gallery).</summary>
internal Action<string>? HubApplied { get; set; }
```

At every successful return from `TryApply` (transition started `return true` and instant `DesktopWallpaper.TrySet` success), before return:

```csharp
try { HubApplied?.Invoke(full); } catch { /* ignore */ }
```

Do **not** invoke on failed apply.

- [ ] **Step 2: Implement WallpaperThemeSync**

```csharp
using Wpf.Ui.Appearance;
using WharfBar.Models;

namespace WharfBar.Services;

internal sealed class WallpaperThemeSync
{
    private readonly ConfigService _config;
    private readonly WallpaperService _wallpaper;

    internal WallpaperThemeSync(ConfigService config, WallpaperService wallpaper)
    {
        _config = config;
        _wallpaper = wallpaper;
    }

    internal void Remember(string path)
    {
        if (!_wallpaper.IsAllowedPath(path))
        {
            return;
        }

        string full = Path.GetFullPath(path);
        AppConfig cfg = _config.Current;
        if (string.Equals(cfg.Island.LastHubWallpaper, full, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        cfg.Island.LastHubWallpaper = full;
        _config.Save(cfg);
    }

    internal void FollowResolvedTheme(bool fromExternalWindows)
    {
        AppConfig cfg = _config.Current;
        if (fromExternalWindows
            && !WallpaperThemeSyncPolicy.ShouldFollowExternalWindows(cfg.Theme))
        {
            return;
        }

        ApplicationTheme resolved = AppThemePreference.Resolve();
        if (!WallpaperThemeSyncPolicy.ShouldSyncForResolved(resolved))
        {
            return;
        }

        string last = cfg.Island.LastHubWallpaper;
        if (string.IsNullOrWhiteSpace(last) || !_wallpaper.IsAllowedPath(last))
        {
            return;
        }

        bool wantDark = resolved == ApplicationTheme.Dark;
        string? partner = WallpaperPair.TryResolvePartner(last, wantDark);
        if (partner is null)
        {
            return;
        }

        _ = _wallpaper.TryApply(partner);
    }
}
```

(`Remember` is also called from `HubApplied` when partner Apply succeeds, updating `lastHubWallpaper`.)

- [ ] **Step 3: Wire App + primary island**

On `App`:

```csharp
internal WallpaperThemeSync? WallpaperSync { get; private set; }

internal void SetWallpaperSync(WallpaperThemeSync? sync) => WallpaperSync = sync;

internal void FollowWallpaperTheme(bool fromExternalWindows)
    => WallpaperSync?.FollowResolvedTheme(fromExternalWindows);
```

In `TopBarWindow.Island.cs` where `_wallpaper` is created / `AttachWallpaper` runs (primary only):

```csharp
_wallpaper ??= new WallpaperService(...);
// existing TransitionRequest / AccentEnabled setup ...
var app = (App)Application.Current;
var sync = new WallpaperThemeSync(app.Config!, _wallpaper);
_wallpaper.HubApplied = sync.Remember;
app.SetWallpaperSync(sync);
IslandHost.AttachWallpaper(_wallpaper);
```

On detach/dispose of primary island wallpaper, `app.SetWallpaperSync(null)` and clear `HubApplied`.

- [ ] **Step 4: Build + run pair/policy/normalizer tests**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj --filter "FullyQualifiedName~WallpaperPair|FullyQualifiedName~WallpaperThemeSyncPolicy|FullyQualifiedName~IslandNormalizerLastHub"`

Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Services/WallpaperThemeSync.cs src/WharfBar/Services/WallpaperService.cs src/WharfBar/App.xaml.cs src/WharfBar/Views/TopBarWindow.Island.cs
git commit -m "feat(wallpaper): sync hub pairs to resolved theme"
```

---

### Task 6: Appearance + ImmersiveColorSet + UI strings

**Files:**
- Modify: `src/WharfBar/Views/Pages/AppearancePage.xaml.cs` — after existing theme apply, call `SystemThemeWriter` for light/dark only; then `FollowWallpaperTheme(false)`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` — in `WM_SETTINGCHANGE` ImmersiveColorSet branch, after existing refresh, call `((App)Application.Current).FollowWallpaperTheme(fromExternalWindows: true)`
- Modify: `src/WharfBar/Views/DockWindow.xaml.cs` — same one-liner on ImmersiveColorSet (idempotent)
- Modify: `src/WharfBar/Resources/Strings.resx` — `Appearance_ThemeDescription`
- Modify: `src/WharfBar/Resources/Strings.tr.resx` — Turkish equivalent

**Interfaces:**
- Consumes: `SystemThemeWriter.TryApply(bool)`, `App.FollowWallpaperTheme`, `WallpaperThemeSyncPolicy` (indirect)

- [ ] **Step 1: Update `OnThemeChanged` in AppearancePage**

After `AppThemePreference.Mode = theme` and `ApplicationThemeManager.Apply(...)`, before/after accent re-apply:

```csharp
if (theme == AppConfig.ThemeLight)
{
    _ = SystemThemeWriter.TryApply(light: true);
}
else if (theme == AppConfig.ThemeDark)
{
    _ = SystemThemeWriter.TryApply(light: false);
}
// theme == system → do not write Windows

// existing UseSystemAccentColor + Save + ApplyLogoChanges ...

WharfApp.FollowWallpaperTheme(fromExternalWindows: false);
```

Add `using WharfBar.Native;`

- [ ] **Step 2: ImmersiveColorSet hooks**

TopBar `WM_SETTINGCHANGE` ImmersiveColorSet block — append:

```csharp
if (Application.Current is App app)
{
    app.FollowWallpaperTheme(fromExternalWindows: true);
}
```

Dock ImmersiveColorSet block — same.

- [ ] **Step 3: Strings**

EN `Appearance_ThemeDescription`:

`Light or dark also updates Windows personalization. System leaves Windows alone and WharfBar follows it.`

TR: the Turkish satellite copy lives in `Strings.tr.resx`; the public English docs do not repeat it.

- [ ] **Step 4: Build**

Run: `dotnet build WharfBar.sln`

Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Views/Pages/AppearancePage.xaml.cs src/WharfBar/Views/TopBarWindow.xaml.cs src/WharfBar/Views/DockWindow.xaml.cs src/WharfBar/Resources/Strings.resx src/WharfBar/Resources/Strings.tr.resx
git commit -m "feat(theme): wire Windows write and wallpaper follow"
```

---

### Task 7: Documentation

**Files:**
- Modify: `docs/01-architecture.md` — theme write, `lastHubWallpaper`, `WallpaperPair` / `WallpaperThemeSync` / `SystemThemeWriter`
- Modify: `docs/02-win32-appbar-dwm.md` — Personalize keys + ImmersiveColorNotify
- Modify: `.agent/project.md` — short note under appearance / wallpaper
- Modify: `src/WharfBar/Wallpapers/README.md` — pairing drives auto-switch when hub-applied
- Modify: `docs/05-theme-windows-wallpaper.md` — set Status to Implemented (or leave Approved until manual verify)

- [ ] **Step 1: Update docs** — mirror language from the design spec sections 1–4 and file map above. Keep docs English.

- [ ] **Step 2: Commit**

```bash
git add docs/01-architecture.md docs/02-win32-appbar-dwm.md docs/05-theme-windows-wallpaper.md .agent/project.md src/WharfBar/Wallpapers/README.md
git commit -m "docs: theme/Windows sync and wallpaper pair behavior"
```

---

### Task 8: Manual verification checklist

No code. Run the app normally (do not force-kill).

- [ ] **Step 1: Windows theme write**
  1. Settings → Appearance → Dark → confirm Windows Settings → Personalization → Colors is Dark.
  2. Switch to Light → Windows becomes Light.
  3. Switch to System → changing Windows Colors updates WharfBar chrome; WharfBar does not overwrite Windows when left on System.

- [ ] **Step 2: Wallpaper pair**
  1. Island hub → apply `pier-horizon-light` (or alpine/glass-rose pair).
  2. Appearance → Dark → desktop becomes `*-dark` (transition if enabled).
  3. Appearance → Light → back to `*-light`.
  4. Apply `solid-navy` from hub → theme change leaves solid in place.
  5. Fixed Dark + change Windows theme externally → wallpaper does not flip; System mode + external theme change does flip a hub pair.

- [ ] **Step 3: Full test suite**

Run: `dotnet test WharfBar.sln`

Expected: all green

- [ ] **Step 4: Final commit only if docs status / leftover fixes** (skip empty commit)

---

## Spec coverage (self-review)

| Spec requirement | Task |
|------------------|------|
| Light/Dark write AppsUseLightTheme + SystemUsesLightTheme | 3, 6 |
| System does not write Windows | 6 |
| No exit restore | 3 (documented) |
| `-light`/`-dark` pairing | 1 |
| Hub-only memory `lastHubWallpaper` | 4, 5 |
| Sync on Appearance change | 6 |
| Sync on ImmersiveColorSet only when theme=system | 2, 5, 6 |
| HighContrast skip | 2, 5 |
| Gallery UI unchanged | (no task) |
| Strings EN+TR | 6 |
| Docs | 7 |
| Unit tests pair/policy | 1, 2, 4 |

No TBD placeholders. Signatures consistent across tasks (`TryApply(bool light)`, `FollowResolvedTheme(bool fromExternalWindows)`, `HubApplied`).
