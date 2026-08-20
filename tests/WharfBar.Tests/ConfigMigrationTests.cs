using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ConfigMigrationTests
{
    [Fact]
    public void NeedsMonitorsMigration_MissingKey_IsTrue()
    {
        var config = new AppConfig();
        Assert.True(ConfigService.NeedsMonitorsMigration(config, monitorsKeyMissing: true));
    }

    [Fact]
    public void NeedsMonitorsMigration_Schema1_IsTrue()
    {
        var config = new AppConfig { SchemaVersion = 1, Monitors = [] };
        Assert.True(ConfigService.NeedsMonitorsMigration(config, monitorsKeyMissing: false));
    }

    [Fact]
    public void NeedsMonitorsMigration_CurrentSchemaWithMonitors_IsFalse()
    {
        var config = new AppConfig
        {
            SchemaVersion = AppConfig.CurrentSchemaVersion,
            Monitors = [new MonitorConfig { DeviceId = "PCI\\VEN_A" }],
        };
        Assert.False(ConfigService.NeedsMonitorsMigration(config, monitorsKeyMissing: false));
    }

    [Fact]
    public void MigrateMonitors_Version1_LeavesListEmptyForRuntimeDefaults()
    {
        // v1 configs have no monitor knowledge; the runtime (MonitorManager.Reconcile)
        // synthesizes primary-on entries on first launch and persists them.
        var config = new AppConfig { SchemaVersion = 1, Monitors = [] };

        AppConfig result = ConfigService.MigrateMonitors(config);

        Assert.Equal(AppConfig.CurrentSchemaVersion, result.SchemaVersion);
        Assert.Empty(result.Monitors);
    }
}