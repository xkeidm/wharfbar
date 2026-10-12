using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class LaunchpadToolsTests
{
    [Fact]
    public void Normalize_KeepsKnownIds_AndDropsJunk()
    {
        List<string> result = LaunchpadTools.Normalize(
            ["documents", " ", "nope", "POWER", "documents", "settings"]);

        Assert.Equal(["documents", "power", "settings"], result);
    }

    [Fact]
    public void Normalize_MissingList_IsEveryDefault_AndEmptyStaysEmpty()
    {
        Assert.Equal(LaunchpadTools.DefaultIds, LaunchpadTools.Normalize(null));
        Assert.Empty(LaunchpadTools.Normalize([]));
        Assert.Empty(LaunchpadTools.Normalize(["", "junk"]));
    }

    [Fact]
    public void Save_PersistsToolChoice_AndFillsAMissingList()
    {
        string root = Path.Combine(Path.GetTempPath(), "wharfbar-launchpad-tools", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new ConfigService(root);
            AppConfig config = ConfigService.CreateDefault();
            config.Launchpad.Tools = ["documents", "nope", "", "power", "documents"];
            service.Save(config);
            Assert.Equal(["documents", "power"], service.Current.Launchpad.Tools);

            AppConfig reloaded = new ConfigService(root).LoadOrCreate();
            Assert.Equal(["documents", "power"], reloaded.Launchpad.Tools);

            JsonObject document = JsonNode.Parse(File.ReadAllText(service.ConfigPath))!.AsObject();
            document["launchpad"]!.AsObject().Remove("tools");
            File.WriteAllText(service.ConfigPath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            AppConfig missing = new ConfigService(root).LoadOrCreate();
            Assert.Equal(LaunchpadTools.DefaultIds, missing.Launchpad.Tools);

            missing.Launchpad.Tools = [];
            new ConfigService(root).Save(missing);
            Assert.Empty(new ConfigService(root).LoadOrCreate().Launchpad.Tools);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
