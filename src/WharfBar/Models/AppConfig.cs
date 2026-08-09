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

    public List<PinnedApp> PinnedApps { get; set; } = [];
}

/// <summary>Dock-specific settings under <see cref="AppConfig.Dock"/>.</summary>
internal sealed class DockConfig
{
    public const double DefaultIconSize = 48d;
    public const double MinIconSize = 24d;
    public const double MaxIconSize = 72d;

    /// <summary>Stored for later; <see cref="Services.DockAutoHideController"/> still stubs behavior.</summary>
    public bool AutoHide { get; set; } = true;

    public double IconSize { get; set; } = DefaultIconSize;
}
