using System.Globalization;
using System.Windows.Media;

namespace WharfBar.Models;

/// <summary>
/// Root JSON config persisted at <c>%AppData%/WharfBar/config.json</c>.
/// </summary>
internal sealed class AppConfig
{
    internal const int CurrentSchemaVersion = 1;

    internal const string ThemeSystem = "system";
    internal const string ThemeLight = "light";
    internal const string ThemeDark = "dark";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary><c>system</c>, <c>light</c>, or <c>dark</c>.</summary>
    public string Theme { get; set; } = ThemeSystem;

    /// <summary>When true, applies Windows system accent color to application controls.</summary>
    public bool UseSystemAccentColor { get; set; } = true;

    /// <summary>When true, <see cref="Services.AutostartService"/> keeps a Startup-folder shortcut.</summary>
    public bool Autostart { get; set; } = true;

    public DockConfig Dock { get; set; } = new();

    public TopBarConfig TopBar { get; set; } = new();

    public VeilConfig Veil { get; set; } = new();

    public List<PinnedApp> PinnedApps { get; set; } = [];
}

/// <summary>Dock surface backdrop styles (<see cref="DockConfig.Surface"/>).</summary>
internal static class DockSurfaceStyles
{
    internal const string Mica = "mica";

    internal const string Acrylic = "acrylic";

    internal static string NormalizeStyle(string? style)
    {
        return string.Equals(style, Acrylic, StringComparison.OrdinalIgnoreCase)
            ? Acrylic
            : Mica;
    }
}

/// <summary>Dock-specific settings under <see cref="AppConfig.Dock"/>.</summary>
internal sealed class DockConfig
{
    /// <summary>Taskbar-like icon size in DIP (slightly above shell medium; not macOS-dock large).</summary>
    public const double DefaultIconSize = 28d;

    /// <summary>Previous brief default; remapped to <see cref="DefaultIconSize"/> on load.</summary>
    public const double PreviousDefaultIconSize = 24d;

    public const double MinIconSize = 16d;

    /// <summary>Caps oversized early defaults (48/72); allows a modest bump above default.</summary>
    public const double MaxIconSize = 36d;

    /// <summary>Stored for later; <see cref="Services.DockAutoHideController"/> still stubs behavior.</summary>
    public bool AutoHide { get; set; } = true;

    public double IconSize { get; set; } = DefaultIconSize;

    /// <summary>
    /// Surface backdrop: <c>mica</c> or <c>acrylic</c>. The tint color comes from the Windows
    /// system theme via the DWM backdrop — the same source as the top bar.
    /// </summary>
    public string Surface { get; set; } = DockSurfaceStyles.Mica;
}

/// <summary>Top bar settings under <see cref="AppConfig.TopBar"/>.</summary>
internal sealed class TopBarConfig
{
    /// <summary>
    /// Soft catalog revision (<see cref="AppConfig.SchemaVersion"/> stays 1).
    /// Missing JSON → 0 (pre-<c>controlCenter</c>); current = <see cref="CurrentStatusCatalogVersion"/>.
    /// </summary>
    internal const int CurrentStatusCatalogVersion = 5;

    /// <summary>
    /// Soft catalog revision. 0 = pre-controlCenter configs (migrated on load).
    /// </summary>
    public int StatusCatalogVersion { get; set; }

    /// <summary>
    /// Visible status item ids in left→right order. Omitted ids are hidden.
    /// Unknown ids stripped on load.
    /// </summary>
    public List<string> StatusItems { get; set; } = TopBarStatusItemIds.DefaultOrder.ToList();

    /// <summary><c>mica</c>, <c>transparent</c>, or <c>tint</c>.</summary>
    public string BackgroundMode { get; set; } = TopBarBackgroundModes.Mica;

    /// <summary>0–100; used only when <see cref="BackgroundMode"/> is <c>tint</c>.</summary>
    public int TintOpacity { get; set; } = TopBarBackgroundModes.DefaultOpacity;
}

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

    /// <summary>Formats a <see cref="Color"/> as a lowercase <c>#rrggbb</c> string (alpha ignored).</summary>
    internal static string NormalizeColor(Color color)
    {
        return $"#{color.R:x2}{color.G:x2}{color.B:x2}";
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
