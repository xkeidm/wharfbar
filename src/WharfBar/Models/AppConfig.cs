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

    /// <summary>When true, <see cref="Services.AutostartService"/> keeps a Startup-folder shortcut.</summary>
    public bool Autostart { get; set; } = true;

    public DockConfig Dock { get; set; } = new();

    public TopBarConfig TopBar { get; set; } = new();

    public List<PinnedApp> PinnedApps { get; set; } = [];
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
}

/// <summary>Top bar settings under <see cref="AppConfig.TopBar"/>.</summary>
internal sealed class TopBarConfig
{
    /// <summary>
    /// Soft catalog revision (<see cref="AppConfig.SchemaVersion"/> stays 1).
    /// Missing JSON → 0 (pre-<c>controlCenter</c>); current = <see cref="CurrentStatusCatalogVersion"/>.
    /// </summary>
    internal const int CurrentStatusCatalogVersion = 1;

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
