using System.IO;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class PluginCatalogTests
{
    [Fact]
    public void TryParse_ReadsKindSlotAndIgnoresAssembly()
    {
        const string json = """
            {
              "id": "Hava",
              "name": "Hava durumu",
              "description": "Kısa durum.",
              "kind": "Widget",
              "slot": "TOPBAR",
              "assembly": "evil.dll",
              "url": "https://example.invalid/payload",
              "entryPoint": "Run",
              "download": true
            }
            """;

        PluginManifest? manifest = PluginCatalog.TryParse(json);

        Assert.NotNull(manifest);
        Assert.Equal("hava", manifest.Id);
        Assert.Equal("Hava durumu", manifest.Name);
        Assert.Equal("Kısa durum.", manifest.Summary);
        Assert.Equal(PluginKinds.Widget, manifest.Kind);
        Assert.Equal(PluginSlots.TopBar, manifest.Slot);
    }

    [Fact]
    public void TryParse_DefaultsSlotFromKind()
    {
        PluginManifest? widget = PluginCatalog.TryParse("""{"id":"saat","name":"Saat","kind":"widget"}""");
        PluginManifest? plugin = PluginCatalog.TryParse("""{"id":"not","name":"Not"}""");

        Assert.NotNull(widget);
        Assert.Equal(PluginSlots.Desktop, widget.Slot);
        Assert.NotNull(plugin);
        Assert.Equal(PluginKinds.Plugin, plugin.Kind);
        Assert.Equal(PluginSlots.Island, plugin.Slot);
    }

    [Theory]
    [InlineData("""{"name":"Hava"}""")]
    [InlineData("""{"id":"Hava!","name":"Hava"}""")]
    [InlineData("""{"id":"-hava","name":"Hava"}""")]
    [InlineData("""{"id":"hava","name":"  "}""")]
    [InlineData("""{"id":"hava","name":"Hava","kind":"theme"}""")]
    [InlineData("""{"id":"hava","name":"Hava","slot":"menu"}""")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsUnusableManifests(string? json)
    {
        Assert.Null(PluginCatalog.TryParse(json));
    }

    [Fact]
    public void TryParse_AcceptsSummaryAliasAndTruncates()
    {
        string name = new string('A', 100);
        string summary = new string('B', 300);
        PluginManifest? manifest = PluginCatalog.TryParse(
            $$"""{"id":"uzun","name":"{{name}}","summary":"{{summary}}"}""");

        Assert.NotNull(manifest);
        Assert.Equal(PluginCatalog.MaxNameLength, manifest.Name.Length);
        Assert.Equal(PluginCatalog.MaxSummaryLength, manifest.Summary.Length);
    }

    [Fact]
    public void Discover_ReadsChildManifestsAndSkipsNoise()
    {
        string root = NewFolder();
        string outside = NewFolder();
        try
        {
            WriteManifest(outside, "outside", "Dışarı");
            WriteManifest(root, "root-one", "Kök");
            WriteManifest(Path.Combine(root, "alpha"), "alpha", "Alpha", kind: "widget", slot: "dock");
            Directory.CreateDirectory(Path.Combine(root, "bad"));
            File.WriteAllText(Path.Combine(root, "bad", "manifest.json"), "{");
            WriteManifest(Path.Combine(root, "builtin"), PluginCatalog.Clock, "Saat");
            WriteManifest(Path.Combine(root, "dup-a"), "same", "Ayni");
            WriteManifest(Path.Combine(root, "dup-b"), "same", "Ayni");
            Directory.CreateDirectory(Path.Combine(root, "note"));
            File.WriteAllText(Path.Combine(root, "note", "readme.txt"), "yok");
            Directory.CreateDirectory(Path.Combine(root, "huge"));
            File.WriteAllText(Path.Combine(root, "huge", "manifest.json"), new string('x', PluginCatalog.MaxManifestBytes + 8));
            Assert.True(TryPlantJunction(Path.Combine(root, "link"), outside));

            IReadOnlyList<PluginManifest> found = PluginCatalog.Discover(root);

            Assert.Equal(["alpha", "same"], found.Select(item => item.Id).ToArray());
            PluginManifest alpha = found[0];
            Assert.Equal(PluginSlots.Dock, alpha.Slot);
            Assert.Equal(PluginKinds.Widget, alpha.Kind);
        }
        finally
        {
            TryDeleteJunction(Path.Combine(root, "link"));
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void Discover_MissingFolder_IsEmpty()
    {
        Assert.Empty(PluginCatalog.Discover(Path.Combine(Path.GetTempPath(), "wharfbar-missing-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Compose_MapsBuiltInsAndLocalEnableFlag()
    {
        var config = new AppConfig
        {
            Island = new IslandConfig
            {
                Visualizer = false,
                BluetoothNotice = true,
                CameraMirror = true,
                OpenWidgets = ["calendar", "nope", "CLOCK"],
            },
            Plugins = new PluginsConfig { Enabled = ["alpha", "clock", "!!"] },
        };
        var local = new[]
        {
            new PluginManifest("alpha", "Alpha", "Dock kaydı", PluginKinds.Widget, PluginSlots.Dock),
            new PluginManifest(PluginCatalog.Clock, "Sahte", "Yok", PluginKinds.Widget, PluginSlots.TopBar),
        };

        IReadOnlyList<PluginCard> cards = PluginCatalog.Compose(config, local);

        Assert.Equal(
            [PluginCatalog.Visualizer, PluginCatalog.BluetoothNotice, PluginCatalog.CameraMirror, PluginCatalog.Clock, PluginCatalog.Calendar, "alpha"],
            cards.Select(card => card.Id).ToArray());
        Assert.False(cards[0].Enabled);
        Assert.True(cards[1].Enabled);
        Assert.True(cards[2].Enabled);
        Assert.True(cards.Single(card => card.Id == PluginCatalog.Clock).Enabled);
        Assert.True(cards.Single(card => card.Id == PluginCatalog.Calendar).Enabled);
        PluginCard alpha = cards.Single(card => card.Id == "alpha");
        Assert.False(alpha.BuiltIn);
        Assert.True(alpha.Enabled);
        Assert.Equal(PluginSlots.Dock, alpha.Slot);
        Assert.Equal("Dock", PluginSlots.Label(alpha.Slot));
    }

    [Fact]
    public void Filter_KeepsOneKind()
    {
        IReadOnlyList<PluginCard> cards = PluginCatalog.Compose(new AppConfig(), []);
        IReadOnlyList<PluginCard> widgets = PluginCatalog.Filter(cards, PluginKinds.Widget);

        Assert.Equal([PluginCatalog.Clock, PluginCatalog.Calendar], widgets.Select(card => card.Id).ToArray());
        Assert.All(widgets, card => Assert.Equal(PluginSlots.Desktop, card.Slot));
    }

    [Fact]
    public void SetEnabled_TogglesBuiltInFlagsWithoutTouchingLocalList()
    {
        var config = new AppConfig();

        Assert.True(PluginCatalog.SetEnabled(config, "Visualizer", false));
        Assert.True(PluginCatalog.SetEnabled(config, PluginCatalog.BluetoothNotice, false));
        Assert.True(PluginCatalog.SetEnabled(config, PluginCatalog.CameraMirror, true));
        Assert.True(PluginCatalog.SetEnabled(config, PluginCatalog.Clock, true));
        Assert.True(PluginCatalog.SetEnabled(config, "CLOCK", false));
        Assert.True(PluginCatalog.SetEnabled(config, PluginCatalog.Calendar, true));

        Assert.False(config.Island.Visualizer);
        Assert.False(config.Island.BluetoothNotice);
        Assert.True(config.Island.CameraMirror);
        Assert.Equal([PluginCatalog.Calendar], config.Island.OpenWidgets);
        Assert.Empty(config.Plugins.Enabled);
    }

    [Fact]
    public void SetEnabled_TogglesLocalIdOnce()
    {
        var config = new AppConfig();

        Assert.True(PluginCatalog.SetEnabled(config, "Hava", true));
        Assert.True(PluginCatalog.SetEnabled(config, "hava", true));
        Assert.Equal(["hava"], config.Plugins.Enabled);

        Assert.True(PluginCatalog.SetEnabled(config, "hava", false));
        Assert.Empty(config.Plugins.Enabled);
        Assert.False(PluginCatalog.SetEnabled(config, "../hava", true));
        Assert.False(PluginCatalog.SetEnabled(config, " ", true));
    }

    [Fact]
    public void NormalizeEnabled_DropsBuiltInsDuplicatesAndJunk()
    {
        List<string> enabled = PluginCatalog.NormalizeEnabled(
            [" clock ", "Hava", "hava", "../x", "", "a-", "-b", "ok-1"]);

        Assert.Equal(["hava", "ok-1"], enabled);
    }

    [Fact]
    public void NormalizeOpenWidgets_KeepsClockAndCalendar()
    {
        Assert.Equal(
            [PluginCatalog.Calendar, PluginCatalog.Clock],
            PluginCatalog.NormalizeOpenWidgets(["calendar", "nope", "Clock", "calendar"]));
        Assert.Empty(PluginCatalog.NormalizeOpenWidgets(null));
    }

    [Fact]
    public void Load_NormalizesPluginFields_WithoutRaisingSchema()
    {
        string dir = NewFolder();
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "config.json"),
                """
                {
                  "schemaVersion": 2,
                  "island": { "openWidgets": ["calendar", "extra"] },
                  "plugins": { "enabled": ["clock", "Hava", "hava", "../x"] }
                }
                """);

            AppConfig loaded = new ConfigService(dir).LoadOrCreate();

            Assert.Equal(AppConfig.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal([PluginCatalog.Calendar], loaded.Island.OpenWidgets);
            Assert.Equal(["hava"], loaded.Plugins.Enabled);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Save_RoundTripsLocalEnabledWithoutRaisingSchema()
    {
        string dir = NewFolder();
        try
        {
            var service = new ConfigService(dir);
            AppConfig config = service.LoadOrCreate();
            Assert.True(PluginCatalog.SetEnabled(config, "hava", true));
            service.Save(config);

            AppConfig loaded = new ConfigService(dir).LoadOrCreate();

            Assert.Equal(2, loaded.SchemaVersion);
            Assert.Equal(["hava"], loaded.Plugins.Enabled);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "wharfbar-plugins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteManifest(string directory, string id, string name, string kind = "plugin", string slot = "island")
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, PluginCatalog.ManifestFileName),
            $$"""{"id":"{{id}}","name":"{{name}}","kind":"{{kind}}","slot":"{{slot}}"}""");
    }

    private static bool TryPlantJunction(string junction, string target)
    {
        using System.Diagnostics.Process? link = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junction}\" \"{target}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        if (link is null || !link.WaitForExit(5000))
        {
            return false;
        }

        return link.ExitCode == 0;
    }

    private static void TryDeleteJunction(string junction)
    {
        try
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }
        }
        catch (IOException)
        {
        }
    }
}
