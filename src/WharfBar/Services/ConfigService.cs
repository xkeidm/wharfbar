using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> from <c>%AppData%/WharfBar/config.json</c>.
/// Missing or unreadable files are replaced with defaults seeded from <see cref="DemoPinnedApps"/>.
/// </summary>
internal sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal string ConfigDirectory { get; }

    internal string ConfigPath { get; }

    internal AppConfig Current { get; private set; }

    /// <summary>
    /// True when this instance owns the real user configuration (<c>%AppData%/WharfBar</c>),
    /// i.e. it was constructed without an explicit directory. False for test-scoped instances
    /// pointed at a temp directory, which must not touch machine state outside that directory.
    /// </summary>
    internal bool IsRealUserConfig { get; }

    internal ConfigService(string? configDirectory = null)
    {
        IsRealUserConfig = configDirectory is null;
        ConfigDirectory = configDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WharfBar");
        ConfigPath = Path.Combine(ConfigDirectory, "config.json");
        Current = CreateDefault();
    }

    /// <summary>
    /// Loads existing config, or writes defaults on first run / unsupported schema / corrupt JSON.
    /// </summary>
    internal AppConfig LoadOrCreate()
    {
        if (!File.Exists(ConfigPath))
        {
            Current = CreateDefault();
            Save(Current);
            return Current;
        }

        try
        {
            string json = File.ReadAllText(ConfigPath);
            bool topBarKeyMissing = !JsonRootHasProperty(json, "topBar");
            bool veilKeyMissing = !JsonRootHasProperty(json, "veil");
            bool islandKeyMissing = !JsonRootHasProperty(json, "island");
            AppConfig? loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);

            if (loaded is null)
            {
                return ReplaceWithDefault("deserialized null");
            }

            if (loaded.SchemaVersion > AppConfig.CurrentSchemaVersion)
            {
                return ReplaceWithDefault($"unsupported schemaVersion {loaded.SchemaVersion}");
            }

            bool monitorsKeyMissing = !JsonRootHasProperty(json, "monitors");
            bool monitorsMigrated = NeedsMonitorsMigration(loaded, monitorsKeyMissing);
            MigrateMonitors(loaded);
            bool dockItemsMigrated = NeedsDockItemsMigration(loaded);
            if (dockItemsMigrated)
            {
                MigrateDockItems(loaded);
            }

            double originalIconSize = loaded.Dock?.IconSize ?? DockConfig.DefaultIconSize;
            int originalCatalogVersion = loaded.TopBar?.StatusCatalogVersion ?? 0;
            Current = Normalize(loaded);

            bool statusChanged = topBarKeyMissing || !StatusItemsEqual(
                loaded.TopBar?.StatusItems,
                Current.TopBar.StatusItems);
            bool catalogChanged = Current.TopBar.StatusCatalogVersion != originalCatalogVersion;
            bool iconChanged = Math.Abs(Current.Dock.IconSize - originalIconSize) > 0.01;
            bool monitorsChanged = monitorsMigrated
                || MonitorCountChanged(loaded.Monitors, Current.Monitors);

            if (iconChanged || statusChanged || catalogChanged || veilKeyMissing
                || islandKeyMissing || monitorsChanged || dockItemsMigrated)
            {
                Save(Current);
            }

            return Current;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ReplaceWithDefault(ex.Message);
        }
    }

    /// <summary>Writes <paramref name="config"/> atomically (temp file + replace) and updates <see cref="Current"/>.</summary>
    internal void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        AppConfig normalized = Normalize(config);
        Directory.CreateDirectory(ConfigDirectory);

        string json = JsonSerializer.Serialize(normalized, JsonOptions);
        string tempPath = ConfigPath + ".tmp";

        File.WriteAllText(tempPath, json);

        if (File.Exists(ConfigPath))
        {
            File.Replace(tempPath, ConfigPath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, ConfigPath);
        }

        Current = normalized;

        // Only the real user config owns the Startup shortcut; a test-scoped service writes to a
        // temp directory and must not create, rewrite or delete %AppData%\...\Startup\WharfBar.lnk.
        if (IsRealUserConfig)
        {
            new AutostartService().Apply(normalized.Autostart);
        }
    }

    internal static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            SchemaVersion = AppConfig.CurrentSchemaVersion,
            Theme = AppConfig.ThemeSystem,
            Language = AppConfig.LanguageSystem,
            Autostart = true,
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
            TopBar = new TopBarConfig
            {
                StatusCatalogVersion = TopBarConfig.CurrentStatusCatalogVersion,
                StatusItems = TopBarStatusItemIds.DefaultOrder.ToList(),
                BackgroundMode = TopBarBackgroundModes.Mica,
                TintOpacity = TopBarBackgroundModes.DefaultOpacity,
            },
            Veil = new VeilConfig(),
            Island = new IslandConfig(),
        };
    }

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

            config.PinnedApps = [];
        }

        return config;
    }

    internal static bool NeedsMonitorsMigration(AppConfig loaded, bool monitorsKeyMissing)
        => monitorsKeyMissing || loaded.SchemaVersion < AppConfig.CurrentSchemaVersion;

    internal static AppConfig MigrateMonitors(AppConfig config)
    {
        config.SchemaVersion = AppConfig.CurrentSchemaVersion;
        config.Monitors ??= [];
        return config;
    }

    private static bool MonitorCountChanged(IReadOnlyList<MonitorConfig>? loaded, IReadOnlyList<MonitorConfig> current)
    {
        if (loaded is null)
        {
            return current.Count > 0;
        }

        if (loaded.Count != current.Count)
        {
            return true;
        }

        for (int i = 0; i < current.Count; i++)
        {
            if (!string.Equals(loaded[i]?.DeviceId, current[i].DeviceId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private AppConfig ReplaceWithDefault(string reason)
    {
        Debug.WriteLine($"[WharfBar] Config reset to defaults: {reason}");
        TryBackupCorruptFile();
        Current = CreateDefault();
        Save(Current);
        return Current;
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return;
            }

            string backupPath = ConfigPath + ".bak";
            File.Copy(ConfigPath, backupPath, overwrite: true);
        }
        catch (IOException)
        {
            // Best-effort; defaults still get written.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static AppConfig Normalize(AppConfig config)
    {
        config.SchemaVersion = AppConfig.CurrentSchemaVersion;
        config.Theme = NormalizeTheme(config.Theme);
        config.Language = LocalizationService.NormalizeLanguage(config.Language);
        config.Dock ??= new DockConfig();
        config.TopBar ??= new TopBarConfig();
        config.Veil ??= new VeilConfig();
        config.Veil.Opacity = VeilNormalizer.ClampOpacity(config.Veil.Opacity);
        config.Veil.Color = VeilNormalizer.NormalizeColor(config.Veil.Color);
        config.Island ??= new IslandConfig();
        config.Launchpad ??= new LaunchpadConfig();
        config.Launchpad.Style = LaunchpadStyles.Normalize(config.Launchpad.Style);
        config.Launchpad.Tools = LaunchpadTools.Normalize(config.Launchpad.Tools);
        config.Island.Persistence = IslandNormalizer.NormalizePersistence(config.Island.Persistence);
        config.Island.RetentionDays = IslandNormalizer.ClampRetentionDays(config.Island.RetentionDays);
        config.Island.MaxItems = IslandNormalizer.ClampMaxItems(config.Island.MaxItems);
        config.Island.MusicPresence = IslandNormalizer.NormalizeMusicPresence(config.Island.MusicPresence);
        config.Island.IdleLabel = IslandNormalizer.NormalizeIdleLabel(config.Island.IdleLabel);
        config.Island.LowBatteryPercent = IslandNormalizer.NormalizeLowBatteryPercent(config.Island.LowBatteryPercent);
        config.Island.ColorMode = IslandNormalizer.NormalizeColorMode(config.Island.ColorMode);
        config.Island.OpenWidgets = PluginCatalog.NormalizeOpenWidgets(config.Island.OpenWidgets);
        config.Plugins ??= new PluginsConfig();
        config.Plugins.Enabled = PluginCatalog.NormalizeEnabled(config.Plugins.Enabled);
        config.TopBar.BackgroundMode = TopBarBackgroundModes.NormalizeMode(config.TopBar.BackgroundMode);
        config.TopBar.TintOpacity = TopBarBackgroundModes.ClampOpacity(config.TopBar.TintOpacity);
        config.TopBar.StatusItems = TopBarStatusItemIds.Normalize(config.TopBar.StatusItems);
        // Her öğe kendi tanıtım sürümüyle eşiklenir: kullanıcı gizlemişse (hide must stick)
        // upgrade'te yeniden eklenmez. controlCenter v1, brightness v2, media v4, trayMirror v5 ile geldi.
        if (config.TopBar.StatusCatalogVersion < 1)
        {
            TopBarStatusItemIds.EnsureControlCenter(config.TopBar.StatusItems);
        }

        if (config.TopBar.StatusCatalogVersion < 2)
        {
            TopBarStatusItemIds.EnsureBrightness(config.TopBar.StatusItems);
        }

        if (config.TopBar.StatusCatalogVersion < 4)
        {
            TopBarStatusItemIds.EnsureMedia(config.TopBar.StatusItems);
        }

        if (config.TopBar.StatusCatalogVersion < 5)
        {
            TopBarStatusItemIds.EnsureTrayMirror(config.TopBar.StatusItems);
        }

        config.TopBar.StatusCatalogVersion = TopBarConfig.CurrentStatusCatalogVersion;
        config.TopBar.TrayOverflowIcons =
            TrayOverflowMembership.Normalize(config.TopBar.TrayOverflowIcons).ToList();

        // Soft migrations: oversized early defaults (48/72) and the previous 28 DIP default → 24.
        // Other values inside Min–Max are preserved.
        if (config.Dock.IconSize > DockConfig.MaxIconSize
            || Math.Abs(config.Dock.IconSize - DockConfig.PreviousDefaultIconSize) < 0.01)
        {
            config.Dock.IconSize = DockConfig.DefaultIconSize;
        }

        config.Dock.IconSize = Math.Clamp(
            config.Dock.IconSize <= 0 ? DockConfig.DefaultIconSize : config.Dock.IconSize,
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);
        config.Dock.Surface = DockSurfaceStyles.Mica;
        config.Dock.BackgroundMode = TopBarBackgroundModes.NormalizeMode(config.Dock.BackgroundMode);
        config.Dock.TintOpacity = TopBarBackgroundModes.ClampOpacity(config.Dock.TintOpacity);
        config.Dock.Items ??= [];
        config.Dock.Position = DockPositions.Normalize(config.Dock.Position);
        config.Dock.Magnification = DockMagnification.Clamp(config.Dock.Magnification);
        config.PinnedApps ??= [];
        config.Monitors = MonitorConfigNormalizer.Normalize(config.Monitors).ToList();

        NormalizePinnedList(config.PinnedApps);
        NormalizePinnedList(config.Dock.Items);

        return config;
    }

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

            string path = app.Path.Trim();

            // Never leave an empty label: a trailing separator survives Trim too, so a folder
            // pin ("C:\tools\", "C:\", "\\server\share\") derives an empty name. Fall back to
            // the separator-trimmed path, then to the raw path for an all-separator input.
            // Same rule in DockItemStore.TryAddCore — keep the two in step.
            string label = string.IsNullOrWhiteSpace(app.Label)
                ? Path.GetFileNameWithoutExtension(path)
                : app.Label.Trim();

            if (string.IsNullOrWhiteSpace(label))
            {
                string trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                label = trimmedPath.Length == 0 ? path : trimmedPath;
            }

            items[i] = new PinnedApp(
                path,
                app.Args?.Trim() ?? string.Empty,
                label);
        }
    }

    private static bool JsonRootHasProperty(string json, string propertyName)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatusItemsEqual(IList<string>? a, IList<string> b)
    {
        if (a is null)
        {
            return false; // missing → we wrote defaults; persist once
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < b.Count; i++)
        {
            if (!string.Equals(a[i]?.Trim(), b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeTheme(string? theme)
    {
        if (string.Equals(theme, AppConfig.ThemeLight, StringComparison.OrdinalIgnoreCase))
        {
            return AppConfig.ThemeLight;
        }

        if (string.Equals(theme, AppConfig.ThemeDark, StringComparison.OrdinalIgnoreCase))
        {
            return AppConfig.ThemeDark;
        }

        return AppConfig.ThemeSystem;
    }
}
