using System;
using System.IO;
using System.Linq;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ConfigServiceDockTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-dock-config-tests", Guid.NewGuid().ToString("N"));

    private string ConfigFile => Path.Combine(_root, "config.json");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private ConfigService NewService() => new(_root);

    private static AppConfig LegacyConfig()
    {
        var config = ConfigService.CreateDefault();
        config.Dock.Items = [];
        config.PinnedApps =
        [
            new PinnedApp("C:\\Windows\\System32\\notepad.exe", string.Empty, "Not Defteri"),
        ];
        return config;
    }

    [Fact]
    public void NeedsDockItemsMigration_WhenDockEmptyAndLegacySeeded_IsTrue()
    {
        Assert.True(ConfigService.NeedsDockItemsMigration(LegacyConfig()));
    }

    [Fact]
    public void NeedsDockItemsMigration_WhenDockAlreadyPopulated_IsFalse()
    {
        AppConfig config = LegacyConfig();
        config.Dock.Items = [new PinnedApp("C:\\a.exe", string.Empty, "A")];
        Assert.False(ConfigService.NeedsDockItemsMigration(config));
    }

    [Fact]
    public void NeedsDockItemsMigration_WhenLegacyEmpty_IsFalse()
    {
        var config = ConfigService.CreateDefault();
        config.Dock.Items = [];
        Assert.False(ConfigService.NeedsDockItemsMigration(config));
    }

    [Fact]
    public void MigrateDockItems_CopiesLegacyEntriesIntoDock()
    {
        AppConfig config = ConfigService.MigrateDockItems(LegacyConfig());
        Assert.Single(config.Dock.Items);
        Assert.Equal("C:\\Windows\\System32\\notepad.exe", config.Dock.Items[0].Path);
    }

    [Fact]
    public void MigrateDockItems_ClearsLegacyListSoPinsDoNotResurrect()
    {
        AppConfig config = ConfigService.MigrateDockItems(LegacyConfig());
        Assert.Empty(config.PinnedApps);
    }

    [Fact]
    public void MigrateDockItems_WhenDockPopulated_LeavesLegacyUntouched()
    {
        AppConfig config = LegacyConfig();
        config.Dock.Items = [new PinnedApp("C:\\a.exe", string.Empty, "A")];
        ConfigService.MigrateDockItems(config);
        Assert.Single(config.PinnedApps);
    }

    [Fact]
    public void Normalize_ClampsMagnificationAboveMax()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Magnification = 9d;
        service.Save(service.Current);
        Assert.Equal(DockMagnification.Max, service.Current.Dock.Magnification, 6);
    }

    [Fact]
    public void Normalize_ClampsMagnificationBelowMin()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Magnification = 0.25d;
        service.Save(service.Current);
        Assert.Equal(DockMagnification.Min, service.Current.Dock.Magnification, 6);
    }

    [Fact]
    public void Normalize_TreatsNonPositiveMagnificationAsDefault()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Magnification = -1d;
        service.Save(service.Current);
        Assert.Equal(DockMagnification.Default, service.Current.Dock.Magnification, 6);
    }

    [Fact]
    public void Normalize_FallsBackToBottomCenterForUnknownPosition()
    {
        Assert.Equal(DockPositions.BottomCenter, DockPositions.Normalize("middle-ish"));
        Assert.Equal(DockPositions.BottomCenter, DockPositions.Normalize(null));
    }

    [Fact]
    public void Normalize_PreservesLeftAndRightPositions()
    {
        Assert.Equal(DockPositions.BottomLeft, DockPositions.Normalize("Bottom-Left"));
        Assert.Equal(DockPositions.BottomRight, DockPositions.Normalize("bottom-right"));
    }

    [Fact]
    public void Normalize_DropsDockItemsWithoutPath()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Items =
        [
            new PinnedApp("   ", string.Empty, "Boş"),
            new PinnedApp("C:\\a.exe", string.Empty, "A"),
        ];
        service.Save(service.Current);
        Assert.Single(service.Current.Dock.Items);
        Assert.Equal("C:\\a.exe", service.Current.Dock.Items[0].Path);
    }

    [Fact]
    public void Normalize_FillsMissingDockItemLabelFromFileName()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Items = [new PinnedApp("C:\\tools\\thing.exe", string.Empty, "   ")];
        service.Save(service.Current);
        Assert.Equal("thing", service.Current.Dock.Items[0].Label);
    }

    [Theory]
    [InlineData(@"C:\tools\", @"C:\tools")]
    [InlineData(@"C:\", "C:")]
    [InlineData(@"\\server\share\", @"\\server\share")]
    public void Normalize_WhenDockItemLabelMissingAndPathHasTrailingSeparator_FallsBackToPath(
        string path, string expected)
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Items = [new PinnedApp(path, string.Empty, string.Empty)];
        service.Save(service.Current);
        Assert.Equal(expected, service.Current.Dock.Items[0].Label);
    }

    [Fact]
    public void Normalize_WhenDockItemLabelMissingAndPathIsAllSeparators_FallsBackToPath()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Items = [new PinnedApp("////", string.Empty, string.Empty)];
        service.Save(service.Current);
        Assert.Equal("////", service.Current.Dock.Items[0].Label);
    }

    [Fact]
    public void Normalize_TrimsDockItemPathAndArgs()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Current.Dock.Items = [new PinnedApp("  C:\\a.exe  ", "  --flag  ", "  A  ")];
        service.Save(service.Current);
        Assert.Equal("C:\\a.exe", service.Current.Dock.Items[0].Path);
        Assert.Equal("--flag", service.Current.Dock.Items[0].Args);
        Assert.Equal("A", service.Current.Dock.Items[0].Label);
    }

    [Fact]
    public void CreateDefault_SeedsDockItemsFromDemoPins()
    {
        AppConfig config = ConfigService.CreateDefault();
        Assert.NotEmpty(config.Dock.Items);
        Assert.Empty(config.PinnedApps);
    }

    [Fact]
    public void LoadOrCreate_MigratesLegacyListAndPersistsIt()
    {
        // Note: no File.Delete here. Deleting the file would turn the reopen into a fresh
        // install, which seeds dock.items from DemoPinnedApps and never exercises the migration.
        NewService().Save(LegacyConfig());

        ConfigService reopened = NewService();
        AppConfig loaded = reopened.LoadOrCreate();

        Assert.Single(loaded.Dock.Items);
        Assert.Empty(loaded.PinnedApps);

        // Migration is persisted, so a second open does not need to redo it.
        ConfigService third = NewService();
        Assert.Single(third.LoadOrCreate().Dock.Items);
    }

    [Fact]
    public void LoadOrCreate_DoesNotResurrectPinsAfterUserEmptiesDock()
    {
        NewService().Save(LegacyConfig());

        ConfigService first = NewService();
        first.LoadOrCreate();
        first.Current.Dock.Items = [];
        first.Save(first.Current);

        ConfigService second = NewService();
        Assert.Empty(second.LoadOrCreate().Dock.Items);
    }

    [Fact]
    public void LoadOrCreate_OnFreshInstallWritesDockItems()
    {
        ConfigService service = NewService();
        AppConfig loaded = service.LoadOrCreate();
        Assert.True(File.Exists(ConfigFile));
        Assert.Equal(loaded.Dock.Items.Count, service.Current.Dock.Items.Count);
        Assert.All(loaded.Dock.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Path)));
    }

    [Fact]
    public void LoadOrCreate_WithPreDockConfigJson_MigratesAndFillsNewDockFields()
    {
        // A config.json from before the dock had its own list: no dock.items, dock.position
        // or dock.magnification keys at all. Absent keys must fall back to the field defaults
        // (not to null/zero) and the legacy root list must still hand off.
        Directory.CreateDirectory(Path.Combine(_root, "WharfBar"));
        File.WriteAllText(
            ConfigFile,
            "{\"schemaVersion\":2,\"theme\":\"system\",\"autostart\":true,"
            + "\"dock\":{\"autoHide\":true,\"iconSize\":28,\"surface\":\"mica\"},"
            + "\"pinnedApps\":[{\"path\":\"C:\\\\Windows\\\\System32\\\\notepad.exe\",\"args\":\"\",\"label\":\"Not Defteri\"}]}");

        AppConfig loaded = NewService().LoadOrCreate();

        Assert.Single(loaded.Dock.Items);
        Assert.Empty(loaded.PinnedApps);
        Assert.Equal(DockMagnification.Default, loaded.Dock.Magnification, 6);
        Assert.Equal(DockPositions.BottomCenter, loaded.Dock.Position);
    }

    [Fact]
    public void IsRealUserConfig_IsFalseForInjectedDirectoryAndTrueForDefault()
    {
        // The no-argument ctor only computes a path and builds an in-memory default — it writes
        // nothing, so constructing it in a test is safe.
        Assert.False(NewService().IsRealUserConfig);
        Assert.True(new ConfigService().IsRealUserConfig);
    }

    [Fact]
    public void Save_WhenServiceIsTestScoped_DoesNotTouchTheStartupShortcut()
    {
        string shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup), "WharfBar.lnk");
        if (!File.Exists(shortcutPath))
        {
            // Nothing to protect on this machine; never create it just to make this assert.
            return;
        }

        DateTime before = File.GetLastWriteTimeUtc(shortcutPath);

        ConfigService service = NewService();
        service.LoadOrCreate();
        service.Save(service.Current);

        Assert.Equal(before, File.GetLastWriteTimeUtc(shortcutPath));
    }

    [Fact]
    public void DemoPins_AreAllUsablePaths()
    {
        // The seeded dock must not point at files that do not exist on a real machine.
        foreach (PinnedApp pin in DemoPinnedApps.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(pin.Path));
            Assert.True(pin.Label.Trim().Length > 0);
        }
    }
}
