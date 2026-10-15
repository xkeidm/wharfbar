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

    internal const string LanguageSystem = "system";
    internal const string LanguageEn = "en";
    internal const string LanguageTr = "tr";

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary><c>system</c>, <c>light</c>, or <c>dark</c>.</summary>
    public string Theme { get; set; } = ThemeSystem;

    /// <summary><c>system</c>, <c>en</c>, or <c>tr</c>. Soft field; schemaVersion unchanged.</summary>
    public string Language { get; set; } = LanguageSystem;

    /// <summary>When true, applies Windows system accent color to application controls.</summary>
    public bool UseSystemAccentColor { get; set; } = true;

    /// <summary>
    /// Logo on the top bar and the dock launch tile: <c>wharfbar</c> (default) or <c>windows</c>.
    /// Soft field; schemaVersion unchanged. See <see cref="LogoStyles"/>.
    /// </summary>
    public string LogoStyle { get; set; } = LogoStyles.Default;

    /// <summary>When true, <see cref="Services.AutostartService"/> keeps a Startup-folder shortcut.</summary>
    public bool Autostart { get; set; } = true;

    public DockConfig Dock { get; set; } = new();

    public TopBarConfig TopBar { get; set; } = new();

    public VeilConfig Veil { get; set; } = new();

    public IslandConfig Island { get; set; } = new();

    public LaunchpadConfig Launchpad { get; set; } = new();

    /// <summary>
    /// Local plugin enable flags (<c>plugins.enabled</c>).
    /// Soft block: <see cref="CurrentSchemaVersion"/> stays 2.
    /// Built-in island plugins and desktop widgets stay on <see cref="Island"/>.
    /// </summary>
    public PluginsConfig Plugins { get; set; } = new();

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

/// <summary>Brand logo choice (<see cref="AppConfig.LogoStyle"/>), shared by every bar and dock.</summary>
internal static class LogoStyles
{
    internal const string WharfBar = "wharfbar";

    internal const string Windows = "windows";

    internal const string Default = WharfBar;

    /// <summary>Canonical lowercase style; unknown text falls back to <see cref="Default"/>.</summary>
    internal static string Normalize(string? style) =>
        string.Equals(style?.Trim(), Windows, StringComparison.OrdinalIgnoreCase)
            ? Windows
            : Default;

    internal static bool IsWindows(string? style) => Normalize(style) == Windows;
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

    /// <summary>Full-height left edge. The plate is vertical and reserves <c>ABE_LEFT</c>.</summary>
    internal const string Left = "left";

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

        if (string.Equals(position, Left, StringComparison.OrdinalIgnoreCase))
        {
            return Left;
        }

        return BottomCenter;
    }

    internal static bool IsLeft(string? position) =>
        string.Equals(Normalize(position), Left, StringComparison.Ordinal);
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

    /// <summary>Anchor: <c>bottom-left</c>, <c>bottom-center</c>, <c>bottom-right</c>, or <c>left</c>.</summary>
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

    /// <summary>
    /// Mirrored tray keys parked in the overflow box (<c>topBar.trayOverflowIcons</c>).
    /// Keys missing from this list stay on the bar. Soft field: <see cref="AppConfig.SchemaVersion"/>
    /// stays <see cref="AppConfig.CurrentSchemaVersion"/> and <see cref="StatusItems"/> is not rewritten.
    /// </summary>
    public List<string> TrayOverflowIcons { get; set; } = [];

    /// <summary><c>mica</c>, <c>transparent</c>, or <c>tint</c>.</summary>
    public string BackgroundMode { get; set; } = TopBarBackgroundModes.Mica;

    /// <summary>0–100; used only when <see cref="BackgroundMode"/> is <c>tint</c>.</summary>
    public int TintOpacity { get; set; } = TopBarBackgroundModes.DefaultOpacity;

    /// <summary>CPU/GPU/RAM readout beside the brand logo (left cluster).</summary>
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

    /// <summary>Boşta hap sabit <c>Pano</c> yazar.</summary>
    internal const string IdleLabelClipboard = "clipboard";

    /// <summary>Boşta hap son metin kaydının önizlemesini yazar.</summary>
    internal const string IdleLabelLastClip = "lastClip";

    /// <summary>Çözülemeyen <c>IdleLabel</c> değerlerinin düştüğü mod. Şema değişmez.</summary>
    internal const string DefaultIdleLabel = IdleLabelClipboard;

    internal const string HubTabClipboard = "clipboard";
    internal const string HubTabWallpaper = "wallpaper";
    internal const string HubTabMusic = "music";
    internal const string DefaultHubTab = HubTabClipboard;

    internal const string TransitionOff = "off";

    internal const string TransitionFade = "fade";

    internal const string TransitionBlurFade = "blurFade";

    internal const string DefaultWallpaperTransition = TransitionBlurFade;

    internal const bool DefaultWallpaperRespectSystemAnimation = true;

    internal const bool DefaultWallpaperAccent = true;

    /// <summary>Düşük pil HUD eşiği. Aralık dışı yükleme buna döner.</summary>
    internal const int DefaultLowBatteryPercent = 20;

    /// <summary>Kabul edilen eşiğin alt sınırı.</summary>
    internal const int MinLowBatteryPercent = 5;

    /// <summary>Kabul edilen eşiğin üst sınırı.</summary>
    internal const int MaxLowBatteryPercent = 50;

    /// <summary>Tint from the playing track's cover (default).</summary>
    internal const string ColorModeAlbum = "album";

    /// <summary>Tint is the system accent color.</summary>
    internal const string ColorModeSystem = "system";

    /// <summary>No tint; theme resources only.</summary>
    internal const string ColorModeNeutral = "neutral";

    /// <summary>Fallback for unresolvable <c>ColorMode</c> values. Schema does not change.</summary>
    internal const string DefaultColorMode = ColorModeAlbum;

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

    /// <summary>
    /// Boşta hapta ne yazar: <c>clipboard</c> (varsayılan, "Pano") veya <c>lastClip</c>.
    /// Soft alan — <see cref="AppConfig.CurrentSchemaVersion"/> artmaz.
    /// </summary>
    public string IdleLabel { get; set; } = DefaultIdleLabel;

    /// <summary>
    /// Last island hub page: <c>clipboard</c> (default), <c>wallpaper</c>, or <c>music</c>.
    /// Soft field — <see cref="AppConfig.CurrentSchemaVersion"/> does not change.
    /// </summary>
    public string HubTab { get; set; } = DefaultHubTab;

    /// <summary>
    /// Wallpaper change animation: <c>off</c>, <c>fade</c>, or <c>blurFade</c> (default).
    /// Soft field — <see cref="AppConfig.CurrentSchemaVersion"/> does not change.
    /// </summary>
    public string WallpaperTransition { get; set; } = DefaultWallpaperTransition;

    /// <summary>
    /// When true, skip animation if Windows client-area animations are disabled.
    /// Soft field — schema unchanged.
    /// </summary>
    public bool WallpaperRespectSystemAnimation { get; set; } = DefaultWallpaperRespectSystemAnimation;

    /// <summary>
    /// After a hub wallpaper apply, sync WharfBar (+ best-effort Windows) accent from the image.
    /// Soft field — schema unchanged. Default true.
    /// </summary>
    public bool WallpaperAccent { get; set; } = DefaultWallpaperAccent;

    /// <summary>
    /// Pil sayacının eşiği (yüzde). Soft alan <c>lowBatteryPercent</c> —
    /// <see cref="AppConfig.CurrentSchemaVersion"/> artmaz. Bilinmeyen veya 5–50 dışı
    /// yükleme <see cref="DefaultLowBatteryPercent"/> olur; kullanıcının yazdığı değer
    /// 5–50 aralığına sıkıştırılır.
    /// </summary>
    public int LowBatteryPercent { get; set; } = DefaultLowBatteryPercent;

    /// <summary>
    /// Pill and panel tint: <c>album</c> (default), <c>system</c> or <c>neutral</c>.
    /// Soft field — <see cref="AppConfig.CurrentSchemaVersion"/> does not change.
    /// </summary>
    public string ColorMode { get; set; } = DefaultColorMode;

    /// <summary>Bars beside the track title while music is playing. Soft field.</summary>
    public bool Visualizer { get; set; } = true;

    /// <summary>Island notice when a paired Bluetooth device connects. Soft field.</summary>
    public bool BluetoothNotice { get; set; } = true;

    /// <summary>Live camera inside the pill. Off until the gallery enables it.</summary>
    public bool CameraMirror { get; set; }

    /// <summary>Desktop widgets currently placed: <c>clock</c>, <c>calendar</c>.</summary>
    public List<string> OpenWidgets { get; set; } = [];
}

/// <summary>
/// Enable flags for local plugin manifests. Soft block — schema stays 2.
/// An id here is a record only; WharfBar does not load an assembly or download code.
/// </summary>
internal sealed class PluginsConfig
{
    /// <summary>Enabled local manifest ids. Built-in ids are not stored here.</summary>
    public List<string> Enabled { get; set; } = [];
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

    /// <summary>
    /// Returns <see cref="IslandConfig.IdleLabelLastClip"/> or
    /// <see cref="IslandConfig.DefaultIdleLabel"/>. Unknown text falls back to clipboard.
    /// </summary>
    internal static string NormalizeIdleLabel(string? value)
    {
        return string.Equals(value, IslandConfig.IdleLabelLastClip, StringComparison.OrdinalIgnoreCase)
            ? IslandConfig.IdleLabelLastClip
            : IslandConfig.DefaultIdleLabel;
    }

    internal static bool IsLastClipIdle(string? value)
        => NormalizeIdleLabel(value) == IslandConfig.IdleLabelLastClip;

    internal static string NormalizeHubTab(string? value) => IslandHub.ToConfig(IslandHub.Normalize(value));

    /// <summary>
    /// Returns <see cref="IslandConfig.TransitionOff"/>, <see cref="IslandConfig.TransitionFade"/>,
    /// or <see cref="IslandConfig.TransitionBlurFade"/>. Unknown text falls back to blurFade.
    /// </summary>
    internal static string NormalizeWallpaperTransition(string? value)
    {
        if (string.Equals(value, IslandConfig.TransitionOff, StringComparison.OrdinalIgnoreCase))
        {
            return IslandConfig.TransitionOff;
        }

        if (string.Equals(value, IslandConfig.TransitionFade, StringComparison.OrdinalIgnoreCase))
        {
            return IslandConfig.TransitionFade;
        }

        if (string.Equals(value, IslandConfig.TransitionBlurFade, StringComparison.OrdinalIgnoreCase))
        {
            return IslandConfig.TransitionBlurFade;
        }

        return IslandConfig.DefaultWallpaperTransition;
    }

    /// <summary>Canonical lowercase color mode; unknown text falls back to <c>album</c>.</summary>
    internal static string NormalizeColorMode(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            IslandConfig.ColorModeSystem => IslandConfig.ColorModeSystem,
            IslandConfig.ColorModeNeutral => IslandConfig.ColorModeNeutral,
            _ => IslandConfig.ColorModeAlbum,
        };

    /// <summary>
    /// Loaded config. Values inside 5–50 stay. Unknown or out of range becomes 20.
    /// </summary>
    internal static int NormalizeLowBatteryPercent(int value)
        => value is >= IslandConfig.MinLowBatteryPercent and <= IslandConfig.MaxLowBatteryPercent
            ? value
            : IslandConfig.DefaultLowBatteryPercent;

    /// <summary>A value the user just chose. Clamped into 5–50.</summary>
    internal static int ClampLowBatteryPercent(int value)
        => Math.Clamp(value, IslandConfig.MinLowBatteryPercent, IslandConfig.MaxLowBatteryPercent);
}

/// <summary>Which launchpad face opens from the dock Launchpad tile.</summary>
internal sealed class LaunchpadConfig
{
    /// <summary><c>win11</c>, <c>mac</c>, or <c>win8</c>. See <see cref="Services.LaunchpadStyles"/>.</summary>
    public string Style { get; set; } = LaunchpadStyles.Win11;

    /// <summary>
    /// Visible footer tool ids (<c>launchpad.tools</c>). A missing list means every default.
    /// An empty list hides the strip. See <see cref="Services.LaunchpadTools"/>.
    /// </summary>
    public List<string> Tools { get; set; } = LaunchpadTools.DefaultIds.ToList();
}
