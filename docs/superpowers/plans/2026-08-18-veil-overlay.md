# Renk Kaplaması (Veil) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WharfBar'a ColorVeil tarzı tam ekran renk kaplaması ekle: QS karosu + Ctrl+F11 kısayoluyla aç/kapa, Kaplama ayarlar sayfası (renk/opaklık/kısayol).

**Architecture:** Katmanlı WPF penceresi (`VeilWindow`, DockWindow deseni) — `AllowsTransparency=True`, tam ekran rect, `Topmost`, `WS_EX_TRANSPARENT` passthrough, 280 ms fade. `VeilService` durum/konfig/hotkey'i yönetir (mesaj-only `HwndSource` + `RegisterHotKey`), `Changed` olayı QS karosunu anlık günceller. Config: `AppConfig.Veil` (`VeilConfig`, eklenebilir alanlar — `schemaVersion` değişmez).

**Tech Stack:** .NET 8 WPF, `net8.0-windows10.0.19041.0`, WPF-UI, Win32 P/Invoke (`RegisterHotKey`), xUnit.

**Spec:** [`../superpowers/specs/2026-08-18-veil-overlay-design.md`](../superpowers/specs/2026-08-18-veil-overlay-design.md)

## Global Constraints

- TFM `net8.0-windows10.0.19041.0`; **yeni paket eklenmez**; `DllImport` (LibraryImport değil); `AllowUnsafeBlocks` kapalı kalır.
- UI metinleri ve commit mesajları Türkçe; dokümanlar Türkçe.
- `AllowsTransparency=true` + Mica birlikte **yasak** (VeilWindow düz renk dolgu — Mica yok).
- Fiziksel piksel yerleşim (`SetWindowPos`), asla WPF DIP `Left/Top`.
- Geliştirici uygulamayı **çalıştırmaz** (AppBar rezervasyonu + görev çubuğu gizleme; manuel doğrulama kullanıcıya aittir). Build/test: `dotnet build WharfBar.sln`, `dotnet test`.
- Çıkış yollarında hotkey unregister garantisi (`VeilService.Dispose` → `App.OnExit`).
- `QuickSettingsFlyout.xaml` tile grid'i `Rows="2"` → `"3"` olur (7. karo için).
- `TopBarWindow` ctor'u değişir: `TopBarWindow(TaskbarVisibilityService, ConfigService, VeilService)` — her iki çağrı yeri (App.OnStartup ve App.RecreateTopBar) güncellenir.

## File Structure

| Dosya | Sorumluluk | Görev |
|-------|------------|-------|
| `src/WharfBar/Models/AppConfig.cs` | `VeilConfig` + `VeilNormalizer` ekle; `AppConfig.Veil` | 1 |
| `src/WharfBar/Services/ConfigService.cs` | Normalize/CreateDefault/veil anahtar kayıp kontrolü | 1 |
| `tests/WharfBar.Tests/VeilConfigTests.cs` | `ClampOpacity` / `NormalizeColor` testleri | 1 |
| `src/WharfBar/Native/Hotkey.cs` | `HotkeyNative` (P/Invoke + sabitler) + `HotkeyParser` (parse/format) | 2 |
| `tests/WharfBar.Tests/HotkeyParserTests.cs` | `TryParse` / `Format` testleri | 2 |
| `src/WharfBar/Views/VeilWindow.xaml(.cs)` | Tam ekran katmanlı pencere: rect, fade, passthrough, display handler | 3 |
| `src/WharfBar/Services/VeilService.cs` | Durum, config uygula, hotkey host, `Changed`, Dispose | 4 |
| `src/WharfBar/Services/QuickSettingsService.cs` | `veil` karosu + ctor enjeksiyonu | 5 |
| `src/WharfBar/Views/QuickSettingsFlyout.xaml` | TilesGrid `Rows="3"` | 5 |
| `src/WharfBar/Views/TopBarWindow.xaml.cs` | ctor + QS kurulumu | 5 |
| `src/WharfBar/App.xaml.cs` | `Veil` property, OnStartup/OnExit/RecreateTopBar | 5 |
| `src/WharfBar/Views/Pages/VeilPage.xaml(.cs)` | Kaplama ayarları sayfası (4 kart) | 6 |
| `src/WharfBar/Views/SettingsWindow.xaml` | "Kaplama" menü öğesi | 6 |
| `docs/decisions/007-veil-overlay.md` | ADR-007 | 7 |
| `docs/01-architecture.md`, `docs/02-win32-appbar-dwm.md` | Mimari + Win32 imzaları | 7 |
| `.agent/project.md`, `.agent/context/roadmap.md`, `.agent/screens/README.md` | Proje durumu + UI turu | 7 |

---

### Task 1: VeilConfig + ConfigService tümleşimi

**Files:**
- Modify: `src/WharfBar/Models/AppConfig.cs` (dosya üstüne `using System.Globalization;` + `using System.Windows.Media;` ekle)
- Modify: `src/WharfBar/Services/ConfigService.cs`
- Create: `tests/WharfBar.Tests/VeilConfigTests.cs`

**Interfaces:**
- Produces: `AppConfig.Veil` (`VeilConfig`); `VeilConfig` (`Enabled: bool`, `Color: string`, `Opacity: int`, `HotKey: string`; consts `DefaultColor="#4A90D9"`, `DefaultOpacity=40`, `DefaultHotKey="Ctrl+F11"`); `VeilNormalizer.ClampOpacity(int) → int`; `VeilNormalizer.NormalizeColor(string?) → string`; `VeilNormalizer.TryParseColor(string?, out Color) → bool`

- [ ] **Step 1: Write the failing tests**

`tests/WharfBar.Tests/VeilConfigTests.cs`:

```csharp
using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class VeilConfigTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void ClampOpacity_BoundsValues(int input, int expected)
    {
        Assert.Equal(expected, VeilNormalizer.ClampOpacity(input));
    }

    [Theory]
    [InlineData("#4A90D9", "#4a90d9")]
    [InlineData("#4a90d9", "#4a90d9")]
    [InlineData("#ABC", "#aabbcc")]
    [InlineData("F0F0F0", "#f0f0f0")]
    public void NormalizeColor_ValidHex_NormalizesToLowercaseRgb(string input, string expected)
    {
        Assert.Equal(expected, VeilNormalizer.NormalizeColor(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void NormalizeColor_Invalid_FallsBackToDefault(string? input)
    {
        Assert.Equal(VeilConfig.DefaultColor, VeilNormalizer.NormalizeColor(input));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj`
Expected: derleme hatası (`VeilNormalizer` bulunamadı) → FAIL

- [ ] **Step 3: Implement `VeilConfig` + `VeilNormalizer`**

`src/WharfBar/Models/AppConfig.cs` — `TopBarConfig` sınıfından sonra ekle:

```csharp
/// <summary>Veil (screen color overlay) settings under <see cref="AppConfig.Veil"/>.</summary>
internal sealed class VeilConfig
{
    internal const string DefaultColor = "#4A90D9";

    internal const int DefaultOpacity = 40;

    internal const string DefaultHotKey = "Ctrl+F11";

    public bool Enabled { get; set; }

    /// <summary>Overlay color as <c>#RRGGBB</c>.</summary>
    public string Color { get; set; } = DefaultColor;

    /// <summary>0–100 overlay strength.</summary>
    public int Opacity { get; set; } = DefaultOpacity;

    /// <summary>Hotkey text (<c>Ctrl+F11</c>); empty = no hotkey. Soft field: invalid text is kept.</summary>
    public string HotKey { get; set; } = DefaultHotKey;
}

/// <summary>Pure static normalization helpers for <see cref="VeilConfig"/> (unit-tested).</summary>
internal static class VeilNormalizer
{
    internal static int ClampOpacity(int value) => Math.Clamp(value, 0, 100);

    /// <summary>Returns a lowercase <c>#rrggbb</c> string, or <see cref="VeilConfig.DefaultColor"/> when unparsable.</summary>
    internal static string NormalizeColor(string? text)
    {
        return TryParseColor(text, out Color color)
            ? $"#{color.R:x2}{color.G:x2}{color.B:x2}"
            : VeilConfig.DefaultColor;
    }

    internal static bool TryParseColor(string? text, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string hex = text.Trim().TrimStart('#');
        return hex.Length is 3 or 6
            && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value)
            && ExpandHex(hex.Length, value, out color);
    }

    private static bool ExpandHex(int length, int value, out Color color)
    {
        int r;
        int g;
        int b;
        if (length == 3)
        {
            r = (value >> 8) & 0xF;
            r |= r << 4;
            g = (value >> 4) & 0xF;
            g |= g << 4;
            b = value & 0xF;
            b |= b << 4;
        }
        else
        {
            r = (value >> 16) & 0xFF;
            g = (value >> 8) & 0xFF;
            b = value & 0xFF;
        }

        color = Color.FromRgb((byte)r, (byte)g, (byte)b);
        return true;
    }
}
```

`AppConfig` sınıfına (`TopBar` satırının yanına) ekle:

```csharp
    public VeilConfig Veil { get; set; } = new();
```

- [ ] **Step 4: Integrate into ConfigService**

`src/WharfBar/Services/ConfigService.cs`:

`CreateDefault()` içine (TopBar bloğundan sonra):

```csharp
            Veil = new VeilConfig(),
```

`Normalize()` içine (`config.TopBar ??= new TopBarConfig();` satırından sonra):

```csharp
        config.Veil ??= new VeilConfig();
        config.Veil.Opacity = VeilNormalizer.ClampOpacity(config.Veil.Opacity);
        config.Veil.Color = VeilNormalizer.NormalizeColor(config.Veil.Color);
```

`LoadOrCreate()` içine (`bool topBarKeyMissing = ...` satırından sonra):

```csharp
            bool veilKeyMissing = !JsonRootHasProperty(json, "veil");
```

Aynı yöntemde kaydetme koşulunu güncelle:

```csharp
            if (iconChanged || statusChanged || catalogChanged || veilKeyMissing)
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj`
Expected: VeilConfigTests dahil tüm testler PASS; mevcut testler kırılmadı

- [ ] **Step 6: Build + Commit**

Run: `dotnet build WharfBar.sln`
Expected: temiz derleme

```bash
git add src/WharfBar/Models/AppConfig.cs src/WharfBar/Services/ConfigService.cs tests/WharfBar.Tests/VeilConfigTests.cs
git commit -m "feat(config): Kaplama (veil) config şeması + normalize (renk/opaklık)"
```

---

### Task 2: Native/Hotkey.cs — P/Invoke + parser

**Files:**
- Create: `src/WharfBar/Native/Hotkey.cs`
- Create: `tests/WharfBar.Tests/HotkeyParserTests.cs`

**Interfaces:**
- Consumes: —
- Produces: `HotkeyNative.WM_HOTKEY` (const int); `HotkeyNative.MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN` (const uint); `HotkeyNative.RegisterHotKey(IntPtr, int, uint, uint) → bool`; `HotkeyNative.UnregisterHotKey(IntPtr, int) → bool`; `HotkeyParser.TryParse(string?, out uint modifiers, out uint vk) → bool`; `HotkeyParser.Format(uint modifiers, uint vk) → string`

- [ ] **Step 1: Write the failing tests**

`tests/WharfBar.Tests/HotkeyParserTests.cs`:

```csharp
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("Ctrl+F11", HotkeyNative.MOD_CONTROL, 0x7B)]
    [InlineData("ctrl+f11", HotkeyNative.MOD_CONTROL, 0x7B)]
    [InlineData("Alt+Shift+A", HotkeyNative.MOD_ALT | HotkeyNative.MOD_SHIFT, 0x41)]
    [InlineData("Win+1", HotkeyNative.MOD_WIN, 0x31)]
    [InlineData("Ctrl+Alt+Shift+Win+F5", HotkeyNative.MOD_CONTROL | HotkeyNative.MOD_ALT | HotkeyNative.MOD_SHIFT | HotkeyNative.MOD_WIN, 0x74)]
    public void TryParse_ValidHotkey_Parses(string text, uint expectedMods, uint expectedVk)
    {
        Assert.True(HotkeyParser.TryParse(text, out uint modifiers, out uint vk));
        Assert.Equal(expectedMods, modifiers);
        Assert.Equal(expectedVk, vk);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("F11")]
    [InlineData("Ctrl+Ctrl+A")]
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Space")]
    [InlineData("Ctrl+A+B")]
    public void TryParse_Invalid_ReturnsFalse(string? text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData(HotkeyNative.MOD_CONTROL, 0x7B, "Ctrl+F11")]
    [InlineData(HotkeyNative.MOD_CONTROL | HotkeyNative.MOD_ALT | HotkeyNative.MOD_SHIFT | HotkeyNative.MOD_WIN, 0x74, "Ctrl+Alt+Shift+Win+F5")]
    [InlineData(HotkeyNative.MOD_ALT | HotkeyNative.MOD_SHIFT, 0x41, "Alt+Shift+A")]
    [InlineData(HotkeyNative.MOD_WIN, 0x31, "Win+1")]
    public void Format_BuildsCanonicalString(uint modifiers, uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyParser.Format(modifiers, vk));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj`
Expected: derleme hatası (`HotkeyParser` bulunamadı) → FAIL

- [ ] **Step 3: Implement HotkeyNative + HotkeyParser**

`src/WharfBar/Native/Hotkey.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace WharfBar.Native;

/// <summary>Win32 global hotkey registration (<c>RegisterHotKey</c>).</summary>
internal static class HotkeyNative
{
    internal const int WM_HOTKEY = 0x0312;

    internal const uint MOD_ALT = 0x0001;

    internal const uint MOD_CONTROL = 0x0002;

    internal const uint MOD_SHIFT = 0x0004;

    internal const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>
/// Parses <c>Ctrl+F11</c>-style hotkey strings and formats back to canonical form.
/// Supported keys: F1–F12, A–Z, 0–9. At least one modifier (Ctrl/Alt/Shift/Win) required.
/// </summary>
internal static class HotkeyParser
{
    internal static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        uint mods = 0;
        uint key = 0;
        foreach (string part in parts)
        {
            if (TryParseModifier(part, out uint m))
            {
                if ((mods & m) != 0)
                {
                    return false; // tekrar eden modifier
                }

                mods |= m;
            }
            else if (key == 0 && TryParseKey(part, out uint k))
            {
                key = k;
            }
            else
            {
                return false;
            }
        }

        if (mods == 0 || key == 0)
        {
            return false;
        }

        modifiers = mods;
        vk = key;
        return true;
    }

    internal static string Format(uint modifiers, uint vk)
    {
        var sb = new StringBuilder();
        AppendModifier(sb, modifiers, HotkeyNative.MOD_CONTROL, "Ctrl");
        AppendModifier(sb, modifiers, HotkeyNative.MOD_ALT, "Alt");
        AppendModifier(sb, modifiers, HotkeyNative.MOD_SHIFT, "Shift");
        AppendModifier(sb, modifiers, HotkeyNative.MOD_WIN, "Win");
        AppendKey(sb, vk);
        return sb.ToString();
    }

    private static bool TryParseModifier(string text, out uint modifier)
    {
        modifier = text.ToLowerInvariant() switch
        {
            "ctrl" or "control" => HotkeyNative.MOD_CONTROL,
            "alt" => HotkeyNative.MOD_ALT,
            "shift" => HotkeyNative.MOD_SHIFT,
            "win" or "windows" => HotkeyNative.MOD_WIN,
            _ => 0,
        };
        return modifier != 0;
    }

    private static bool TryParseKey(string text, out uint vk)
    {
        vk = 0;
        if (text.Length == 1 && text[0] is >= 'A' and <= 'Z')
        {
            vk = (uint)text[0];
            return true;
        }

        if (text.Length == 1 && text[0] is >= '0' and <= '9')
        {
            vk = (uint)text[0];
            return true;
        }

        if (text.Length >= 2 && text[0] is 'F' or 'f')
        {
            if (int.TryParse(text.AsSpan(1), out int f) && f is >= 1 and <= 12)
            {
                vk = (uint)(0x70 + f - 1);
                return true;
            }
        }

        return false;
    }

    private static void AppendModifier(StringBuilder sb, uint modifiers, uint flag, string label)
    {
        if ((modifiers & flag) != 0)
        {
            if (sb.Length > 0)
            {
                sb.Append('+');
            }

            sb.Append(label);
        }
    }

    private static void AppendKey(StringBuilder sb, uint vk)
    {
        if (sb.Length > 0)
        {
            sb.Append('+');
        }

        if (vk is >= 0x70 and <= 0x7B)
        {
            sb.Append('F').Append(vk - 0x70 + 1);
        }
        else
        {
            sb.Append((char)vk);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/WharfBar.Tests/WharfBar.Tests.csproj`
Expected: HotkeyParserTests dahil tüm testler PASS

- [ ] **Step 5: Commit**

```bash
git add src/WharfBar/Native/Hotkey.cs tests/WharfBar.Tests/HotkeyParserTests.cs
git commit -m "feat(native): RegisterHotKey P/Invoke + kısayol string parser/formatter"
```

### Task 3: VeilWindow — tam ekran katmanlı pencere

**Files:**
- Create: `src/WharfBar/Views/VeilWindow.xaml`
- Create: `src/WharfBar/Views/VeilWindow.xaml.cs`

**Interfaces:**
- Consumes: `NativeMethods.SetWindowPos` / `HWND_TOPMOST` / `SWP_NOACTIVATE` / `WM_DISPLAYCHANGE` / `WM_DPICHANGED` (mevcut), `Display.GetPrimaryMonitorBounds(IntPtr) → RECT` (mevcut), `DockHitTest.ApplyPassthrough(IntPtr)` (mevcut)
- Produces: `VeilWindow()` (XAML: `AllowsTransparency=True`, `Topmost=True`, `ShowActivated=False`, `ShowInTaskbar=False`, `WindowStyle=None`, transparent bg; içerik `Border x:Name="VeilSurface"`); `VeilWindow.ApplyTint(Color color, int opacity)`; `VeilWindow.SetVisible(bool visible, bool animate)`

- [ ] **Step 1: Create XAML**

`src/WharfBar/Views/VeilWindow.xaml`:

```xml
<Window x:Class="WharfBar.Views.VeilWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="WharfBarKaplama"
        WindowStyle="None"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Topmost="True"
        ShowActivated="False"
        WindowStartupLocation="Manual"
        Background="Transparent"
        AllowsTransparency="True">
    <Border x:Name="VeilSurface" />
</Window>
```

- [ ] **Step 2: Implement code-behind**

`src/WharfBar/Views/VeilWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WharfBar.Native;

namespace WharfBar.Views;

/// <summary>
/// Full-screen click-through color overlay (ColorVeil style). Always topmost; never activates;
/// covers the primary monitor only (v1). No Mica — plain color fill, so AllowsTransparency is safe.
/// </summary>
public partial class VeilWindow : Window
{
    private const double FadeMilliseconds = 280d;

    private HwndSource? _source;
    private bool _targetVisible;

    public VeilWindow()
    {
        // Must be set before InitializeComponent — WPF locks AllowsTransparency after creation.
        AllowsTransparency = true;
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _source = (HwndSource?)PresentationSource.FromVisual(this);
        _source?.AddHook(WndProc);
        DockHitTest.ApplyPassthrough(_source?.Handle ?? IntPtr.Zero);
        ApplyRect();
    }

    internal void ApplyTint(Color color, int opacity)
    {
        byte alpha = (byte)(Math.Clamp(opacity, 0, 100) * 255 / 100);
        VeilSurface.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    internal void SetVisible(bool visible, bool animate)
    {
        _targetVisible = visible;
        BeginAnimation(OpacityProperty, null);

        if (visible)
        {
            if (!IsVisible)
            {
                Show();
            }

            if (animate)
            {
                AnimateOpacityTo(1d, completed: null);
            }
            else
            {
                Opacity = 1d;
            }
        }
        else if (animate)
        {
            AnimateOpacityTo(0d, completed: () =>
            {
                if (!_targetVisible)
                {
                    Hide();
                }
            });
        }
        else
        {
            Hide();
        }
    }

    private void AnimateOpacityTo(double to, Action? completed)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(FadeMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (completed is not null)
        {
            animation.Completed += (_, _) => completed();
        }

        BeginAnimation(OpacityProperty, animation);
    }

    private void ApplyRect()
    {
        if (_source is null)
        {
            return;
        }

        RECT rc = Display.GetPrimaryMonitorBounds(_source.Handle);
        if (rc.Right <= rc.Left || rc.Bottom <= rc.Top)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _source.Handle,
            NativeMethods.HWND_TOPMOST,
            rc.Left,
            rc.Top,
            rc.Right - rc.Left,
            rc.Bottom - rc.Top,
            NativeMethods.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED)
        {
            ApplyRect();
        }

        return IntPtr.Zero;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WharfBar.sln`
Expected: temiz derleme (sınıf henüz hiçbir yerden kullanılmıyor; derlenir)

- [ ] **Step 4: Commit**

```bash
git add src/WharfBar/Views/VeilWindow.xaml src/WharfBar/Views/VeilWindow.xaml.cs
git commit -m "feat(veil): tam ekran katmanlı kaplama penceresi (rect, fade, passthrough)"
```

---

### Task 4: VeilService — durum, hotkey host, config uygulama

**Files:**
- Create: `src/WharfBar/Services/VeilService.cs`

**Interfaces:**
- Consumes: `VeilConfig`/`VeilNormalizer` (Task 1), `HotkeyNative`/`HotkeyParser` (Task 2), `VeilWindow` (Task 3), `ConfigService`
- Produces: `VeilService(ConfigService)`, `VeilService.IsEnabled` (bool), `VeilService.HotkeyRegistrationError` (string?), `VeilService.Changed` (event EventHandler), `VeilService.ApplyConfig(VeilConfig)`, `VeilService.SetEnabled(bool)`, `VeilService.Dispose()`

- [ ] **Step 1: Implement**

`src/WharfBar/Services/VeilService.cs`:

```csharp
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Views;

namespace WharfBar.Services;

/// <summary>
/// Owns the full-screen color veil overlay: state, fade, hotkey registration, config persistence.
/// </summary>
internal sealed class VeilService : IDisposable
{
    private const int HotkeyId = 0x5645; // "VE"

    private readonly ConfigService _config;
    private VeilWindow? _window;
    private HwndSource? _hotkeySource;
    private bool _hotkeyRegistered;
    private bool _disposed;

    internal VeilService(ConfigService config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    internal bool IsEnabled { get; private set; }

    /// <summary>Hotkey registration failure text; null when no hotkey or registration succeeded.</summary>
    internal string? HotkeyRegistrationError { get; private set; }

    internal event EventHandler? Changed;

    /// <summary>Applies config (startup, settings page). Always recreates window state.</summary>
    internal void ApplyConfig(VeilConfig cfg)
    {
        EnsureWindow();
        Color color = VeilNormalizer.TryParseColor(cfg.Color, out Color parsed)
            ? parsed
            : DefaultColor;
        _window!.ApplyTint(color, VeilNormalizer.ClampOpacity(cfg.Opacity));
        SetEnabledCore(cfg.Enabled, animate: true);
        ReconfigureHotkey(cfg.HotKey);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Toggles the veil (QS tile, hotkey); persists Enabled to config.</summary>
    internal void SetEnabled(bool enabled)
    {
        SetEnabledCore(enabled, animate: true);
        VeilConfig cfg = _config.Current.Veil ??= new VeilConfig();
        cfg.Enabled = enabled;
        _config.Save(_config.Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterHotkey();
        _hotkeySource?.RemoveHook(WndProc);
        _hotkeySource?.Dispose();
        _hotkeySource = null;
        _window?.Close();
        _window = null;
    }

    private void SetEnabledCore(bool enabled, bool animate)
    {
        IsEnabled = enabled;
        _window?.SetVisible(enabled, animate);
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new VeilWindow();
        _window.Show();
    }

    private void ReconfigureHotkey(string? hotkeyText)
    {
        UnregisterHotkey();
        HotkeyRegistrationError = null;

        if (!HotkeyParser.TryParse(hotkeyText, out uint modifiers, out uint vk))
        {
            return; // boş veya geçersiz → kısayol yok (soft alan)
        }

        if (_hotkeySource is null)
        {
            var parameters = new HwndSourceParameters("WharfBarVeilHotkey")
            {
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
                WindowStyle = 0,
                // Message-only window: no desktop presence, receives WM_HOTKEY.
                ParentWindow = new IntPtr(-3),
            };
            _hotkeySource = new HwndSource(parameters);
            _hotkeySource.AddHook(WndProc);
        }

        if (HotkeyNative.RegisterHotKey(_hotkeySource.Handle, HotkeyId, modifiers, vk))
        {
            _hotkeyRegistered = true;
        }
        else
        {
            HotkeyRegistrationError = "Kısayol kullanılamıyor (başka bir uygulama tarafından kullanılıyor olabilir).";
            Debug.WriteLine("[WharfBar] Veil hotkey registration failed.");
        }
    }

    private void UnregisterHotkey()
    {
        if (!_hotkeyRegistered)
        {
            return;
        }

        HotkeyNative.UnregisterHotKey(_hotkeySource?.Handle ?? IntPtr.Zero, HotkeyId);
        _hotkeyRegistered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyNative.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            SetEnabled(!IsEnabled);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static Color DefaultColor =>
        VeilNormalizer.TryParseColor(VeilConfig.DefaultColor, out Color color) ? color : Colors.Blue;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WharfBar.sln`
Expected: temiz derleme (henüz çağıran yok; derlenir)

- [ ] **Step 3: Commit**

```bash
git add src/WharfBar/Services/VeilService.cs
git commit -m "feat(veil): kaplama servisi — durum, fade, RegisterHotKey host, config kalıcılığı"
```

### Task 5: QS karosu + uygulama kablolaması

**Files:**
- Modify: `src/WharfBar/Services/QuickSettingsService.cs` (field + ctor + BuildTiles + ToggleTileAsync)
- Modify: `src/WharfBar/Views/QuickSettingsFlyout.xaml` (satır 86: `Rows="2"` → `Rows="3"`)
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs` (field, ctor, QS kurulumu ~satır 130)
- Modify: `src/WharfBar/App.xaml.cs` (field, `Veil` property, OnStartup, RecreateTopBar, OnExit)

**Interfaces:**
- Consumes: `VeilService` (Task 4), `QuickSettingsService` mevcut yapısı
- Produces: QS tile id `"veil"` (label "Kaplama", glyph `\uE790`, `IsActive = veil.IsEnabled`); `App.Veil` (property); `TopBarWindow(TaskbarVisibilityService, ConfigService, VeilService)` — yeni imza

- [ ] **Step 1: QuickSettingsService — veil tile + enjeksiyon**

`src/WharfBar/Services/QuickSettingsService.cs`:

`_nightLight` alanının yanına:

```csharp
    private readonly VeilService _veil;
```

Ctor imzası ve gövdesi (değişen kısımlar):

```csharp
    internal QuickSettingsService(NightLightService nightLight, VeilService veil)
    {
        _nightLight = nightLight;
        _veil = veil;
        veil.Changed += (_, _) =>
        {
            if (_disposed)
            {
                return;
            }

            Tiles = BuildTiles();
            Changed?.Invoke(this, EventArgs.Empty);
        };
        Network = ReadNetworkDetails();
        Tiles = BuildTiles();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => OnPollTick();
        _ = RefreshRadiosAsync();
    }
```

`BuildTiles()` içinde nightlight girişinden sonra:

```csharp
            new QuickTileState(
                "veil",
                "Kaplama",
                "\uE790",
                IsEnabled: true,
                IsActive: _veil.IsEnabled,
                HasChevron: false,
                Tooltip: "Renk kaplaması"),
```

`ToggleTileAsync` içinde nightlight case'inden sonra:

```csharp
            case "veil":
                _veil.SetEnabled(!_veil.IsEnabled);
                break;
```

- [ ] **Step 2: Flyout grid 3 satır**

`src/WharfBar/Views/QuickSettingsFlyout.xaml` satır 86: `Rows="2"` → `Rows="3"`

- [ ] **Step 3: TopBarWindow — ctor + QS kurulumu**

`src/WharfBar/Views/TopBarWindow.xaml.cs`:

`_config` alanının yanına:

```csharp
    private readonly VeilService _veil;
```

Ctor imzası ve ataması:

```csharp
    internal TopBarWindow(TaskbarVisibilityService taskbar, ConfigService config, VeilService veil)
    {
        _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _veil = veil ?? throw new ArgumentNullException(nameof(veil));
```

`OnSourceInitialized` içindeki (satır ~130):

```csharp
        _quick = new QuickSettingsService(_nightLight);
```

→

```csharp
        _quick = new QuickSettingsService(_nightLight, _veil);
```

- [ ] **Step 4: App — yaşam döngüsü**

`src/WharfBar/App.xaml.cs`:

Alan (diğer alanların yanına):

```csharp
    private VeilService? _veil;
```

Property (`TopBar` property'sinden sonra):

```csharp
    /// <summary>Exposes the veil service for settings pages and shutdown.</summary>
    internal VeilService? Veil => _veil;
```

`OnStartup` içine, `new AutostartService().Apply(config.Autostart);` satırından sonra:

```csharp
        _veil = new VeilService(_config);
        _veil.ApplyConfig(config.Veil);
```

`OnStartup` içindeki (satır ~105):

```csharp
        var topBar = new TopBarWindow(_taskbar, _config);
```

→

```csharp
        var topBar = new TopBarWindow(_taskbar, _config, _veil);
```

`RecreateTopBar` içindeki (satır ~149):

```csharp
            next = new TopBarWindow(_taskbar, _config);
```

→

```csharp
            next = new TopBarWindow(_taskbar, _config, _veil);
```

`OnExit` içine (`_tray?.Dispose();` bloğundan sonra):

```csharp
        _veil?.Dispose();
        _veil = null;
```

- [ ] **Step 5: Build + Commit**

Run: `dotnet build WharfBar.sln`
Expected: temiz derleme

```bash
git add src/WharfBar/Services/QuickSettingsService.cs src/WharfBar/Views/QuickSettingsFlyout.xaml src/WharfBar/Views/TopBarWindow.xaml.cs src/WharfBar/App.xaml.cs
git commit -m "feat(veil): Hızlı Ayarlar karosu + uygulama yaşam döngüsü kablolaması"
```

---

### Task 6: VeilPage — Kaplama ayarlar sayfası

**Files:**
- Create: `src/WharfBar/Views/Pages/VeilPage.xaml`
- Create: `src/WharfBar/Views/Pages/VeilPage.xaml.cs`
- Modify: `src/WharfBar/Views/SettingsWindow.xaml` (MenuItems'e yeni öğe)

**Interfaces:**
- Consumes: `App.Veil` (Task 5), `VeilConfig`/`VeilNormalizer` (Task 1), `HotkeyParser`/`HotkeyNative` (Task 2), `ConfigService`
- Produces: `VeilPage` (NavigationView hedefi)

- [ ] **Step 1: Create XAML**

`src/WharfBar/Views/Pages/VeilPage.xaml`:

```xml
<Page x:Class="WharfBar.Views.Pages.VeilPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
      Title="Kaplama"
      Background="Transparent">

    <Page.Resources>
        <Style TargetType="TextBlock">
            <Setter Property="FontFamily" Value="{DynamicResource AppTextFontFamily}" />
            <Setter Property="Foreground" Value="{DynamicResource TextFillColorPrimaryBrush}" />
        </Style>
        <Style x:Key="PowerToysCard" TargetType="Border">
            <Setter Property="Background" Value="{DynamicResource CardBackgroundFillColorDefaultBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource CardStrokeColorDefaultBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="8" />
            <Setter Property="Padding" Value="16,14" />
            <Setter Property="Margin" Value="0,0,0,8" />
        </Style>
    </Page.Resources>

    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" Padding="32,24,32,32">
        <StackPanel MaxWidth="850" HorizontalAlignment="Left">

            <TextBlock Text="Kaplama" FontSize="28" FontWeight="SemiBold" Margin="0,0,0,24" />

            <!-- Etkinleştir -->
            <Border Style="{StaticResource PowerToysCard}">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>

                    <ui:SymbolIcon Grid.Column="0" Symbol="Color24" FontSize="20"
                                   Foreground="{DynamicResource AccentFillColorDefaultBrush}"
                                   VerticalAlignment="Center" Margin="0,0,16,0" />

                    <StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="0,0,16,0">
                        <TextBlock Text="Etkinleştir" FontSize="14" FontWeight="Medium" />
                        <TextBlock Text="Ekranın üzerine yarı saydam renk kaplaması koy (disleksi / görsel stres / parlaklık azaltma)"
                                   FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                   TextWrapping="Wrap" Margin="0,2,0,0" />
                    </StackPanel>

                    <ui:ToggleSwitch x:Name="VeilEnabledSwitch"
                                     Grid.Column="2"
                                     HorizontalAlignment="Right"
                                     VerticalAlignment="Center" />
                </Grid>
            </Border>

            <!-- Renk -->
            <Border Style="{StaticResource PowerToysCard}">
                <StackPanel>
                    <TextBlock Text="Renk" FontSize="14" FontWeight="Medium" Margin="0,0,0,10" />
                    <WrapPanel x:Name="ColorSwatches" />
                    <DockPanel Margin="0,4,0,0">
                        <TextBlock Text="Özel renk (#RRGGBB):" FontSize="12"
                                   Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                                   VerticalAlignment="Center" Margin="0,0,8,0" />
                        <TextBox x:Name="ColorHexBox" Width="110" VerticalContentAlignment="Center"
                                 MaxLength="7" FontFamily="Consolas" />
                        <TextBlock x:Name="ColorErrorText" Text="Geçersiz renk kodu (#RRGGBB veya #RGB)"
                                   FontSize="12" Foreground="#E57373"
                                   VerticalAlignment="Center" Margin="8,0,0,0"
                                   Visibility="Collapsed" />
                    </DockPanel>
                </StackPanel>
            </Border>

            <!-- Opaklık -->
            <Border Style="{StaticResource PowerToysCard}">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition Height="Auto" />
                        <RowDefinition Height="Auto" />
                    </Grid.RowDefinitions>

                    <Grid Grid.Row="0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="Opaklık" FontSize="14" FontWeight="Medium" VerticalAlignment="Center" />
                        <TextBlock x:Name="OpacityLabel" Grid.Column="1" Text="40%"
                                   HorizontalAlignment="Right" VerticalAlignment="Center"
                                   FontSize="13" FontWeight="SemiBold"
                                   Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                    </Grid>
                    <Slider x:Name="OpacitySlider" Grid.Row="1"
                            Minimum="0" Maximum="100"
                            IsSnapToTickEnabled="True" TickFrequency="1"
                            Margin="0,12,0,4"
                            ValueChanged="OnOpacityChanged" />
                </Grid>
            </Border>

            <!-- Kısayol -->
            <Border Style="{StaticResource PowerToysCard}">
                <StackPanel>
                    <TextBlock Text="Kısayol" FontSize="14" FontWeight="Medium" Margin="0,0,0,4" />
                    <TextBlock Text="Kaplamayı açıp kapatmak için global kısayol"
                               FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                               TextWrapping="Wrap" Margin="0,2,0,10" />

                    <StackPanel Orientation="Horizontal">
                        <TextBlock Text="Geçerli kısayol:" FontSize="12" VerticalAlignment="Center"
                                   Foreground="{DynamicResource TextFillColorSecondaryBrush}" />
                        <TextBlock x:Name="HotkeyLabel" Text="Ctrl+F11" FontSize="13" FontWeight="SemiBold"
                                   VerticalAlignment="Center" Margin="8,0,0,0" />
                    </StackPanel>

                    <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
                        <Button x:Name="CaptureHotkeyButton" Content="Kısayolu değiştir" Click="OnCaptureHotkeyClick" />
                        <Button x:Name="RemoveHotkeyButton" Content="Kaldır" Margin="8,0,0,0" Click="OnRemoveHotkeyClick" />
                        <Button x:Name="ResetHotkeyButton" Content="Sıfırla (Ctrl+F11)" Margin="8,0,0,0" Click="OnResetHotkeyClick" />
                    </StackPanel>

                    <TextBlock x:Name="CaptureHotkeyHint" Text="Yeni kısayola basın… (Esc: iptal)"
                               FontSize="12" Foreground="{DynamicResource AccentFillColorDefaultBrush}"
                               Margin="0,10,0,0" Visibility="Collapsed" />
                    <TextBlock x:Name="HotkeyErrorText" FontSize="12" Foreground="#E57373"
                               Margin="0,10,0,0" Visibility="Collapsed" />
                </StackPanel>
            </Border>

        </StackPanel>
    </ScrollViewer>
</Page>
```

- [ ] **Step 2: Implement code-behind**

`src/WharfBar/Views/Pages/VeilPage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views.Pages;

/// <summary>
/// Veil (color overlay) settings: enable, color palette + hex, opacity, hotkey.
/// Every change saves config and applies live via VeilService.ApplyConfig.
/// </summary>
public partial class VeilPage : Page
{
    private static readonly (string Name, string Hex)[] PresetColors =
    [
        ("Mavi", "#4A90D9"),
        ("Yeşil", "#7CB342"),
        ("Pembe", "#E57373"),
        ("Turuncu", "#E6A23C"),
        ("Sarı", "#D4C24A"),
        ("Mor", "#9E7BB5"),
        ("Gri", "#8E8E8E"),
        ("Koyu", "#2B2B2B"),
    ];

    private bool _isLoading = true;
    private bool _capturingHotkey;
    private bool _updatingColorHex;

    public VeilPage()
    {
        InitializeComponent();
        BuildSwatches();
        LoadSettings();

        VeilEnabledSwitch.Checked += OnEnabledChanged;
        VeilEnabledSwitch.Unchecked += OnEnabledChanged;
        OpacitySlider.ValueChanged += OnOpacityChanged;
        ColorHexBox.TextChanged += OnColorHexChanged;
        CaptureHotkeyButton.Click += OnCaptureHotkeyClick;
        RemoveHotkeyButton.Click += OnRemoveHotkeyClick;
        ResetHotkeyButton.Click += OnResetHotkeyClick;
        PreviewKeyDown += OnPageKeyDown;

        _isLoading = false;
    }

    private App WharfApp => (App)Application.Current;
    private ConfigService Config => WharfApp.Config!;
    private AppConfig Cfg => Config.Current;

    private void BuildSwatches()
    {
        foreach ((string name, string hex) in PresetColors)
        {
            var button = new Button
            {
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 8, 8),
                Tag = hex,
                ToolTip = name,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            button.Click += (_, _) => ApplyColor(hex);
            ColorSwatches.Children.Add(button);
        }
    }

    private void LoadSettings()
    {
        VeilConfig veil = Cfg.Veil;
        VeilEnabledSwitch.IsChecked = veil.Enabled;
        OpacitySlider.Value = VeilNormalizer.ClampOpacity(veil.Opacity);
        OpacityLabel.Text = $"{VeilNormalizer.ClampOpacity(veil.Opacity)}%";
        _updatingColorHex = true;
        ColorHexBox.Text = VeilNormalizer.NormalizeColor(veil.Color).ToUpperInvariant();
        _updatingColorHex = false;
        UpdateHotkeyUi(veil.HotKey);
    }

    private void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        Cfg.Veil.Enabled = VeilEnabledSwitch.IsChecked == true;
        SaveAndApply();
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        int v = (int)Math.Round(e.NewValue);
        OpacityLabel.Text = $"{v}%";
        if (_isLoading)
        {
            return;
        }

        Cfg.Veil.Opacity = VeilNormalizer.ClampOpacity(v);
        SaveAndApply();
    }

    private void OnColorHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading || _updatingColorHex)
        {
            return;
        }

        if (VeilNormalizer.TryParseColor(ColorHexBox.Text, out _))
        {
            ColorErrorText.Visibility = Visibility.Collapsed;
            Cfg.Veil.Color = VeilNormalizer.NormalizeColor(ColorHexBox.Text);
            SaveAndApply();
        }
        else
        {
            ColorErrorText.Visibility = Visibility.Visible;
        }
    }

    private void ApplyColor(string hex)
    {
        string normalized = VeilNormalizer.NormalizeColor(hex);
        _updatingColorHex = true;
        ColorHexBox.Text = normalized.ToUpperInvariant();
        _updatingColorHex = false;
        ColorErrorText.Visibility = Visibility.Collapsed;
        Cfg.Veil.Color = normalized;
        SaveAndApply();
    }

    private void OnCaptureHotkeyClick(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        CaptureHotkeyHint.Visibility = Visibility.Visible;
    }

    private void OnRemoveHotkeyClick(object sender, RoutedEventArgs e)
    {
        Cfg.Veil.HotKey = string.Empty;
        SaveAndApply();
        UpdateHotkeyUi(string.Empty);
    }

    private void OnResetHotkeyClick(object sender, RoutedEventArgs e)
    {
        Cfg.Veil.HotKey = VeilConfig.DefaultHotKey;
        SaveAndApply();
        UpdateHotkeyUi(VeilConfig.DefaultHotKey);
    }

    private void OnPageKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey)
        {
            return;
        }

        e.Handled = true;
        if (e.Key is Key.Escape or Key.Enter)
        {
            EndCapture();
            return;
        }

        if (!TryMapKey(e.Key, out uint vk))
        {
            return;
        }

        uint modifiers = MapModifiers(Keyboard.Modifiers);
        if (modifiers == 0)
        {
            return; // en az bir modifier zorunlu
        }

        string hotkey = HotkeyParser.Format(modifiers, vk);
        Cfg.Veil.HotKey = hotkey;
        SaveAndApply();
        EndCapture();
        UpdateHotkeyUi(hotkey);
    }

    private void EndCapture()
    {
        _capturingHotkey = false;
        CaptureHotkeyHint.Visibility = Visibility.Collapsed;
    }

    private void UpdateHotkeyUi(string hotkey)
    {
        HotkeyLabel.Text = string.IsNullOrEmpty(hotkey) ? "Kısayol yok" : hotkey;
        string? error = WharfApp.Veil?.HotkeyRegistrationError;
        HotkeyErrorText.Text = error ?? string.Empty;
        HotkeyErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SaveAndApply()
    {
        Config.Save(Cfg);
        WharfApp.Veil?.ApplyConfig(Cfg.Veil);
    }

    private static bool TryMapKey(Key key, out uint vk)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            vk = (uint)(key - Key.A + 0x41);
            return true;
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            vk = (uint)(key - Key.D0 + 0x30);
            return true;
        }

        if (key is >= Key.F1 and <= Key.F12)
        {
            vk = (uint)(key - Key.F1 + 0x70);
            return true;
        }

        vk = 0;
        return false;
    }

    private static uint MapModifiers(ModifierKeys modifiers)
    {
        uint m = 0;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            m |= HotkeyNative.MOD_CONTROL;
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            m |= HotkeyNative.MOD_ALT;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            m |= HotkeyNative.MOD_SHIFT;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            m |= HotkeyNative.MOD_WIN;
        }

        return m;
    }
}
```

- [ ] **Step 3: SettingsWindow — menü öğesi**

`src/WharfBar/Views/SettingsWindow.xaml`, "Üst Çubuk" NavigationViewItem'ından sonra (satır ~67):

```xml
                <ui:NavigationViewItem Content="Kaplama"
                                       TargetPageType="{x:Type pages:VeilPage}">
                    <ui:NavigationViewItem.Icon>
                        <ui:SymbolIcon Symbol="Color24" />
                    </ui:NavigationViewItem.Icon>
                </ui:NavigationViewItem>
```

- [ ] **Step 4: Build + Commit**

Run: `dotnet build WharfBar.sln`
Expected: temiz derleme

```bash
git add src/WharfBar/Views/Pages/VeilPage.xaml src/WharfBar/Views/Pages/VeilPage.xaml.cs src/WharfBar/Views/SettingsWindow.xaml
git commit -m "feat(settings): Kaplama ayarlar sayfası (renk/opaklık/kısayol)"
```

---

### Task 7: Dokümanlar + genel doğrulama

**Files:**
- Create: `docs/decisions/007-veil-overlay.md`
- Modify: `docs/01-architecture.md`, `docs/02-win32-appbar-dwm.md`, `.agent/project.md`, `.agent/context/roadmap.md`, `.agent/screens/README.md`

- [ ] **Step 1: ADR-007**

`docs/decisions/007-veil-overlay.md` oluştur (mevcut ADR formatını `docs/decisions/006-*.md`'den kontrol et; kısa tut):

```markdown
# ADR-007: Renk kaplaması (veil) — katmanlı pencere yaklaşımı

**Tarih:** 2026-08-18
**Kapsam:** ColorVeil tarzı tam ekran renk kaplaması
(spec: docs/superpowers/specs/2026-08-18-veil-overlay-design.md)

## Karar

- Overlay: tek katmanlı WPF penceresi (`VeilWindow`) — `AllowsTransparency=True` düz renk dolgu,
  tam ekran rect, `Topmost`, `WS_EX_TRANSPARENT` passthrough, 280 ms fade. Magnification API ve
  gamma ramp reddedildi (belgelenmemiş/istikrarsız; admin gerekli; renk uzayı dönüşümü).
- Kontrol: Hızlı Ayarlar karosu + global kısayol (`RegisterHotKey`, varsayılan Ctrl+F11, config'te değiştirilebilir).
- Kaplama her zaman en üstte (tam ekran uygulamalar dahil). Exclusive fullscreen (DWM dışı render)
  üzerinde görünmez — ColorVeil'de de aynı sınır.
- Parlaklık azaltma ayrı kontrol değildir: siyah + yüksek opaklık aynı mekanizmadır.
- Çıkışta `UnregisterHotKey` garantisi (`VeilService.Dispose` → `App.OnExit`).

## Sonuçlar

- Yeni bağımlılık yok; kaynak maliyeti ~0 (statik pencere).
- Elevated pencerelerin üzerine kaplama çizilemez (süreç sınırı).
- v1 yalnızca birincil monitör.
```

- [ ] **Step 2: Mimari dokümanları**

`docs/01-architecture.md` — ilgili bölümlere ekle:
- Views listesine: `VeilWindow.xaml(.cs)   # tam ekran kaplama overlay (click-through, fade)`
- Views/Pages listesine (mevcut yoksa listeye): `VeilPage.xaml(.cs)   # Kaplama ayarları`
- Native listesine: `Hotkey.cs               # RegisterHotKey P/Invoke + kısayol parser`
- Services listesine: `VeilService.cs        # kaplama durumu, fade, hotkey host, config`
- Config şemasına `veil` bloğu + tablo satırları (spec'teki `VeilConfig` şemasını kullan)
- Sorumluluklar tablosuna `VeilService` / `VeilWindow` / `HotkeyParser` satırları

`docs/02-win32-appbar-dwm.md` — kısa bölüm ekle:

```markdown
## Global kısayol (RegisterHotKey)

```csharp
RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk)      // user32.dll
UnregisterHotKey(IntPtr hWnd, int id)                               // user32.dll
// fsModifiers: MOD_ALT=0x1, MOD_CONTROL=0x2, MOD_SHIFT=0x4, MOD_WIN=0x8
// WM_HOTKEY = 0x0312; wParam = id; mesaj-only HwndSource (ParentWindow=-3)
```
```

- [ ] **Step 3: Proje dokümanları**

`.agent/project.md` — "Mevcut repo durumu" bölümüne ekle:
"Renk kaplaması (veil): QS karosu + global kısayol (Ctrl+F11) ile tam ekran renk overlay; Kaplama ayarlar sayfası (renk/opaklık/kısayol). Spec/plan: 2026-08-18-veil-overlay."

`.agent/context/roadmap.md` — ilgili bölüme kaplama özelliğini işaretle.

`.agent/screens/README.md` — Hızlı ayarlar ve Ayarlar bölümlerine Kaplama karosu / sayfasını ekle (mevcut yapıyı okuyup aynı formatta).

- [ ] **Step 4: Genel doğrulama**

Run: `dotnet build WharfBar.sln` → temiz; `dotnet test` → tüm testler PASS
Run: `git status` → yalnızca beklenen dosyalar; `git log --oneline -8` → 7 temiz commit

- [ ] **Step 5: Commit**

```bash
git add docs/decisions/007-veil-overlay.md docs/01-architecture.md docs/02-win32-appbar-dwm.md .agent/project.md .agent/context/roadmap.md .agent/screens/README.md
git commit -m "docs(veil): ADR-007, mimari ve proje dokümanı güncellemeleri"
```

- [ ] **Step 6: Manuel doğrulama (kullanıcıya bırakılır)**

Uygulama çalıştırmayı kullanıcı yapar (`dotnet run --project src/WharfBar`). Doğrulama listesi spec'ten:

1. QS karosu aç → ~280 ms fade ile kaplama gelir, karo vurgulu; kapat → fade ile gider
2. Kaplama açıkken masaüstü/pencere tıklamaları normal çalışır (passthrough)
3. `Ctrl+F11` → aç/kapa (karoyla senkron)
4. Kaplama sayfası: swatch/hex renk canlı değişir; geçersiz hex reddedilir
5. Opaklık 100 + siyah → ekran pratikte kararır (parlaklık azaltma)
6. Kısayol değiştir (örn. `Ctrl+Alt+V`) → yeni kısayol çalışır, eski çalışmaz; çakışan kısayol → uyarı, çökme yok
7. Yeniden başlat → kaplama durumu/renk/opaklık/kısayol korunur
8. Tam ekran video → kaplama üstte; Quit sonrası hotkey serbest, ikinci başlatmada yeniden çalışır


