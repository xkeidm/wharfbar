using System.Globalization;
using System.Windows.Media;
using WharfBar.Services;

namespace WharfBar.Models;

/// <summary>
/// Root JSON config persisted at <c>%AppData%/WharfBar/config.json</c>.
/// </summary>
internal sealed class AppConfig
{
    internal const int CurrentSchemaVersion = 2;

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

    public IslandConfig Island { get; set; } = new();

    public List<PinnedApp> PinnedApps { get; set; } = [];

    public List<MonitorConfig> Monitors { get; set; } = [];
}

/// <summary>Per-monitor visibility settings under <see cref="AppConfig.Monitors"/>.</summary>
internal sealed record MonitorConfig
{
    public MonitorConfig()
    {
    }

    public MonitorConfig(string deviceId, string deviceName, string name, bool topBar, bool dock)
    {
        DeviceId = deviceId;
        DeviceName = deviceName;
        Name = name;
        TopBar = topBar;
        Dock = dock;
    }

    public string DeviceId { get; set; } = string.Empty;

    /// <summary>szDevice (e.g. "\\\\.\\DISPLAY1"); secondary match key when DeviceId is empty.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Display-only friendly name (e.g. "DELL U2720Q"); refreshed on save.</summary>
    public string Name { get; set; } = string.Empty;

    public bool TopBar { get; set; }

    public bool Dock { get; set; }
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

/// <summary>Dock-specific settings under <see cref="AppConfig.Dock"/>.</summary>
internal sealed class DockConfig
{
    /// <summary>Windows 11 taskbar glyph size in DIP.</summary>
    public const double DefaultIconSize = 24d;

    /// <summary>Previous default; remapped to <see cref="DefaultIconSize"/> on load.</summary>
    public const double PreviousDefaultIconSize = 28d;

    public const double MinIconSize = 16d;

    /// <summary>Caps oversized early defaults (48/72); allows a modest bump above default.</summary>
    public const double MaxIconSize = 36d;

    /// <summary>Stored for later; <see cref="Services.DockAutoHideController"/> still stubs behavior.</summary>
    public bool AutoHide { get; set; } = true;

    public double IconSize { get; set; } = DefaultIconSize;

    /// <summary>
    /// Retired. Old configs stored <c>mica</c> or <c>acrylic</c> here; both load as
    /// <see cref="BackgroundMode"/> <c>mica</c>. Kept so those JSON keys still deserialize.
    /// </summary>
    public string Surface { get; set; } = DockSurfaceStyles.Mica;

    /// <summary><c>mica</c>, <c>transparent</c>, or <c>tint</c>. Independent of the top bar.</summary>
    public string BackgroundMode { get; set; } = TopBarBackgroundModes.Mica;

    /// <summary>0–100; used only when <see cref="BackgroundMode"/> is <c>tint</c>.</summary>
    public int TintOpacity { get; set; } = TopBarBackgroundModes.DefaultOpacity;

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

    /// <summary>CPU/GPU/RAM readout beside the Windows logo (left cluster).</summary>
    public bool ShowSystemStats { get; set; } = true;

    /// <summary>
    /// Show one GPU segment per physical adapter (GPU0/GPU1…) instead of a combined total.
    /// Single-adapter systems display identically either way.
    /// </summary>
    public bool ShowGpuPerAdapter { get; set; } = true;
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

/// <summary>Island (clipboard geçmişi + bırakılan dosyalar) ayarları.</summary>
internal sealed class IslandConfig
{
    internal const bool DefaultEnabled = true;

    internal const int DefaultRetentionDays = 30;

    internal const int DefaultMaxItems = 200;

    /// <summary><c>session</c> — yalnız bellekte; WharfBar kapanınca silinir.</summary>
    internal const string PersistenceSession = "session";

    /// <summary><c>persistent</c> — diske yazılır; <c>RetentionDays</c> ile budanır.</summary>
    internal const string PersistencePersistent = "persistent";

    /// <summary>Çözülemeyen <c>Persistence</c> değerlerinin düştüğü mod.</summary>
    internal const string DefaultPersistence = PersistenceSession;

    /// <summary>Müzik varken ada parça adını göstermeye devam eder.</summary>
    internal const string MusicPresencePersistent = "persistent";

    /// <summary>Müzik varken ada boşta kalır; panel yalnız kısa süre açılır.</summary>
    internal const string MusicPresenceTransient = "transient";

    /// <summary>Çözülemeyen <c>MusicPresence</c> değerlerinin düştüğü mod. Şema değişmez.</summary>
    internal const string DefaultMusicPresence = MusicPresencePersistent;

    public bool Enabled { get; set; } = DefaultEnabled;

    /// <summary>Geçmişin nerede tutulacağı: <c>session</c> veya <c>persistent</c>.</summary>
    public string Persistence { get; set; } = DefaultPersistence;

    /// <summary>Persistent modda kaç gün geriye bakılır.</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>Kayıt sayısı sert tavanı (gün limitinden bağımsız).</summary>
    public int MaxItems { get; set; } = DefaultMaxItems;

    /// <summary>
    /// Müzik adada nasıl durur: <c>persistent</c> (varsayılan) veya <c>transient</c>.
    /// Soft alan — <see cref="AppConfig.CurrentSchemaVersion"/> artmaz.
    /// </summary>
    public string MusicPresence { get; set; } = DefaultMusicPresence;
}

/// <summary>Pure static normalization helpers for <see cref="IslandConfig"/> (unit-tested).</summary>
internal static class IslandNormalizer
{
    internal static int ClampRetentionDays(int value) => Math.Clamp(value, 1, 3650);

    internal static int ClampMaxItems(int value) => Math.Clamp(value, 10, 5000);

    /// <summary>Returns the canonical lowercase mode, or <see cref="IslandConfig.DefaultPersistence"/> when unresolvable.</summary>
    internal static string NormalizePersistence(string? persistence)
    {
        return string.Equals(persistence, IslandConfig.PersistencePersistent, StringComparison.OrdinalIgnoreCase)
            ? IslandConfig.PersistencePersistent
            : IslandConfig.DefaultPersistence;
    }

    /// <summary>
    /// Returns <see cref="IslandConfig.MusicPresenceTransient"/> or
    /// <see cref="IslandConfig.DefaultMusicPresence"/>. Unknown text falls back to persistent.
    /// </summary>
    internal static string NormalizeMusicPresence(string? value)
    {
        return string.Equals(value, IslandConfig.MusicPresenceTransient, StringComparison.OrdinalIgnoreCase)
            ? IslandConfig.MusicPresenceTransient
            : IslandConfig.DefaultMusicPresence;
    }

    internal static bool IsTransientMusic(string? value)
        => NormalizeMusicPresence(value) == IslandConfig.MusicPresenceTransient;
}
