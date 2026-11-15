using System;
using System.IO;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Catalog v6 (2026-11-12): the default status-strip order became the Windows tray-corner order
/// (tray icons → hidden icons → volume → brightness → network → battery → quick settings → clock →
/// notifications) and volume + brightness are on by default. A saved list that is still exactly the
/// old untouched default is migrated once; a customized order is never reordered.
/// </summary>
public class TopBarStatusOrderMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-status-order-tests", Guid.NewGuid().ToString("N"));

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

    private void WriteConfig(int catalogVersion, string statusItemsJson) =>
        WriteRaw($$"""
            {
              "schemaVersion": 2,
              "onboardingCompleted": true,
              "topBar": {
                "statusCatalogVersion": {{catalogVersion}},
                "statusItems": {{statusItemsJson}}
              }
            }
            """);

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ConfigFile, json);
    }

    private AppConfig Load()
    {
        var service = new ConfigService(_root);
        return service.LoadOrCreate();
    }

    [Fact]
    public void UntouchedLegacyDefault_MovesToTheWindowsOrder_WithVolumeAndBrightnessOn()
    {
        WriteConfig(5, """
            ["trayMirror","trayOverflow","dateTime","notifications","controlCenter","network","battery"]
            """);

        AppConfig config = Load();

        Assert.Equal(TopBarStatusItemIds.DefaultOrder, config.TopBar.StatusItems);
        Assert.Contains(TopBarStatusItemIds.Volume, config.TopBar.StatusItems);
        Assert.Contains(TopBarStatusItemIds.Brightness, config.TopBar.StatusItems);
        Assert.Equal(TopBarConfig.CurrentStatusCatalogVersion, config.TopBar.StatusCatalogVersion);
    }

    [Fact]
    public void CustomizedOrder_IsNeverReordered_AndKeepsHiddenItemsHidden()
    {
        // The user reordered the strip and left volume/brightness off: that is theirs to keep.
        WriteConfig(5, """
            ["battery","dateTime","network","trayMirror"]
            """);

        AppConfig config = Load();

        Assert.Equal(
            [
                TopBarStatusItemIds.Battery,
                TopBarStatusItemIds.DateTime,
                TopBarStatusItemIds.Network,
                TopBarStatusItemIds.TrayMirror,
            ],
            config.TopBar.StatusItems);
        Assert.DoesNotContain(TopBarStatusItemIds.Volume, config.TopBar.StatusItems);
        Assert.DoesNotContain(TopBarStatusItemIds.Brightness, config.TopBar.StatusItems);
        Assert.Equal(TopBarConfig.CurrentStatusCatalogVersion, config.TopBar.StatusCatalogVersion);
    }

    [Fact]
    public void AlreadyV6Config_KeepsItsSavedOrder()
    {
        WriteConfig(6, """
            ["dateTime","volume","notifications"]
            """);

        AppConfig config = Load();

        Assert.Equal(
            [TopBarStatusItemIds.DateTime, TopBarStatusItemIds.Volume, TopBarStatusItemIds.Notifications],
            config.TopBar.StatusItems);
    }

    [Fact]
    public void FreshConfig_UsesTheNewDefaultOrder_WithMediaHidden()
    {
        AppConfig config = Load();

        Assert.Equal(TopBarStatusItemIds.DefaultOrder, config.TopBar.StatusItems);
        Assert.DoesNotContain(TopBarStatusItemIds.Media, config.TopBar.StatusItems);
        Assert.Equal(TopBarConfig.CurrentStatusCatalogVersion, config.TopBar.StatusCatalogVersion);
    }
}
