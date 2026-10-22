using System.IO;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WidgetPlacementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wharfbar-widgets-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void Normalize_DropsUnknownIds_NonFiniteNumbers_AndKeepsTheLastDuplicate()
    {
        var raw = new List<WidgetPlacement>
        {
            new() { Id = "clock", X = 1, Y = 2 },
            new() { Id = "nope", X = 3, Y = 4 },
            new() { Id = " Calendar ", X = 5, Y = 6 },
            new() { Id = "clock", X = double.NaN, Y = 8 },
            new() { Id = "clock", X = 9, Y = 10 },
        };

        IReadOnlyList<WidgetPlacement> cleaned = WidgetPlacementNormalizer.Normalize(raw);

        Assert.Equal(2, cleaned.Count);
        Assert.Equal(PluginCatalog.Clock, cleaned[0].Id);
        Assert.Equal(9, cleaned[0].X);
        Assert.Equal(10, cleaned[0].Y);
        Assert.Equal(PluginCatalog.Calendar, cleaned[1].Id);
        Assert.Equal(5, cleaned[1].X);
        Assert.Equal(6, cleaned[1].Y);
    }

    [Fact]
    public void Normalize_Null_IsEmpty()
    {
        Assert.Empty(WidgetPlacementNormalizer.Normalize(null));
    }

    [Fact]
    public void Defaults_StackTheCalendarUnderTheClock()
    {
        var work = new DipRect(0, 0, 1920, 1080);

        DipPoint clock = WidgetDefaults.Clock(work);
        DipPoint calendar = WidgetDefaults.Under(clock, WidgetMetrics.SizeOf(PluginCatalog.Clock));

        Assert.Equal(new DipPoint(24, 24), clock);
        Assert.Equal(new DipPoint(24, 24 + 132 + 12), calendar);
    }

    [Fact]
    public void SaveLoad_PreservesPlacements_AndSchemaStays2()
    {
        var service = new ConfigService(_root);
        AppConfig config = ConfigService.CreateDefault();
        config.Island.WidgetPlacements =
        [
            new WidgetPlacement { Id = "Clock", X = -40, Y = 15 },
            new WidgetPlacement { Id = "calendar", X = 80, Y = 90 },
            new WidgetPlacement { Id = "extra", X = 1, Y = 1 },
        ];
        service.Save(config);

        string json = File.ReadAllText(service.ConfigPath);
        Assert.Contains("\"widgetPlacements\"", json);
        Assert.Contains("\"schemaVersion\": 2", json);

        AppConfig loaded = new ConfigService(_root).LoadOrCreate();
        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal(2, loaded.Island.WidgetPlacements.Count);
        Assert.Equal(PluginCatalog.Clock, loaded.Island.WidgetPlacements[0].Id);
        Assert.Equal(-40, loaded.Island.WidgetPlacements[0].X);
        Assert.Equal(15, loaded.Island.WidgetPlacements[0].Y);
    }
}
