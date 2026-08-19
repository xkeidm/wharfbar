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

    internal ConfigService()
    {
        ConfigDirectory = Path.Combine(
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
            AppConfig? loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);

            if (loaded is null)
            {
                return ReplaceWithDefault("deserialized null");
            }

            if (loaded.SchemaVersion != AppConfig.CurrentSchemaVersion)
            {
                return ReplaceWithDefault($"unsupported schemaVersion {loaded.SchemaVersion}");
            }

            double originalIconSize = loaded.Dock?.IconSize ?? DockConfig.DefaultIconSize;
            int originalCatalogVersion = loaded.TopBar?.StatusCatalogVersion ?? 0;
            Current = Normalize(loaded);

            bool statusChanged = topBarKeyMissing || !StatusItemsEqual(
                loaded.TopBar?.StatusItems,
                Current.TopBar.StatusItems);
            bool catalogChanged = Current.TopBar.StatusCatalogVersion != originalCatalogVersion;
            bool iconChanged = Math.Abs(Current.Dock.IconSize - originalIconSize) > 0.01;

            if (iconChanged || statusChanged || catalogChanged || veilKeyMissing)
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
        new AutostartService().Apply(normalized.Autostart);
    }

    internal static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            SchemaVersion = AppConfig.CurrentSchemaVersion,
            Theme = AppConfig.ThemeSystem,
            Autostart = true,
            Dock = new DockConfig
            {
                AutoHide = true,
                IconSize = DockConfig.DefaultIconSize,
            },
            PinnedApps = DemoPinnedApps.All.Select(static a => new PinnedApp(a.Path, a.Args, a.Label)).ToList(),
            TopBar = new TopBarConfig
            {
                StatusCatalogVersion = TopBarConfig.CurrentStatusCatalogVersion,
                StatusItems = TopBarStatusItemIds.DefaultOrder.ToList(),
                BackgroundMode = TopBarBackgroundModes.Mica,
                TintOpacity = TopBarBackgroundModes.DefaultOpacity,
            },
            Veil = new VeilConfig(),
        };
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
        config.Dock ??= new DockConfig();
        config.TopBar ??= new TopBarConfig();
        config.Veil ??= new VeilConfig();
        config.Veil.Opacity = VeilNormalizer.ClampOpacity(config.Veil.Opacity);
        config.Veil.Color = VeilNormalizer.NormalizeColor(config.Veil.Color);
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

        // Soft migrations: oversized early defaults (48/72) and the brief 24 DIP default → 28.
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
        config.Dock.Surface = DockSurfaceStyles.NormalizeStyle(config.Dock.Surface);
        config.PinnedApps ??= [];

        for (int i = config.PinnedApps.Count - 1; i >= 0; i--)
        {
            PinnedApp? app = config.PinnedApps[i];
            if (app is null || string.IsNullOrWhiteSpace(app.Path))
            {
                config.PinnedApps.RemoveAt(i);
                continue;
            }

            config.PinnedApps[i] = new PinnedApp(
                app.Path.Trim(),
                app.Args ?? string.Empty,
                string.IsNullOrWhiteSpace(app.Label) ? Path.GetFileNameWithoutExtension(app.Path) : app.Label.Trim());
        }

        return config;
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
